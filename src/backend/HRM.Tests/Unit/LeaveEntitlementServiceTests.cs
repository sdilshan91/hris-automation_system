// ============================================================================
// US-LV-002: Leave Entitlement Service Unit Tests
// Tests rule CRUD, override CRUD, entitlement resolution (override > rule > default),
// pro-rata for mid-year joiners, probation eligibility (BR-3), negative clamping (BR-4),
// tenant isolation, and Hangfire accrual job ledger entries.
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveEntitlementServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<LeaveEntitlementService> _logger;

    // Seeded reference data IDs.
    private Guid _leaveTypeId;
    private Guid _leaveTypeSickId;
    private Guid _departmentId;
    private Guid _jobTitleId;
    private Guid _employeeId;

    public LeaveEntitlementServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("admin@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<LeaveEntitlementService>>();

        SeedReferenceData();
    }

    public void Dispose() { }

    private LeaveEntitlementService CreateService(ILeaveEntitlementRecalcJobScheduler? recalcScheduler = null)
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new LeaveEntitlementService(dbContext, _tenantContext, _currentUser, _logger,
            new TenantLeaveYearResolver(dbContext, _tenantContext), recalcScheduler);
    }

    private AppDbContext CreateDbContext()
    {
        return TestDbContextFactory.Create(_tenantContext, _dbName);
    }

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        var dept = new Department
        {
            Id = _departmentId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Engineering",
            Code = "ENG",
            IsActive = true,
        };

        var jt = new JobTitle
        {
            Id = _jobTitleId = Guid.NewGuid(),
            TenantId = _tenantId,
            TitleName = "Senior Developer",
            IsActive = true,
        };

        var lt = new LeaveType
        {
            Id = _leaveTypeId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Annual Leave",
            AnnualEntitlement = 14,
            AccrualFrequency = AccrualFrequency.Upfront,
            ProbationEligible = false,
            IsActive = true,
        };

        var ltSick = new LeaveType
        {
            Id = _leaveTypeSickId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Sick Leave",
            AnnualEntitlement = 7,
            AccrualFrequency = AccrualFrequency.Upfront,
            ProbationEligible = true,
            IsActive = true,
        };

        var emp = new Employee
        {
            Id = _employeeId = Guid.NewGuid(),
            TenantId = _tenantId,
            EmployeeNo = "EMP-0001",
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            DateOfJoining = new DateTime(2026, 1, 1),
            DepartmentId = dept.Id,
            JobTitleId = jt.Id,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        };

        db.Departments.Add(dept);
        db.JobTitles.Add(jt);
        db.LeaveTypes.Add(lt);
        db.LeaveTypes.Add(ltSick);
        db.Employees.Add(emp);
        db.SaveChanges();
    }

    // ══════════════════════════════════════════════════════════════
    //  Rule CRUD
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateRule_ValidRequest_Succeeds()
    {
        var svc = CreateService();
        var result = await svc.CreateRuleAsync(MakeRuleRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value!.LeaveTypeId.Should().Be(_leaveTypeId);
        result.Value.EntitlementDays.Should().Be(20);
    }

    [Fact]
    public async Task CreateRule_InvalidLeaveType_Fails()
    {
        var svc = CreateService();
        var request = MakeRuleRequest() with { LeaveTypeId = Guid.NewGuid() };
        var result = await svc.CreateRuleAsync(request);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Leave type not found");
    }

    [Fact]
    public async Task UpdateRule_ExistingRule_Succeeds()
    {
        var svc = CreateService();
        var created = await svc.CreateRuleAsync(MakeRuleRequest());
        var ruleId = created.Value!.Id;

        var updated = await svc.UpdateRuleAsync(ruleId,
            MakeRuleRequest() with { EntitlementDays = 25 });

        updated.IsSuccess.Should().BeTrue();
        updated.Value!.EntitlementDays.Should().Be(25);
    }

    [Fact]
    public async Task DeleteRule_ExistingRule_SoftDeletes()
    {
        var svc = CreateService();
        var created = await svc.CreateRuleAsync(MakeRuleRequest());
        var ruleId = created.Value!.Id;

        var deleted = await svc.DeleteRuleAsync(ruleId);
        deleted.IsSuccess.Should().BeTrue();

        // Should not be visible anymore.
        var get = await svc.GetRuleByIdAsync(ruleId);
        get.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetRules_FilterByLeaveType_ReturnsOnlyMatching()
    {
        var svc = CreateService();
        await svc.CreateRuleAsync(MakeRuleRequest());
        await svc.CreateRuleAsync(MakeRuleRequest() with { LeaveTypeId = _leaveTypeSickId, EntitlementDays = 7 });

        var result = await svc.GetRulesAsync(_leaveTypeId);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value![0].LeaveTypeId.Should().Be(_leaveTypeId);
    }

    [Fact]
    public async Task BulkCreateRules_CreatesMultiple()
    {
        var svc = CreateService();
        // ISSUE-033: the two rules must target DIFFERENT dimensions — the duplicate-rule guard now rejects
        // a second rule for the same (leaveType + department + jobTitle + ...) combination.
        var requests = new List<UpsertLeaveEntitlementRuleRequest>
        {
            MakeRuleRequest() with { EntitlementDays = 15, EmploymentType = "FullTime" },
            MakeRuleRequest() with { EntitlementDays = 20, EmploymentType = "PartTime" },
        };

        var result = await svc.BulkCreateRulesAsync(requests);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
    }

    // ══════════════════════════════════════════════════════════════
    //  Override CRUD
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpsertOverride_Create_Succeeds()
    {
        var svc = CreateService();
        var result = await svc.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId,
            LeaveYear = 2026,
            EntitlementDays = 30,
            Reason = "Exceptional contribution",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.EntitlementDays.Should().Be(30);
        result.Value.EmployeeName.Should().Be("John Doe");
    }

    [Fact]
    public async Task UpsertOverride_Update_UpdatesExisting()
    {
        var svc = CreateService();
        await svc.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId,
            LeaveYear = 2026,
            EntitlementDays = 30,
            Reason = "Initial",
        });

        var updated = await svc.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId,
            LeaveYear = 2026,
            EntitlementDays = 35,
            Reason = "Updated",
        });

        updated.IsSuccess.Should().BeTrue();
        updated.Value!.EntitlementDays.Should().Be(35);
        updated.Value.Reason.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteOverride_SoftDeletes()
    {
        var svc = CreateService();
        var created = await svc.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId,
            LeaveYear = 2026,
            EntitlementDays = 30,
            Reason = "Test",
        });

        var deleted = await svc.DeleteOverrideAsync(created.Value!.Id);
        deleted.IsSuccess.Should().BeTrue();

        // Should not be visible.
        var overrides = await svc.GetOverridesAsync(_employeeId, _leaveTypeId, 2026);
        overrides.Value!.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════
    //  Effective Entitlement Resolution (AC-2, AC-3)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ComputeEffective_Override_BeatsRules_AC3()
    {
        var svc = CreateService();

        // Create a rule giving 20 days.
        await svc.CreateRuleAsync(MakeRuleRequest());

        // Create an override giving 30 days.
        await svc.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId,
            LeaveYear = 2026,
            EntitlementDays = 30,
            Reason = "Manager override",
        });

        var result = await svc.ComputeEffectiveEntitlementAsync(_employeeId, _leaveTypeId, 2026);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BaseEntitlementDays.Should().Be(30);
        result.Value.Source.Should().Be("override");
    }

    [Fact]
    public async Task ComputeEffective_RuleBeatsDefault_AC2()
    {
        var svc = CreateService();

        // Create a department-specific rule giving 20 days (more than the default 14).
        await svc.CreateRuleAsync(MakeRuleRequest());

        var result = await svc.ComputeEffectiveEntitlementAsync(_employeeId, _leaveTypeId, 2026);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BaseEntitlementDays.Should().Be(20);
        result.Value.Source.Should().StartWith("rule:");
    }

    [Fact]
    public async Task ComputeEffective_NoRulesNoOverride_FallsBackToDefault()
    {
        var svc = CreateService();

        var result = await svc.ComputeEffectiveEntitlementAsync(_employeeId, _leaveTypeId, 2026);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BaseEntitlementDays.Should().Be(14); // LeaveType.AnnualEntitlement
        result.Value.Source.Should().Be("leave_type_default");
    }

    // ══════════════════════════════════════════════════════════════
    //  Pro-Rata (AC-4)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    [Trait("TC", "TC-LV-266")]
    public async Task ComputeEffective_MidYearJoiner_ProRated_AC4()
    {
        // Create employee who joined July 1.
        using (var db = CreateDbContext())
        {
            var emp = await db.Employees.FindAsync(_employeeId);
            emp!.DateOfJoining = new DateTime(2026, 7, 1);
            await db.SaveChangesAsync();
        }

        var svc = CreateService();
        var result = await svc.ComputeEffectiveEntitlementAsync(_employeeId, _leaveTypeId, 2026);

        result.IsSuccess.Should().BeTrue();
        // ISSUE-034 month-fraction: July joiner → Jul–Dec = 6 months, 14 × 6/12 = 7.00
        result.Value!.ProratedEntitlementDays.Should().Be(7.00m);
    }

    // ══════════════════════════════════════════════════════════════
    //  Probation Eligibility (BR-3)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ComputeEffective_ProbationEmployee_IneligibleType_ReturnsZero_BR3()
    {
        // Set employee to probation.
        using (var db = CreateDbContext())
        {
            var emp = await db.Employees.FindAsync(_employeeId);
            emp!.Status = EmployeeStatus.Probation;
            await db.SaveChangesAsync();
        }

        var svc = CreateService();
        // Annual Leave is NOT probation eligible.
        var result = await svc.ComputeEffectiveEntitlementAsync(_employeeId, _leaveTypeId, 2026);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BaseEntitlementDays.Should().Be(0);
        result.Value.ProratedEntitlementDays.Should().Be(0);
        result.Value.Source.Should().Be("probation_ineligible");
    }

    [Fact]
    public async Task ComputeEffective_ProbationEmployee_EligibleType_ReturnsEntitlement_BR3()
    {
        // Set employee to probation.
        using (var db = CreateDbContext())
        {
            var emp = await db.Employees.FindAsync(_employeeId);
            emp!.Status = EmployeeStatus.Probation;
            await db.SaveChangesAsync();
        }

        var svc = CreateService();
        // Sick Leave IS probation eligible.
        var result = await svc.ComputeEffectiveEntitlementAsync(_employeeId, _leaveTypeSickId, 2026);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BaseEntitlementDays.Should().Be(7); // Sick leave default
        result.Value.ProratedEntitlementDays.Should().Be(7);
    }

    // ══════════════════════════════════════════════════════════════
    //  BUG-124: batch prorated entitlement resolution == per-pair
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ComputeProratedEntitlementsBatch_MatchesPerPair_IncludingMidYearJoiner_BUG124()
    {
        // BUG-124: the batch resolver must produce byte-identical prorated values to the per-pair
        // ComputeEffectiveEntitlementAsync it replaces in the reports. Exercise all three resolution
        // branches (override / rule / leave-type default) plus a mid-year joiner whose proration != full.
        //
        //  - John (joined 2026-01-01): annual has a dept rule (20) AND a per-employee override (30) →
        //    override wins → 30 full-year; sick falls back to the default 7.
        //  - Jane (joined 2026-07-01): annual takes the dept rule (20) pro-rated for a mid-year joiner;
        //    sick takes the default 7 pro-rated.
        var janeId = Guid.NewGuid();
        using (var db = CreateDbContext())
        {
            db.Employees.Add(new Employee
            {
                Id = janeId, TenantId = _tenantId, EmployeeNo = "EMP-0002",
                FirstName = "Jane", LastName = "Roe", Email = "jane@test.com",
                DateOfJoining = new DateTime(2026, 7, 1),
                DepartmentId = _departmentId, JobTitleId = _jobTitleId,
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.CreateRuleAsync(MakeRuleRequest());               // Engineering annual rule = 20 days
        await svc.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = _employeeId, LeaveTypeId = _leaveTypeId, LeaveYear = 2026,
            EntitlementDays = 30, Reason = "Override John annual",
        });

        List<Employee> employees;
        List<LeaveType> leaveTypes;
        using (var db = CreateDbContext())
        {
            employees = db.Employees.AsNoTracking().OrderBy(e => e.EmployeeNo).ToList();
            leaveTypes = db.LeaveTypes.AsNoTracking().OrderBy(l => l.Name).ToList();
        }

        var batch = await svc.ComputeProratedEntitlementsBatchAsync(employees, leaveTypes, 2026);

        // Every (employee × leave type) pair must equal the per-pair path exactly.
        foreach (var e in employees)
        {
            foreach (var lt in leaveTypes)
            {
                var perPair = await svc.ComputeEffectiveEntitlementAsync(e.Id, lt.Id, 2026);
                perPair.IsSuccess.Should().BeTrue();
                batch[(e.Id, lt.Id)].Should().Be(perPair.Value!.ProratedEntitlementDays,
                    $"batch must equal per-pair for {e.EmployeeNo} / {lt.Name}");
            }
        }

        // Explicit anchors so proration is demonstrably exercised (not all pairs full-year):
        batch[(_employeeId, _leaveTypeId)].Should().Be(30m);   // John: override 30, full year
        batch[(janeId, _leaveTypeId)].Should().Be(10.00m);     // Jane (Jul-1): ISSUE-034 month-fraction 20 × 6/12 = 10.00
    }

    // ══════════════════════════════════════════════════════════════
    //  Tenant Isolation
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TenantIsolation_RulesNotVisibleAcrossTenants()
    {
        // Create rule in tenant A.
        var svc = CreateService();
        await svc.CreateRuleAsync(MakeRuleRequest());

        // Switch to tenant B.
        var tenantBContext = Substitute.For<ITenantContext>();
        tenantBContext.TenantId.Returns(_otherTenantId);
        tenantBContext.IsResolved.Returns(true);

        var dbB = TestDbContextFactory.Create(tenantBContext, _dbName);
        var svcB = new LeaveEntitlementService(dbB, tenantBContext, _currentUser, _logger,
            new TenantLeaveYearResolver(dbB, tenantBContext));

        var result = await svcB.GetRulesAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════
    //  Accrual Processing (Hangfire Job)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessAccruals_CreatesLedgerEntry()
    {
        var svc = CreateService();
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);

        // Check ledger entry was created.
        using var db = CreateDbContext();
        var entries = db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _employeeId && l.LeaveTypeId == _leaveTypeId && l.LeaveYear == 2026)
            .ToList();

        entries.Should().HaveCount(1);
        entries[0].EntryType.Should().Be(LedgerEntryType.Accrual);
        entries[0].Amount.Should().Be(14); // Default annual entitlement, full year.
        entries[0].BalanceAfter.Should().Be(14);
    }

    [Fact]
    public async Task ProcessAccruals_DoesNotDuplicate()
    {
        var svc = CreateService();
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId); // Run again.

        using var db = CreateDbContext();
        var entries = db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _employeeId && l.LeaveTypeId == _leaveTypeId && l.LeaveYear == 2026)
            .ToList();

        entries.Should().HaveCount(1); // Not duplicated.
    }

    [Fact]
    public async Task ProcessAccruals_SkipsProbationIneligible_BR3()
    {
        // Set employee to probation.
        using (var db = CreateDbContext())
        {
            var emp = await db.Employees.FindAsync(_employeeId);
            emp!.Status = EmployeeStatus.Probation;
            await db.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId); // Annual Leave is not probation eligible.

        using var dbRead = CreateDbContext();
        var entries = dbRead.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _employeeId && l.LeaveTypeId == _leaveTypeId)
            .ToList();

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAccruals_AccruesProbationEligible_BR3()
    {
        // Set employee to probation.
        using (var db = CreateDbContext())
        {
            var emp = await db.Employees.FindAsync(_employeeId);
            emp!.Status = EmployeeStatus.Probation;
            await db.SaveChangesAsync();
        }

        var svc = CreateService();
        await svc.ProcessAccrualsAsync(2026, _leaveTypeSickId); // Sick Leave IS probation eligible.

        using var dbRead = CreateDbContext();
        var entries = dbRead.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _employeeId && l.LeaveTypeId == _leaveTypeSickId)
            .ToList();

        entries.Should().HaveCount(1);
        entries[0].Amount.Should().Be(7);
    }

    [Fact]
    public async Task ProcessAccruals_UsesRuleEntitlement_WhenRuleExists()
    {
        var svc = CreateService();
        // Create a rule giving 25 days instead of the default 14.
        await svc.CreateRuleAsync(MakeRuleRequest() with { EntitlementDays = 25 });

        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);

        using var db = CreateDbContext();
        var entry = db.LeaveLedgerEntries
            .First(l => l.EmployeeId == _employeeId && l.LeaveTypeId == _leaveTypeId && l.LeaveYear == 2026);

        entry.Amount.Should().Be(25);
    }

    [Fact]
    public async Task ProcessAccruals_UsesOverride_WhenOverrideExists()
    {
        var svc = CreateService();
        await svc.CreateRuleAsync(MakeRuleRequest() with { EntitlementDays = 20 });
        await svc.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId,
            LeaveYear = 2026,
            EntitlementDays = 30,
            Reason = "Special",
        });

        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);

        using var db = CreateDbContext();
        var entry = db.LeaveLedgerEntries
            .First(l => l.EmployeeId == _employeeId && l.LeaveTypeId == _leaveTypeId && l.LeaveYear == 2026);

        entry.Amount.Should().Be(30); // Override wins.
    }

    // ══════════════════════════════════════════════════════════════
    //  Validation
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateRule_NegativeEntitlement_Fails()
    {
        var svc = CreateService();
        var result = await svc.CreateRuleAsync(MakeRuleRequest() with { EntitlementDays = -5 });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("negative");
    }

    [Fact]
    public async Task CreateRule_InvalidEmploymentType_Fails()
    {
        var svc = CreateService();
        var result = await svc.CreateRuleAsync(MakeRuleRequest() with { EmploymentType = "InvalidType" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("employment type");
    }

    [Fact]
    public async Task CreateRule_TenureMinGteMax_Fails()
    {
        var svc = CreateService();
        var result = await svc.CreateRuleAsync(MakeRuleRequest() with
        {
            TenureMinMonths = 24,
            TenureMaxMonths = 12,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("tenure");
    }

    [Fact]
    public async Task UpsertOverride_NegativeEntitlement_Fails()
    {
        var svc = CreateService();
        var result = await svc.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = _employeeId,
            LeaveTypeId = _leaveTypeId,
            LeaveYear = 2026,
            EntitlementDays = -5,
            Reason = "Test",
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("negative");
    }

    [Fact]
    public async Task CreateRule_TenantNotResolved_Fails()
    {
        _tenantContext.IsResolved.Returns(false);
        var svc = CreateService();
        var result = await svc.CreateRuleAsync(MakeRuleRequest());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context");
    }

    // ══════════════════════════════════════════════════════════════
    //  BUG-118: recalc-on-rule-edit — enqueue + Adjusted ledger delta
    // ══════════════════════════════════════════════════════════════

    private async Task<List<LeaveLedger>> LedgerFor(Guid leaveTypeId, int year)
    {
        await using var db = CreateDbContext();
        return await db.LeaveLedgerEntries
            .Where(l => l.EmployeeId == _employeeId && l.LeaveTypeId == leaveTypeId && l.LeaveYear == year)
            .OrderBy(l => l.OccurredAt).ThenBy(l => l.CreatedAt)
            .ToListAsync();
    }

    [Fact]
    public async Task UpdateRule_enqueues_entitlement_recalc_bug118()
    {
        var scheduler = Substitute.For<ILeaveEntitlementRecalcJobScheduler>();
        var svc = CreateService(scheduler);
        var created = await svc.CreateRuleAsync(MakeRuleRequest());
        created.IsSuccess.Should().BeTrue();

        var updated = await svc.UpdateRuleAsync(created.Value!.Id, MakeRuleRequest(entitlementDays: 25));
        updated.IsSuccess.Should().BeTrue();

        // A rule edit must enqueue a tenant-scoped recalc for THIS leave type and the CURRENT leave year
        // (no tenant seeded → calendar fiscal year → the current UTC year). Create does NOT enqueue.
        scheduler.Received(1).Enqueue(
            _tenantId, Arg.Any<string>(), Arg.Is<int>(y => y == DateTime.UtcNow.Year), _leaveTypeId);
    }

    [Fact]
    public async Task Recalculate_writes_positive_delta_when_rule_increased_bug118()
    {
        var svc = CreateService();
        var created = await svc.CreateRuleAsync(MakeRuleRequest(entitlementDays: 20));
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);                                   // accrue 20
        await svc.UpdateRuleAsync(created.Value!.Id, MakeRuleRequest(entitlementDays: 25));   // rule → 25

        await svc.RecalculateEntitlementsAsync(2026, _leaveTypeId);

        var adjustment = (await LedgerFor(_leaveTypeId, 2026))
            .Single(l => l.EntryType == LedgerEntryType.Adjusted);
        adjustment.Amount.Should().Be(5m);
        adjustment.BalanceAfter.Should().Be(25m);   // 20 accrued + 5 delta
        adjustment.Description.Should().StartWith("Entitlement recalculation");
    }

    [Fact]
    public async Task Recalculate_writes_negative_delta_when_rule_decreased_bug118()
    {
        var svc = CreateService();
        var created = await svc.CreateRuleAsync(MakeRuleRequest(entitlementDays: 20));
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);                                   // accrue 20
        await svc.UpdateRuleAsync(created.Value!.Id, MakeRuleRequest(entitlementDays: 15));   // rule → 15

        await svc.RecalculateEntitlementsAsync(2026, _leaveTypeId);

        var adjustment = (await LedgerFor(_leaveTypeId, 2026))
            .Single(l => l.EntryType == LedgerEntryType.Adjusted);
        adjustment.Amount.Should().Be(-5m);
        adjustment.BalanceAfter.Should().Be(15m);
    }

    [Fact]
    public async Task Recalculate_is_idempotent_bug118()
    {
        var svc = CreateService();
        var created = await svc.CreateRuleAsync(MakeRuleRequest(entitlementDays: 20));
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);
        await svc.UpdateRuleAsync(created.Value!.Id, MakeRuleRequest(entitlementDays: 25));

        await svc.RecalculateEntitlementsAsync(2026, _leaveTypeId);  // writes +5
        await svc.RecalculateEntitlementsAsync(2026, _leaveTypeId);  // must add nothing (granted now == target)

        (await LedgerFor(_leaveTypeId, 2026))
            .Count(l => l.EntryType == LedgerEntryType.Adjusted).Should().Be(1);
    }

    [Fact]
    public async Task Recalculate_skips_employee_not_yet_accrued_bug118()
    {
        var svc = CreateService();
        var created = await svc.CreateRuleAsync(MakeRuleRequest(entitlementDays: 20));
        await svc.UpdateRuleAsync(created.Value!.Id, MakeRuleRequest(entitlementDays: 25)); // never accrued

        await svc.RecalculateEntitlementsAsync(2026, _leaveTypeId);

        // No Accrual row → nothing to adjust; the accrual run will credit them at the new amount.
        (await LedgerFor(_leaveTypeId, 2026)).Should().BeEmpty();
    }

    [Fact]
    public async Task Recalculate_does_not_count_a_manual_adjustment_as_rule_grant_bug118()
    {
        var svc = CreateService();
        var created = await svc.CreateRuleAsync(MakeRuleRequest(entitlementDays: 20));
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);   // accrue 20

        // A manual admin adjustment of +3 (NO recalc sentinel in its Description).
        await using (var db = CreateDbContext())
        {
            db.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = LedgerEntryType.Adjusted,
                EmployeeId = _employeeId, LeaveTypeId = _leaveTypeId, LeaveYear = 2026,
                Amount = 3m, BalanceAfter = 23m, Description = "Manual bonus by HR",
                OccurredAt = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        await svc.UpdateRuleAsync(created.Value!.Id, MakeRuleRequest(entitlementDays: 25)); // rule → 25
        await svc.RecalculateEntitlementsAsync(2026, _leaveTypeId);

        var entries = await LedgerFor(_leaveTypeId, 2026);
        entries.Should().Contain(l => l.Description == "Manual bonus by HR" && l.Amount == 3m); // survives
        var recalc = entries.Single(l =>
            l.EntryType == LedgerEntryType.Adjusted && l.Description!.StartsWith("Entitlement recalculation"));
        // target 25 − rule-granted 20 (the manual +3 is NOT counted) = +5, not +2.
        recalc.Amount.Should().Be(5m);
    }

    [Fact]
    public async Task Recalculate_honors_employee_override_writes_no_adjustment_bug118()
    {
        // AC-3: a per-employee override wins over the rule. Editing the RULE must NOT touch an overridden
        // employee — a recalc that resolved the rule instead of the override would write a spurious negative
        // delta that silently CLOBBERS the override (a BUG-003-adjacent corruption).
        var svc = CreateService();
        var rule = await svc.CreateRuleAsync(MakeRuleRequest(entitlementDays: 20));
        await svc.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = _employeeId, LeaveTypeId = _leaveTypeId, LeaveYear = 2026,
            EntitlementDays = 30, Reason = "Exceptional contribution",
        });
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);                                // accrues the override (30)
        await svc.UpdateRuleAsync(rule.Value!.Id, MakeRuleRequest(entitlementDays: 25));   // rule → 25 (irrelevant to this employee)

        await svc.RecalculateEntitlementsAsync(2026, _leaveTypeId);

        // target = override 30, rule-granted = accrued 30 → delta 0 → NO adjustment.
        (await LedgerFor(_leaveTypeId, 2026))
            .Any(l => l.EntryType == LedgerEntryType.Adjusted).Should().BeFalse();
    }

    [Fact]
    public async Task Recalculate_prorates_delta_for_mid_year_joiner_bug118()
    {
        // A mid-year joiner's recalc delta must be PRORATED (via CalculateProRata), not the full rule delta.
        var joinerId = await SeedEmployee(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var svc = CreateService();
        var rule = await svc.CreateRuleAsync(MakeRuleRequest(entitlementDays: 20));
        await svc.ProcessAccrualsAsync(2026, _leaveTypeId);                                // both accrue (joiner prorated)
        await svc.UpdateRuleAsync(rule.Value!.Id, MakeRuleRequest(entitlementDays: 24));   // rule → 24

        await svc.RecalculateEntitlementsAsync(2026, _leaveTypeId);

        await using var db = CreateDbContext();
        var joinerAdj = await db.LeaveLedgerEntries.SingleAsync(l =>
            l.EmployeeId == joinerId && l.LeaveTypeId == _leaveTypeId && l.EntryType == LedgerEntryType.Adjusted);
        // A full (non-prorated) delta would be 24 − 20 = 4; a July joiner's prorated delta is strictly less.
        joinerAdj.Amount.Should().BeGreaterThan(0m);
        joinerAdj.Amount.Should().BeLessThan(4m);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private async Task<Guid> SeedEmployee(DateTime dateOfJoining)
    {
        using var db = CreateDbContext();
        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            EmployeeNo = $"EMP-{Guid.NewGuid().ToString()[..4]}",
            FirstName = "Mid",
            LastName = "Year",
            Email = $"mid-{Guid.NewGuid().ToString()[..4]}@test.com",
            DateOfJoining = dateOfJoining,
            DepartmentId = _departmentId,
            JobTitleId = _jobTitleId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    // ── ISSUE-033: entitlement-rule validation gaps (TC-LV-038 steps 4, 7, 11) ──

    [Fact]
    public async Task CreateRule_MaxDays_AtBoundary_Succeeds_ISSUE033()
    {
        var svc = CreateService();
        var result = await svc.CreateRuleAsync(MakeRuleRequest() with { EntitlementDays = 365 });

        result.IsSuccess.Should().BeTrue();
        result.Value!.EntitlementDays.Should().Be(365);
    }

    [Fact]
    public async Task CreateRule_ExceedsMaxDays_Fails_ISSUE033()
    {
        var svc = CreateService();
        var result = await svc.CreateRuleAsync(MakeRuleRequest() with { EntitlementDays = 999.99m });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("cannot exceed 365");

        using var db = CreateDbContext();
        db.LeaveEntitlementRules.Any().Should().BeFalse("an over-cap rule must not be persisted");
    }

    [Fact]
    public async Task CreateRule_DuplicateDimensions_Fails_ISSUE033()
    {
        var svc = CreateService();
        (await svc.CreateRuleAsync(MakeRuleRequest() with { EntitlementDays = 20 }))
            .IsSuccess.Should().BeTrue();

        // Same (leaveType + department + jobTitle + ...) combination, different days — a duplicate.
        var second = await svc.CreateRuleAsync(MakeRuleRequest() with { EntitlementDays = 25 });

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(400);
        second.Error.Should().Contain("already exists");

        using var db = CreateDbContext();
        db.LeaveEntitlementRules.Count().Should().Be(1, "the duplicate rule must not be persisted");
    }

    [Fact]
    public async Task CreateRule_UnknownJobLevel_Fails_ISSUE033()
    {
        var svc = CreateService();
        var result = await svc.CreateRuleAsync(MakeRuleRequest() with { JobLevelId = Guid.NewGuid() });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Job level not found");

        using var db = CreateDbContext();
        db.LeaveEntitlementRules.Any().Should().BeFalse("a rule with an unknown job level must not be persisted");
    }

    private UpsertLeaveEntitlementRuleRequest MakeRuleRequest(
        decimal entitlementDays = 20,
        int priority = 1) => new()
    {
        LeaveTypeId = _leaveTypeId,
        DepartmentId = _departmentId,
        JobTitleId = null,
        EmploymentType = null,
        TenureMinMonths = null,
        TenureMaxMonths = null,
        EntitlementDays = entitlementDays,
        Priority = priority,
        EffectiveFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        EffectiveTo = null,
    };
}
