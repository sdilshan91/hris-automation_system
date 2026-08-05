// ============================================================================
// ISSUE-338 — the invite path (UserManagementService) and the direct-create path
// (EmployeeService) must AGREE on the same effective employee cap for the same
// tenant. Both now resolve through PlanLimitResolver (override > plan > snapshot),
// so a per-tenant override that RAISES the ceiling governs BOTH paths — neither is
// stuck on the stale Tenant.MaxEmployees snapshot.
//
// MUTATION: revert UserManagementService.InviteOneAsync to read `tenant.MaxEmployees`
// raw and this test fails — the invite path would block at the snapshot (1) while the
// create path still honours the override (5), i.e. the two paths DISAGREE.
//
// Provider: EF Core InMemory (single-table resolver reads; the cross-table storage
// sum that InMemory masks is covered by TenantStorageQuotaPostgresTests).
// ============================================================================

using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class InvitePlanLimitAgreementTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private Guid _deptId, _jtId, _roleId;

    public InvitePlanLimitAgreementTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private EmployeeService EmployeeSvc()
    {
        var hr = Substitute.For<ICurrentUser>();
        hr.IsAuthenticated.Returns(true);
        hr.UserId.Returns(Guid.NewGuid());
        hr.Email.Returns("hr@acme.com");
        var cf = Substitute.For<ICustomFieldService>();
        cf.ValidateCustomFieldValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return new EmployeeService(Db(), _tenantContext, hr,
            Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(), cf,
            Substitute.For<IPayrollAuditLogger>(), NullLogger<EmployeeService>.Instance);
    }

    private UserManagementService InviteSvc()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Email.Returns("admin@acme.com");
        currentUser.TenantId.Returns(_tenantId);
        return new UserManagementService(Db(), _tenantContext, currentUser,
            Substitute.For<IPermissionCache>(), Substitute.For<IUserManagementNotificationService>(),
            NullLogger<UserManagementService>.Instance);
    }

    private async Task SeedAsync()
    {
        using var db = Db();
        // Snapshot=1 (the stale value); a per-tenant override raises the effective cap to 5.
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "acme", Name = "Acme", Status = TenantStatus.Active, MaxEmployees = 1,
        });
        db.PlanLimitOverrides.Add(new PlanLimitOverride
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LimitKey = PlanLimitKeys.MaxEmployees,
            Value = 5, CreatedAt = DateTime.UtcNow,
        });

        _deptId = Guid.NewGuid();
        _jtId = Guid.NewGuid();
        _roleId = Guid.NewGuid();
        db.Departments.Add(new Department { Id = _deptId, TenantId = _tenantId, Name = "Eng", Code = "ENG", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = _jtId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true });
        db.Roles.Add(new Role { Id = _roleId, TenantId = _tenantId, Name = "Employee", IsBuiltIn = true });

        // One existing active membership → the invite path starts AT the snapshot (1 active), so any invite that
        // succeeds proves the override (5), not the snapshot (1), governs.
        var existingUser = Guid.NewGuid();
        db.Users.Add(new User { Id = existingUser, Email = "member@acme.com", IsActive = true });
        db.UserTenants.Add(new UserTenant
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, UserId = existingUser, Status = UserTenantStatus.Active,
        });

        await db.SaveChangesAsync();
    }

    private Task<Result<EmployeeDto>> CreateEmployee(string email) =>
        EmployeeSvc().CreateAsync(new CreateEmployeeRequest
        {
            FirstName = "T", LastName = "U", Email = email,
            DateOfJoining = DateTime.UtcNow, DepartmentId = _deptId, JobTitleId = _jtId,
            EmploymentType = EmploymentType.FullTime,
        });

    [Fact]
    public async Task InviteAndCreate_BothHonourOverrideAboveSnapshot_AndAgree()
    {
        await SeedAsync();

        // Direct-create path: snapshot=1 would block the 2nd employee, but the override (5) allows 5.
        (await CreateEmployee("a@acme.com")).IsSuccess.Should().BeTrue();
        (await CreateEmployee("b@acme.com")).IsSuccess.Should().BeTrue("the override (5) governs, not the snapshot (1)");
        (await CreateEmployee("c@acme.com")).IsSuccess.Should().BeTrue();

        // Invite path: starts at 1 active membership; snapshot=1 would block the FIRST invite. The override lets it
        // through — proving the invite path agrees with the create path that the override governs.
        (await InviteSvc().InviteAsync("invite1@acme.com", new[] { _roleId }))
            .IsSuccess.Should().BeTrue("the invite path must honour the same override the create path does");
        (await InviteSvc().InviteAsync("invite2@acme.com", new[] { _roleId }))
            .IsSuccess.Should().BeTrue();
    }
    // ── ISSUE-354: the THIRD path enforcing the same limit ────────────────────
    // EmployeeService was fixed under BUG-008 and the invite path under ISSUE-338, but
    // BulkEmployeeImportService still read Tenant.MaxEmployees RAW — missed by BOTH that fix and the ISSUE-342
    // plan-write sweep. So a tenant who PURCHASED an override could create employees one-by-one and invite
    // users, yet was still refused on bulk import: three paths, three answers, one limit.
    //
    // Snapshot=1 with one active employee already present, override=5. Under the old raw-snapshot read the
    // available slots computed to 0 and this returned 403. It must now allow the import.
    [Fact]
    public async Task BulkImport_HonoursOverride_NotTheStaleSnapshot_ISSUE354()
    {
        await SeedAsync();

        // Put the tenant AT its stale snapshot cap (1 active employee) so the snapshot would refuse outright.
        var seeded = await CreateEmployee("first@acme.com");
        seeded.IsSuccess.Should().BeTrue("the first employee fits under both the snapshot and the override");

        var csv = "first_name,last_name,email,date_of_joining,department_name,job_title_name,employment_type\n"
                + "Bulk,One,bulk1@acme.com,2026-01-15,Eng,Engineer,Full-Time\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var result = await BulkSvc().ImportAsync(
            stream, "import.csv", stream.Length, importUpToLimit: false, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "the tenant purchased an override raising the cap to 5, so a bulk import of one more employee must "
            + $"be accepted — reading the stale snapshot (1) refuses it. Error was: {result.Error}");
    }

    private BulkEmployeeImportService BulkSvc()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        return new BulkEmployeeImportService(
            Db(), _tenantContext, currentUser,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BulkEmployeeImportService>.Instance,
            DataProtectionProvider.Create(nameof(InvitePlanLimitAgreementTests)));
    }

}
