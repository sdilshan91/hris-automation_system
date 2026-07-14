// ============================================================================
// US-PAY-013 (F&F Phase 1) — TenantFnFPolicy configuration VALIDATION + effective-dated resolution.
//
// Complements FinalSettlementIntegrationTests (which prove the settlement COMPUTATION). This class proves the
// policy-config surface the settlement reads:
//   - AC-1: the CreateFnFPolicyValidator rejects a missing effective date (the only structural rule);
//   - AC-1/AC-2 (money-adjacent): one version per effective-from date — a same-date re-config REPLACES the prior
//     version (soft-delete), so the resolver never tie-breaks between two same-date active rows;
//   - AC-2: GetEffectiveAsync resolves the latest EffectiveFrom <= asOf, and returns the safe code-default when
//     no policy is configured (works without seeding).
// InMemory-through-real-EF (the config path has no Postgres-only concern).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.Validators;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PAY-013-08")]
public sealed class FnFPolicyServiceTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenant = Guid.NewGuid();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
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

    private AppDbContext Db()
    {
        var tc = new MutableTenantContext { TenantId = _tenant };
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tc);
    }

    private FnFPolicyService Service(AppDbContext db) =>
        new(db, new MutableTenantContext { TenantId = _tenant }, NullLogger<FnFPolicyService>.Instance);

    private static CreateFnFPolicyInput Input(DateOnly eff, bool encash = true) =>
        new(eff, IncludeProRatedFinalPay: true, IncludeStatutory: true, IncludeLeaveEncashment: encash,
            FinalPeriodOwnedBySettlement: true, IsActive: true);

    // ── AC-1: the only structural rule — an effective date is required ──
    [Fact]
    public void Validator_RequiresAnEffectiveFromDate()
    {
        var validator = new CreateFnFPolicyValidator();

        validator.Validate(new CreateFnFPolicyCommand(default, true, true, true, true, true))
            .IsValid.Should().BeFalse("a policy version with no effective date is rejected");
        validator.Validate(new CreateFnFPolicyCommand(new DateOnly(2026, 1, 1), true, true, true, true, true))
            .IsValid.Should().BeTrue();
    }

    // ── AC-1/AC-2 (money-adjacent): one version per effective-from date — a same-date re-config REPLACES the
    //    prior so two same-date active rows never coexist (else the resolver's OrderByDescending(EffectiveFrom)
    //    .First() would tie-break non-deterministically on a money path). ──
    [Fact]
    public async Task Create_SameEffectiveFromDate_ReplacesThePriorVersion()
    {
        var date = new DateOnly(2026, 6, 1);
        using (var db = Db()) (await Service(db).CreateAsync(Input(date, encash: true))).IsSuccess.Should().BeTrue();
        using (var db = Db()) (await Service(db).CreateAsync(Input(date, encash: false))).IsSuccess.Should().BeTrue();

        using var read = Db();
        // Exactly ONE active (non-deleted) version on that date — the prior was soft-deleted, not accumulated.
        (await read.TenantFnFPolicies.CountAsync(p => p.EffectiveFrom == date))
            .Should().Be(1, "a same-date re-config replaces the prior version");
        // And the effective policy on/after that date is the SECOND config (encashment OFF), deterministically.
        var eff = await Service(read).GetEffectiveAsync(new DateOnly(2026, 7, 1));
        eff.Value!.IncludeLeaveEncashment.Should().BeFalse("the replacement version governs");
    }

    // ── AC-2: GetEffectiveAsync picks the latest EffectiveFrom <= asOf, and returns the safe code-default
    //    (all components on) when nothing is configured — the feature works without seeding. ──
    [Fact]
    public async Task GetEffective_PicksLatestOnOrBeforeAsOf_AndDefaultsWhenNone()
    {
        // No policy configured → code-default (all includes on).
        using (var db = Db())
        {
            var def = await Service(db).GetEffectiveAsync(new DateOnly(2026, 6, 1));
            def.IsSuccess.Should().BeTrue();
            def.Value!.IncludeProRatedFinalPay.Should().BeTrue();
            def.Value.IncludeStatutory.Should().BeTrue();
            def.Value.IncludeLeaveEncashment.Should().BeTrue();
        }

        using (var db = Db()) (await Service(db).CreateAsync(Input(new DateOnly(2026, 1, 1), encash: true))).IsSuccess.Should().BeTrue();
        using (var db = Db()) (await Service(db).CreateAsync(Input(new DateOnly(2026, 6, 1), encash: false))).IsSuccess.Should().BeTrue();

        // asOf BETWEEN the two versions → the January version governs (encashment ON).
        (await Service(Db()).GetEffectiveAsync(new DateOnly(2026, 3, 1)))
            .Value!.IncludeLeaveEncashment.Should().BeTrue("the latest EffectiveFrom <= 2026-03-01 is the Jan version");
        // asOf ON/AFTER the June version → June governs (encashment OFF).
        (await Service(Db()).GetEffectiveAsync(new DateOnly(2026, 8, 1)))
            .Value!.IncludeLeaveEncashment.Should().BeFalse("the latest EffectiveFrom <= 2026-08-01 is the Jun version");
    }
}
