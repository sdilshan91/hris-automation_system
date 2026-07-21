// ============================================================================
// DF-4 / BUG-118 — LeaveEntitlementReconcileJob (durability backstop sweep).
//
// LeaveEntitlementService.UpdateRuleAsync commits a rule edit and then does a best-effort, post-commit
// Enqueue of the recalc job. If Hangfire storage is unavailable in that window the enqueue is LOST and the
// leave type's already-accrued employees never converge to the new rule. LeaveEntitlementReconcileJob is a
// daily sweep that re-runs the (idempotent) recalc across active tenants so a dropped enqueue self-heals.
//
// These tests drive the real job.RunAsync end-to-end over a real DI graph (real TenantJobRunner with RLS off,
// real TenantLeaveYearResolver, real LeaveEntitlementService) on an InMemory-through-real-EF store, proving:
//   1. the sweep alone (no UpdateRuleAsync enqueue) makes an employee converge to the edited rule,
//   2. a second sweep is idempotent (writes nothing new),
//   3. the sweep is tenant-isolated and skips inactive tenants,
//   4. the sweep invokes ITenantJobRunner.RunForTenantAsync exactly once per active tenant.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveEntitlementReconcileJobTests
{
    // A fixed clock inside leave year 2026 (calendar tenants → LabelFor == 2026), matching the ledger the
    // seed writes. Hand-rolled to avoid depending on the MS TimeProvider.Testing package (not referenced).
    private static readonly TimeProvider FixedClock2026 =
        new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));

    // ── 1. The sweep backstops a lost enqueue ───────────────────────────────

    [Fact]
    [Trait("TC", "TC-LV-268")]
    public async Task ReconcileSweep_converges_a_tenant_whose_recalc_enqueue_was_lost()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        var (empId, ltId) = await SeedTenantWithLostEnqueueAsync(
            provider, tenantId, "acme", TenantStatus.Active, initialDays: 20, newDays: 25);

        // Precondition: only the accrual (20) exists — the recalc was never run (enqueue lost).
        (await ReadLedgerAsync(provider, tenantId, empId, ltId))
            .Any(l => l.EntryType == LedgerEntryType.Adjusted).Should().BeFalse();

        var job = new LeaveEntitlementReconcileJob(
            provider.GetRequiredService<IServiceScopeFactory>(), FixedClock2026);
        await job.RunAsync();

        // The sweep alone brought the employee into line with the edited rule: +5 Adjusted delta → balance 25.
        var adjustment = (await ReadLedgerAsync(provider, tenantId, empId, ltId))
            .Single(l => l.EntryType == LedgerEntryType.Adjusted);
        adjustment.Amount.Should().Be(5m);
        adjustment.BalanceAfter.Should().Be(25m);
        adjustment.Description.Should().StartWith("Entitlement recalculation");
    }

    // ── 2. Idempotent on re-run ──────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-LV-268")]
    public async Task ReconcileSweep_second_run_writes_nothing_new()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        var (empId, ltId) = await SeedTenantWithLostEnqueueAsync(
            provider, tenantId, "acme", TenantStatus.Active, initialDays: 20, newDays: 25);

        var job = new LeaveEntitlementReconcileJob(
            provider.GetRequiredService<IServiceScopeFactory>(), FixedClock2026);

        await job.RunAsync(); // converges (+5)
        await job.RunAsync(); // granted now == target → delta 0 → must add nothing

        (await ReadLedgerAsync(provider, tenantId, empId, ltId))
            .Count(l => l.EntryType == LedgerEntryType.Adjusted).Should().Be(1);
    }

    // ── 3. Tenant isolation + inactive-tenant skip ───────────────────────────

    [Fact]
    [Trait("TC", "TC-LV-268")]
    public async Task ReconcileSweep_isolates_tenants_and_skips_inactive()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString());
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantC = Guid.NewGuid();

        var (empA, ltA) = await SeedTenantWithLostEnqueueAsync(
            provider, tenantA, "a", TenantStatus.Active, initialDays: 20, newDays: 25);   // needs +5
        var (empB, ltB) = await SeedTenantWithLostEnqueueAsync(
            provider, tenantB, "b", TenantStatus.Trial, initialDays: 20, newDays: 23);    // needs +3
        var (empC, ltC) = await SeedTenantWithLostEnqueueAsync(
            provider, tenantC, "c", TenantStatus.Suspended, initialDays: 20, newDays: 25); // suspended → skipped

        var job = new LeaveEntitlementReconcileJob(
            provider.GetRequiredService<IServiceScopeFactory>(), FixedClock2026);
        await job.RunAsync();

        // Each active tenant converged to its OWN rule under its OWN scope (no cross-tenant leak).
        (await ReadLedgerAsync(provider, tenantA, empA, ltA))
            .Single(l => l.EntryType == LedgerEntryType.Adjusted).Amount.Should().Be(5m);
        (await ReadLedgerAsync(provider, tenantB, empB, ltB))
            .Single(l => l.EntryType == LedgerEntryType.Adjusted).Amount.Should().Be(3m);

        // The suspended tenant is not swept — its lost enqueue stays lost until reactivation.
        (await ReadLedgerAsync(provider, tenantC, empC, ltC))
            .Any(l => l.EntryType == LedgerEntryType.Adjusted).Should().BeFalse();
    }

    // ── 4. Runner invoked once per active tenant ─────────────────────────────

    [Fact]
    [Trait("TC", "TC-LV-268")]
    public async Task ReconcileSweep_invokes_the_runner_once_per_active_tenant()
    {
        var runner = Substitute.For<ITenantJobRunner>();
        var provider = BuildProvider(Guid.NewGuid().ToString(), runnerOverride: runner);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var tenantC = Guid.NewGuid();
        await SeedTenantRowsAsync(provider,
            (tenantA, "a", TenantStatus.Active),
            (tenantB, "b", TenantStatus.Trial),
            (tenantC, "c", TenantStatus.Suspended));

        var job = new LeaveEntitlementReconcileJob(
            provider.GetRequiredService<IServiceScopeFactory>(), FixedClock2026);
        await job.RunAsync();

        await runner.Received(1).RunForTenantAsync(
            tenantA, Arg.Any<string>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
        await runner.Received(1).RunForTenantAsync(
            tenantB, Arg.Any<string>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
        await runner.DidNotReceive().RunForTenantAsync(
            tenantC, Arg.Any<string>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    // ══════════════════════════════════════════════════════════════
    //  Fixtures
    // ══════════════════════════════════════════════════════════════

    private static ServiceProvider BuildProvider(string dbName, ITenantJobRunner? runnerOverride = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // Rls:Enabled defaults false
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser>(_ =>
        {
            var cu = Substitute.For<ICurrentUser>();
            cu.IsAuthenticated.Returns(false);
            return cu;
        });
        services.AddScoped<ITenantLeaveYearResolver, TenantLeaveYearResolver>();
        services.AddScoped<ILeaveEntitlementRecalcJobScheduler>(_ =>
            Substitute.For<ILeaveEntitlementRecalcJobScheduler>());
        services.AddScoped<ILeaveEntitlementService, LeaveEntitlementService>();

        if (runnerOverride is not null)
            services.AddSingleton(runnerOverride);
        else
            services.AddScoped<ITenantJobRunner, TenantJobRunner>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Seeds one tenant with a leave type, an active employee, a rule (<paramref name="initialDays"/>), an
    /// upfront accrual, and then edits the rule to <paramref name="newDays"/> WITHOUT running the recalc — i.e.
    /// exactly the state left behind when UpdateRuleAsync's post-commit enqueue is lost.
    /// </summary>
    private static async Task<(Guid EmployeeId, Guid LeaveTypeId)> SeedTenantWithLostEnqueueAsync(
        IServiceProvider provider, Guid tenantId, string subdomain, TenantStatus status,
        decimal initialDays, decimal newDays)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        // Seed under an Active context so the EF query filter (by TenantId) admits the writes; the Tenant ROW's
        // Status (which the sweep filters on) is set to `status` below independently of the context.
        ctx.SetTenant(tenantId, subdomain, TenantStatus.Active);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = status, FiscalYearStartMonth = 1,
        });
        var lt = new LeaveType
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Annual Leave", AnnualEntitlement = 10,
            AccrualFrequency = AccrualFrequency.Upfront, ProbationEligible = false, IsActive = true,
        };
        var emp = new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = $"EMP-{subdomain}",
            FirstName = "Test", LastName = "Employee", Email = $"{subdomain}@test.com",
            DateOfJoining = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        };
        db.LeaveTypes.Add(lt);
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var svc = scope.ServiceProvider.GetRequiredService<ILeaveEntitlementService>();
        var created = await svc.CreateRuleAsync(new UpsertLeaveEntitlementRuleRequest
        {
            LeaveTypeId = lt.Id,
            EntitlementDays = initialDays,
            Priority = 1,
            EffectiveFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        created.IsSuccess.Should().BeTrue();
        await svc.ProcessAccrualsAsync(2026, leaveTypeId: null); // accrues `initialDays`

        // Simulate the lost enqueue: the rule now reads `newDays`, but no recalc ran.
        var rule = await db.LeaveEntitlementRules.FirstAsync(r => r.LeaveTypeId == lt.Id);
        rule.EntitlementDays = newDays;
        await db.SaveChangesAsync();

        return (emp.Id, lt.Id);
    }

    private static async Task SeedTenantRowsAsync(
        IServiceProvider provider, params (Guid Id, string Subdomain, TenantStatus Status)[] tenants)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (id, subdomain, status) in tenants)
        {
            db.Tenants.Add(new Tenant
            {
                Id = id, Subdomain = subdomain, Name = subdomain, Status = status, FiscalYearStartMonth = 1,
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<List<LeaveLedger>> ReadLedgerAsync(
        IServiceProvider provider, Guid tenantId, Guid employeeId, Guid leaveTypeId)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        ctx.SetTenant(tenantId, "read", TenantStatus.Active);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == employeeId && l.LeaveTypeId == leaveTypeId && l.LeaveYear == 2026)
            .ToListAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
