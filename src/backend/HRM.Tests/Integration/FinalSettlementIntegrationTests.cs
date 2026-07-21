// ============================================================================
// ISSUE-294 (F&F Phase 1): Full-and-Final settlement engine — integration tests.
//
// Exercises RealPayrollFnFIntegration through a real composed EF pipeline (InMemory provider — the verify gate
// runs `dotnet test` with no Postgres/Docker; the DB-only concerns, i.e. the offboarding-instance unique index
// and the dormant RLS policy, are proven separately on real Postgres in FinalSettlementPostgresTests). Covers:
//   - policy toggles change the computed components (pro-rated / statutory / encashment include vs exclude);
//   - IDEMPOTENCY: a second trigger for the same offboarding instance returns the SAME ref + one row;
//   - per-country statutory resolution + the null-country skip+flag (money-critical fail-closed);
//   - the net payable is never negative (floored at 0);
//   - the payroll-run double-pay guard excludes a settlement-owned terminated employee.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PAY-FNF-001")]
public sealed class FinalSettlementIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenant = Guid.NewGuid();

    // WORKING-WEEK BASIS (corrected 2026-07-15, US-ATT-011/CAL-1). This fixture previously seeded NO shift and
    // relied on ShiftScheduleResolver returning an EMPTY set, which callers read as "every calendar day is a
    // working day" ⇒ 30 working days in June, daily rate 30000/30 = 1000. **Production never behaves that
    // way**: DbInitializer.EnsureDefaultShiftAsync seeds a Mon–Fri IsDefault "General Shift" for EVERY tenant
    // on startup, and TenantProvisioningService.SeedDefaultShift does the same for every new tenant — so the
    // resolver always found Mon–Fri and the real daily rate was always 30000/22. The old 10000 encashment
    // oracle asserted a figure production could not produce. These tests now seed the Mon–Fri default
    // EXPLICITLY (mirroring DbInitializer) so the money figures match production and do not silently depend on
    // the resolver's tier-4 code default.
    //
    // June 2026: 30 calendar days, 22 Mon–Fri working days. A mid-month leaver with LWD on the 15th works 11
    // of those 22 ⇒ pro-ration factor 11/22 = 0.5 (unchanged from the old 15/30 = 0.5 — which is why
    // ExpectedProRatedGross is untouched). BASIC 30000 ⇒ pro-rated gross 15000; daily rate 30000/22 = 1363.64
    // ⇒ 10 encashable days × 1363.64 = 13636.40.
    private static readonly DateOnly Lwd = new(2026, 6, 15);
    private const decimal MonthlyBasic = 30_000m;
    private const decimal ExpectedProRatedGross = 15_000m;

    /// <summary>10 encashable days × the Mon–Fri daily rate (30000/22 = 1363.64) — the production figure.</summary>
    private const decimal ExpectedEncashmentTotal = 13_636.40m;

    /// <summary>
    /// Seeds the tenant's Mon–Fri default shift, mirroring <c>DbInitializer.EnsureDefaultShiftAsync</c> /
    /// <c>TenantProvisioningService.SeedDefaultShift</c>. These InMemory tests bypass DbInitializer, so without
    /// this the fixture sits in a shift-less state production never reaches and the working-day denominator
    /// silently falls to the resolver's tier-4 code default.
    /// </summary>
    private void SeedTenantDefaultShift(AppDbContext db) =>
        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenant,
            Name = "General Shift",
            Type = ShiftType.Single,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            WorkingDays = [1, 2, 3, 4, 5],
            IsDefault = true,
            IsActive = true,
        });

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

    private RealPayrollFnFIntegration BuildService(AppDbContext db)
    {
        var tc = new MutableTenantContext { TenantId = _tenant };
        var resolver = new StatutoryDeductionResolver(db, tc, NullLogger<StatutoryDeductionResolver>.Instance);
        return new RealPayrollFnFIntegration(db, tc, resolver, NullLogger<RealPayrollFnFIntegration>.Instance);
    }

    // ── seed helpers ───────────────────────────────────────────────────────────

    private Guid SeedEmployeeWithBasic(AppDbContext db, Guid? locationId = null)
    {
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenant, EmployeeNo = "EMP-1", FirstName = "A", LastName = "B",
            Email = "a@t.com", DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.Empty, JobTitleId = Guid.Empty, EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true, LocationId = locationId,
        });
        var compId = BaseEntity.NewUuidV7();
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = compId, TenantId = _tenant, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsActive = true, ProcessingOrder = 1,
        });
        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = empId,
            SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = compId,
            AnnualAmount = MonthlyBasic * 12m, MonthlyAmount = MonthlyBasic,
            EffectiveFrom = new DateOnly(2020, 1, 1), EffectiveTo = null,
        });
        return empId;
    }

    private void SeedTenantDefaultCountry(AppDbContext db, string? country) =>
        db.Tenants.Add(new Tenant { Id = _tenant, Subdomain = "acme", Name = "Acme", DefaultCountryCode = country });

    /// <summary>Income-tax rule: [0,10000)@0%, [10000,∞)@rate% for the given country.</summary>
    private void SeedIncomeTax(AppDbContext db, string country, decimal topRate)
    {
        var ruleId = BaseEntity.NewUuidV7();
        db.StatutoryRules.Add(new StatutoryRule
        {
            Id = ruleId, TenantId = _tenant, RuleType = StatutoryRuleType.IncomeTax, RuleName = "PAYE",
            CountryCode = country, FiscalYear = "2026", EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true,
        });
        db.TaxSlabs.Add(new TaxSlab { Id = BaseEntity.NewUuidV7(), TenantId = _tenant, StatutoryRuleId = ruleId, SlabFrom = 0m, SlabTo = 10_000m, RatePercentage = 0m, OrderIndex = 0 });
        db.TaxSlabs.Add(new TaxSlab { Id = BaseEntity.NewUuidV7(), TenantId = _tenant, StatutoryRuleId = ruleId, SlabFrom = 10_000m, SlabTo = null, RatePercentage = topRate, OrderIndex = 1 });
    }

    private void SeedProfessionalTaxOnGross(AppDbContext db, string country, decimal employeeRate)
    {
        var ruleId = BaseEntity.NewUuidV7();
        db.StatutoryRules.Add(new StatutoryRule
        {
            Id = ruleId, TenantId = _tenant, RuleType = StatutoryRuleType.ProfessionalTax, RuleName = "PT",
            CountryCode = country, FiscalYear = "2026", EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true,
        });
        db.SocialSecurityRules.Add(new SocialSecurityRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, StatutoryRuleId = ruleId,
            EmployeeRate = employeeRate, EmployerRate = 0m, ApplicableOn = StatutoryApplicableOn.Gross,
        });
    }

    /// <summary>Encashable Annual leave with carry-forward limit 5 + max-encash 10, ledger balance 20.</summary>
    private void SeedEncashableLeave(AppDbContext db, Guid empId)
    {
        var ltId = BaseEntity.NewUuidV7();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = ltId, TenantId = _tenant, Name = "Annual", Encashable = true, IsActive = true,
            CarryForwardLimit = 5m, MaxEncashDays = 10m, AnnualEntitlement = 20m,
        });
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EntryType = LedgerEntryType.Accrual,
            EmployeeId = empId, LeaveTypeId = ltId, LeaveYear = 2026, Amount = 20m, BalanceAfter = 20m,
            OccurredAt = DateTime.UtcNow,
        });
    }

    private void SeedPolicy(AppDbContext db, bool proRated, bool statutory, bool encashment, bool ownedBySettlement = true)
    {
        db.TenantFnFPolicies.Add(new TenantFnFPolicy
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EffectiveFrom = new DateOnly(2026, 1, 1),
            IncludeProRatedFinalPay = proRated, IncludeStatutory = statutory, IncludeLeaveEncashment = encashment,
            FinalPeriodOwnedBySettlement = ownedBySettlement, IsActive = true,
        });
    }

    private async Task<FinalSettlement> ActAsync(Guid empId, Guid offboardingId)
    {
        Guid settlementId;
        using (var db = Db())
        {
            settlementId = await BuildService(db).TriggerFinalSettlementAsync(_tenant, empId, offboardingId, Lwd);
        }
        using var read = Db();
        return await read.FinalSettlements.AsNoTracking().Include(s => s.Lines)
            .FirstAsync(s => s.Id == settlementId);
    }

    // ── (a) policy toggles change the components ────────────────────────────────

    [Fact]
    public async Task ProRatedOnly_Policy_ComputesGross_NoStatutory_NoEncashment()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultShift(db);
            SeedTenantDefaultCountry(db, "LK");
            empId = SeedEmployeeWithBasic(db);
            SeedIncomeTax(db, "LK", 10m);      // present, but the policy disables statutory.
            SeedEncashableLeave(db, empId);    // present, but the policy disables encashment.
            SeedPolicy(db, proRated: true, statutory: false, encashment: false);
            await db.SaveChangesAsync();
        }

        var s = await ActAsync(empId, Guid.NewGuid());

        s.ProRatedGross.Should().Be(ExpectedProRatedGross);
        s.StatutoryTotal.Should().Be(0m);
        s.LeaveEncashmentTotal.Should().Be(0m);
        s.NetPayable.Should().Be(ExpectedProRatedGross);
        s.Lines.Should().OnlyContain(l => l.Type == FinalSettlementLineType.Earning);
    }

    [Fact]
    public async Task AllTogglesOff_Policy_ComputesZeroSettlement()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultShift(db);
            SeedTenantDefaultCountry(db, "LK");
            empId = SeedEmployeeWithBasic(db);
            SeedIncomeTax(db, "LK", 10m);
            SeedEncashableLeave(db, empId);
            SeedPolicy(db, proRated: false, statutory: false, encashment: false);
            await db.SaveChangesAsync();
        }

        var s = await ActAsync(empId, Guid.NewGuid());

        s.ProRatedGross.Should().Be(0m);
        s.StatutoryTotal.Should().Be(0m);
        s.LeaveEncashmentTotal.Should().Be(0m);
        s.NetPayable.Should().Be(0m);
        s.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task EncashmentToggle_IncludesForfeitableEncashment()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultShift(db);
            SeedTenantDefaultCountry(db, "LK");
            empId = SeedEmployeeWithBasic(db);
            SeedEncashableLeave(db, empId);
            SeedPolicy(db, proRated: true, statutory: false, encashment: true);
            await db.SaveChangesAsync();
        }

        var s = await ActAsync(empId, Guid.NewGuid());

        // Forfeitable = balance(20) − carryForwardLimit(5) = 15, capped at maxEncash(10) = 10 days × 1000/day = 10000.
        s.LeaveEncashmentTotal.Should().Be(ExpectedEncashmentTotal);
        s.NetPayable.Should().Be(ExpectedProRatedGross + ExpectedEncashmentTotal);
        s.Lines.Should().Contain(
            l => l.Type == FinalSettlementLineType.Encashment && l.Amount == ExpectedEncashmentTotal);
    }

    // ── (b) idempotency ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Trigger_IsIdempotent_OnOffboardingInstance()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultShift(db);
            SeedTenantDefaultCountry(db, "LK");
            empId = SeedEmployeeWithBasic(db);
            SeedPolicy(db, proRated: true, statutory: false, encashment: false);
            await db.SaveChangesAsync();
        }

        var offboardingId = Guid.NewGuid();
        Guid ref1, ref2;
        using (var db = Db()) ref1 = await BuildService(db).TriggerFinalSettlementAsync(_tenant, empId, offboardingId, Lwd);
        using (var db = Db()) ref2 = await BuildService(db).TriggerFinalSettlementAsync(_tenant, empId, offboardingId, Lwd);

        ref2.Should().Be(ref1, "a retried offboarding-complete must return the existing settlement, not recompute");

        using var read = Db();
        var count = await read.FinalSettlements.CountAsync(s => s.OffboardingInstanceId == offboardingId);
        count.Should().Be(1, "exactly one settlement row exists for the offboarding instance");
    }

    // ── (c) per-country statutory + null-country skip+flag ───────────────────────

    [Fact]
    public async Task Statutory_ResolvedUnderTenantDefaultCountry_AppliesTax()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultShift(db);
            SeedTenantDefaultCountry(db, "LK");
            empId = SeedEmployeeWithBasic(db);           // no location ⇒ falls back to tenant default "LK".
            SeedIncomeTax(db, "LK", 10m);
            SeedPolicy(db, proRated: true, statutory: true, encashment: false);
            await db.SaveChangesAsync();
        }

        var s = await ActAsync(empId, Guid.NewGuid());

        // taxable 15000; tax = (15000 − 10000) × 10% = 500.
        s.CountryCode.Should().Be("LK");
        s.FiscalYear.Should().Be("2026");
        s.StatutoryTotal.Should().Be(500m);
        s.StatutorySkipped.Should().BeFalse();
        s.NetPayable.Should().Be(ExpectedProRatedGross - 500m);
        s.Lines.Should().Contain(l => l.Type == FinalSettlementLineType.Statutory);
    }

    [Fact]
    public async Task Statutory_NullCountry_MultiCountryRules_SkipsAndFlags()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultCountry(db, null);          // no tenant default …
            empId = SeedEmployeeWithBasic(db);           // … and no location ⇒ country unresolvable.
            SeedIncomeTax(db, "LK", 10m);                // two countries ⇒ no single-country fallback.
            SeedIncomeTax(db, "IN", 20m);
            SeedPolicy(db, proRated: true, statutory: true, encashment: false);
            await db.SaveChangesAsync();
        }

        var s = await ActAsync(empId, Guid.NewGuid());

        s.CountryCode.Should().BeNull();
        s.StatutorySkipped.Should().BeTrue("a multi-country tenant with no resolvable tax country must never be taxed");
        s.Notes.Should().NotBeNullOrEmpty();
        s.StatutoryTotal.Should().Be(0m);
        s.NetPayable.Should().Be(ExpectedProRatedGross, "the pro-rated pay is still paid; only statutory is skipped");
    }

    // ── (d) net never negative ───────────────────────────────────────────────────

    [Fact]
    public async Task NetPayable_IsFlooredAtZero_WhenStatutoryExceedsGross()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultShift(db);
            SeedTenantDefaultCountry(db, "LK");
            empId = SeedEmployeeWithBasic(db);
            SeedIncomeTax(db, "LK", 100m);               // 100% of income over 10000 → (15000−10000) = 5000 …
            SeedProfessionalTaxOnGross(db, "LK", 100m);  // … + 100% of gross (15000) = 20000 statutory total.
            SeedPolicy(db, proRated: true, statutory: true, encashment: false);
            await db.SaveChangesAsync();
        }

        var s = await ActAsync(empId, Guid.NewGuid());

        s.StatutoryTotal.Should().Be(20_000m);
        s.StatutoryTotal.Should().BeGreaterThan(s.ProRatedGross, "statutory exceeds gross so the raw net is negative");
        s.NetPayable.Should().Be(0m, "the net payable is floored at 0 and is never negative");
    }

    // ── (e) payroll-run double-pay guard ─────────────────────────────────────────

    [Fact]
    public async Task PayrollRun_ExcludesEmployeeWithSettlementOwnedFinalPeriod()
    {
        // A normal active employee (gets a slip) + an active employee whose final period is owned by an F&F
        // settlement covering the run month (must be excluded — no double pay).
        var runId = BaseEntity.NewUuidV7();
        Guid normalEmp, ownedEmp;
        using (var scope = Provider().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            normalEmp = SeedEmployeeWithBasic(db);
            ownedEmp = SeedEmployeeWithBasic2(db);
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = runId, TenantId = _tenant, PayYear = 2026, PayMonth = 6, Status = PayrollRunStatus.Queued,
                InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            });
            db.FinalSettlements.Add(new FinalSettlement
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = ownedEmp,
                OffboardingInstanceId = Guid.NewGuid(), LastWorkingDay = new DateOnly(2026, 6, 15),
                FiscalYear = string.Empty, ProRatedGross = 15_000m, NetPayable = 15_000m,
                PolicyEffectiveFrom = new DateOnly(2026, 1, 1), FinalPeriodOwnedBySettlement = true,
                ComputedAtUtc = DateTime.UtcNow, Status = FinalSettlementStatus.Computed,
            });
            await db.SaveChangesAsync();
        }

        using (var scope = Provider().CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IPayrollRunProcessor>();
            var result = await processor.ProcessAsync(runId);
            result.IsSuccess.Should().BeTrue(result.Error);
        }

        using var read = Db();
        (await read.PayrollSlips.CountAsync(s => s.EmployeeId == ownedEmp && s.PayrollRunId == runId))
            .Should().Be(0, "the settlement-owned employee's final period must not be paid again by the run");
        (await read.PayrollSlips.CountAsync(s => s.EmployeeId == normalEmp && s.PayrollRunId == runId))
            .Should().Be(1, "the normal employee is still paid");
    }

    // ── (f) effective-dated policy resolution — a newer policy is NOT applied retroactively (user requirement) ──

    [Fact]
    public async Task Policy_IsResolvedEffectiveDated_ANewerPolicyDoesNotChangeAnEarlierSettlement()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultShift(db);
            SeedTenantDefaultCountry(db, "LK");
            empId = SeedEmployeeWithBasic(db);
            SeedEncashableLeave(db, empId);
            // Policy A (older): encashment ON. Policy B (newer, Jun 1): encashment OFF.
            SeedPolicyAt(db, new DateOnly(2026, 1, 1), proRated: true, statutory: false, encashment: true);
            SeedPolicyAt(db, new DateOnly(2026, 6, 1), proRated: true, statutory: false, encashment: false);
            await db.SaveChangesAsync();
        }

        // A leaver whose LWD is BEFORE policy B's effective date resolves policy A (encashment included) — the
        // newer policy must not retroactively rewrite the earlier settlement.
        var early = await ActWithLwdAsync(empId, Guid.NewGuid(), new DateOnly(2026, 2, 20));
        early.PolicyEffectiveFrom.Should().Be(new DateOnly(2026, 1, 1));
        early.LeaveEncashmentTotal.Should().BeGreaterThan(0m, "the LWD predates policy B, so policy A (encashment ON) governs");

        // A leaver whose LWD is on/after policy B resolves policy B (encashment excluded).
        var later = await ActWithLwdAsync(empId, Guid.NewGuid(), new DateOnly(2026, 6, 15));
        later.PolicyEffectiveFrom.Should().Be(new DateOnly(2026, 6, 1));
        later.LeaveEncashmentTotal.Should().Be(0m, "on/after policy B's effective date, encashment is OFF");
    }

    // ── (g) code-default policy governs when the tenant has configured NONE (works without seeding) ──

    [Fact]
    public async Task NoConfiguredPolicy_UsesTheCodeDefault_AllComponentsOn()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultShift(db);
            SeedTenantDefaultCountry(db, "LK");
            empId = SeedEmployeeWithBasic(db);
            SeedIncomeTax(db, "LK", 10m);
            SeedEncashableLeave(db, empId);
            // NO TenantFnFPolicy seeded → the code-default (all includes ON) must govern.
            await db.SaveChangesAsync();
        }

        var s = await ActAsync(empId, Guid.NewGuid());

        s.ProRatedGross.Should().Be(ExpectedProRatedGross);
        s.StatutoryTotal.Should().Be(500m);
        s.LeaveEncashmentTotal.Should().Be(ExpectedEncashmentTotal);
        s.NetPayable.Should().Be(ExpectedProRatedGross + ExpectedEncashmentTotal - 500m);
    }

    // ── (h) multi-component gross: sum ALL earnings/reimbursements; drop structure deductions ──

    [Fact]
    public async Task ProRatedGross_SumsAllEarnings_AndDropsStructureDeductions()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultShift(db);
            SeedTenantDefaultCountry(db, "LK");
            empId = SeedEmployeeMultiComponent(db); // BASIC 30000 + HRA 10000 (Earning) + LOAN 2000 (Deduction) + PF 1500 (Statutory)
            SeedPolicy(db, proRated: true, statutory: false, encashment: false);
            await db.SaveChangesAsync();
        }

        var s = await ActAsync(empId, Guid.NewGuid());

        // Earnings = (30000 + 10000) pro-rated 15/30 = 20000; the LOAN deduction AND the PF structure-statutory
        // line are BOTH dropped (only Earning/Reimbursement feed the settlement gross — no double-count).
        s.ProRatedGross.Should().Be(20_000m);
        s.Lines.Count(l => l.Type == FinalSettlementLineType.Earning).Should().Be(2, "both BASIC and HRA earnings are itemized");
        s.Lines.Should().NotContain(l => l.Label.Contains("Loan") || l.Label.Contains("Provident"),
            "a structure deduction/statutory line is not a settlement earning line");
    }

    // ── (i) the OTHER statutory skip path: country RESOLVES but has no rules configured for it ──

    [Fact]
    public async Task Statutory_ResolvedCountryWithNoRules_SkipsAndFlags()
    {
        Guid empId;
        using (var db = Db())
        {
            SeedTenantDefaultCountry(db, "US");   // resolves to US …
            empId = SeedEmployeeWithBasic(db);
            SeedIncomeTax(db, "LK", 10m);          // … but rules exist only for LK + IN, never US.
            SeedIncomeTax(db, "IN", 20m);
            SeedPolicy(db, proRated: true, statutory: true, encashment: false);
            await db.SaveChangesAsync();
        }

        var s = await ActAsync(empId, Guid.NewGuid());

        s.CountryCode.Should().Be("US");
        s.StatutorySkipped.Should().BeTrue("rules exist but none for the employee's resolved country — never apply another country's tax");
        s.Notes.Should().Contain("US");
        s.StatutoryTotal.Should().Be(0m);
        s.NetPayable.Should().Be(ExpectedProRatedGross);
    }

    // ── (j) run-guard negative arm: a settlement with FinalPeriodOwnedBySettlement=FALSE does NOT exclude ──

    [Fact]
    public async Task PayrollRun_StillPaysEmployee_WhenSettlementNotOwned()
    {
        var runId = BaseEntity.NewUuidV7();
        Guid notOwnedEmp;
        using (var scope = Provider().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            notOwnedEmp = SeedEmployeeWithBasic(db);
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = runId, TenantId = _tenant, PayYear = 2026, PayMonth = 6, Status = PayrollRunStatus.Queued,
                InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            });
            db.FinalSettlements.Add(new FinalSettlement
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = notOwnedEmp,
                OffboardingInstanceId = Guid.NewGuid(), LastWorkingDay = new DateOnly(2026, 6, 15),
                FiscalYear = string.Empty, ProRatedGross = 15_000m, NetPayable = 15_000m,
                PolicyEffectiveFrom = new DateOnly(2026, 1, 1), FinalPeriodOwnedBySettlement = false, // NOT owned
                ComputedAtUtc = DateTime.UtcNow, Status = FinalSettlementStatus.Computed,
            });
            await db.SaveChangesAsync();
        }

        using (var scope = Provider().CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IPayrollRunProcessor>();
            (await processor.ProcessAsync(runId)).IsSuccess.Should().BeTrue();
        }

        using var read = Db();
        (await read.PayrollSlips.CountAsync(s => s.EmployeeId == notOwnedEmp && s.PayrollRunId == runId))
            .Should().Be(1, "a not-owned settlement must NOT exclude the employee from the run");
    }

    // ── act with a custom last-working-day (effective-dated policy arms) ──
    private async Task<FinalSettlement> ActWithLwdAsync(Guid empId, Guid offboardingId, DateOnly lwd)
    {
        Guid settlementId;
        using (var db = Db())
            settlementId = await BuildService(db).TriggerFinalSettlementAsync(_tenant, empId, offboardingId, lwd);
        using var read = Db();
        return await read.FinalSettlements.AsNoTracking().Include(s => s.Lines).FirstAsync(s => s.Id == settlementId);
    }

    private void SeedPolicyAt(AppDbContext db, DateOnly effectiveFrom, bool proRated, bool statutory, bool encashment, bool ownedBySettlement = true)
    {
        db.TenantFnFPolicies.Add(new TenantFnFPolicy
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EffectiveFrom = effectiveFrom,
            IncludeProRatedFinalPay = proRated, IncludeStatutory = statutory, IncludeLeaveEncashment = encashment,
            FinalPeriodOwnedBySettlement = ownedBySettlement, IsActive = true,
        });
    }

    /// <summary>An employee with BASIC 30000 + HRA 10000 (both Earning) + a LOAN 2000 structure Deduction.</summary>
    private Guid SeedEmployeeMultiComponent(AppDbContext db)
    {
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenant, EmployeeNo = "EMP-MC", FirstName = "M", LastName = "C",
            Email = "mc@t.com", DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.Empty, JobTitleId = Guid.Empty, EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true,
        });
        void AddComp(string name, string code, SalaryComponentType type, decimal monthly, int order)
        {
            var compId = BaseEntity.NewUuidV7();
            db.SalaryComponents.Add(new SalaryComponent
            {
                Id = compId, TenantId = _tenant, Name = name, Code = code, Type = type,
                CalculationMethod = CalculationMethod.Fixed, IsActive = true, ProcessingOrder = order,
            });
            db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = empId,
                SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = compId,
                AnnualAmount = monthly * 12m, MonthlyAmount = monthly,
                EffectiveFrom = new DateOnly(2020, 1, 1), EffectiveTo = null,
            });
        }
        AddComp("Basic Salary", "BASIC", SalaryComponentType.Earning, 30_000m, 1);
        AddComp("House Rent Allowance", "HRA", SalaryComponentType.Earning, 10_000m, 2);
        AddComp("Loan Recovery", "LOAN", SalaryComponentType.Deduction, 2_000m, 3);
        AddComp("Provident Fund", "PF", SalaryComponentType.Statutory, 1_500m, 4); // structure statutory line — must be dropped from gross.
        return empId;
    }

    /// <summary>A second active employee with a BASIC structure (distinct employee no).</summary>
    private Guid SeedEmployeeWithBasic2(AppDbContext db)
    {
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenant, EmployeeNo = "EMP-2", FirstName = "C", LastName = "D",
            Email = "c@t.com", DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.Empty, JobTitleId = Guid.Empty, EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true,
        });
        var compId = BaseEntity.NewUuidV7();
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = compId, TenantId = _tenant, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsActive = true, ProcessingOrder = 1,
        });
        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = empId,
            SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = compId,
            AnnualAmount = MonthlyBasic * 12m, MonthlyAmount = MonthlyBasic,
            EffectiveFrom = new DateOnly(2020, 1, 1), EffectiveTo = null,
        });
        return empId;
    }

    /// <summary>DI provider for the run-guard test — mirrors PayrollRunIntegrationTests.Provider.</summary>
    private ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new MutableTenantContext { TenantId = _tenant });
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = _tenant });
        services.AddSingleton<IPayrollNotificationService, LogOnlyPayrollNotificationService>();
        services.AddScoped<IReportExportStorage, InMemoryExportStorage>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddScoped<IAttendancePayrollService, AttendancePayrollService>();
        services.AddScoped<IStatutoryDeductionResolver, StatutoryDeductionResolver>();
        services.AddScoped<IPayrollAdjustmentResolver, PayrollAdjustmentResolver>();
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IPayrollSlipCleaner, PayrollSlipCleaner>();
        services.AddScoped<IPayrollRunProcessor, PayrollRunProcessor>();
        return services.BuildServiceProvider();
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; } = Guid.NewGuid();
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

    private sealed class InMemoryExportStorage : IReportExportStorage
    {
        public Task<string> SaveAsync(Guid tenantId, Guid reportId, string fileName,
            string contentType, byte[] content, CancellationToken cancellationToken = default)
            => Task.FromResult($"mem://{tenantId}/{reportId}/{fileName}");
    }
}
