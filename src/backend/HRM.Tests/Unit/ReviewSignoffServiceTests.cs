// ============================================================================
// US-PRF-006: Performance-review meeting notes + immutable digital sign-off — service unit tests.
//
// Drives ReviewSignoffService through the real EF Core InMemory provider (so the review_meeting_notes,
// review_meeting_notes_action and review_signoff rows are actually persisted) and the REAL Ganss HTML
// sanitizer (so the FR-1/§10 stored-XSS guard is exercised end-to-end, not mocked). Covers:
//   - BR-1: meeting notes can only be added AFTER the manager review is Submitted (before → 409).
//   - Sign-off happy path: manager save notes → request sign-off (manager signature, PendingEmployeeSignOff)
//     → employee Acknowledge & Sign (immutable signature name+timestamp+IP, SignedOff, review LOCKED).
//   - Dispute (FR-4/FR-5/BR-4): dispute REQUIRES comments (empty → 422); status Disputed; HR resolve
//     (amend → NotesAdded / confirm → SignedOff+lock) transitions out.
//   - IMMUTABILITY (NFR-3/BR-5): a SignedOff/locked review rejects notes edits; the recorded sign-off log is
//     append-only — the service never updates/deletes an existing ReviewSignoff row.
//   - HTML sanitization: meeting-notes rich text is sanitized on write (script/dangerous markup stripped).
//   - Tenant scoping: an unresolved tenant context is refused.
//   - Authorization: a manager may not add notes for a non-direct-report; only the reviewed employee may
//     sign/dispute; only HR may resolve a dispute.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ReviewSignoffServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _reportUserId = Guid.NewGuid();
    private readonly Guid _hrUserId = Guid.NewGuid();

    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _reportEmpId = Guid.NewGuid();
    private readonly Guid _otherEmpId = Guid.NewGuid(); // NOT a direct report of the manager
    private readonly Guid _cycleId = Guid.NewGuid();

    public ReviewSignoffServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    // A fresh service each time = a fresh scoped DbContext, exactly as a new HTTP request would get.
    private ReviewSignoffService CreateService(ICurrentUser user) => new(
        CreateDbContext(),
        _tenantContext,
        user,
        new GanssHtmlSanitizer(), // the REAL sanitizer — exercises FR-1/§10 for real
        Substitute.For<IPerformanceNotificationService>(),
        Substitute.For<ILogger<ReviewSignoffService>>());

    private ICurrentUser ManagerUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_managerUserId);
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewTeam });
        return u;
    }

    private ICurrentUser EmployeeUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_reportUserId);
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ViewOwn });
        return u;
    }

    private ICurrentUser HrUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_hrUserId);
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewAll });
        return u;
    }

    /// <summary>
    /// Seeds the tenant, a manager + direct report (+ a non-report), an active cycle, and a manager review
    /// in the given <paramref name="reviewStatus"/>. The report's employee row is linked to <see cref="_reportUserId"/>
    /// so the employee-side sign-off path resolves the actor.
    /// </summary>
    private void Seed(ManagerReviewStatus reviewStatus = ManagerReviewStatus.Submitted, int autoCloseDays = 7)
    {
        using var db = CreateDbContext();
        var now = DateTime.UtcNow;

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });

        db.Employees.Add(new Employee
        {
            Id = _managerEmpId, TenantId = _tenantId, UserId = _managerUserId,
            EmployeeNo = "EMP-MGR", FirstName = "Grace", LastName = "Hopper",
            Email = "grace@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _reportEmpId, TenantId = _tenantId, UserId = _reportUserId,
            EmployeeNo = "EMP-RPT", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Status = EmployeeStatus.Active,
            ReportsToEmployeeId = _managerEmpId, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _otherEmpId, TenantId = _tenantId,
            EmployeeNo = "EMP-OTH", FirstName = "Alan", LastName = "Turing",
            Email = "alan@acme.com", Status = EmployeeStatus.Active,
            ReportsToEmployeeId = Guid.NewGuid(), IsDeleted = false, // reports to someone else
        });

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-90), GoalSettingEnd = now.AddDays(-60),
            SelfAssessmentStart = now.AddDays(-30), SelfAssessmentEnd = now.AddDays(-10),
            ManagerReviewStart = now.AddDays(-5), ManagerReviewEnd = now.AddDays(15),
            RatingScaleMax = 5, SelfWeightPercent = 30,
            SignoffAutoCloseDays = autoCloseDays, IsDeleted = false,
        });

        db.ManagerReviews.Add(new ManagerReview
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _reportEmpId,
            ReviewerEmployeeId = _managerEmpId, Status = reviewStatus,
            SubmittedAt = reviewStatus == ManagerReviewStatus.Submitted ? now.AddDays(-1) : null,
            SignoffStatus = ReviewSignoffStatus.NotStarted, IsDeleted = false,
        });

        db.SaveChanges();
    }

    private SaveMeetingNotesInput Notes(Guid? employeeId = null, string? body = "<p>Discussed delivery.</p>")
        => new(_cycleId, employeeId ?? _reportEmpId, body, Strengths: "<p>Strong</p>",
            DevelopmentAreas: "<p>Grow</p>", Summary: "<p>Good year</p>",
            Actions: [new MeetingNotesActionInput("Lead the migration", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)))]);

    // Convenience: drive the workflow up to PendingEmployeeSignOff (manager requested sign-off).
    private async Task RequestSignOffAsync(string? ip = "203.0.113.5")
    {
        var result = await CreateService(ManagerUser()).RequestSignOffAsync(Notes(), ip);
        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
    }

    /// <summary>
    /// BR-2 (BUG-065): simulate the reviewed employee having opened their notes, so Acknowledge &amp; Sign is
    /// permitted (read-before-sign). The read-path stamping itself is covered by ReviewSignoffSelfServiceTests.
    /// </summary>
    private async Task OpenNotesAsync()
    {
        using var db = CreateDbContext();
        var review = await db.ManagerReviews.FirstAsync(r => r.EmployeeId == _reportEmpId && r.CycleId == _cycleId);
        review.NotesOpenedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // ── BR-1: notes only after the manager review is submitted ──────────

    [Fact]
    public async Task SaveNotes_Rejected_WhenManagerReviewNotSubmitted()
    {
        Seed(reviewStatus: ManagerReviewStatus.Draft);

        var result = await CreateService(ManagerUser()).SaveNotesAsync(Notes());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("review_not_submitted");
    }

    [Fact]
    public async Task SaveNotes_Succeeds_AfterManagerReviewSubmitted_StatusNotesAdded()
    {
        Seed();

        var result = await CreateService(ManagerUser()).SaveNotesAsync(Notes());

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        result.Value!.SignoffStatus.Should().Be(ReviewSignoffStatus.NotesAdded);
        result.Value!.IsLocked.Should().BeFalse();
        result.Value!.Actions.Should().ContainSingle(a => a.Description == "Lead the migration");
    }

    // ── Tenant-context guard ─────────────────────────────────────────────

    [Fact]
    public async Task SaveNotes_Rejected_WhenTenantNotResolved()
    {
        Seed();
        var unresolved = Substitute.For<ITenantContext>();
        unresolved.IsResolved.Returns(false);
        var svc = new ReviewSignoffService(
            TestDbContextFactory.Create(unresolved, _dbName), unresolved, ManagerUser(),
            new GanssHtmlSanitizer(), Substitute.For<IPerformanceNotificationService>(),
            Substitute.For<ILogger<ReviewSignoffService>>());

        var result = await svc.SaveNotesAsync(Notes());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── Authorization: manager cannot add notes for a non-report ─────────

    [Fact]
    public async Task SaveNotes_Forbidden_WhenEmployeeNotADirectReport()
    {
        Seed();

        var result = await CreateService(ManagerUser()).SaveNotesAsync(Notes(employeeId: _otherEmpId));

        result.IsFailure.Should().BeTrue();
        // No manager review row exists for the non-report; authz fails before that with 403.
        result.StatusCode.Should().Be(403);
    }

    // ── AC-2: request sign-off records the manager signature + status ────

    [Fact]
    public async Task RequestSignOff_RecordsManagerSignature_AndSetsPending()
    {
        Seed();

        var result = await CreateService(ManagerUser()).RequestSignOffAsync(Notes(), "203.0.113.5");

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        var dto = result.Value!;
        dto.SignoffStatus.Should().Be(ReviewSignoffStatus.PendingEmployeeSignOff);
        dto.SignoffRequestedAt.Should().NotBeNull();
        dto.Signoffs.Should().ContainSingle();
        var entry = dto.Signoffs.Single();
        entry.Party.Should().Be(SignoffParty.Manager);
        entry.Action.Should().Be(SignoffAction.RequestedSignOff);
        entry.SignerName.Should().Be("Grace Hopper");
        entry.ClientIpAddress.Should().Be("203.0.113.5");
    }

    // ── AC-3 happy path: employee acknowledge & sign → SignedOff + locked ─

    [Fact]
    public async Task Acknowledge_RecordsImmutableSignature_LocksReview()
    {
        Seed();
        await RequestSignOffAsync();
        await OpenNotesAsync();   // BR-2 read-before-sign precondition
        var before = DateTime.UtcNow;

        var input = new SignoffActionInput(_cycleId, _reportEmpId, Comments: null, ClientIpAddress: "198.51.100.9");
        var result = await CreateService(EmployeeUser()).AcknowledgeAsync(input);

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        var dto = result.Value!;
        dto.SignoffStatus.Should().Be(ReviewSignoffStatus.SignedOff);
        dto.IsLocked.Should().BeTrue();
        dto.SignoffCompletedAt.Should().NotBeNull();

        var ack = dto.Signoffs.Single(s => s.Action == SignoffAction.Acknowledged);
        ack.Party.Should().Be(SignoffParty.Employee);
        ack.SignerName.Should().Be("Ada Lovelace");          // immutable signature name (FR-3)
        ack.ClientIpAddress.Should().Be("198.51.100.9");      // immutable IP (FR-7)
        ack.SignedAt.Should().BeOnOrAfter(before);            // immutable timestamp (FR-3)

        // Persisted lock survives a fresh context.
        using var db = CreateDbContext();
        var stored = await db.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == _reportEmpId);
        stored.IsLocked.Should().BeTrue();
        stored.SignoffStatus.Should().Be(ReviewSignoffStatus.SignedOff);
    }

    [Fact]
    public async Task Acknowledge_Forbidden_WhenCallerIsNotTheReviewedEmployee()
    {
        Seed();
        await RequestSignOffAsync();

        // The manager (not the reviewed employee) attempts to sign on the employee's behalf.
        var input = new SignoffActionInput(_cycleId, _reportEmpId, Comments: null, ClientIpAddress: "203.0.113.5");
        var result = await CreateService(ManagerUser()).AcknowledgeAsync(input);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_reviewed_employee");
    }

    [Fact]
    public async Task Acknowledge_Rejected_WhenNotPendingSignOff()
    {
        Seed(); // status NotStarted, never requested

        var input = new SignoffActionInput(_cycleId, _reportEmpId, Comments: null, ClientIpAddress: null);
        var result = await CreateService(EmployeeUser()).AcknowledgeAsync(input);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("not_pending_signoff");
    }

    // ── Dispute (FR-4/FR-5/BR-4) ─────────────────────────────────────────

    [Fact]
    public async Task Dispute_Rejected_WhenCommentsMissing()
    {
        Seed();
        await RequestSignOffAsync();

        var input = new SignoffActionInput(_cycleId, _reportEmpId, Comments: "   ", ClientIpAddress: "198.51.100.9");
        var result = await CreateService(EmployeeUser()).DisputeAsync(input);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("dispute_comments_required");
    }

    [Fact]
    public async Task Dispute_WithComments_SetsDisputed_RecordsSignature()
    {
        Seed();
        await RequestSignOffAsync();

        var input = new SignoffActionInput(_cycleId, _reportEmpId, "I disagree with goal 2's rating.", "198.51.100.9");
        var result = await CreateService(EmployeeUser()).DisputeAsync(input);

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        result.Value!.SignoffStatus.Should().Be(ReviewSignoffStatus.Disputed);
        result.Value!.IsLocked.Should().BeFalse();
        var entry = result.Value!.Signoffs.Single(s => s.Action == SignoffAction.Disputed);
        entry.Party.Should().Be(SignoffParty.Employee);
        entry.Comments.Should().Be("I disagree with goal 2's rating.");
        entry.ClientIpAddress.Should().Be("198.51.100.9");
    }

    [Fact]
    public async Task ResolveDispute_Amend_ReturnsToNotesAdded_HrSignatureRecorded()
    {
        Seed();
        await RequestSignOffAsync();
        await CreateService(EmployeeUser())
            .DisputeAsync(new SignoffActionInput(_cycleId, _reportEmpId, "Disagree.", "198.51.100.9"));

        var input = new ResolveDisputeInput(_cycleId, _reportEmpId, Amend: true, Comments: "Will revise.", ClientIpAddress: "203.0.113.1");
        var result = await CreateService(HrUser()).ResolveDisputeAsync(input);

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        result.Value!.SignoffStatus.Should().Be(ReviewSignoffStatus.NotesAdded);
        result.Value!.IsLocked.Should().BeFalse();
        result.Value!.SignoffRequestedAt.Should().BeNull();
        result.Value!.Signoffs.Should().Contain(s => s.Action == SignoffAction.DisputeAmended && s.Party == SignoffParty.Hr);
    }

    [Fact]
    public async Task ResolveDispute_Confirm_SignsOff_AndLocks()
    {
        Seed();
        await RequestSignOffAsync();
        await CreateService(EmployeeUser())
            .DisputeAsync(new SignoffActionInput(_cycleId, _reportEmpId, "Disagree.", "198.51.100.9"));

        var input = new ResolveDisputeInput(_cycleId, _reportEmpId, Amend: false, Comments: "Confirmed as-is.", ClientIpAddress: "203.0.113.1");
        var result = await CreateService(HrUser()).ResolveDisputeAsync(input);

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        result.Value!.SignoffStatus.Should().Be(ReviewSignoffStatus.SignedOff);
        result.Value!.IsLocked.Should().BeTrue();
        result.Value!.Signoffs.Should().Contain(s => s.Action == SignoffAction.DisputeConfirmed && s.Party == SignoffParty.Hr);
    }

    [Fact]
    public async Task ResolveDispute_Forbidden_WhenCallerIsNotHr()
    {
        Seed();
        await RequestSignOffAsync();
        await CreateService(EmployeeUser())
            .DisputeAsync(new SignoffActionInput(_cycleId, _reportEmpId, "Disagree.", "198.51.100.9"));

        var input = new ResolveDisputeInput(_cycleId, _reportEmpId, Amend: false, Comments: null, ClientIpAddress: null);
        var result = await CreateService(ManagerUser()).ResolveDisputeAsync(input); // manager, not HR

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("resolve_forbidden");
    }

    [Fact]
    public async Task ResolveDispute_Rejected_WhenNotDisputed()
    {
        Seed();
        await RequestSignOffAsync(); // PendingEmployeeSignOff, not Disputed

        var input = new ResolveDisputeInput(_cycleId, _reportEmpId, Amend: false, Comments: null, ClientIpAddress: null);
        var result = await CreateService(HrUser()).ResolveDisputeAsync(input);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("not_disputed");
    }

    // ── IMMUTABILITY (NFR-3/BR-5) — the most important guarantee ─────────

    [Fact]
    public async Task SaveNotes_Rejected_AfterReviewSignedOff_Locked()
    {
        Seed();
        await RequestSignOffAsync();
        await OpenNotesAsync();   // BR-2 read-before-sign precondition
        await CreateService(EmployeeUser())
            .AcknowledgeAsync(new SignoffActionInput(_cycleId, _reportEmpId, null, "198.51.100.9"));

        // A locked (signed-off) review cannot be edited — by anyone, including HR.
        var mgr = await CreateService(ManagerUser()).SaveNotesAsync(Notes(body: "<p>tamper</p>"));
        mgr.IsFailure.Should().BeTrue();
        mgr.StatusCode.Should().Be(409);
        mgr.ErrorCode.Should().Be("review_locked");

        var hr = await CreateService(HrUser()).SaveNotesAsync(Notes(body: "<p>tamper</p>"));
        hr.IsFailure.Should().BeTrue();
        hr.ErrorCode.Should().Be("review_locked");
    }

    [Fact]
    public async Task SaveNotes_Rejected_WhileDisputed_UntilHrResolves()
    {
        Seed();
        await RequestSignOffAsync();
        await CreateService(EmployeeUser())
            .DisputeAsync(new SignoffActionInput(_cycleId, _reportEmpId, "Disagree.", "198.51.100.9"));

        var result = await CreateService(ManagerUser()).SaveNotesAsync(Notes(body: "<p>change</p>"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("review_disputed");
    }

    [Fact]
    public async Task SignoffLog_IsAppendOnly_NeverMutatesOrDeletesExistingEntries()
    {
        Seed();

        // 1) Manager requests sign-off — one entry recorded.
        await RequestSignOffAsync();
        Guid managerEntryId;
        DateTime managerSignedAt;
        string? managerIp;
        using (var db = CreateDbContext())
        {
            var entries = await db.ReviewSignoffs.AsNoTracking().OrderBy(s => s.SignedAt).ToListAsync();
            entries.Should().ContainSingle();
            managerEntryId = entries[0].Id;
            managerSignedAt = entries[0].SignedAt;
            managerIp = entries[0].ClientIpAddress;
        }

        // 2) Employee disputes, then HR amends, then manager re-requests, then employee signs off.
        await CreateService(EmployeeUser())
            .DisputeAsync(new SignoffActionInput(_cycleId, _reportEmpId, "Disagree.", "198.51.100.9"));
        await CreateService(HrUser())
            .ResolveDisputeAsync(new ResolveDisputeInput(_cycleId, _reportEmpId, Amend: true, "Revising.", "203.0.113.1"));
        await RequestSignOffAsync("203.0.113.7");
        await OpenNotesAsync();   // BR-2 read-before-sign precondition
        await CreateService(EmployeeUser())
            .AcknowledgeAsync(new SignoffActionInput(_cycleId, _reportEmpId, null, "198.51.100.42"));

        using (var db = CreateDbContext())
        {
            var entries = await db.ReviewSignoffs.AsNoTracking().OrderBy(s => s.SignedAt).ToListAsync();

            // Every workflow action appended exactly one row — nothing was overwritten.
            entries.Should().HaveCount(5);
            entries.Select(e => e.Action).Should().ContainInOrder(
                SignoffAction.RequestedSignOff,
                SignoffAction.Disputed,
                SignoffAction.DisputeAmended,
                SignoffAction.RequestedSignOff,
                SignoffAction.Acknowledged);

            // The ORIGINAL manager entry is byte-for-byte unchanged (immutable: same id, timestamp, IP).
            var original = entries.Single(e => e.Id == managerEntryId);
            original.SignedAt.Should().Be(managerSignedAt);
            original.ClientIpAddress.Should().Be(managerIp);
            original.Action.Should().Be(SignoffAction.RequestedSignOff);
        }
    }

    // ── HTML sanitization on write (FR-1/§10) ────────────────────────────

    [Fact]
    public async Task SaveNotes_StripsDangerousHtml_FromAllRichTextFields()
    {
        Seed();
        var input = new SaveMeetingNotesInput(
            _cycleId, _reportEmpId,
            Body: "<p>Hi</p><script>alert('xss')</script>",
            Strengths: "<img src=x onerror=alert(1)>strong",
            DevelopmentAreas: "<a href=\"javascript:alert(1)\">click</a>",
            Summary: "<p onclick=\"steal()\">summary</p>",
            Actions: []);

        var result = await CreateService(ManagerUser()).SaveNotesAsync(input);

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);

        using var db = CreateDbContext();
        var notes = await db.ReviewMeetingNotes.AsNoTracking().FirstAsync();
        notes.Body.Should().NotContain("<script");
        notes.Body.Should().Contain("Hi");
        notes.Strengths.Should().NotContain("onerror");
        notes.DevelopmentAreas.Should().NotContain("javascript:");
        notes.Summary.Should().NotContain("onclick");
        notes.Summary.Should().Contain("summary");
    }

    // ── Export record reflects the signed, locked state (AC-4) ───────────

    [Fact]
    public async Task GetExportRecord_IncludesSignoffLog_AndLockState()
    {
        Seed();
        await RequestSignOffAsync();
        await OpenNotesAsync();   // BR-2 read-before-sign precondition
        await CreateService(EmployeeUser())
            .AcknowledgeAsync(new SignoffActionInput(_cycleId, _reportEmpId, null, "198.51.100.9"));

        var result = await CreateService(ManagerUser()).GetExportRecordAsync(_reportEmpId, _cycleId);

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        result.Value!.IsLocked.Should().BeTrue();
        result.Value!.SignoffStatus.Should().Be(ReviewSignoffStatus.SignedOff);
        result.Value!.Signoffs.Select(s => s.Action)
            .Should().ContainInOrder(SignoffAction.RequestedSignOff, SignoffAction.Acknowledged);
    }
}
