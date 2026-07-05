// ============================================================================
// ISSUE-019 (US-CHR-003 FR-4) — regression.
//
// The documented directory sort contract is ?sort=<field>&sortDirection=asc|desc
// (per TC-CHR-127 / TC-CHR-136). PRE-FIX the controller bound only sortBy/
// sortDescending, so a request using the DOCUMENTED names (sort/sortDirection) had
// them silently ignored and fell back to the default (name asc) with a 200 — sort
// requests were dropped without error. POST-FIX EmployeesController.GetDirectory
// also accepts sort/sortDirection and ResolveSort maps them (documented names win).
//
// This drives the REAL controller -> MediatR -> GetEmployeeDirectoryQueryHandler ->
// EmployeeDirectoryService -> AppDbContext(InMemory) path, so it exercises the actual
// ResolveSort mapping AND the real OrderBy over seeded rows. Employees are seeded so
// that the default (FirstName asc) order is the REVERSE of employee_no desc — keying
// the assertion on the sort actually changing the row order.
//
// RED PRE-FIX: sort/sortDirection ignored → default name-asc → [E-001,E-002,E-003];
// the "employee_no desc" assertion [E-003,E-002,E-001] fails.
// GREEN POST-FIX: the documented params are honored → [E-003,E-002,E-001].
// ============================================================================

using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class EmployeeDirectorySortParamIssue019Tests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantId = Guid.NewGuid();

    // Seed and query share one ServiceProvider (and one explicit InMemory root) so
    // both provably hit the same store — a defensive guarantee rather than relying on
    // EF's internal-provider cache to coincidentally reuse the same named store across
    // two separately-built option builders. (The actual empty-result cause was the
    // missing Department/JobTitle seed — see SeedEmployees.)
    private readonly InMemoryDatabaseRoot _dbRoot = new();
    private readonly ServiceProvider _provider;

    public EmployeeDirectorySortParamIssue019Tests()
    {
        _provider = BuildProvider();
        SeedEmployees();
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public required Guid TenantId { get; init; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => true;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private ServiceProvider BuildProvider()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        // Employee.View.All ⇒ Full visibility (employee_no is present in every visibility anyway).
        currentUser.Permissions.Returns(new[] { "Employee.View.All" });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new FixedTenantContext { TenantId = _tenantId });
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName, _dbRoot));
        services.AddScoped<IEmployeeDirectoryService, EmployeeDirectoryService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetEmployeeDirectoryQuery).Assembly));

        return services.BuildServiceProvider();
    }

    private EmployeesController BuildController()
        => new EmployeesController(_provider.GetRequiredService<IMediator>());

    private void SeedEmployees()
    {
        // Seed through the SAME provider the query uses so both share one InMemory store.
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The directory service Includes Department + JobTitle (required navigations,
        // each with its own tenant/soft-delete query filter). Without seeding those
        // rows, EF Core's required-navigation join drops every employee at
        // materialization time (CountAsync ignores the Include → 3, ToListAsync
        // honours it → 0), yielding a total>0 but an empty page. Seed them so the
        // required navigations resolve and the employees actually come back.
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        db.Departments.Add(new Department
        {
            Id = deptId,
            TenantId = _tenantId,
            Name = "Engineering",
            Code = "ENG",
            IsActive = true,
            IsDeleted = false,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId,
            TenantId = _tenantId,
            TitleName = "Engineer",
            IsActive = true,
            IsDeleted = false,
        });

        // Default sort is FirstName asc → Amy, Mike, Zoe → [E-001, E-002, E-003].
        // employee_no desc → [E-003, E-002, E-001] (the reverse), so the sort visibly changes the order.
        AddEmployee(db, "E-001", "Amy", deptId, jobTitleId);
        AddEmployee(db, "E-002", "Mike", deptId, jobTitleId);
        AddEmployee(db, "E-003", "Zoe", deptId, jobTitleId);
        db.SaveChanges();
    }

    private void AddEmployee(AppDbContext db, string employeeNo, string firstName, Guid deptId, Guid jobTitleId)
        => db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            EmployeeNo = employeeNo,
            FirstName = firstName,
            LastName = "Tester",
            Email = $"{firstName.ToLowerInvariant()}@acme.test",
            DateOfJoining = new DateTime(2022, 1, 1),
            DepartmentId = deptId,
            JobTitleId = jobTitleId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
            IsDeleted = false,
        });

    private static IReadOnlyList<string> EmployeeNoOrder(IActionResult action)
    {
        var ok = action.Should().BeOfType<OkObjectResult>().Subject;
        var resp = ok.Value.Should().BeOfType<ApiResponse<EmployeeDirectoryResult>>().Subject;
        return resp.Data!.Data.Select(e => e.EmployeeNo).ToList();
    }

    // ── Focused regression: documented sort/sortDirection are honored ───────────

    [Fact]
    public async Task GetDirectory_SortAndSortDirectionDesc_HonorsDocumentedParams_ISSUE019()
    {
        var controller = BuildController();

        var action = await controller.GetDirectory(sort: "employee_no", sortDirection: "desc");

        // Guard: the seeded rows must actually come back before order can be meaningful.
        EmployeeNoOrder(action).Should().HaveCount(3);
        // PRE-FIX: sort/sortDirection ignored → default FirstName-asc → [E-001,E-002,E-003] → this fails.
        EmployeeNoOrder(action).Should().Equal("E-003", "E-002", "E-001");
    }

    // ── Control: with NO sort params the default order is the reverse ───────────
    // (proves the seed's default order genuinely differs, so the regression assertion is meaningful.)

    [Fact]
    public async Task GetDirectory_NoSortParams_DefaultsToNameAscending_ISSUE019()
    {
        var controller = BuildController();

        var action = await controller.GetDirectory();

        // Guard: the seeded rows must actually come back before order can be meaningful.
        EmployeeNoOrder(action).Should().HaveCount(3);
        EmployeeNoOrder(action).Should().Equal("E-001", "E-002", "E-003");
    }
}
