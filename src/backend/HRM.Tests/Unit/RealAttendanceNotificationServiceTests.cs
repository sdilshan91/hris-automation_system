// ============================================================================
// US-NTF-006 Phase 6 — RealAttendanceNotificationService unit tests (US-ATT-001/004/006).
//
// The Real service RESOLVES recipients from the DB (the employee / the reporting-line manager / the
// attendance-admin pool) and dispatches BOTH legs (in-app + email) when the recipient has a linked User, else
// email-only against Employee.Email. Load-bearing behaviours genuinely observed here:
//   • EMPLOYEE RECIPIENT — late / regularization-decided / overtime-decided route to the subject employee, both legs.
//   • MANAGER HOP — regularization-requested / overtime-pre-approval-requested route to the employee's
//     ReportsToEmployeeId manager, NOT the employee.
//   • ADMIN POOL — overtime-maxima-exceeded goes to the Attendance.Edit holders ONLY (not the non-holder).
//   • EVENT-KEY MAPPING — approve vs reject map to the *_approved / *_rejected keys.
//   • EMAIL-ONLY FALLBACK — an employee with UserId==null is reached email-only via Employee.Email.
//   • NEVER-THROW — a dispatcher failure must not break the committed attendance write.
//
// PROVIDER: EF Core InMemory AppDbContext (the admin-pool resolution is a real UserTenants⋈UserTenantRoles⋈
// RolePermissions query with IgnoreQueryFilters; the manager hop is a real ReportsToEmployeeId lookup) + a hand
// RecordingDispatcher. Mirrors RealPerformanceNotificationServiceTests' seeding.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealAttendanceNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    // Attendance-admin pool: two users holding Attendance.Edit + one decoy without it.
    private readonly Guid _admin1 = Guid.NewGuid();
    private readonly Guid _admin2 = Guid.NewGuid();
    private readonly Guid _nonAdmin = Guid.NewGuid();

    // Reporting chain: an employee (with a linked user) reporting to a manager (with a linked user).
    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _employeeEmpId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();

    // An employee with NO linked user → email-only fallback.
    private readonly Guid _emailOnlyEmpId = Guid.NewGuid();
    private const string EmailOnlyAddress = "no.user@acme.test";

    private static readonly DateOnly Date = new(2026, 7, 1);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public RealAttendanceNotificationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private RealAttendanceNotificationService Service(INotificationDispatcher dispatcher) =>
        new(Db(), dispatcher, _tenantContext, NullLogger<RealAttendanceNotificationService>.Instance);

    // ── Fake dispatcher ────────────────────────────────────────────────────────────────────────────

    /// <summary>Records dispatched requests per leg; optionally throws to prove the never-throw contract.</summary>
    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        private readonly bool _throw;
        public List<NotificationRequest> InApp { get; } = new();
        public List<NotificationRequest> Email { get; } = new();
        public RecordingDispatcher(bool throwOnDispatch = false) => _throw = throwOnDispatch;

        public Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("in-app dispatch boom");
            InApp.Add(request);
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("email dispatch boom");
            Email.Add(request);
            return Task.CompletedTask;
        }
    }

    // ── Seeding ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Two users holding an Attendance.Edit role + one user without it (the admin pool + a decoy).</summary>
    private async Task SeedAdminPoolAsync()
    {
        using var db = Db();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = "Attendance Admin" });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Attendance.Edit });

        foreach (var uid in new[] { _admin1, _admin2 })
        {
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = uid, TenantId = _tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        }

        // A tenant user WITHOUT Attendance.Edit (a role with no such permission) — must never be notified.
        var plainRoleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = plainRoleId, TenantId = _tenantId, Name = "Employee" });
        var nonUt = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant { Id = nonUt, UserId = _nonAdmin, TenantId = _tenantId, Status = UserTenantStatus.Active });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = nonUt, RoleId = plainRoleId });

        await db.SaveChangesAsync();
    }

    /// <summary>Manager + employee (employee → manager via ReportsToEmployeeId) + an email-only employee.</summary>
    private async Task SeedEmployeesAsync()
    {
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = _managerEmpId, TenantId = _tenantId, FirstName = "Morgan", LastName = "Hale",
            Email = "morgan.hale@acme.test", UserId = _managerUserId,
        });
        db.Employees.Add(new Employee
        {
            Id = _employeeEmpId, TenantId = _tenantId, FirstName = "Jordan", LastName = "Rivera",
            Email = "jordan.rivera@acme.test", UserId = _employeeUserId, ReportsToEmployeeId = _managerEmpId,
        });
        db.Employees.Add(new Employee
        {
            Id = _emailOnlyEmpId, TenantId = _tenantId, FirstName = "Casey", LastName = "Lee",
            Email = EmailOnlyAddress, UserId = null,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Reads a dotted path (e.g. "attendance.expected") out of a dispatched PayloadJson; null when the path is
    /// absent. Used to prove the payload actually POPULATES the (non-tenant) placeholder keys the matching catalog
    /// template renders — a template token can be DECLARED (guarded by NotificationEventCatalogPhase6Tests) yet the
    /// service could still fail to supply its value, rendering a blank email. tenant.* is filled downstream by the
    /// dispatcher, so it is intentionally not asserted here.
    /// </summary>
    private static string? PayloadStr(string payloadJson, string dottedPath)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var el = doc.RootElement;
        foreach (var seg in dottedPath.Split('.'))
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(seg, out el))
                return null;
        }
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            _ => el.ToString(),
        };
    }

    // ── EMPLOYEE RECIPIENT — late → the subject employee, both legs, correct key ──

    [Fact]
    public async Task NotifyLateAsync_DispatchesToTheEmployee_BothLegs()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyLateAsync(
            _employeeEmpId, Date, new TimeOnly(9, 22), new TimeOnly(9, 0));

        dispatcher.InApp.Should().ContainSingle();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.InApp.Single().RecipientUserId.Should().Be(_employeeUserId);
        dispatcher.Email.Single().RecipientUserId.Should().Be(_employeeUserId);
        dispatcher.InApp.Single().EventKey.Should().Be("attendance_late");
        dispatcher.Email.Single().EventKey.Should().Be("attendance_late");
        dispatcher.Email.Single().TenantId.Should().Be(_tenantId);

        // Payload-completeness: the template renders employee.firstName + attendance.date/checkIn/expected;
        // prove each is actually supplied (declared-but-unpopulated → blank email).
        var payload = dispatcher.Email.Single().PayloadJson;
        PayloadStr(payload, "employee.firstName").Should().Be("Jordan");
        PayloadStr(payload, "attendance.date").Should().Be("2026-07-01");
        PayloadStr(payload, "attendance.checkIn").Should().NotBeNullOrEmpty();
        PayloadStr(payload, "attendance.expected").Should().NotBeNullOrEmpty();
    }

    // ── MANAGER HOP — regularization-requested → the manager, NOT the employee ──

    [Fact]
    public async Task NotifyRegularizationRequestedAsync_DispatchesToManager_NotTheEmployee()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyRegularizationRequestedAsync(
            _employeeEmpId, Date, "Forgot to clock in; badge records confirm presence.");

        dispatcher.InApp.Should().ContainSingle();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.InApp.Single().RecipientUserId.Should().Be(_managerUserId);
        dispatcher.Email.Single().RecipientUserId.Should().Be(_managerUserId);
        dispatcher.Email.Single().RecipientUserId.Should().NotBe(_employeeUserId);
        dispatcher.Email.Single().EventKey.Should().Be("attendance_regularization_requested");
    }

    // ── MANAGER HOP — overtime-pre-approval-requested → the manager ──

    [Fact]
    public async Task NotifyOvertimePreApprovalRequestedAsync_DispatchesToManager()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyOvertimePreApprovalRequestedAsync(_employeeEmpId, Date, 2.0m);

        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().RecipientUserId.Should().Be(_managerUserId);
        dispatcher.Email.Single().EventKey.Should().Be("overtime_preapproval_requested");
    }

    // ── EVENT-KEY MAPPING — regularization approve vs reject ──

    [Theory]
    [InlineData(true, "attendance_regularization_approved")]
    [InlineData(false, "attendance_regularization_rejected")]
    public async Task NotifyRegularizationDecidedAsync_MapsKey_AndDispatchesToTheEmployee(bool approved, string key)
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyRegularizationDecidedAsync(
            approved, _employeeEmpId, Date, "A ten-plus character decision reason.");

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == key);
        dispatcher.InApp.Should().OnlyContain(r => r.EventKey == key);
        dispatcher.Email.Single().RecipientUserId.Should().Be(_employeeUserId);

        // Payload-completeness: the decision reason must reach the template's {{regularization.reason}}.
        PayloadStr(dispatcher.Email.Single().PayloadJson, "regularization.reason")
            .Should().Be("A ten-plus character decision reason.");
    }

    // ── EVENT-KEY MAPPING — overtime approve vs reject ──

    [Theory]
    [InlineData(true, "overtime_approved")]
    [InlineData(false, "overtime_rejected")]
    public async Task NotifyOvertimeDecidedAsync_MapsKey_AndDispatchesToTheEmployee(bool approved, string key)
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyOvertimeDecidedAsync(
            approved, _employeeEmpId, Date, 2.5m, approved ? null : "Not pre-authorized for this period.");

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == key);
        dispatcher.Email.Single().RecipientUserId.Should().Be(_employeeUserId);

        // Payload-completeness: the rejection reason is supplied (and only the rejected event carries it —
        // overtime_rejected declares {{overtime.reason}}; the approved event does not).
        var payload = dispatcher.Email.Single().PayloadJson;
        PayloadStr(payload, "overtime.hours").Should().NotBeNullOrEmpty();
        if (approved)
            PayloadStr(payload, "overtime.reason").Should().BeNull();
        else
            PayloadStr(payload, "overtime.reason").Should().Be("Not pre-authorized for this period.");
    }

    // ── ADMIN POOL — overtime-maxima-exceeded → Attendance.Edit holders ONLY, not the non-holder ──

    [Fact]
    public async Task NotifyOvertimeMaximaExceededAsync_DispatchesToAdminPoolOnly_NotTheNonHolder()
    {
        await SeedAdminPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyOvertimeMaximaExceededAsync(_employeeEmpId, 3.5m, 3.0m, "daily");

        // Both legs go to exactly the two Attendance.Edit holders — NOT the non-holder, NOT the employee.
        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _admin1, _admin2 });
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _admin1, _admin2 });
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_nonAdmin);
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_employeeUserId);
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "overtime_maxima_exceeded");

        // Payload-completeness: the template renders overtime.hours/limit/period — prove each is supplied.
        var payload = dispatcher.Email.First().PayloadJson;
        PayloadStr(payload, "overtime.hours").Should().NotBeNullOrEmpty();
        PayloadStr(payload, "overtime.limit").Should().NotBeNullOrEmpty();
        PayloadStr(payload, "overtime.period").Should().Be("daily");
    }

    // ── EMAIL-ONLY FALLBACK — an employee with no linked user → email-only via Employee.Email ──

    [Fact]
    public async Task NotifyLateAsync_EmployeeWithoutUser_FallsBackToEmailOnly()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyLateAsync(_emailOnlyEmpId, Date, new TimeOnly(9, 22), new TimeOnly(9, 0));

        dispatcher.InApp.Should().BeEmpty();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().RecipientEmail.Should().Be(EmailOnlyAddress);
        dispatcher.Email.Single().RecipientUserId.Should().BeNull();
        dispatcher.Email.Single().EventKey.Should().Be("attendance_late");
    }

    // ── NEVER-THROW — a dispatcher failure must not break the committed attendance write ──

    [Fact]
    public async Task NotifyLateAsync_DispatcherThrows_DoesNotThrow()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).NotifyLateAsync(_employeeEmpId, Date, new TimeOnly(9, 22), new TimeOnly(9, 0));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyOvertimeMaximaExceededAsync_DispatcherThrows_DoesNotThrow()
    {
        await SeedAdminPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).NotifyOvertimeMaximaExceededAsync(_employeeEmpId, 3.5m, 3.0m, "daily");

        await act.Should().NotThrowAsync();
    }

    // ── CHRONIC LATENESS (US-ATT-008 FR-7, DF-33/ISSUE-087) — line manager ∪ attendance-admin pool ──

    /// <summary>Adds the reporting-line manager (_managerUserId) into the Attendance.Edit admin pool.</summary>
    private async Task SeedManagerInAdminPoolAsync()
    {
        using var db = Db();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = "Attendance Admin (manager)" });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Attendance.Edit });
        var utId = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant { Id = utId, UserId = _managerUserId, TenantId = _tenantId, Status = UserTenantStatus.Active });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task NotifyChronicLatenessAsync_DispatchesToBothManagerAndAdminPool_BothLegs()
    {
        await SeedAdminPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyChronicLatenessAsync(
            _employeeEmpId, lateCount: 6, threshold: 5, new DateOnly(2026, 7, 15));

        // Recipients are exactly the two admin-pool holders PLUS the line manager — both legs, correct key.
        var expected = new Guid?[] { _admin1, _admin2, _managerUserId };
        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(expected);
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(expected);
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_nonAdmin);
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_employeeUserId);
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "attendance_chronic_lateness");

        // Payload-completeness: the template renders attendance.lateCount/threshold/month + employee name.
        var payload = dispatcher.Email.First().PayloadJson;
        PayloadStr(payload, "attendance.lateCount").Should().Be("6");
        PayloadStr(payload, "attendance.threshold").Should().Be("5");
        PayloadStr(payload, "attendance.month").Should().Be("July 2026");
        PayloadStr(payload, "employee.firstName").Should().Be("Jordan");
    }

    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task NotifyChronicLatenessAsync_ManagerAlsoInAdminPool_IsNotNotifiedTwice()
    {
        await SeedAdminPoolAsync();
        await SeedEmployeesAsync();
        await SeedManagerInAdminPoolAsync();   // the manager now ALSO holds Attendance.Edit
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyChronicLatenessAsync(
            _employeeEmpId, lateCount: 6, threshold: 5, new DateOnly(2026, 7, 15));

        // De-dup: exactly three distinct recipients, and the manager appears exactly once (not twice).
        dispatcher.Email.Should().HaveCount(3);
        dispatcher.Email.Select(r => r.RecipientUserId)
            .Should().BeEquivalentTo(new Guid?[] { _admin1, _admin2, _managerUserId });
        dispatcher.Email.Count(r => r.RecipientUserId == _managerUserId).Should().Be(1);
    }

    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task NotifyChronicLatenessAsync_NoManagerAndNoAdminPool_DoesNotThrow_AndSendsNothing()
    {
        // _emailOnlyEmpId has no ReportsToEmployeeId (no manager) and no admin pool is seeded → empty recipients.
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        var act = () => Service(dispatcher).NotifyChronicLatenessAsync(
            _emailOnlyEmpId, lateCount: 6, threshold: 5, new DateOnly(2026, 7, 15));

        await act.Should().NotThrowAsync();
        dispatcher.Email.Should().BeEmpty();
        dispatcher.InApp.Should().BeEmpty();
    }

    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task NotifyChronicLatenessAsync_DispatcherThrows_DoesNotThrow()
    {
        await SeedAdminPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).NotifyChronicLatenessAsync(
            _employeeEmpId, lateCount: 6, threshold: 5, new DateOnly(2026, 7, 15));

        await act.Should().NotThrowAsync();
    }
}
