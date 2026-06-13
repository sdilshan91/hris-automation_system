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

    private LeaveEntitlementService CreateService()
    {
        var dbContext = TestDbContextFactory.Create(_tenantContext, _dbName);
        return new LeaveEntitlementService(dbContext, _tenantContext, _currentUser, _logger);
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
        var requests = new List<UpsertLeaveEntitlementRuleRequest>
        {
            MakeRuleRequest() with { EntitlementDays = 15 },
            MakeRuleRequest() with { EntitlementDays = 20 },
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
        // Default 14, pro-rated: 184/365 * 14 = 7.06
        result.Value!.ProratedEntitlementDays.Should().Be(7.06m);
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
        var svcB = new LeaveEntitlementService(dbB, tenantBContext, _currentUser, _logger);

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
    //  Helpers
    // ══════════════════════════════════════════════════════════════

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
