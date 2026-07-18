// ============================================================================
// BUG-008 regression: the employee-count plan limit must resolve with precedence
// OVERRIDE > PLAN > SNAPSHOT (via PlanLimitResolver), not just read the
// Tenant.MaxEmployees snapshot (which ignored plan changes + per-tenant overrides).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeePlanLimitResolutionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private Guid _deptId;
    private Guid _jtId;

    public EmployeePlanLimitResolutionTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private EmployeeService Service()
    {
        var hr = Substitute.For<ICurrentUser>();
        hr.IsAuthenticated.Returns(true);
        hr.UserId.Returns(Guid.NewGuid());
        hr.Email.Returns("hr@acme.com");
        var cf = Substitute.For<ICustomFieldService>();
        cf.ValidateCustomFieldValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(HRM.Application.Common.Models.Result.Success());
        return new EmployeeService(Db(), _tenantContext, hr,
            Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(), cf,
            Substitute.For<IPayrollAuditLogger>(), NullLogger<EmployeeService>.Instance);
    }

    private async Task SeedAsync(int? snapshot, int? planMax, long? overrideValue)
    {
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "acme", Name = "Acme Corp", Status = TenantStatus.Active,
            PlanId = "pro", MaxEmployees = snapshot,
        });
        // Seed the plan row (Code matches Tenant.PlanId); MaxEmployees may be null (no plan cap).
        db.SubscriptionPlans.Add(new SubscriptionPlan { Id = Guid.NewGuid(), Code = "pro", Name = "Pro", MaxEmployees = planMax });
        if (overrideValue is not null)
            db.PlanLimitOverrides.Add(new PlanLimitOverride
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, LimitKey = PlanLimitKeys.MaxEmployees,
                Value = overrideValue, CreatedAt = DateTime.UtcNow,
            });
        _deptId = Guid.NewGuid();
        _jtId = Guid.NewGuid();
        db.Departments.Add(new Department { Id = _deptId, TenantId = _tenantId, Name = "Eng", Code = "ENG", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = _jtId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true });
        await db.SaveChangesAsync();
    }

    private Task<HRM.Application.Common.Models.Result<EmployeeDto>> CreateAsync(string email) =>
        Service().CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "T", LastName = "U", Email = email,
            DateOfJoining = DateTime.UtcNow, DepartmentId = _deptId, JobTitleId = _jtId,
            EmploymentType = EmploymentType.FullTime,
        });

    [Fact]
    public async Task PlanValue_WinsOverSnapshot()
    {
        // snapshot=5 but plan cap=2 → effective 2.
        await SeedAsync(snapshot: 5, planMax: 2, overrideValue: null);

        (await CreateAsync("a@acme.com")).IsSuccess.Should().BeTrue();
        (await CreateAsync("b@acme.com")).IsSuccess.Should().BeTrue();
        var third = await CreateAsync("c@acme.com");

        third.IsFailure.Should().BeTrue("the plan cap (2) must win over the snapshot (5)");
        third.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Override_WinsOverPlan()
    {
        // plan cap=2 but a per-tenant override=1 → effective 1.
        await SeedAsync(snapshot: 5, planMax: 2, overrideValue: 1);

        (await CreateAsync("a@acme.com")).IsSuccess.Should().BeTrue();
        var second = await CreateAsync("b@acme.com");

        second.IsFailure.Should().BeTrue("the override (1) must win over the plan (2)");
        second.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Snapshot_UsedWhenPlanHasNoValue()
    {
        // no plan value, no override → fall back to the snapshot (1).
        await SeedAsync(snapshot: 1, planMax: null, overrideValue: null);

        (await CreateAsync("a@acme.com")).IsSuccess.Should().BeTrue();
        var second = await CreateAsync("b@acme.com");

        second.IsFailure.Should().BeTrue("with no plan/override, the snapshot (1) applies");
        second.StatusCode.Should().Be(403);
    }
}
