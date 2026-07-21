// ============================================================================
// US-ONB-005: Offboarding / exit-clearance integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (with the MediatR
// pipeline, the FluentValidation ValidationBehavior, and the global query filters), covering:
//   - Happy path: initiate -> clear all mandatory -> complete; on completion the employee is terminated, the
//     account is deactivated, sessions are revoked, and F&F is triggered (AC-1/AC-4/FR-5/FR-6/FR-7).
//   - Blocked completion with a pending mandatory clearance returns the pending list (AC-5/BR-2).
//   - Asset return updates the register (AC-2/BR-3).
//   - Tenant isolation: Tenant B cannot read Tenant A's offboarding (AC-6/NFR-2).
//
// NOTE ON PROVIDER: mirrors the other module integration tests — EF Core InMemory through the real composed
// pipeline (no PostgreSQL, no Docker in the verify gate). The PG-specific schema (enum-as-string) is
// validated by the `migrations` CI job applying the generated migration to real PG. IAuthService and the
// FR-6/FR-7 seams (ISessionRevoker, IPayrollFnFIntegration) are substituted so we assert the orchestration
// without real auth/payroll side effects.
// ============================================================================

using FluentAssertions;
using FluentValidation;
using HRM.Application.Common.Behaviors;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Application.Features.Onboarding.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class OffboardingIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _hrUserA = Guid.NewGuid();

    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeUserA = Guid.NewGuid();
    private readonly Guid _managerA = Guid.NewGuid();
    private readonly Guid _managerUserA = Guid.NewGuid();

    private readonly Guid _settlementRef = Guid.NewGuid();
    private IAuthService _authService = null!;
    private ISessionRevoker _sessionRevoker = null!;
    private IPayrollFnFIntegration _payrollFnF = null!;

    public OffboardingIntegrationTests()
    {
        SeedTenantsAndEmployees();
    }

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

    private IMediator BuildPipeline(Guid tenantId, Guid userId, bool realFnF = false)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns("hr@test.com");
        currentUser.IsAuthenticated.Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        // FR-5/FR-6/FR-7 seams — substituted so we assert orchestration without real side effects.
        _authService = Substitute.For<IAuthService>();
        _authService.RevokeAllSessionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _sessionRevoker = Substitute.For<ISessionRevoker>();
        services.AddSingleton(_authService);
        services.AddSingleton(_sessionRevoker);

        if (realFnF)
        {
            // ISSUE-294 DI-swap seam: register the REAL F&F integration so the CompleteAsync → RealPayrollFnFIntegration
            // → persisted FinalSettlement chain is exercised end-to-end (a revert to LogOnly or a dropped call is caught).
            services.AddScoped<IStatutoryDeductionResolver, StatutoryDeductionResolver>();
            // ISSUE-305: required by the leave services — a missing registration now THROWS rather than
        // silently falling back to the calendar year (which is how the credit/debit ledger split hid).
        services.AddScoped<ITenantLeaveYearResolver, TenantLeaveYearResolver>();
        services.AddScoped<IPayrollFnFIntegration, RealPayrollFnFIntegration>();
        }
        else
        {
            _payrollFnF = Substitute.For<IPayrollFnFIntegration>();
            _payrollFnF.TriggerFinalSettlementAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(_settlementRef);
            services.AddSingleton(_payrollFnF);
        }

        services.AddScoped<IOffboardingService, OffboardingService>();

        services.AddValidatorsFromAssembly(typeof(InitiateOffboardingCommand).Assembly);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(InitiateOffboardingCommand).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedTenantsAndEmployees()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        db.Tenants.AddRange(
            new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme Corp" },
            new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });

        db.Users.Add(new User { Id = _employeeUserA, Email = "dana@acme.com", IsActive = true });

        db.Employees.AddRange(
            new Employee
            {
                Id = _managerA, TenantId = _tenantA, EmployeeNo = "EMP-0001",
                FirstName = "Mona", LastName = "Manager", Email = "mona@acme.com",
                DateOfJoining = DateTime.UtcNow.AddYears(-3).Date, UserId = _managerUserA,
                Status = EmployeeStatus.Active,
            },
            new Employee
            {
                Id = _employeeA, TenantId = _tenantA, EmployeeNo = "EMP-0002",
                FirstName = "Dana", LastName = "Departing", Email = "dana@acme.com",
                DateOfJoining = DateTime.UtcNow.AddYears(-2).Date, UserId = _employeeUserA,
                ReportsToEmployeeId = _managerA, Status = EmployeeStatus.Suspended,
            });

        db.SaveChanges();
    }

    private void SeedAsset(Guid tenantId, Guid id, string tag, AssetStatus status, Guid? assignedTo = null)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        db.Assets.Add(new Asset
        {
            Id = id, TenantId = tenantId, AssetType = "Laptop", AssetTag = tag,
            Status = status, Condition = AssetCondition.Good, AssignedEmployeeId = assignedTo,
        });
        db.SaveChanges();
    }

    private static InitiateOffboardingCommand InitiateCmd(Guid employeeId) =>
        new(employeeId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)), null, OffboardingReason.Resignation, "Moving on.");

    private static async Task ClearAllMandatory(IMediator mediator, OffboardingInstanceDto instance)
    {
        foreach (var task in instance.Departments.SelectMany(d => d.Tasks).Where(t => t.IsMandatory))
            await mediator.Send(new RecordClearanceCommand(task.Id, ClearanceStatus.Approved, null));
    }

    // ── AC-1 / AC-4 / FR-5 / FR-6 / FR-7 happy path ─────────────────────

    [Fact]
    public async Task Initiate_then_clear_all_then_complete_terminates_deactivates_and_triggers_fnf()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);

        var initiated = await mediator.Send(InitiateCmd(_employeeA));
        initiated.IsSuccess.Should().BeTrue();
        initiated.Value!.Status.Should().Be(OffboardingStatus.InProgress);
        initiated.Value.TotalTasks.Should().Be(6);

        await ClearAllMandatory(mediator, initiated.Value);

        var complete = await mediator.Send(new CompleteOffboardingCommand(initiated.Value.Id));

        complete.IsSuccess.Should().BeTrue();
        complete.Value!.Completed.Should().BeTrue();
        complete.Value.FinalSettlementRef.Should().Be(_settlementRef);

        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        await using var db = new AppDbContext(options, ctx);
        var employee = await db.Employees.FirstAsync(e => e.Id == _employeeA);
        employee.Status.Should().Be(EmployeeStatus.Terminated);
        employee.IsActive.Should().BeFalse();
        (await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == _employeeUserA)).IsActive.Should().BeFalse();
        (await db.OffboardingInstances.FirstAsync(o => o.Id == initiated.Value.Id)).Status
            .Should().Be(OffboardingStatus.Completed);

        await _authService.Received(1).RevokeAllSessionsAsync(_employeeUserA, _tenantA, Arg.Any<CancellationToken>());
        await _payrollFnF.Received(1).TriggerFinalSettlementAsync(
            _tenantA, _employeeA, initiated.Value.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    // ── ISSUE-294 DI-swap seam: CompleteAsync drives the REAL RealPayrollFnFIntegration → a FinalSettlement row ──
    [Fact]
    public async Task Complete_withRealIntegration_persistsAFinalSettlementRow()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA, realFnF: true);

        var initiated = (await mediator.Send(InitiateCmd(_employeeA))).Value!;
        await ClearAllMandatory(mediator, initiated);
        var complete = await mediator.Send(new CompleteOffboardingCommand(initiated.Id));

        complete.IsSuccess.Should().BeTrue(complete.Error);
        complete.Value!.Completed.Should().BeTrue();
        complete.Value.FinalSettlementRef.Should().NotBeNull("the real integration returns the persisted settlement ref");

        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        await using var db = new AppDbContext(options, ctx);
        // The REAL integration persisted a FinalSettlement for this offboarding — proves the DI swap + the
        // CompleteAsync → RealPayrollFnFIntegration seam are wired (a revert to LogOnly leaves no row → this fails).
        var settlement = await db.FinalSettlements.FirstOrDefaultAsync(s => s.OffboardingInstanceId == initiated.Id);
        settlement.Should().NotBeNull();
        settlement!.Id.Should().Be(complete.Value.FinalSettlementRef!.Value);
        settlement.EmployeeId.Should().Be(_employeeA);
    }

    // ── AC-5 / BR-2 blocked completion ──────────────────────────────────

    [Fact]
    public async Task Complete_with_a_pending_mandatory_clearance_returns_the_pending_list()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var initiated = (await mediator.Send(InitiateCmd(_employeeA))).Value!;

        // Flag the mandatory Finance clearance with issues; leave the IT asset return pending too.
        var financeTask = initiated.Departments
            .Single(d => d.ClearanceCategory == ClearanceCategory.Finance).Tasks.Single();
        await mediator.Send(new RecordClearanceCommand(financeTask.Id, ClearanceStatus.PendingIssues, "Loan due."));

        var complete = await mediator.Send(new CompleteOffboardingCommand(initiated.Id));

        complete.IsSuccess.Should().BeTrue();
        complete.Value!.Completed.Should().BeFalse();
        complete.Value.PendingItems.Should().HaveCountGreaterThanOrEqualTo(2);
        complete.Value.PendingItems.Select(p => p.ClearanceCategory)
            .Should().Contain(new[] { ClearanceCategory.IT, ClearanceCategory.Finance });

        // Nothing was terminated and F&F was not triggered.
        await _payrollFnF.DidNotReceive().TriggerFinalSettlementAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    // ── AC-2 / BR-3 asset return updates the register ───────────────────

    [Fact]
    public async Task Return_asset_updates_the_register_and_completes_the_task()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var assetId = BaseEntity.NewUuidV7();
        SeedAsset(_tenantA, assetId, "LAP-RET", AssetStatus.Assigned, assignedTo: _employeeA);

        var initiated = (await mediator.Send(InitiateCmd(_employeeA))).Value!;
        var itReturnTask = initiated.Departments
            .Single(d => d.ClearanceCategory == ClearanceCategory.IT).Tasks
            .Single(t => t.Title == "Return IT assets");

        var result = await mediator.Send(
            new ReturnAssetCommand(itReturnTask.Id, assetId, AssetCondition.Fair, Disposed: false));

        result.IsSuccess.Should().BeTrue();

        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        await using var db = new AppDbContext(options, ctx);
        var asset = await db.Assets.FirstAsync(a => a.Id == assetId);
        asset.Status.Should().Be(AssetStatus.Available);
        asset.AssignedEmployeeId.Should().BeNull();
        (await db.OffboardingTaskInstances.FirstAsync(t => t.Id == itReturnTask.Id)).Status
            .Should().Be(OnboardingTaskStatus.Completed);
    }

    // ── AC-6 / NFR-2 tenant isolation ───────────────────────────────────

    [Fact]
    public async Task Tenant_B_cannot_read_tenant_A_offboarding()
    {
        var hrA = BuildPipeline(_tenantA, _hrUserA);
        var initiated = (await hrA.Send(InitiateCmd(_employeeA))).Value!;

        var hrB = BuildPipeline(_tenantB, Guid.NewGuid());
        var result = await hrB.Send(new GetOffboardingInstanceQuery(initiated.Id));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }
}
