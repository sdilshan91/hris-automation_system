// ============================================================================
// US-NTF-006 Phase 7 — RealCoreHrNotificationService unit tests (US-CHR-008/009/011).
//
// The Real service RESOLVES recipients from the DB and dispatches both legs (in-app + email) when the recipient
// has a linked User, else email-only against Employee.Email. Load-bearing behaviours genuinely observed here:
//   • HR POOL — probation-ending / manager-reassignment route to the Employee.ChangeStatus holders ONLY (a decoy
//     user without the permission is never notified).
//   • CROSS-TENANT TENANT-ID — probation passes the tenant id EXPLICITLY (the caller runs under system context);
//     manager-reassignment resolves the tenant from the manager's OWN record (works request-scoped AND in the job).
//   • OWNER EMPLOYEE — document-expiry routes to the document-owner employee (both legs), with email-only fallback
//     when the owner has no linked user.
//   • EVENT-KEY MAPPING — each method dispatches its exact catalog event key.
//   • PAYLOAD-COMPLETENESS — the (non-tenant) placeholder keys the matching template renders are actually populated.
//   • NEVER-THROW — a dispatcher failure must not break the committed write / scan.
//
// PROVIDER: EF Core InMemory AppDbContext (the HR-pool resolution is a real UserTenants⋈UserTenantRoles⋈
// RolePermissions query with IgnoreQueryFilters; the owner/manager lookups are real Employees queries) + a hand
// RecordingDispatcher. Mirrors RealAttendanceNotificationServiceTests' seeding.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealCoreHrNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    // HR pool: two users holding Employee.ChangeStatus + one decoy without it.
    private readonly Guid _hr1 = Guid.NewGuid();
    private readonly Guid _hr2 = Guid.NewGuid();
    private readonly Guid _nonHr = Guid.NewGuid();

    // Probation subject.
    private readonly Guid _probationEmpId = Guid.NewGuid();

    // Manager (terminated/suspended) whose direct reports need reassignment.
    private readonly Guid _managerEmpId = Guid.NewGuid();

    // Document owner with a linked user, and one without (email-only fallback).
    private readonly Guid _docOwnerEmpId = Guid.NewGuid();
    private readonly Guid _docOwnerUserId = Guid.NewGuid();
    private readonly Guid _emailOnlyEmpId = Guid.NewGuid();
    private const string EmailOnlyAddress = "no.user@acme.test";

    private static readonly DateOnly ProbationEnd = new(2026, 7, 8);
    private static readonly DateOnly DocExpiry = new(2026, 8, 1);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public RealCoreHrNotificationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private RealCoreHrNotificationService Service(INotificationDispatcher dispatcher) =>
        new(Db(), dispatcher, NullLogger<RealCoreHrNotificationService>.Instance);

    // ── Fake dispatcher ────────────────────────────────────────────────────────────────────────────

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

    /// <summary>Two users holding an Employee.ChangeStatus role + one user without it (the HR pool + a decoy).</summary>
    private async Task SeedHrPoolAsync()
    {
        using var db = Db();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = "HR Officer" });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Employee.ChangeStatus });

        foreach (var uid in new[] { _hr1, _hr2 })
        {
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = uid, TenantId = _tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        }

        // A tenant user WITHOUT Employee.ChangeStatus — must never be notified.
        var plainRoleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = plainRoleId, TenantId = _tenantId, Name = "Employee" });
        var nonUt = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant { Id = nonUt, UserId = _nonHr, TenantId = _tenantId, Status = UserTenantStatus.Active });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = nonUt, RoleId = plainRoleId });

        await db.SaveChangesAsync();
    }

    private async Task SeedEmployeesAsync()
    {
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = _probationEmpId, TenantId = _tenantId, FirstName = "Jane", LastName = "Doe",
            EmployeeNo = "EMP-0042", Email = "jane.doe@acme.test", UserId = Guid.NewGuid(),
        });
        db.Employees.Add(new Employee
        {
            Id = _managerEmpId, TenantId = _tenantId, FirstName = "Morgan", LastName = "Hale",
            EmployeeNo = "EMP-0001", Email = "morgan.hale@acme.test", UserId = Guid.NewGuid(),
        });
        db.Employees.Add(new Employee
        {
            Id = _docOwnerEmpId, TenantId = _tenantId, FirstName = "Jordan", LastName = "Rivera",
            EmployeeNo = "EMP-0007", Email = "jordan.rivera@acme.test", UserId = _docOwnerUserId,
        });
        db.Employees.Add(new Employee
        {
            Id = _emailOnlyEmpId, TenantId = _tenantId, FirstName = "Casey", LastName = "Lee",
            EmployeeNo = "EMP-0009", Email = EmailOnlyAddress, UserId = null,
        });
        await db.SaveChangesAsync();
    }

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

    // ── HR POOL — probation-ending → the Employee.ChangeStatus holders ONLY, not the decoy ──

    [Fact]
    public async Task NotifyProbationEndingAsync_DispatchesToHrPoolOnly_NotTheDecoy()
    {
        await SeedHrPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyProbationEndingAsync(_tenantId, _probationEmpId, ProbationEnd, 5);

        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _hr1, _hr2 });
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _hr1, _hr2 });
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_nonHr);
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "employee_probation_ending");
        dispatcher.Email.Should().OnlyContain(r => r.TenantId == _tenantId);

        // Payload-completeness: employee.firstName/lastName/employeeNo + probation.endDate/daysRemaining populated.
        var payload = dispatcher.Email.First().PayloadJson;
        PayloadStr(payload, "employee.firstName").Should().Be("Jane");
        PayloadStr(payload, "employee.lastName").Should().Be("Doe");
        PayloadStr(payload, "employee.employeeNo").Should().Be("EMP-0042");
        PayloadStr(payload, "probation.endDate").Should().Be("2026-07-08");
        PayloadStr(payload, "probation.daysRemaining").Should().Be("5");
    }

    // ── HR POOL — manager-reassignment → HR pool, tenant resolved from the manager's own record ──

    [Fact]
    public async Task NotifyManagerReassignmentNeededAsync_DispatchesToHrPool_TenantFromManagerRecord()
    {
        await SeedHrPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        // Note: NO tenant id argument — the service must resolve it from the manager's own record.
        await Service(dispatcher).NotifyManagerReassignmentNeededAsync(_managerEmpId, EmployeeStatus.Terminated, 4);

        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _hr1, _hr2 });
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "manager_reassignment_needed");
        dispatcher.Email.Should().OnlyContain(r => r.TenantId == _tenantId);

        // Payload-completeness: manager.firstName/lastName/newStatus + reassignment.directReportCount populated.
        var payload = dispatcher.Email.First().PayloadJson;
        PayloadStr(payload, "manager.firstName").Should().Be("Morgan");
        PayloadStr(payload, "manager.lastName").Should().Be("Hale");
        PayloadStr(payload, "manager.newStatus").Should().Be("Terminated");
        PayloadStr(payload, "reassignment.directReportCount").Should().Be("4");
    }

    // ── OWNER EMPLOYEE — document-expiry → the document-owner employee, both legs ──

    [Fact]
    public async Task NotifyDocumentExpiryAsync_DispatchesToOwnerEmployee_BothLegs()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyDocumentExpiryAsync(
            _tenantId, _docOwnerEmpId, "passport.pdf", "Passport", DocExpiry, 30);

        dispatcher.InApp.Should().ContainSingle();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.InApp.Single().RecipientUserId.Should().Be(_docOwnerUserId);
        dispatcher.Email.Single().RecipientUserId.Should().Be(_docOwnerUserId);
        dispatcher.Email.Single().EventKey.Should().Be("document_expiry_warning");
        dispatcher.Email.Single().TenantId.Should().Be(_tenantId);

        // Payload-completeness: document.* + employee.firstName populated.
        var payload = dispatcher.Email.Single().PayloadJson;
        PayloadStr(payload, "employee.firstName").Should().Be("Jordan");
        PayloadStr(payload, "document.fileName").Should().Be("passport.pdf");
        PayloadStr(payload, "document.category").Should().Be("Passport");
        PayloadStr(payload, "document.expiryDate").Should().Be("2026-08-01");
        PayloadStr(payload, "document.daysUntilExpiry").Should().Be("30");
    }

    // ── EMAIL-ONLY FALLBACK — a document-owner with no linked user → email-only via Employee.Email ──

    [Fact]
    public async Task NotifyDocumentExpiryAsync_OwnerWithoutUser_FallsBackToEmailOnly()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyDocumentExpiryAsync(
            _tenantId, _emailOnlyEmpId, "visa.pdf", "Visa", DocExpiry, 7);

        dispatcher.InApp.Should().BeEmpty();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().RecipientEmail.Should().Be(EmailOnlyAddress);
        dispatcher.Email.Single().RecipientUserId.Should().BeNull();
        dispatcher.Email.Single().EventKey.Should().Be("document_expiry_warning");
    }

    // ── NEVER-THROW — a dispatcher failure must not break the committed write / scan ──

    [Fact]
    public async Task NotifyProbationEndingAsync_DispatcherThrows_DoesNotThrow()
    {
        await SeedHrPoolAsync();
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).NotifyProbationEndingAsync(_tenantId, _probationEmpId, ProbationEnd, 5);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyDocumentExpiryAsync_DispatcherThrows_DoesNotThrow()
    {
        await SeedEmployeesAsync();
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).NotifyDocumentExpiryAsync(
            _tenantId, _docOwnerEmpId, "passport.pdf", "Passport", DocExpiry, 30);

        await act.Should().NotThrowAsync();
    }
}
