// ============================================================================
// ISSUE-065 / Phase 2b sibling fix — BUG-246: the attendance⇄payroll "has attendance this month" gate
// (AttendancePayrollService.EmployeesWithAttendanceRecordsAsync) windows the AttendanceLogs scan by
// tenant-LOCAL month boundaries, so a punch that lands in a different month locally than in UTC is
// attributed to the correct local month.
//
// Exercised through the full composed MediatR pipeline (mirrors AttendancePayrollIntegrationTests)
// because GetPayrollDataAsync fans out to the real AttendanceSummaryService for the core rollup — the
// month gate is only meaningfully observable at the public-method boundary (a row is emitted for the
// employee iff they have real records in the local month). InMemory provider, same rationale as the
// other attendance integration tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker).
//
// SCENARIO: a punch at 2026-04-01T02:00:00Z is 2026-03-31 22:00 in New York (EDT, UTC-4). The employee
// must show up in the MARCH pull and NOT the APRIL pull — the exact inverse of a UTC-only gate, which
// would place the 02:00Z punch in April. A UTC-tenant control asserts the pre-fix behavior is unchanged.
//
// Traceability: @TC-ATT-TZ-025 (non-UTC month gate → local month) · @TC-ATT-TZ-026 (UTC control).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class AttendancePayrollTimezoneTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _empId = Guid.NewGuid();

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

    private sealed class InMemoryExportStorage : IReportExportStorage
    {
        public Task<string> SaveAsync(Guid tenantId, Guid reportId, string fileName,
            string contentType, byte[] content, CancellationToken cancellationToken = default)
            => Task.FromResult($"mem://{tenantId}/{reportId}/{fileName}");
    }

    private IMediator BuildPipeline()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(_userId);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Permissions.Returns(Array.Empty<string>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IReportExportStorage, InMemoryExportStorage>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddScoped<IAttendancePayrollService, AttendancePayrollService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetPayrollDataQuery).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private AppDbContext Db()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    /// <summary>Seeds the tenant (with its zone), one active employee, and a single boundary punch.</summary>
    private void Seed(string timeZone, DateTime clockInUtc)
    {
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Subdomain = "tz-" + _tenantId.ToString("N")[..8],
            Name = "TZ Payroll Tenant",
            TimeZone = timeZone,
        });
        db.Employees.Add(new Employee
        {
            Id = _empId, TenantId = _tenantId, UserId = _userId,
            EmployeeNo = "EMP-0001", FirstName = "Test", LastName = "Emp", Email = "e@t.com",
            Gender = Gender.Male, DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = _empId,
            ClockIn = clockInUtc, ClockOut = clockInUtc.AddMinutes(480),
            TotalWorkMinutes = 480, Status = "COMPLETE", Source = "WEB",
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Payroll_NonUtcTenant_PunchAfterUtcMonthBoundary_AttributedToLocalMonth_BUG246()
    {
        // 2026-04-01T02:00:00Z is 2026-03-31 22:00 in New York (EDT, UTC-4). The month gate must attribute
        // it to MARCH (employee has records → row emitted) and NOT April (no local records → row omitted).
        Seed("America/New_York", new DateTime(2026, 4, 1, 2, 0, 0, DateTimeKind.Utc));
        var mediator = BuildPipeline();

        var march = await mediator.Send(new GetPayrollDataQuery(2026, 3, null));
        var april = await mediator.Send(new GetPayrollDataQuery(2026, 4, null));

        march.IsSuccess.Should().BeTrue();
        april.IsSuccess.Should().BeTrue();

        march.Value!.Rows.Should().Contain(r => r.EmployeeId == _empId,
            "the 02:00Z-Apr-1 punch is Mar 31 22:00 in New York, so the employee has MARCH attendance");
        april.Value!.Rows.Should().NotContain(r => r.EmployeeId == _empId,
            "for a New York tenant the punch does not fall in the local April window — no row must be fabricated");
    }

    [Fact]
    public async Task Payroll_UtcTenant_PunchAfterUtcMonthBoundary_AttributedToUtcMonth_BUG246Control()
    {
        // Same 02:00Z-Apr-1 instant, but a UTC tenant: the local month IS the UTC month (April). Proves
        // the fix is a no-op for UTC tenants and the New York result came from the zone, not a hardcoded shift.
        Seed("UTC", new DateTime(2026, 4, 1, 2, 0, 0, DateTimeKind.Utc));
        var mediator = BuildPipeline();

        var march = await mediator.Send(new GetPayrollDataQuery(2026, 3, null));
        var april = await mediator.Send(new GetPayrollDataQuery(2026, 4, null));

        april.Value!.Rows.Should().Contain(r => r.EmployeeId == _empId,
            "for a UTC tenant the 02:00Z-Apr-1 punch belongs to the UTC month April");
        march.Value!.Rows.Should().NotContain(r => r.EmployeeId == _empId);
    }
}
