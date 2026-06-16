// ============================================================================
// US-PRF-006: Performance-review meeting notes + immutable digital sign-off — integration tests.
//
// Exercises ReviewSignoffService + ReviewSignoffAutoCloseService over a real AppDbContext (InMemory) with the
// ITenantContext-driven global query filter and the REAL Ganss HTML sanitizer, covering the cross-cutting
// concerns a service unit test cannot:
//   - End-to-end sign-off: manager request → employee acknowledge → SignedOff + LOCKED, persisted.
//   - NFR-2 tenant isolation: meeting notes / sign-offs in Tenant A are invisible & inaccessible from Tenant B;
//     a cross-tenant read returns nothing and a cross-tenant sign-off attempt is refused (never crosses tenants).
//   - NFR-3/BR-5 IMMUTABILITY: once a review is SignedOff/locked, no service path edits the notes; the
//     append-only sign-off log keeps the original signature row byte-for-byte across fresh scoped contexts.
//   - BR-3 auto-close: a review unsigned past the cycle's configurable window transitions to NoResponse with an
//     immutable system AutoClosedNoResponse entry; one NOT yet past the window is left untouched; the sweep
//     never crosses tenants.
//
// PROVIDER: InMemory — same rationale as the other Performance integration tests here (the verify gate runs
// `dotnet test` with no PostgreSQL / Docker). Each `Service(...)` / `Db(...)` builds a FRESH scoped DbContext
// over the same shared InMemory database, exactly as separate HTTP requests would get.
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class ReviewSignoffIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private ReviewSignoffService Service(Guid tenantId, Guid userId, params string[] permissions)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        user.Permissions.Returns(permissions);

        return new ReviewSignoffService(db, ctx, user,
            new GanssHtmlSanitizer(),
            Substitute.For<IPerformanceNotificationService>(),
            NullLogger<ReviewSignoffService>.Instance);
    }

    private ReviewSignoffAutoCloseService AutoCloseService(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);
        return new ReviewSignoffAutoCloseService(db, ctx,
            Substitute.For<IPerformanceNotificationService>(),
            NullLogger<ReviewSignoffAutoCloseService>.Instance);
    }

    private sealed record Seeded(Guid ManagerUserId, Guid ManagerEmpId, Guid ReportUserId, Guid ReportEmpId, Guid CycleId);

    /// <summary>
    /// Seeds a manager + direct report (the report linked to a user id), an active cycle with the given
    /// auto-close window, and a SUBMITTED manager review for the report (NotStarted sign-off).
    /// </summary>
    private async Task<Seeded> SeedAsync(Guid tenantId, int autoCloseDays = 7)
    {
        using var db = Db(tenantId);
        var now = DateTime.UtcNow;

        var managerUserId = Guid.NewGuid();
        var managerEmpId = Guid.NewGuid();
        var reportUserId = Guid.NewGuid();
        var reportEmpId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();

        db.Employees.Add(new Employee
        {
            Id = managerEmpId, TenantId = tenantId, UserId = managerUserId, EmployeeNo = "MGR",
            FirstName = "Grace", LastName = "Hopper", Email = "grace@t.com", Status = EmployeeStatus.Active,
        });
        db.Employees.Add(new Employee
        {
            Id = reportEmpId, TenantId = tenantId, UserId = reportUserId, EmployeeNo = "RPT",
            FirstName = "Ada", LastName = "Lovelace", Email = "ada@t.com", Status = EmployeeStatus.Active,
            ReportsToEmployeeId = managerEmpId,
        });

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = cycleId, TenantId = tenantId, Name = "FY2026", Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-90), GoalSettingEnd = now.AddDays(-60),
            SelfAssessmentStart = now.AddDays(-30), SelfAssessmentEnd = now.AddDays(-10),
            ManagerReviewStart = now.AddDays(-5), ManagerReviewEnd = now.AddDays(15),
            RatingScaleMax = 5, SelfWeightPercent = 30, SignoffAutoCloseDays = autoCloseDays,
        });

        db.ManagerReviews.Add(new ManagerReview
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CycleId = cycleId, EmployeeId = reportEmpId,
            ReviewerEmployeeId = managerEmpId, Status = ManagerReviewStatus.Submitted,
            SubmittedAt = now.AddDays(-1), SignoffStatus = ReviewSignoffStatus.NotStarted,
        });

        await db.SaveChangesAsync();
        return new Seeded(managerUserId, managerEmpId, reportUserId, reportEmpId, cycleId);
    }

    private SaveMeetingNotesInput Notes(Guid cycleId, Guid empId)
        => new(cycleId, empId, "<p>Discussed.</p>", "<p>Strong</p>", "<p>Grow</p>", "<p>Good</p>", Actions: []);

    // ── End-to-end sign-off, persisted ──────────────────────────────────

    [Fact]
    public async Task EndToEnd_ManagerRequest_EmployeeAcknowledge_LocksReview()
    {
        var s = await SeedAsync(_tenantA);

        var request = await Service(_tenantA, s.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .RequestSignOffAsync(Notes(s.CycleId, s.ReportEmpId), "203.0.113.5");
        request.IsSuccess.Should().BeTrue(request.ErrorCode + ": " + request.Error);
        request.Value!.SignoffStatus.Should().Be(ReviewSignoffStatus.PendingEmployeeSignOff);

        var ack = await Service(_tenantA, s.ReportUserId, PermissionCatalog.Performance.ViewOwn)
            .AcknowledgeAsync(new SignoffActionInput(s.CycleId, s.ReportEmpId, null, "198.51.100.9"));
        ack.IsSuccess.Should().BeTrue(ack.ErrorCode + ": " + ack.Error);

        using var db = Db(_tenantA);
        var stored = await db.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == s.ReportEmpId);
        stored.SignoffStatus.Should().Be(ReviewSignoffStatus.SignedOff);
        stored.IsLocked.Should().BeTrue();
        stored.SignoffCompletedAt.Should().NotBeNull();

        var entries = await db.ReviewSignoffs.AsNoTracking().OrderBy(e => e.SignedAt).ToListAsync();
        entries.Select(e => e.Action).Should().ContainInOrder(
            SignoffAction.RequestedSignOff, SignoffAction.Acknowledged);
        entries.Single(e => e.Action == SignoffAction.Acknowledged).SignerName.Should().Be("Ada Lovelace");
    }

    // ── NFR-2: tenant isolation ─────────────────────────────────────────

    [Fact]
    public async Task SignoffsAndNotes_NotVisible_AcrossTenants()
    {
        var a = await SeedAsync(_tenantA);
        await SeedAsync(_tenantB);

        // Tenant A drives a full sign-off.
        await Service(_tenantA, a.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .RequestSignOffAsync(Notes(a.CycleId, a.ReportEmpId), "203.0.113.5");
        await Service(_tenantA, a.ReportUserId, PermissionCatalog.Performance.ViewOwn)
            .AcknowledgeAsync(new SignoffActionInput(a.CycleId, a.ReportEmpId, null, "198.51.100.9"));

        // Tenant B sees no meeting notes and no sign-off rows at all.
        using var dbB = Db(_tenantB);
        (await dbB.ReviewMeetingNotes.AsNoTracking().CountAsync()).Should().Be(0);
        (await dbB.ReviewSignoffs.AsNoTracking().CountAsync()).Should().Be(0);

        // Tenant A sees exactly its own rows.
        using var dbA = Db(_tenantA);
        (await dbA.ReviewMeetingNotes.AsNoTracking().CountAsync()).Should().Be(1);
        (await dbA.ReviewSignoffs.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ManagerInTenantA_CannotReadNotes_OfTenantBEmployee()
    {
        var a = await SeedAsync(_tenantA);
        var b = await SeedAsync(_tenantB);

        // Tenant A's manager tries to read Tenant B's review notes. The global filter hides B's employee, so
        // the lookup fails (employee_not_found / forbidden) — never a cross-tenant read.
        var result = await Service(_tenantA, a.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .GetNotesAsync(b.ReportEmpId, b.CycleId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().BeOneOf(403, 404);
    }

    [Fact]
    public async Task EmployeeInTenantB_CannotSignOff_TenantAReview()
    {
        var a = await SeedAsync(_tenantA);
        await SeedAsync(_tenantB);

        await Service(_tenantA, a.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .RequestSignOffAsync(Notes(a.CycleId, a.ReportEmpId), "203.0.113.5");

        // A Tenant B user (random id) attempts to acknowledge Tenant A's review. The filtered review lookup
        // returns nothing under Tenant B's context → review_not_found. The sign-off never crosses tenants.
        var result = await Service(_tenantB, Guid.NewGuid(), PermissionCatalog.Performance.ViewOwn)
            .AcknowledgeAsync(new SignoffActionInput(a.CycleId, a.ReportEmpId, null, "198.51.100.9"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().BeOneOf(403, 404);

        // Tenant A's review is untouched.
        using var dbA = Db(_tenantA);
        (await dbA.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == a.ReportEmpId))
            .SignoffStatus.Should().Be(ReviewSignoffStatus.PendingEmployeeSignOff);
    }

    // ── NFR-3/BR-5: immutability across fresh scoped contexts ───────────

    [Fact]
    public async Task SignedOffReview_RejectsAllNotesEdits_AndKeepsSignatureImmutable()
    {
        var s = await SeedAsync(_tenantA);

        await Service(_tenantA, s.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .RequestSignOffAsync(Notes(s.CycleId, s.ReportEmpId), "203.0.113.5");
        await Service(_tenantA, s.ReportUserId, PermissionCatalog.Performance.ViewOwn)
            .AcknowledgeAsync(new SignoffActionInput(s.CycleId, s.ReportEmpId, null, "198.51.100.9"));

        // Capture the immutable acknowledgement entry.
        Guid ackId;
        DateTime ackAt;
        using (var db = Db(_tenantA))
        {
            var ack = await db.ReviewSignoffs.AsNoTracking()
                .FirstAsync(e => e.Action == SignoffAction.Acknowledged);
            ackId = ack.Id;
            ackAt = ack.SignedAt;
            ack.ClientIpAddress.Should().Be("198.51.100.9");
        }

        // Neither the manager nor HR can edit a locked review.
        (await Service(_tenantA, s.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .SaveNotesAsync(Notes(s.CycleId, s.ReportEmpId))).ErrorCode.Should().Be("review_locked");
        (await Service(_tenantA, Guid.NewGuid(), PermissionCatalog.Performance.ReviewAll)
            .SaveNotesAsync(Notes(s.CycleId, s.ReportEmpId))).ErrorCode.Should().Be("review_locked");

        // The recorded signature row is byte-for-byte unchanged and still the only acknowledgement.
        using (var db = Db(_tenantA))
        {
            var acks = await db.ReviewSignoffs.AsNoTracking()
                .Where(e => e.Action == SignoffAction.Acknowledged).ToListAsync();
            acks.Should().ContainSingle();
            acks[0].Id.Should().Be(ackId);
            acks[0].SignedAt.Should().Be(ackAt);
            acks[0].ClientIpAddress.Should().Be("198.51.100.9");
        }
    }

    // ── BR-3: auto-close overdue unsigned reviews ───────────────────────

    [Fact]
    public async Task AutoClose_TransitionsOverdueReview_ToNoResponse_WithSystemEntry()
    {
        var s = await SeedAsync(_tenantA, autoCloseDays: 7);

        // Request sign-off, then back-date the request to 8 days ago (past the 7-day window).
        await Service(_tenantA, s.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .RequestSignOffAsync(Notes(s.CycleId, s.ReportEmpId), "203.0.113.5");
        var requestedAt = DateTime.UtcNow.AddDays(-8);
        using (var db = Db(_tenantA))
        {
            var review = await db.ManagerReviews.FirstAsync(r => r.EmployeeId == s.ReportEmpId);
            review.SignoffRequestedAt = requestedAt;
            await db.SaveChangesAsync();
        }

        var closed = await AutoCloseService(_tenantA).AutoCloseOverdueAsync(DateTime.UtcNow);

        closed.Should().Be(1);
        using var check = Db(_tenantA);
        var stored = await check.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == s.ReportEmpId);
        stored.SignoffStatus.Should().Be(ReviewSignoffStatus.NoResponse);
        stored.SignoffCompletedAt.Should().NotBeNull();

        var system = await check.ReviewSignoffs.AsNoTracking()
            .SingleAsync(e => e.Action == SignoffAction.AutoClosedNoResponse);
        system.Party.Should().Be(SignoffParty.Hr);
        system.SignerName.Should().Be("System");
        system.SignerUserId.Should().BeNull();
        system.ClientIpAddress.Should().BeNull();
    }

    [Fact]
    public async Task AutoClose_LeavesReview_StillWithinWindow_Untouched()
    {
        var s = await SeedAsync(_tenantA, autoCloseDays: 7);

        // Requested just now — well within the 7-day window.
        await Service(_tenantA, s.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
            .RequestSignOffAsync(Notes(s.CycleId, s.ReportEmpId), "203.0.113.5");

        var closed = await AutoCloseService(_tenantA).AutoCloseOverdueAsync(DateTime.UtcNow);

        closed.Should().Be(0);
        using var check = Db(_tenantA);
        (await check.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == s.ReportEmpId))
            .SignoffStatus.Should().Be(ReviewSignoffStatus.PendingEmployeeSignOff);
        (await check.ReviewSignoffs.AsNoTracking().AnyAsync(e => e.Action == SignoffAction.AutoClosedNoResponse))
            .Should().BeFalse();
    }

    [Fact]
    public async Task AutoClose_NeverCrossesTenants()
    {
        var a = await SeedAsync(_tenantA, autoCloseDays: 7);
        var b = await SeedAsync(_tenantB, autoCloseDays: 7);

        // Both tenants have an overdue pending review.
        foreach (var (tenant, seed) in new[] { (_tenantA, a), (_tenantB, b) })
        {
            await Service(tenant, seed.ManagerUserId, PermissionCatalog.Performance.ReviewTeam)
                .RequestSignOffAsync(Notes(seed.CycleId, seed.ReportEmpId), "203.0.113.5");
            using var db = Db(tenant);
            var review = await db.ManagerReviews.FirstAsync(r => r.EmployeeId == seed.ReportEmpId);
            review.SignoffRequestedAt = DateTime.UtcNow.AddDays(-8);
            await db.SaveChangesAsync();
        }

        // Sweeping Tenant A closes ONLY Tenant A's review.
        var closed = await AutoCloseService(_tenantA).AutoCloseOverdueAsync(DateTime.UtcNow);
        closed.Should().Be(1);

        using var checkA = Db(_tenantA);
        (await checkA.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == a.ReportEmpId))
            .SignoffStatus.Should().Be(ReviewSignoffStatus.NoResponse);

        using var checkB = Db(_tenantB);
        (await checkB.ManagerReviews.AsNoTracking().FirstAsync(r => r.EmployeeId == b.ReportEmpId))
            .SignoffStatus.Should().Be(ReviewSignoffStatus.PendingEmployeeSignOff, "Tenant B was not swept");
    }
}
