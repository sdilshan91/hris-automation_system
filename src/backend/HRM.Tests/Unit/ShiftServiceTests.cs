// ============================================================================
// US-ATT-005: Shift management + assignment service unit tests.
// Covers create/update/list (AC-1), duplicate-name rejection (AC-1/BR-1),
// delete-prevention-when-assigned with the EXACT message (AC-4/FR-6), clone (FR-8),
// bulk assign (AC-2/FR-3), effective-dating no-overlap (AC-3/BR-2), default-shift
// fallback (FR-5), rotating-shift resolution for given dates (AC-5/FR-7), and the
// night-shift case (§10). Uses the EF Core InMemory provider (mirrors AttendanceServiceTests).
//
// NOTE: InMemory does not enforce the PostgreSQL partial unique indexes; uniqueness is
// asserted via the service-level checks. PG-specific schema is validated by the `migrations` CI job.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ShiftServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ShiftService> _logger;

    private Guid _employeeId;
    private Guid _employeeId2;

    public ShiftServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.Email.Returns("hr@test.com");

        _logger = Substitute.For<ILogger<ShiftService>>();

        SeedEmployees();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);
    private ShiftService CreateService() => new(CreateDbContext(), _tenantContext, _currentUser, _logger);

    private void SeedEmployees()
    {
        using var db = CreateDbContext();
        db.Employees.AddRange(
            new Employee
            {
                Id = _employeeId = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId,
                EmployeeNo = "EMP-1", FirstName = "John", LastName = "Doe", Email = "j@t.com",
                DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            },
            new Employee
            {
                Id = _employeeId2 = Guid.NewGuid(), TenantId = _tenantId, UserId = Guid.NewGuid(),
                EmployeeNo = "EMP-2", FirstName = "Jane", LastName = "Roe", Email = "jane@t.com",
                DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });
        db.SaveChanges();
    }

    private static ShiftRequest SingleShift(
        string name = "Morning", string start = "09:00", string end = "17:00") => new()
    {
        Name = name,
        Type = ShiftType.Single,
        StartTime = start,
        EndTime = end,
        BreakDurationMinutes = 60,
        GracePeriodMinutes = 10,
        WorkingDays = new[] { 1, 2, 3, 4, 5 },
    };

    // ── Create / list (AC-1) ───────────────────────────────────────────

    [Fact]
    public async Task Create_persists_shift_and_returns_dto()
    {
        var result = await CreateService().CreateAsync(SingleShift());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Morning");
        result.Value.StartTime.Should().Be("09:00");
        result.Value.EndTime.Should().Be("17:00");
        result.Value.WorkingDays.Should().Equal(1, 2, 3, 4, 5);

        var all = await CreateService().GetAllAsync();
        all.Value!.Should().ContainSingle(s => s.Name == "Morning");
    }

    [Fact]
    public async Task Update_changes_fields()
    {
        var created = await CreateService().CreateAsync(SingleShift());
        var id = created.Value!.Id;

        var updated = await CreateService().UpdateAsync(id, SingleShift("Morning", "08:00", "16:00"));

        updated.IsSuccess.Should().BeTrue();
        updated.Value!.StartTime.Should().Be("08:00");
    }

    [Fact]
    public async Task Create_duplicate_name_is_rejected()
    {
        await CreateService().CreateAsync(SingleShift("Day"));

        var dup = await CreateService().CreateAsync(SingleShift("Day"));

        dup.IsFailure.Should().BeTrue();
        dup.StatusCode.Should().Be(409);
        dup.ErrorCode.Should().Be("duplicate_name");
    }

    // ── Delete prevention (AC-4/FR-6) ──────────────────────────────────

    [Fact]
    public async Task Delete_blocked_when_assigned_with_exact_message()
    {
        var created = await CreateService().CreateAsync(SingleShift("Ops"));
        var shiftId = created.Value!.Id;

        await CreateService().AssignAsync(
            shiftId, new[] { _employeeId, _employeeId2 },
            DateOnly.FromDateTime(DateTime.UtcNow));

        var delete = await CreateService().DeleteAsync(shiftId);

        delete.IsFailure.Should().BeTrue();
        delete.StatusCode.Should().Be(409);
        delete.ErrorCode.Should().Be("shift_in_use");
        delete.Error.Should().Be(
            "This shift is assigned to 2 employees. Please reassign them before deleting.");
    }

    [Fact]
    public async Task Delete_succeeds_when_no_active_assignment()
    {
        var created = await CreateService().CreateAsync(SingleShift("Temp"));

        var delete = await CreateService().DeleteAsync(created.Value!.Id);

        delete.IsSuccess.Should().BeTrue();
        var all = await CreateService().GetAllAsync();
        all.Value!.Should().NotContain(s => s.Name == "Temp");
    }

    // ── Clone (FR-8) ───────────────────────────────────────────────────

    [Fact]
    public async Task Clone_creates_copy_with_distinct_name()
    {
        var created = await CreateService().CreateAsync(SingleShift("Base"));

        var clone = await CreateService().CloneAsync(created.Value!.Id);

        clone.IsSuccess.Should().BeTrue();
        clone.Value!.Name.Should().Be("Copy of Base");
        clone.Value.Id.Should().NotBe(created.Value.Id);
        clone.Value.IsDefault.Should().BeFalse();
    }

    // ── Bulk assign + effective-dating (AC-2/AC-3/FR-3/BR-2) ───────────

    [Fact]
    public async Task Assign_creates_records_for_all_employees()
    {
        var created = await CreateService().CreateAsync(SingleShift("A"));

        var result = await CreateService().AssignAsync(
            created.Value!.Id, new[] { _employeeId, _employeeId2 },
            DateOnly.FromDateTime(DateTime.UtcNow));

        result.IsSuccess.Should().BeTrue();
        result.Value!.AssignedCount.Should().Be(2);
        result.Value.EmployeeShiftIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task Assign_future_keeps_current_active_and_avoids_overlap()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var shiftA = (await CreateService().CreateAsync(SingleShift("A"))).Value!;
        var shiftB = (await CreateService().CreateAsync(SingleShift("B"))).Value!;

        // Assign A today.
        await CreateService().AssignAsync(shiftA.Id, new[] { _employeeId }, today);
        // Assign B from next week (future).
        var nextWeek = today.AddDays(7);
        await CreateService().AssignAsync(shiftB.Id, new[] { _employeeId }, nextWeek);

        // Today resolves to A; next week resolves to B; no overlapping active pair on any day.
        var resolvedToday = await CreateService().ResolveForEmployeeAsync(_employeeId, today);
        var resolvedNext = await CreateService().ResolveForEmployeeAsync(_employeeId, nextWeek);

        resolvedToday.Value!.Name.Should().Be("A");
        resolvedNext.Value!.Name.Should().Be("B");

        // A's assignment was closed the day before B starts.
        using var db = CreateDbContext();
        var aAssignment = db.EmployeeShifts.Single(es => es.ShiftId == shiftA.Id && es.EmployeeId == _employeeId);
        aAssignment.EffectiveTo.Should().Be(nextWeek.AddDays(-1));
    }

    [Fact]
    public async Task Assign_today_closes_previous_assignment_immediately()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var shiftA = (await CreateService().CreateAsync(SingleShift("A"))).Value!;
        var shiftB = (await CreateService().CreateAsync(SingleShift("B"))).Value!;

        await CreateService().AssignAsync(shiftA.Id, new[] { _employeeId }, today.AddDays(-10));
        await CreateService().AssignAsync(shiftB.Id, new[] { _employeeId }, today);

        var resolved = await CreateService().ResolveForEmployeeAsync(_employeeId, today);
        resolved.Value!.Name.Should().Be("B");

        // Exactly one active assignment for today.
        using var db = CreateDbContext();
        var active = db.EmployeeShifts.Count(es =>
            es.EmployeeId == _employeeId && es.EffectiveFrom <= today
            && (es.EffectiveTo == null || es.EffectiveTo >= today));
        active.Should().Be(1);
    }

    // ── Default fallback (FR-5) ────────────────────────────────────────

    [Fact]
    public async Task Resolve_falls_back_to_default_when_unassigned()
    {
        // Seed a default shift directly.
        using (var db = CreateDbContext())
        {
            db.Shifts.Add(new Shift
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Default",
                Type = ShiftType.Single, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
                WorkingDays = new List<int> { 1, 2, 3, 4, 5 }, IsDefault = true, IsActive = true,
            });
            db.SaveChanges();
        }

        var resolved = await CreateService().ResolveForEmployeeAsync(
            _employeeId, DateOnly.FromDateTime(DateTime.UtcNow));

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value!.Name.Should().Be("Default");
        resolved.Value.IsDefault.Should().BeTrue();
        resolved.Value.EffectiveFrom.Should().BeNull();
    }

    // ── Rotating resolution (AC-5/FR-7) ────────────────────────────────

    [Fact]
    public async Task Rotating_shift_resolves_correct_step_per_date()
    {
        var svc = CreateService();
        var morning = (await svc.CreateAsync(SingleShift("Morning", "06:00", "14:00"))).Value!;
        var evening = (await CreateService().CreateAsync(SingleShift("Evening", "14:00", "22:00"))).Value!;

        var refDate = new DateOnly(2026, 1, 5); // a Monday
        var rotating = new ShiftRequest
        {
            Name = "2-Week Rotation",
            Type = ShiftType.Rotating,
            StartTime = "06:00",
            EndTime = "14:00",
            BreakDurationMinutes = 30,
            GracePeriodMinutes = 5,
            WorkingDays = new[] { 1, 2, 3, 4, 5 },
            Rotation = new RotationRequest
            {
                CycleLengthDays = 14,
                ReferenceStartDate = refDate,
                Steps = new[]
                {
                    new RotationStepRequest { Order = 0, ShiftId = morning.Id, DurationDays = 7 },
                    new RotationStepRequest { Order = 1, ShiftId = evening.Id, DurationDays = 7 },
                },
            },
        };

        var rot = (await CreateService().CreateAsync(rotating)).Value!;
        await CreateService().AssignAsync(rot.Id, new[] { _employeeId }, refDate);

        // Day 0..6 → Morning; day 7..13 → Evening; day 14 wraps to Morning.
        (await CreateService().ResolveForEmployeeAsync(_employeeId, refDate)).Value!.Name.Should().Be("Morning");
        (await CreateService().ResolveForEmployeeAsync(_employeeId, refDate.AddDays(6))).Value!.Name.Should().Be("Morning");
        (await CreateService().ResolveForEmployeeAsync(_employeeId, refDate.AddDays(7))).Value!.Name.Should().Be("Evening");
        (await CreateService().ResolveForEmployeeAsync(_employeeId, refDate.AddDays(13))).Value!.Name.Should().Be("Evening");
        (await CreateService().ResolveForEmployeeAsync(_employeeId, refDate.AddDays(14))).Value!.Name.Should().Be("Morning");
    }

    // ── Night shift (§10) ──────────────────────────────────────────────

    [Fact]
    public async Task Night_shift_with_end_before_start_is_allowed()
    {
        var night = SingleShift("Night", "22:00", "06:00");

        var result = await CreateService().CreateAsync(night);

        result.IsSuccess.Should().BeTrue();
        result.Value!.StartTime.Should().Be("22:00");
        result.Value.EndTime.Should().Be("06:00");
    }
}
