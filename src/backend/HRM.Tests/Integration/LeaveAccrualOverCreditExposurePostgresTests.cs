// ============================================================================
// BUG-291 exposure report (READ-ONLY): LeaveEntitlementService.GetAccrualOverCreditExposureAsync surfaces the
// employees a LEGACY full-year accrual over-credited relative to their configured Monthly/Quarterly frequency,
// WITHOUT changing anything. These arms run on REAL PostgreSQL because this is ledger arithmetic (numeric(5,2)
// balances feeding encashment / F&F) which InMemory masks in this codebase — the same standing rule that
// governs LeaveAccrualFrequencyPostgresTests.
//
// Arms:
//   (1) Monthly legacy full-year accrual appears with the correct over-credit for a mid-year as-of date.
//   (2) A Yearly type NEVER appears (credited == should-have by construction; excluded at source).
//   (3) A post-fix, period-tagged employee (AccrualPeriod non-null) does NOT appear — the arm that stops the
//       report double-counting employees the fix already handles.
//   (4) Zero over-credit (as-of at year-end → should-have == credited) is excluded.
//   (5) Tenant isolation — tenant B's over-credited employee never appears in tenant A's report.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-LV-002-BUG291-EXPOSURE")] // BUG-291: read-only exposure report of legacy over-credited accruals.
public sealed class LeaveAccrualOverCreditExposurePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

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

    // A context scoped to a specific tenant (the global query filter reads TenantId from this).
    private AppDbContext CreateContext(Guid tenantId)
    {
        var tc = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);
    }

    private LeaveEntitlementService BuildService(AppDbContext db, Guid tenantId)
    {
        var tc = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return new LeaveEntitlementService(
            db, tc, cu, NullLogger<LeaveEntitlementService>.Instance,
            new TenantLeaveYearResolver(db, tc), recalcScheduler: null, timeProvider: null);
    }

    private async Task<(Guid deptId, Guid jobId)> SeedTenantScaffoldAsync(AppDbContext db, Guid tenantId, string sub)
    {
        var deptId = BaseEntity.NewUuidV7();
        var jobId = BaseEntity.NewUuidV7();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = sub, Name = sub, DefaultCountryCode = "LK",
            FiscalYearStartMonth = 1, // calendar tenant → leave year 2026 = 2026-01-01..2026-12-31.
        });
        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Ops", Code = "OPS", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = tenantId, TitleName = "Engineer", IsActive = true });
        await db.SaveChangesAsync();
        return (deptId, jobId);
    }

    private static Employee NewEmployee(
        Guid tenantId, Guid deptId, Guid jobId, string no, bool active = true) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeNo = no, FirstName = "P", LastName = no,
        Email = $"{no}@t.com", DateOfJoining = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        DepartmentId = deptId, JobTitleId = jobId, EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active, IsActive = active, Fte = 1.0m,
    };

    private static LeaveType NewLeaveType(Guid tenantId, decimal annual, AccrualFrequency freq, string name) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = name, AnnualEntitlement = annual,
        AccrualFrequency = freq, Gender = LeaveTypeGender.All, IsActive = true,
    };

    // A LEGACY full-year accrual row: AccrualPeriod == null, the whole annual entitlement credited at once —
    // exactly what the pre-BUG-291 accrual path wrote.
    private static LeaveLedger LegacyAccrual(Guid tenantId, Guid empId, Guid typeId, decimal annual) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EntryType = LedgerEntryType.Accrual,
        EmployeeId = empId, LeaveTypeId = typeId, LeaveYear = 2026, AccrualPeriod = null,
        Amount = annual, BalanceAfter = annual, Description = "Annual accrual (legacy)",
        OccurredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    // A POST-FIX, period-tagged accrual row (AccrualPeriod non-null) — what the fixed accrual job writes.
    private static LeaveLedger PeriodAccrual(
        Guid tenantId, Guid empId, Guid typeId, int period, decimal amount, decimal balanceAfter) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EntryType = LedgerEntryType.Accrual,
        EmployeeId = empId, LeaveTypeId = typeId, LeaveYear = 2026, AccrualPeriod = period,
        Amount = amount, BalanceAfter = balanceAfter, Description = $"Monthly accrual period {period}/12",
        OccurredAt = new DateTime(2026, period, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    // ── (1) Monthly legacy full-year accrual appears with the correct mid-year over-credit ──────────────────
    [Fact]
    public async Task Monthly_LegacyFullYearAccrual_Appears_WithCorrectMidYearOverCredit_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        await using (var mig = CreateContext(tenantId)) await mig.Database.MigrateAsync();

        Guid empId, typeId;
        await using (var seed = CreateContext(tenantId))
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed, tenantId, "acme");
            var emp = NewEmployee(tenantId, deptId, jobId, "M-1");
            empId = emp.Id;
            seed.Employees.Add(emp);
            var lt = NewLeaveType(tenantId, annual: 24m, AccrualFrequency.Monthly, "Monthly-24");
            typeId = lt.Id;
            seed.LeaveTypes.Add(lt);
            seed.LeaveLedgerEntries.Add(LegacyAccrual(tenantId, empId, typeId, 24m));
            await seed.SaveChangesAsync();
        }

        // As of 15 June 2026: 6 of 12 monthly periods elapsed → should-have = 24 × 6/12 = 12; credited = 24.
        await using var check = CreateContext(tenantId);
        var result = await BuildService(check, tenantId)
            .GetAccrualOverCreditExposureAsync(new DateOnly(2026, 6, 15));

        result.IsSuccess.Should().BeTrue();
        result.Value!.LeaveYear.Should().Be(2026);
        result.Value.Rows.Should().HaveCount(1);
        var row = result.Value.Rows[0];
        row.EmployeeId.Should().Be(empId);
        row.LeaveTypeId.Should().Be(typeId);
        row.AccrualFrequency.Should().Be("Monthly");
        row.CreditedDays.Should().Be(24m);
        row.ShouldHaveAccruedDays.Should().Be(12m);
        row.OverCreditedDays.Should().Be(12m);
        row.IsEmployeeActive.Should().BeTrue();
    }

    // ── (2) A Yearly type NEVER appears ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Yearly_LegacyAccrual_NeverAppears_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        await using (var mig = CreateContext(tenantId)) await mig.Database.MigrateAsync();

        await using (var seed = CreateContext(tenantId))
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed, tenantId, "acme");
            var emp = NewEmployee(tenantId, deptId, jobId, "Y-1");
            seed.Employees.Add(emp);
            var lt = NewLeaveType(tenantId, annual: 20m, AccrualFrequency.Yearly, "Yearly-20");
            seed.LeaveTypes.Add(lt);
            // Even a legacy full-year accrual on a Yearly type is not an over-credit (Yearly credits the whole
            // year in one period by design), so it must never surface.
            seed.LeaveLedgerEntries.Add(LegacyAccrual(tenantId, emp.Id, lt.Id, 20m));
            await seed.SaveChangesAsync();
        }

        await using var check = CreateContext(tenantId);
        var result = await BuildService(check, tenantId)
            .GetAccrualOverCreditExposureAsync(new DateOnly(2026, 6, 15));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rows.Should().BeEmpty("a Yearly/Upfront type can never be over-credited");
    }

    // ── (3) A post-fix, period-tagged employee does NOT appear (no double-counting) ─────────────────────────
    [Fact]
    public async Task PeriodTagged_PostFixEmployee_DoesNotAppear_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        await using (var mig = CreateContext(tenantId)) await mig.Database.MigrateAsync();

        await using (var seed = CreateContext(tenantId))
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed, tenantId, "acme");
            var emp = NewEmployee(tenantId, deptId, jobId, "PF-1");
            seed.Employees.Add(emp);
            var lt = NewLeaveType(tenantId, annual: 24m, AccrualFrequency.Monthly, "Monthly-24");
            seed.LeaveTypes.Add(lt);
            // Correctly accrued post-fix: two period-tagged rows (Jan + Feb), no legacy NULL-period row.
            seed.LeaveLedgerEntries.Add(PeriodAccrual(tenantId, emp.Id, lt.Id, 1, 2m, 2m));
            seed.LeaveLedgerEntries.Add(PeriodAccrual(tenantId, emp.Id, lt.Id, 2, 2m, 4m));
            await seed.SaveChangesAsync();
        }

        await using var check = CreateContext(tenantId);
        var result = await BuildService(check, tenantId)
            .GetAccrualOverCreditExposureAsync(new DateOnly(2026, 6, 15));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rows.Should().BeEmpty(
            "an employee whose accrual is already period-tagged is handled by the fix and must not be double-counted");
    }

    // ── (4) Zero over-credit (as-of at year-end) is excluded ────────────────────────────────────────────────
    [Fact]
    public async Task ZeroOverCredit_AtYearEnd_IsExcluded_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        await using (var mig = CreateContext(tenantId)) await mig.Database.MigrateAsync();

        await using (var seed = CreateContext(tenantId))
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed, tenantId, "acme");
            var emp = NewEmployee(tenantId, deptId, jobId, "Z-1");
            seed.Employees.Add(emp);
            var lt = NewLeaveType(tenantId, annual: 24m, AccrualFrequency.Monthly, "Monthly-24");
            seed.LeaveTypes.Add(lt);
            seed.LeaveLedgerEntries.Add(LegacyAccrual(tenantId, emp.Id, lt.Id, 24m));
            await seed.SaveChangesAsync();
        }

        // As of 31 Dec 2026: all 12 monthly periods elapsed → should-have = 24 == credited → over-credit 0.
        await using var check = CreateContext(tenantId);
        var result = await BuildService(check, tenantId)
            .GetAccrualOverCreditExposureAsync(new DateOnly(2026, 12, 31));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rows.Should().BeEmpty("a zero over-credit is not an exposure");
    }

    // ── (5) Tenant isolation — tenant B's over-credited employee never appears in tenant A's report ─────────
    [Fact]
    public async Task TenantIsolation_TenantBEmployee_NeverAppearsInTenantAReport_OnPostgres()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var mig = CreateContext(tenantA)) await mig.Database.MigrateAsync();

        // Tenant B has an over-credited Monthly employee.
        await using (var seedB = CreateContext(tenantB))
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seedB, tenantB, "beta");
            var emp = NewEmployee(tenantB, deptId, jobId, "B-1");
            seedB.Employees.Add(emp);
            var lt = NewLeaveType(tenantB, annual: 24m, AccrualFrequency.Monthly, "Monthly-24-B");
            seedB.LeaveTypes.Add(lt);
            seedB.LeaveLedgerEntries.Add(LegacyAccrual(tenantB, emp.Id, lt.Id, 24m));
            await seedB.SaveChangesAsync();
        }

        // Tenant A has its own over-credited employee.
        Guid empA;
        await using (var seedA = CreateContext(tenantA))
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seedA, tenantA, "acme");
            var emp = NewEmployee(tenantA, deptId, jobId, "A-1");
            empA = emp.Id;
            seedA.Employees.Add(emp);
            var lt = NewLeaveType(tenantA, annual: 24m, AccrualFrequency.Monthly, "Monthly-24-A");
            seedA.LeaveTypes.Add(lt);
            seedA.LeaveLedgerEntries.Add(LegacyAccrual(tenantA, emp.Id, lt.Id, 24m));
            await seedA.SaveChangesAsync();
        }

        await using var check = CreateContext(tenantA);
        var result = await BuildService(check, tenantA)
            .GetAccrualOverCreditExposureAsync(new DateOnly(2026, 6, 15));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Rows.Should().HaveCount(1, "tenant A sees only its own employee");
        result.Value.Rows[0].EmployeeId.Should().Be(empA);
    }

    // ── Export (BUG-291 remediation tooling): CSV/XLSX render the SAME rows for Finance ─────────────────────
    // Seeds one over-credited Monthly employee, returns the mid-year over-credit (12), then exports.

    private async Task<Guid> SeedOneOverCreditedMonthlyAsync(Guid tenantId, string sub, string no)
    {
        await using var seed = CreateContext(tenantId);
        var (deptId, jobId) = await SeedTenantScaffoldAsync(seed, tenantId, sub);
        var emp = NewEmployee(tenantId, deptId, jobId, no);
        seed.Employees.Add(emp);
        var lt = NewLeaveType(tenantId, annual: 24m, AccrualFrequency.Monthly, "Monthly-24");
        seed.LeaveTypes.Add(lt);
        seed.LeaveLedgerEntries.Add(LegacyAccrual(tenantId, emp.Id, lt.Id, 24m));
        await seed.SaveChangesAsync();
        return emp.Id;
    }

    // ── (6) CSV: content type, non-empty, header row, and the seeded over-credit figure appear ──────────────
    [Fact]
    public async Task Export_Csv_HasHeaderAndOverCreditFigure_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        await using (var mig = CreateContext(tenantId)) await mig.Database.MigrateAsync();
        await SeedOneOverCreditedMonthlyAsync(tenantId, "acme", "M-1");

        await using var check = CreateContext(tenantId);
        var result = await BuildService(check, tenantId)
            .ExportAccrualOverCreditExposureAsync(new DateOnly(2026, 6, 15), "csv");

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("text/csv");
        result.Value.FileName.Should().Contain("2026-06-15"); // as-of date embedded → self-describing.
        result.Value.FileContent.Should().NotBeEmpty();

        var text = System.Text.Encoding.UTF8.GetString(result.Value.FileContent);
        text.Should().Contain("Over-Credited Days");        // human-readable header row present.
        text.Should().Contain("As of date,2026-06-15");     // as-of date inside the file.
        text.Should().Contain("M-1");                       // the affected employee.
        text.Should().Contain("12");                        // its over-credited figure.
    }

    // ── (7) XLSX: content type + a real (PK-magic) workbook ─────────────────────────────────────────────────
    [Fact]
    public async Task Export_Xlsx_IsNonEmptyWorkbook_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        await using (var mig = CreateContext(tenantId)) await mig.Database.MigrateAsync();
        await SeedOneOverCreditedMonthlyAsync(tenantId, "acme", "X-1");

        await using var check = CreateContext(tenantId);
        var result = await BuildService(check, tenantId)
            .ExportAccrualOverCreditExposureAsync(new DateOnly(2026, 6, 15), "xlsx");

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should()
            .Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        result.Value.FileContent.Should().NotBeEmpty();
        // XLSX is a zip → first two bytes are the "PK" magic. Sufficient without deep-parsing.
        result.Value.FileContent[0].Should().Be((byte)'P');
        result.Value.FileContent[1].Should().Be((byte)'K');
    }

    // ── (8) An unsupported format ⇒ 400 invalid_format ──────────────────────────────────────────────────────
    [Fact]
    public async Task Export_UnsupportedFormat_Fails_WithInvalidFormatCode_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        await using (var mig = CreateContext(tenantId)) await mig.Database.MigrateAsync();

        await using var check = CreateContext(tenantId);
        var result = await BuildService(check, tenantId)
            .ExportAccrualOverCreditExposureAsync(new DateOnly(2026, 6, 15), "pdf");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_format");
    }
}
