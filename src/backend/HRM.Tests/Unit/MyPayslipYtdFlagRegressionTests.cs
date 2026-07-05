// ============================================================================
// US-PAY-005 / ISSUE-160 (FR-7): the payslip self-service YTD column must be
// gated on the per-tenant Tenant.PayslipYtdEnabled flag (default off) instead of
// a hardcoded false.
//
// PRE-FIX BEHAVIOUR (HEAD): MyPayslipService used
//   private static bool TenantYtdEnabled() => false;
// so YTD was NEVER populated for any tenant — even one that wanted it on.
//
// POST-FIX BEHAVIOUR (under test): TenantYtdEnabledAsync reads
//   Tenant.PayslipYtdEnabled. Flag off  → every component YtdAmount is null.
//                             Flag on   → the existing per-component YTD logic
//                                          (BuildYtdAsync) fires and populates it.
//
// PROVIDER: EF Core InMemory (mirrors the other self-service service unit tests;
// the verify gate runs `dotnet test` with no PostgreSQL / Docker).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class MyPayslipYtdFlagRegressionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _runId = Guid.NewGuid();
    private readonly Guid _slipId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public MyPayslipYtdFlagRegressionTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private MyPayslipService CreateService() =>
        new(CreateDbContext(), _tenantContext, _currentUser, Substitute.For<IFileStorage>());

    /// <summary>
    /// Seeds a tenant (with the YTD flag as given), an employee linked to the current user, a Finalized
    /// payroll run, one payslip for that run, and two slip-detail lines (one earning, one deduction).
    /// </summary>
    private void Seed(bool payslipYtdEnabled)
    {
        using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "acme", Name = "Acme", PayslipYtdEnabled = payslipYtdEnabled,
        });

        db.Employees.Add(new Employee
        {
            Id = _employeeId, TenantId = _tenantId, UserId = _userId, EmployeeNo = "EMP-0001",
            FirstName = "John", LastName = "Doe", Email = "john@acme.com",
            Status = EmployeeStatus.Active, DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            IsDeleted = false,
        });

        db.PayrollRuns.Add(new PayrollRun
        {
            Id = _runId, TenantId = _tenantId, PayMonth = 6, PayYear = 2026,
            Status = PayrollRunStatus.Finalized,
        });

        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = _slipId, TenantId = _tenantId, PayrollRunId = _runId, EmployeeId = _employeeId,
            PayMonth = 6, PayYear = 2026,
            GrossEarnings = 5000m, TotalDeductions = 500m, NetSalary = 4500m,
            WorkingDays = 30m, PaidDays = 30m, LopDays = 0m,
        });

        db.PayrollSlipDetails.Add(new PayrollSlipDetail
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, PayrollSlipId = _slipId,
            SalaryComponentId = Guid.NewGuid(), ComponentName = "Basic",
            ComponentType = nameof(SalaryComponentType.Earning), Amount = 5000m,
        });
        db.PayrollSlipDetails.Add(new PayrollSlipDetail
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, PayrollSlipId = _slipId,
            SalaryComponentId = Guid.NewGuid(), ComponentName = "EPF",
            ComponentType = nameof(SalaryComponentType.Deduction), Amount = 500m,
        });

        db.SaveChanges();
    }

    [Theory]
    [InlineData(false)] // flag OFF (default) → YTD omitted (null on every line)
    [InlineData(true)]  // flag ON            → YTD populated by the existing BuildYtdAsync logic
    public async Task Payslip_YtdEnabledFlag_GatesYtd_ISSUE160(bool ytdEnabled)
    {
        Seed(payslipYtdEnabled: ytdEnabled);

        var result = await CreateService().GetMineAsync(_slipId);

        result.IsSuccess.Should().BeTrue(result.Error);
        var detail = result.Value!;
        var basic = detail.Earnings.Single(c => c.ComponentName == "Basic");
        var epf = detail.Deductions.Single(c => c.ComponentName == "EPF");

        if (ytdEnabled)
        {
            // Single slip in the year ⇒ YTD equals the current-period amount, and is present (non-null).
            basic.YtdAmount.Should().Be(5000m);
            epf.YtdAmount.Should().Be(500m);
        }
        else
        {
            basic.YtdAmount.Should().BeNull("YTD is gated off when the tenant has not enabled it");
            epf.YtdAmount.Should().BeNull();
        }
    }
}
