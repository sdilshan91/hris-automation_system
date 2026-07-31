// ============================================================================
// US-PRF-010: Performance-based recommendations — integration tests.
//
// Exercises RecommendationService over a real AppDbContext (InMemory) with the ITenantContext-driven global query
// filter, covering the cross-cutting concerns a service unit test cannot:
//   - NFR-2 tenant isolation: a recommendation created in Tenant A is invisible & inaccessible from Tenant B (a
//     cross-tenant Get returns not-found; HR in Tenant B sees an empty workspace for the same cycle id).
//   - FR-7/BR-7 history retention: recommendations persist across fresh scoped contexts and across cycles.
//   - BR-6 downstream integration: on final approval the integration seam is invoked and the immutable
//     IntegrationRaised event is persisted (asserted across a fresh context).
//
// PROVIDER: InMemory — same rationale as the other Performance integration tests (the verify gate runs
// `dotnet test` with no PostgreSQL / Docker). Each Service(...) / Db(...) builds a FRESH scoped DbContext over the
// same shared InMemory database, exactly as separate HTTP requests would get.
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

public sealed class RecommendationIntegrationTests
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

    private RecommendationService Service(
        Guid tenantId, Guid userId, IRecommendationIntegrationService integration, params string[] permissions)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        user.Email.Returns("user@t.com");
        user.Permissions.Returns(permissions);

        var auditLogger = new PayrollAuditLogger(db, ctx, user, NullLogger<PayrollAuditLogger>.Instance);
        return new RecommendationService(db, ctx, user, integration, auditLogger, NullLogger<RecommendationService>.Instance);
    }

    private sealed record Seeded(Guid HrUserId, Guid EmployeeEmpId, Guid ApproverUserId, Guid ApproverEmpId, Guid CycleId);

    private async Task<Seeded> SeedAsync(Guid tenantId)
    {
        using var db = Db(tenantId);
        var hrUserId = Guid.NewGuid();
        var employeeEmpId = Guid.NewGuid();
        var approverUserId = Guid.NewGuid();
        var approverEmpId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Eng", Code = "ENG" });
        db.Employees.Add(new Employee { Id = employeeEmpId, TenantId = tenantId, UserId = Guid.NewGuid(), EmployeeNo = "EMP", FirstName = "Ada", LastName = "Lovelace", Email = "a@t.com", Status = EmployeeStatus.Active, DepartmentId = deptId, DateOfJoining = new DateTime(2021, 1, 1) });
        db.Employees.Add(new Employee { Id = approverEmpId, TenantId = tenantId, UserId = approverUserId, EmployeeNo = "APP", FirstName = "Vint", LastName = "Cerf", Email = "v@t.com", Status = EmployeeStatus.Active, DepartmentId = deptId, DateOfJoining = new DateTime(2010, 1, 1) });

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = cycleId, TenantId = tenantId, Name = "FY2026", Status = AppraisalCycleStatus.Completed,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), RatingScaleMax = 5,
        });
        db.ManagerReviews.Add(new ManagerReview
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CycleId = cycleId, EmployeeId = employeeEmpId,
            Status = ManagerReviewStatus.Submitted, FinalScore = 4.6m, Flag = ReviewFlag.Promotion,
            SubmittedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        await db.SaveChangesAsync();
        return new Seeded(hrUserId, employeeEmpId, approverUserId, approverEmpId, cycleId);
    }

    private static SaveRecommendationInput BonusInput(Guid empId, Guid cycleId, decimal amount) =>
        new(empId, cycleId, RecommendationType.Bonus,
            new RecommendationDetailsInput(null, null, null, amount, null, null, null, null, null, null), null, null);

    // ── NFR-2 tenant isolation ──────────────────────────────────────────

    [Fact]
    public async Task Recommendation_in_tenant_A_is_invisible_from_tenant_B()
    {
        var noop = Substitute.For<IRecommendationIntegrationService>();
        var a = await SeedAsync(_tenantA);
        var created = await Service(_tenantA, a.HrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .SaveAsync(BonusInput(a.EmployeeEmpId, a.CycleId, 5000m));
        created.IsSuccess.Should().BeTrue();

        // Tenant B HR cannot read the Tenant A recommendation by id.
        var bHrUserId = Guid.NewGuid();
        var crossRead = await Service(_tenantB, bHrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .GetAsync(created.Value!.Id);
        crossRead.IsFailure.Should().BeTrue();
        crossRead.StatusCode.Should().Be(404);

        // Tenant B has its own seeded data; its workspace for the Tenant A cycle id is empty / no-cycle.
        var bWorkspace = await Service(_tenantB, bHrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .GetWorkspaceAsync(new RecommendationWorkspaceQueryInput(a.CycleId, 1, 100));
        bWorkspace.IsFailure.Should().BeTrue();
        bWorkspace.ErrorCode.Should().Be("no_cycle");
    }

    // ── FR-7/BR-7 history retention across contexts ─────────────────────

    [Fact]
    public async Task Recommendation_history_is_persisted_across_fresh_contexts()
    {
        var noop = Substitute.For<IRecommendationIntegrationService>();
        var a = await SeedAsync(_tenantA);
        var rec = (await Service(_tenantA, a.HrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .SaveAsync(BonusInput(a.EmployeeEmpId, a.CycleId, 5000m))).Value!;

        // Read back in a fresh scoped context.
        var reread = await Service(_tenantA, a.HrUserId, noop, PermissionCatalog.Performance.PublishAll).GetAsync(rec.Id);
        reread.IsSuccess.Should().BeTrue();
        reread.Value!.BonusAmount.Should().Be(5000m);
        reread.Value.Events.Should().Contain(e => e.EventType == RecommendationEventType.Created);
    }

    // ── BR-6 downstream integration seam on approval ────────────────────

    [Fact]
    public async Task Final_approval_raises_the_downstream_integration_seam_and_persists_the_event()
    {
        var integration = Substitute.For<IRecommendationIntegrationService>();
        var a = await SeedAsync(_tenantA);

        var rec = (await Service(_tenantA, a.HrUserId, integration, PermissionCatalog.Performance.PublishAll)
            .SaveAsync(BonusInput(a.EmployeeEmpId, a.CycleId, 5000m))).Value!;
        await Service(_tenantA, a.HrUserId, integration, PermissionCatalog.Performance.PublishAll)
            .SubmitAsync(new SubmitRecommendationInput(rec.Id, new[] { a.ApproverEmpId }, null));

        var approved = await Service(_tenantA, a.ApproverUserId, integration, PermissionCatalog.Performance.ReviewTeam)
            .DecideAsync(new DecideRecommendationInput(rec.Id, true, null, null));
        approved.Value!.Status.Should().Be(RecommendationStatus.Approved);

        // The integration seam was invoked with the payroll target (a Bonus flows to Payroll).
        await integration.Received().RaiseAsync(
            rec.Id, a.EmployeeEmpId, a.CycleId, RecommendationType.Bonus, "payroll", Arg.Any<CancellationToken>());

        // The immutable IntegrationRaised event survives in a fresh context.
        using var db = Db(_tenantA);
        var events = await db.RecommendationEvents.AsNoTracking()
            .Where(e => e.RecommendationId == rec.Id).ToListAsync();
        events.Should().Contain(e => e.EventType == RecommendationEventType.IntegrationRaised);
    }

    // ── PDF export (FR-6, deferred-PDF work item) ───────────────────────

    [Fact]
    public async Task Export_summary_pdf_returns_a_pdf_document()
    {
        var a = await SeedAsync(_tenantA);
        var noop = Substitute.For<IRecommendationIntegrationService>();

        var export = await Service(_tenantA, a.HrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .ExportSummaryAsync(a.CycleId, "pdf");

        export.IsSuccess.Should().BeTrue(export.Error);
        export.Value!.ContentType.Should().Be("application/pdf");
        export.Value!.FileName.Should().EndWith(".pdf");
        export.Value!.FileContent.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(export.Value!.FileContent, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Export_summary_csv_and_xlsx_still_work_unchanged()
    {
        var a = await SeedAsync(_tenantA);
        var noop = Substitute.For<IRecommendationIntegrationService>();

        var csv = await Service(_tenantA, a.HrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .ExportSummaryAsync(a.CycleId, "csv");
        csv.IsSuccess.Should().BeTrue(csv.Error);
        csv.Value!.ContentType.Should().Be("text/csv");

        var xlsx = await Service(_tenantA, a.HrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .ExportSummaryAsync(a.CycleId, "xlsx");
        xlsx.IsSuccess.Should().BeTrue(xlsx.Error);
        xlsx.Value!.FileName.Should().EndWith(".xlsx");
    }

    [Fact]
    public async Task Export_summary_pdf_refuses_a_non_hr_caller()
    {
        var a = await SeedAsync(_tenantA);
        var noop = Substitute.For<IRecommendationIntegrationService>();

        // No PublishAll → not HR → refused. The PDF path is not less protected than the CSV/XLSX it renders.
        var forbidden = await Service(_tenantA, a.HrUserId, noop, PermissionCatalog.Performance.ReadSelf)
            .ExportSummaryAsync(a.CycleId, "pdf");

        forbidden.IsFailure.Should().BeTrue();
        forbidden.StatusCode.Should().Be(403);
    }
    // ── ISSUE-351: the irrecoverable recommendation ───────────────────────────
    // A Draft recommendation for an employee whose ManagerReview was never submitted, on a cycle that has since
    // reached a TERMINAL status, can never be progressed: submit and reopen both need an open manager-review
    // window, IsPhaseOpen needs Status == Active, and Completed has no outbound edge in IsValidTransition.
    // Auto-generate never creates this state (it filters FinalScore != null); the manual path could.

    [Fact]
    public async Task Save_is_refused_on_a_TERMINAL_cycle_when_the_review_was_never_submitted_ISSUE351()
    {
        var noop = Substitute.For<IRecommendationIntegrationService>();
        var tenantId = Guid.NewGuid();
        var a = await SeedAsync(tenantId);

        // A second employee on the SAME completed cycle, with NO manager review at all — the dead-end shape.
        var strandedEmpId = Guid.NewGuid();
        await using (var db = Db(tenantId))
        {
            db.Employees.Add(new Employee
            {
                Id = strandedEmpId, TenantId = tenantId, UserId = Guid.NewGuid(), EmployeeNo = "EMP2",
                FirstName = "Grace", LastName = "Hopper", Email = "g@t.com", Status = EmployeeStatus.Active,
                DepartmentId = db.Employees.First(e => e.Id == a.EmployeeEmpId).DepartmentId,
                DateOfJoining = new DateTime(2021, 1, 1),
            });
            await db.SaveChangesAsync();
        }

        var result = await Service(tenantId, a.HrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .SaveAsync(BonusInput(strandedEmpId, a.CycleId, 5000m));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("cycle_terminal_review_unsubmitted",
            "creating this row would strand the employee permanently — refusing at creation is the whole fix");
    }

    // THE discriminating arm: the guard must NOT restrict legitimate early drafting. While the cycle is still
    // ACTIVE the review can still be submitted, so an unsubmitted review is not a dead end and HR preparing a
    // recommendation in advance must keep working. Without this arm the fix could over-restrict and nothing
    // would notice.
    [Fact]
    public async Task Save_is_STILL_ALLOWED_on_an_ACTIVE_cycle_when_the_review_is_unsubmitted_ISSUE351()
    {
        var noop = Substitute.For<IRecommendationIntegrationService>();
        var tenantId = Guid.NewGuid();
        var a = await SeedAsync(tenantId);

        var earlyEmpId = Guid.NewGuid();
        await using (var db = Db(tenantId))
        {
            db.Employees.Add(new Employee
            {
                Id = earlyEmpId, TenantId = tenantId, UserId = Guid.NewGuid(), EmployeeNo = "EMP3",
                FirstName = "Alan", LastName = "Turing", Email = "t@t.com", Status = EmployeeStatus.Active,
                DepartmentId = db.Employees.First(e => e.Id == a.EmployeeEmpId).DepartmentId,
                DateOfJoining = new DateTime(2021, 1, 1),
            });
            // Reopen the cycle: Active means the review can still land, so this is preparation, not a dead end.
            db.AppraisalCycles.First(c => c.Id == a.CycleId).Status = AppraisalCycleStatus.Active;
            await db.SaveChangesAsync();
        }

        var result = await Service(tenantId, a.HrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .SaveAsync(BonusInput(earlyEmpId, a.CycleId, 5000m));

        result.IsSuccess.Should().BeTrue(
            "drafting ahead of a submission is legitimate while the cycle is open; the guard must fire ONLY on "
            + $"the already-irrecoverable combination. Error was: {result.Error}");
    }

    [Fact]
    public async Task Save_remains_allowed_on_a_terminal_cycle_when_the_review_WAS_submitted_ISSUE351()
    {
        var noop = Substitute.For<IRecommendationIntegrationService>();
        var tenantId = Guid.NewGuid();
        var a = await SeedAsync(tenantId); // seeded employee HAS a submitted review on a Completed cycle

        var result = await Service(tenantId, a.HrUserId, noop, PermissionCatalog.Performance.PublishAll)
            .SaveAsync(BonusInput(a.EmployeeEmpId, a.CycleId, 5000m));

        result.IsSuccess.Should().BeTrue(
            "the normal post-cycle recommendation flow must be untouched — this is the path the feature exists for");
    }

}
