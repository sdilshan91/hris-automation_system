// ============================================================================
// ISSUE-288: Caller-scoped employee self-service review sign-off.
//
// The employee self-view carries neither its own employeeId nor a cycleId. These tests drive the new
// GetMyMeetingNotesAsync / AcknowledgeMyReviewAsync / DisputeMyReviewAsync paths through the REAL EF Core
// InMemory provider + REAL Ganss HTML sanitizer (same rig as ReviewSignoffServiceTests) and assert that each:
//   - resolves the CALLER's OWN employee (Employees.UserId == ICurrentUser.UserId) and the ACTIVE cycle
//     server-side, then reuses the existing acknowledge/dispute/read logic (immutable signature + lock).
//   - returns 403 no_employee_record when the caller is not linked to an employee row.
//   - returns 404 no_active_cycle when no cycle is Active.
//   - enforces the dispute mandatory-comments rule (422) via the reused DisputeAsync path.
// These are ADDITIVE — they do not touch the existing ReviewSignoffServiceTests.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
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

public sealed class ReviewSignoffSelfServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _reportUserId = Guid.NewGuid();
    private readonly Guid _unlinkedUserId = Guid.NewGuid(); // authenticated, but no Employee row

    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _reportEmpId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();

    public ReviewSignoffSelfServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    // A fresh service each time = a fresh scoped DbContext, exactly as a new HTTP request would get.
    private ReviewSignoffService CreateService(ICurrentUser user) =>
        CreateService(user, Substitute.For<IPerformanceNotificationService>());

    // Overload exposing the notification substitute so a test can assert the dispute→manager/HR seam fired.
    private ReviewSignoffService CreateService(ICurrentUser user, IPerformanceNotificationService notifications) => new(
        CreateDbContext(),
        _tenantContext,
        user,
        new GanssHtmlSanitizer(),
        notifications,
        Substitute.For<ILogger<ReviewSignoffService>>());

    // The reviewed employee (Read.Self); UserId matches the seeded report employee.
    private ICurrentUser EmployeeUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_reportUserId);
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ViewOwn });
        return u;
    }

    // Authenticated, but NOT linked to any Employee row.
    private ICurrentUser UnlinkedUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_unlinkedUserId);
        u.IsAuthenticated.Returns(true);
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ViewOwn });
        return u;
    }

    /// <summary>
    /// Seeds the tenant, a manager + direct report (the report is linked to <see cref="_reportUserId"/>), a cycle
    /// in <paramref name="cycleStatus"/>, and the report's manager review in <paramref name="signoffStatus"/>.
    /// </summary>
    private void Seed(
        ReviewSignoffStatus signoffStatus = ReviewSignoffStatus.PendingEmployeeSignOff,
        AppraisalCycleStatus cycleStatus = AppraisalCycleStatus.Active)
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

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = cycleStatus,
            StartDate = now.AddDays(-90), EndDate = now.AddDays(30),
            GoalSettingStart = now.AddDays(-90), GoalSettingEnd = now.AddDays(-60),
            SelfAssessmentStart = now.AddDays(-30), SelfAssessmentEnd = now.AddDays(-10),
            ManagerReviewStart = now.AddDays(-5), ManagerReviewEnd = now.AddDays(15),
            RatingScaleMax = 5, SelfWeightPercent = 30,
            SignoffAutoCloseDays = 7, IsDeleted = false,
        });

        db.ManagerReviews.Add(new ManagerReview
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _reportEmpId,
            ReviewerEmployeeId = _managerEmpId, Status = ManagerReviewStatus.Submitted,
            SubmittedAt = now.AddDays(-1),
            SignoffRequestedAt = signoffStatus == ReviewSignoffStatus.PendingEmployeeSignOff ? now.AddHours(-2) : null,
            SignoffStatus = signoffStatus, IsDeleted = false,
        });

        db.SaveChanges();
    }

    // ── Read: caller resolves their OWN employee + active cycle ──────────

    [Fact]
    public async Task GetMyMeetingNotes_ResolvesCallersOwnReview_ForActiveCycle()
    {
        Seed();

        var result = await CreateService(EmployeeUser()).GetMyMeetingNotesAsync();

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        result.Value!.EmployeeId.Should().Be(_reportEmpId);   // resolved FROM the caller
        result.Value!.CycleId.Should().Be(_cycleId);          // resolved FROM the active cycle
        result.Value!.SignoffStatus.Should().Be(ReviewSignoffStatus.PendingEmployeeSignOff);
    }

    // ── Acknowledge: caller signs their own review → SignedOff + locked ──

    [Fact]
    public async Task AcknowledgeMy_ResolvesCaller_SignsOff_AndLocks()
    {
        Seed();
        await CreateService(EmployeeUser()).GetMyMeetingNotesAsync();   // BR-2: open notes before signing
        var before = DateTime.UtcNow;

        var result = await CreateService(EmployeeUser()).AcknowledgeMyReviewAsync("198.51.100.9");

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        var dto = result.Value!;
        dto.EmployeeId.Should().Be(_reportEmpId);
        dto.CycleId.Should().Be(_cycleId);
        dto.SignoffStatus.Should().Be(ReviewSignoffStatus.SignedOff);
        dto.IsLocked.Should().BeTrue();
        dto.SignoffCompletedAt.Should().NotBeNull();

        var ack = dto.Signoffs.Single(s => s.Action == SignoffAction.Acknowledged);
        ack.Party.Should().Be(SignoffParty.Employee);
        ack.SignerName.Should().Be("Ada Lovelace");      // immutable signature name (FR-3)
        ack.ClientIpAddress.Should().Be("198.51.100.9");  // immutable IP (FR-7)
        ack.SignedAt.Should().BeOnOrAfter(before);

        // Persisted lock survives a fresh context.
        using var db = CreateDbContext();
        var stored = await db.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == _reportEmpId);
        stored.IsLocked.Should().BeTrue();
        stored.SignoffStatus.Should().Be(ReviewSignoffStatus.SignedOff);
    }

    // ── BR-2 read-before-sign (BUG-065) ──────────────────────────────────

    [Fact]
    public async Task GetMyMeetingNotes_StampsNotesOpenedAt_WhenPendingSignoff()
    {
        Seed();

        var result = await CreateService(EmployeeUser()).GetMyMeetingNotesAsync();

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        result.Value!.NotesOpenedAt.Should().NotBeNull();   // stamped on first open

        // Persisted — survives a fresh context.
        using var db = CreateDbContext();
        var stored = await db.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == _reportEmpId);
        stored.NotesOpenedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AcknowledgeMy_Rejected_WhenNotesNeverOpened()
    {
        Seed();   // pending sign-off, notes never opened

        var result = await CreateService(EmployeeUser()).AcknowledgeMyReviewAsync("198.51.100.9");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("notes_not_read");

        // The review is untouched — still pending, not locked.
        using var db = CreateDbContext();
        var stored = await db.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == _reportEmpId);
        stored.SignoffStatus.Should().Be(ReviewSignoffStatus.PendingEmployeeSignOff);
        stored.IsLocked.Should().BeFalse();
    }

    // ── Dispute: caller disputes their own review with comments ──────────

    [Fact]
    public async Task DisputeMy_ResolvesCaller_WithComments_SetsDisputed()
    {
        Seed();
        var notifications = Substitute.For<IPerformanceNotificationService>();

        var result = await CreateService(EmployeeUser(), notifications)
            .DisputeMyReviewAsync("I disagree with goal 2's rating.", "198.51.100.9");

        result.IsSuccess.Should().BeTrue(result.ErrorCode + ": " + result.Error);
        result.Value!.EmployeeId.Should().Be(_reportEmpId);
        result.Value!.SignoffStatus.Should().Be(ReviewSignoffStatus.Disputed);
        result.Value!.IsLocked.Should().BeFalse();
        var entry = result.Value!.Signoffs.Single(s => s.Action == SignoffAction.Disputed);
        entry.Party.Should().Be(SignoffParty.Employee);
        entry.Comments.Should().Be("I disagree with goal 2's rating.");
        entry.ClientIpAddress.Should().Be("198.51.100.9");

        // FR-5: disputing notifies the manager + HR. Verify the seam actually fired for the resolved
        // (own) employee + active cycle — not just that the DTO flipped to Disputed.
        await notifications.Received(1).NotifyReviewDisputedAsync(
            Arg.Any<Guid>(), _reportEmpId, _cycleId, Arg.Any<CancellationToken>());
    }

    // ── Dispute requires comments (422) via the reused DisputeAsync path ─

    [Fact]
    public async Task DisputeMy_Rejected_WhenCommentsMissing()
    {
        Seed();

        var result = await CreateService(EmployeeUser()).DisputeMyReviewAsync("   ", "198.51.100.9");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("dispute_comments_required");
    }

    // ── 403 when the caller is not linked to an employee record ──────────

    [Fact]
    public async Task SelfPath_403_WhenCallerHasNoEmployeeRecord()
    {
        Seed();
        var user = UnlinkedUser();

        var notes = await CreateService(user).GetMyMeetingNotesAsync();
        notes.IsFailure.Should().BeTrue();
        notes.StatusCode.Should().Be(403);
        notes.ErrorCode.Should().Be("no_employee_record");

        var ack = await CreateService(user).AcknowledgeMyReviewAsync("198.51.100.9");
        ack.IsFailure.Should().BeTrue();
        ack.StatusCode.Should().Be(403);
        ack.ErrorCode.Should().Be("no_employee_record");

        var dispute = await CreateService(user).DisputeMyReviewAsync("x", "198.51.100.9");
        dispute.IsFailure.Should().BeTrue();
        dispute.StatusCode.Should().Be(403);
        dispute.ErrorCode.Should().Be("no_employee_record");
    }

    // ── 404 when no cycle is active ──────────────────────────────────────

    [Fact]
    public async Task SelfPath_404_WhenNoActiveCycle()
    {
        Seed(cycleStatus: AppraisalCycleStatus.Draft); // employee linked, but no ACTIVE cycle
        var user = EmployeeUser();

        var notes = await CreateService(user).GetMyMeetingNotesAsync();
        notes.IsFailure.Should().BeTrue();
        notes.StatusCode.Should().Be(404);
        notes.ErrorCode.Should().Be("no_active_cycle");

        var ack = await CreateService(user).AcknowledgeMyReviewAsync("198.51.100.9");
        ack.IsFailure.Should().BeTrue();
        ack.StatusCode.Should().Be(404);
        ack.ErrorCode.Should().Be("no_active_cycle");

        var dispute = await CreateService(user).DisputeMyReviewAsync("x", "198.51.100.9");
        dispute.IsFailure.Should().BeTrue();
        dispute.StatusCode.Should().Be(404);
        dispute.ErrorCode.Should().Be("no_active_cycle");
    }
}
