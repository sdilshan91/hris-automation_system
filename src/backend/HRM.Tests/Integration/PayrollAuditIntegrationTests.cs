// ============================================================================
// US-PAY-012: Payroll audit trail — integration tests.
//
// Exercises the full write -> audit -> read path through a real DI container (MediatR pipeline +
// ITenantContext-driven global query filters + the structured PayrollAuditLogger), covering:
//   - AC-3: updating a salary component via SalaryComponentService produces a SalaryComponent.Updated
//     AuditLog row with non-null before/after JSON, and the changed field appears in the diff.
//   - AC-4: the audit-trail query (PayrollAuditService) filters by action / resource type / resource id /
//     date range, and is restricted to the payroll resource catalog.
//   - AC-2: the per-run audit timeline returns that run's entries in chronological order (oldest first).
//   - AC-5: audit entries are tenant-scoped — a Tenant B context cannot see Tenant A's audit rows.
//   - NFR-4/BR-2 (immutability): the audit service surface is read+export only — no update/delete path.
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the other Payroll integration
// tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker bound).
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Application.Features.Payroll.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class PayrollAuditIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; }
        public string Email => "hr@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    private IServiceProvider Provider(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId, UserId = _actor });
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<ISalaryComponentService, SalaryComponentService>();
        services.AddScoped<IPayrollAuditService, PayrollAuditService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateSalaryComponentCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private IMediator Pipeline(Guid tenantId) => Provider(tenantId).GetRequiredService<IMediator>();

    private AppDbContext Db(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new MutableTenantContext { TenantId = tenantId });
    }

    private static CreateSalaryComponentCommand Earning(string code = "BASIC", decimal value = 1000m)
        => new(code, code, SalaryComponentType.Earning, CalculationMethod.Fixed, value, null, true, false, true, 1);

    private static UpdateSalaryComponentCommand UpdateEarning(Guid id, string code, string name, decimal value)
        => new(id, name, code, SalaryComponentType.Earning, CalculationMethod.Fixed, value, null, true, false, true, 1);

    // ── AC-3: a component update emits an audited before/after ─────────────────────

    [Fact]
    public async Task UpdateSalaryComponent_WritesUpdatedAuditEntry_WithBeforeAfter()
    {
        var mediator = Pipeline(_tenantA);
        var created = await mediator.Send(Earning("BASIC", 1000m));
        created.IsSuccess.Should().BeTrue();
        var id = created.Value!.Id;

        var updated = await mediator.Send(UpdateEarning(id, "BASIC", "Basic Pay", 1250m));
        updated.IsSuccess.Should().BeTrue();

        using var db = Db(_tenantA);
        var entry = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == PayrollAuditAction.SalaryComponentUpdated
                              && a.ResourceId == id.ToString());

        entry.ResourceType.Should().Be(PayrollAuditAction.ResourceType.SalaryComponent);
        entry.TenantId.Should().Be(_tenantA);
        entry.Before.Should().NotBeNullOrWhiteSpace();
        entry.After.Should().NotBeNullOrWhiteSpace();

        // The changed fields show in the before/after diff (name + value).
        var before = JsonSerializer.Deserialize<JsonElement>(entry.Before!);
        var after = JsonSerializer.Deserialize<JsonElement>(entry.After!);
        before.GetProperty("Name").GetString().Should().Be("BASIC");
        after.GetProperty("Name").GetString().Should().Be("Basic Pay");
        before.GetProperty("DefaultValue").GetDecimal().Should().Be(1000m);
        after.GetProperty("DefaultValue").GetDecimal().Should().Be(1250m);
    }

    [Fact]
    public async Task CreateSalaryComponent_WritesCreatedAuditEntry()
    {
        var mediator = Pipeline(_tenantA);
        var created = await mediator.Send(Earning("HRA"));
        created.IsSuccess.Should().BeTrue();

        using var db = Db(_tenantA);
        var entry = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == PayrollAuditAction.SalaryComponentCreated);
        entry.ResourceId.Should().Be(created.Value!.Id.ToString());
        entry.Before.Should().BeNull();          // create has no "before"
        entry.After.Should().NotBeNullOrWhiteSpace();
    }

    // ── DF-55/US-PAY-012: a manual per-employee payslip retry is attributed to the acting user ──

    [Fact]
    [Trait("TC", "TC-PAY-004-06")]
    public async Task RetryOne_WritesPayslipRetriedAudit_AttributedToActingUser()
    {
        var runId = BaseEntity.NewUuidV7();
        var empId = BaseEntity.NewUuidV7();
        var slipId = BaseEntity.NewUuidV7();
        using (var seed = Db(_tenantA))
        {
            seed.PayrollRuns.Add(new PayrollRun
            {
                Id = runId, TenantId = _tenantA, PayMonth = 5, PayYear = 2026,
                Status = PayrollRunStatus.Finalized, InitiatedBy = _actor, InitiatedAt = DateTime.UtcNow,
            });
            seed.PayrollSlips.Add(new PayrollSlip
            {
                Id = slipId, TenantId = _tenantA, PayrollRunId = runId, EmployeeId = empId,
                GrossEarnings = 1000m, TotalDeductions = 0m, NetSalary = 1000m,
                WorkingDays = 22, PaidDays = 22, LopDays = 0, PayMonth = 5, PayYear = 2026,
                PdfStatus = PayslipPdfStatus.Generated,
            });
            await seed.SaveChangesAsync();
        }

        // The audit logger + service MUST share one DbContext so the Payslip.Retried row commits atomically
        // with the slip reset via the service's SaveChanges. The acting user is _actor (not the system actor).
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var cu = new FakeCurrentUser { TenantId = _tenantA, UserId = _actor };
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, ctx);
        var audit = new PayrollAuditLogger(db, ctx, cu, NullLogger<PayrollAuditLogger>.Instance);
        var svc = new PayslipGenerationService(
            db, ctx, Substitute.For<IFileStorage>(), NullLogger<PayslipGenerationService>.Instance, audit);

        var result = await svc.RetryOneAsync(runId, empId);
        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Regenerated.Should().BeTrue();

        using var read = Db(_tenantA);
        var entry = await read.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == PayrollAuditAction.PayslipRetried && a.ResourceId == slipId.ToString());

        entry.ResourceType.Should().Be(PayrollAuditAction.ResourceType.Payslip);
        entry.UserId.Should().Be(_actor,
            "a manual retry must be attributed to the acting HR user, not the system-actor render audit");
        entry.TenantId.Should().Be(_tenantA);
        entry.After.Should().NotBeNullOrWhiteSpace();
        var after = JsonSerializer.Deserialize<JsonElement>(entry.After!);
        after.GetProperty("EmployeeId").GetGuid().Should().Be(empId);
        after.GetProperty("Regenerated").GetBoolean().Should().BeTrue();
    }

    // ── ISSUE-185 / ISSUE-186: audit-trail EXPORT (FR-5/BR-4) ─────────────────────────

    [Fact]
    [Trait("Issue", "ISSUE-186")]
    public async Task ExportAuditTrail_IncludesBeforeAndAfterColumns()
    {
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        var created = await mediator.Send(Earning("BASIC", 1000m));
        await mediator.Send(UpdateEarning(created.Value!.Id, "BASIC", "Basic Pay", 1250m));

        var export = await provider.GetRequiredService<IPayrollAuditService>().ExportAuditTrailAsync(
            new PayrollAuditTrailFilter { Action = PayrollAuditAction.SalaryComponentUpdated },
            PayrollExportFormat.Csv);

        export.IsSuccess.Should().BeTrue(export.Error);
        var csv = System.Text.Encoding.UTF8.GetString(export.Value!.FileContent);
        csv.Split('\n')[0].Should().Contain("Before").And.Contain("After",
            "the exported audit file must carry the before/after JSON (FR-5/BR-4), not just that a change occurred");
        // The change is reconstructable offline from the export: the "after" name + value are present.
        csv.Should().Contain("Basic Pay").And.Contain("1250");
    }

    [Fact]
    [Trait("Issue", "ISSUE-185")]
    public void AuditTrailExport_IsGatedOnPayrollView_NotPayrollExport()
    {
        var export = typeof(HRM.Api.Controllers.PayrollAuditController)
            .GetMethod(nameof(HRM.Api.Controllers.PayrollAuditController.ExportAuditTrail))!;
        var attr = export
            .GetCustomAttributes(typeof(HRM.Infrastructure.Identity.RequirePermissionAttribute), inherit: false)
            .Cast<HRM.Infrastructure.Identity.RequirePermissionAttribute>()
            .Single();

        attr.Policy.Should().Contain("Payroll.View").And.NotContain("Payroll.Export",
            "the export is a downloadable form of the trail the caller can already view — gating it on " +
            "Payroll.Export locked out the Auditor role (holds Payroll.View, not Payroll.Export) (ISSUE-185)");
    }

    // ── AC-4: audit-trail query filtering ──────────────────────────────────────────

    [Fact]
    public async Task AuditTrail_FiltersByAction()
    {
        var mediator = Pipeline(_tenantA);
        var created = await mediator.Send(Earning("BASIC"));
        await mediator.Send(UpdateEarning(created.Value!.Id, "BASIC", "Basic Pay", 1100m));

        // Only the Updated action.
        var result = await mediator.Send(new GetPayrollAuditTrailQuery(
            new PayrollAuditTrailFilter { Action = PayrollAuditAction.SalaryComponentUpdated }));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().OnlyContain(e => e.Action == PayrollAuditAction.SalaryComponentUpdated);
        result.Value.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task AuditTrail_FiltersByResourceTypeAndResourceId()
    {
        var mediator = Pipeline(_tenantA);
        var a = await mediator.Send(Earning("BASIC"));
        var b = await mediator.Send(Earning("HRA"));

        var byResource = await mediator.Send(new GetPayrollAuditTrailQuery(
            new PayrollAuditTrailFilter
            {
                ResourceType = PayrollAuditAction.ResourceType.SalaryComponent,
                ResourceId = a.Value!.Id.ToString(),
            }));

        byResource.IsSuccess.Should().BeTrue();
        byResource.Value!.Items.Should().ContainSingle()
            .Which.ResourceId.Should().Be(a.Value.Id.ToString());
        byResource.Value.Items.Should().NotContain(e => e.ResourceId == b.Value!.Id.ToString());
    }

    [Fact]
    public async Task AuditTrail_FiltersByDateRange()
    {
        var mediator = Pipeline(_tenantA);
        await mediator.Send(Earning("BASIC"));

        // A window entirely in the past returns nothing; an open-from window returns the entry.
        var pastOnly = await mediator.Send(new GetPayrollAuditTrailQuery(
            new PayrollAuditTrailFilter { ToUtc = DateTime.UtcNow.AddDays(-1) }));
        pastOnly.Value!.TotalCount.Should().Be(0);

        var fromYesterday = await mediator.Send(new GetPayrollAuditTrailQuery(
            new PayrollAuditTrailFilter { FromUtc = DateTime.UtcNow.AddDays(-1) }));
        fromYesterday.Value!.TotalCount.Should().BeGreaterThan(0);
    }

    // ── AC-2: per-run audit timeline in chronological order ─────────────────────────

    [Fact]
    public async Task RunTimeline_ReturnsRunEntries_InChronologicalOrder()
    {
        var runId = await SeedRunWithTimelineAsync(_tenantA);

        var result = await Pipeline(_tenantA).Send(new GetPayrollRunAuditTimelineQuery(runId));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(3);
        result.Value.Select(e => e.Action).Should().ContainInOrder(
            PayrollAuditAction.PayrollRunInitiated,
            PayrollAuditAction.PayrollRunApproved,
            PayrollAuditAction.PayrollRunFinalized);

        // Strictly non-decreasing timestamps (AC-2 oldest-first).
        result.Value.Select(e => e.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task RunTimeline_CrossTenantRun_Returns404()
    {
        var runId = await SeedRunWithTimelineAsync(_tenantA);

        // Tenant B cannot see Tenant A's run -> the global filter hides it -> 404.
        var result = await Pipeline(_tenantB).Send(new GetPayrollRunAuditTimelineQuery(runId));
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("run_not_found");
    }

    // ── AC-5: audit rows are tenant-scoped ──────────────────────────────────────────

    [Fact]
    public async Task TenantB_CannotSeeTenantA_AuditRows()
    {
        // Tenant A writes audit rows via a real component create.
        await Pipeline(_tenantA).Send(Earning("BASIC"));

        // Tenant B's audit trail is empty — the explicit TenantId predicate scopes it (AC-5).
        var trailB = await Pipeline(_tenantB).Send(new GetPayrollAuditTrailQuery(new PayrollAuditTrailFilter()));
        trailB.IsSuccess.Should().BeTrue();
        trailB.Value!.TotalCount.Should().Be(0);

        // Tenant A does see its own row.
        var trailA = await Pipeline(_tenantA).Send(new GetPayrollAuditTrailQuery(new PayrollAuditTrailFilter()));
        trailA.Value!.TotalCount.Should().BeGreaterThan(0);
    }

    // ── NFR-4 / BR-2: immutability — no write surface on the audit read service ─────

    [Fact]
    public void PayrollAuditService_HasNoUpdateOrDeleteMethod()
    {
        var methods = typeof(IPayrollAuditService).GetMethods().Select(m => m.Name).ToList();
        methods.Should().NotContain(n =>
            n.StartsWith("Update", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Delete", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Remove", StringComparison.OrdinalIgnoreCase));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a tenant-A payroll run plus three structured audit rows (Initiated -> Approved -> Finalized)
    /// referencing that run, written with increasing timestamps so the timeline order is deterministic.
    /// </summary>
    private async Task<Guid> SeedRunWithTimelineAsync(Guid tenantId)
    {
        using var db = Db(tenantId);
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = 5, PayYear = 2026,
            Status = PayrollRunStatus.Finalized, InitiatedBy = _actor, InitiatedAt = DateTime.UtcNow,
            TotalEmployees = 1, ProcessedEmployees = 1, FinalizedAt = DateTime.UtcNow,
        });

        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        var runIdStr = runId.ToString();
        void Audit(string action, DateTime when) => db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, UserId = _actor,
            EventType = action, Action = action,
            ResourceType = PayrollAuditAction.ResourceType.PayrollRun, ResourceId = runIdStr,
            CreatedAt = when,
        });

        // Deliberately add out of timestamp order to prove the query sorts, not the insert order.
        Audit(PayrollAuditAction.PayrollRunFinalized, baseTime.AddMinutes(8));
        Audit(PayrollAuditAction.PayrollRunInitiated, baseTime);
        Audit(PayrollAuditAction.PayrollRunApproved, baseTime.AddMinutes(4));

        await db.SaveChangesAsync();
        return runId;
    }
}
