// ============================================================================
// ISSUE-180: leave-encashment daily-rate denominator must be SHIFT working-days (matching the payroll run),
// NOT calendar days. daily_rate = monthly_basic / working_days.
//
// Golden case (September 2025, Sep 1 = Monday, Mon-Fri shift → 22 working days): 22000 / 22 = 1000/day.
// The old calendar-day denominator (30) gave 22000 / 30 = 733.33/day.
//
// PROVIDER: InMemory through the real LeaveEncashmentService + AppDbContext. The adjustment-creation
// collaborator (US-PAY-007) is faked so the test isolates the working-days math; ShiftScheduleResolver runs
// identically on InMemory and Npgsql (LINQ only, no raw SQL).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Integration;

public sealed class LeaveEncashmentProrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenant = Guid.NewGuid();

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

    /// <summary>Captures the created adjustment so the test can assert the encashment amount reached it.</summary>
    private sealed class FakeAdjustmentService : IPayrollAdjustmentService
    {
        public CreateAdjustmentInput? Captured { get; private set; }

        public Task<Result<CreatePayrollAdjustmentResult>> CreateAsync(
            CreateAdjustmentInput input, CancellationToken cancellationToken = default)
        {
            Captured = input;
            var dto = new PayrollAdjustmentDto(
                Id: Guid.NewGuid(), EmployeeId: input.EmployeeId, EmployeeNo: "E1", EmployeeName: "E One",
                AdjustmentType: input.AdjustmentType, Amount: input.Amount, Description: input.Description,
                ApplicablePayMonth: input.ApplicablePayMonth, ApplicablePayYear: input.ApplicablePayYear,
                IsTaxable: input.IsTaxable, IsRecurring: false, RecurrenceEndMonth: null, RecurrenceEndYear: null,
                Status: "Pending", AppliedInPayrollRunId: null, ReferencePayrollSlipId: null,
                HasSupportingDocument: false, RecurringSeriesId: null, CreatedAt: DateTime.UtcNow,
                NegativeNetWarning: false);
            return Task.FromResult(Result<CreatePayrollAdjustmentResult>.Success(
                new CreatePayrollAdjustmentResult(dto, 0, false, null, null)));
        }

        public Task<Result> CancelAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PayrollAdjustmentPageDto>> ListAsync(AdjustmentListFilter f, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PayrollAdjustmentDto>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<BulkAdjustmentResultDto>> BulkCreateAsync(int m, int y, Stream s, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<string>> UploadDocumentAsync(Guid id, Stream c, string fn, string ct2, long len, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AdjustmentDocumentDto>> DownloadDocumentAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new MutableTenantContext { TenantId = _tenant });
    }

    private async Task<Guid> Seed(decimal monthlyBasic)
    {
        using var db = Db();
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenant, EmployeeNo = "E1", FirstName = "E", LastName = "One",
            Email = "e1@t.com", DateOfJoining = new DateTime(2019, 1, 1),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        var componentId = BaseEntity.NewUuidV7();
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = componentId, TenantId = _tenant, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsActive = true, ProcessingOrder = 1,
        });
        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = empId,
            SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = componentId,
            AnnualAmount = monthlyBasic * 12m, MonthlyAmount = monthlyBasic,
            EffectiveFrom = new DateOnly(2019, 1, 1), EffectiveTo = null,
        });

        // Tenant-default Mon-Fri shift → 22 working days in September 2025.
        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, Name = "Standard Mon-Fri",
            Type = ShiftType.Single, WorkingDays = new List<int> { 1, 2, 3, 4, 5 },
            IsDefault = true, IsActive = true,
        });

        await db.SaveChangesAsync();
        return empId;
    }

    [Fact]
    [Trait("Issue", "ISSUE-180")]
    public async Task Encashment_DailyRate_UsesShiftWorkingDays_MatchingTheRun()
    {
        var empId = await Seed(22_000m);
        var adjustments = new FakeAdjustmentService();
        var service = new LeaveEncashmentService(
            Db(), new MutableTenantContext { TenantId = _tenant }, adjustments,
            NullLogger<LeaveEncashmentService>.Instance);

        var result = await service.ProcessAsync(
            new LeaveEncashmentInput(empId, LeaveTypeId: null, EligibleDays: 5m, PayMonth: 9, PayYear: 2025, IsTaxable: true));

        result.IsSuccess.Should().BeTrue(result.Error);
        // 22000 / 22 shift working-days = 1000/day (the run's denominator), NOT 22000/30 calendar = 733.33.
        result.Value!.DailyRate.Should().Be(1_000m);
        result.Value.DailyRate.Should().NotBe(Math.Round(22_000m / 30m, 2, MidpointRounding.AwayFromZero));
        // amount = eligible_days * daily_rate = 5 * 1000 = 5000, and it flows into the created adjustment.
        result.Value.Amount.Should().Be(5_000m);
        adjustments.Captured!.Amount.Should().Be(5_000m);
    }
}
