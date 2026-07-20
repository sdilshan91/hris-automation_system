// ============================================================================
// DF-45 / US-CHR-002 / ISSUE-321 — the employee-profile SUB-ENTITIES (Education / WorkHistory /
// Dependents) shipped in #386, exercised on REAL PostgreSQL through the real EmployeeService full-replace
// path (UpdateProfileAsync). These are the arms InMemory cannot honestly validate:
//
//   1. DELETE-then-REINSERT-the-SAME-PK in ONE SaveChanges. The full-replace does
//      `RemoveRange(existing)` then `Add(new { Id = <same Guid> })`. On real Postgres this must emit
//      DELETE-before-INSERT inside one transaction; if EF got the ordering wrong the reused PK would trip
//      a 23505 duplicate-key violation. InMemory just mutates dictionaries — it can neither enforce the
//      unique PK nor prove the intra-transaction statement ordering, so an InMemory pass is not evidence.
//   2. DateOnly -> `date` column round-trip on Npgsql (WorkHistory.FromDate/ToDate, Dependent.DateOfBirth).
//   3. Cross-tenant isolation: tenant B cannot see tenant A's education/work-history/dependent rows (the
//      EF global query filter must translate to SQL).
//
// Harness copied EXACTLY from AttendanceSettingsCrudPostgresTests (PostgreSqlBuilder "postgres:17-alpine",
// IAsyncLifetime + MigrateAsync, FixedTenantContext, UseNpgsql + UseSnakeCaseNamingConvention — the
// snake_case convention is NOT optional; omitting it makes MigrateAsync throw PendingModelChangesWarning —
// and the Tenant/Audit interceptors wired the same way).
//
// Traceability: @TC-CHR-335 (DF-39 full replace) exercised against the real provider.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class EmployeeProfileSubEntitiesPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db(Guid tenantId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options,
            tc);
    }

    /// <summary>An HR-Officer caller (Employee.Edit → CallerRole.HrOfficer) so UpdateProfileAsync is allowed.</summary>
    private EmployeeService Service(AppDbContext db, Guid tenantId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");
        cu.Permissions.Returns(new List<string> { "Employee.Edit", "Employee.View.All" });
        cu.Roles.Returns(new List<string> { "HR Officer" });

        return new EmployeeService(
            db, tc, cu,
            Substitute.For<IFileStorage>(),
            Substitute.For<IVirusScanner>(),
            Substitute.For<ICustomFieldService>(),
            Substitute.For<IPayrollAuditLogger>(),
            NullLogger<EmployeeService>.Instance);
    }

    // ── seeding ────────────────────────────────────────────────────────

    private static Guid NewEmployee(AppDbContext db, Guid tenantId, string no)
    {
        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = $"D{no}", Code = no };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = $"T{no}" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        var id = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "W",
            Email = $"{no}@acme.test", DepartmentId = dept.Id, JobTitleId = title.Id,
            Status = EmployeeStatus.Active, IsActive = true,
        });
        return id;
    }

    /// <summary>Reads the employee's current xmin concurrency token (real Postgres enforces it, so 0 won't do).</summary>
    private async Task<uint> CurrentRowVersion(Guid tenantId, Guid empId)
    {
        await using var db = Db(tenantId);
        return await db.Employees.AsNoTracking().Where(e => e.Id == empId)
            .Select(e => e.RowVersion).FirstAsync();
    }

    // ══ Arm 1 — delete-then-reinsert the SAME PK in one SaveChanges (Education) ══

    /// <summary>
    /// Seed an Education row with PK X, then full-replace it via UpdateProfileAsync submitting an
    /// EducationInput that REUSES PK X with different data. The service RemoveRange()s the tracked row and
    /// Add()s a fresh one with the same Guid — a delete+reinsert of the same PK inside a single SaveChanges.
    /// On Postgres this must succeed (DELETE ordered before INSERT) and the row must carry the new data.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task Education_DeleteThenReinsertSamePk_InOneSaveChanges_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;
        var eduId = BaseEntity.NewUuidV7();

        await using (var seed = Db(tenantId))
        {
            empId = NewEmployee(seed, tenantId, "EDU1");
            seed.EmployeeEducation.Add(new EmployeeEducation
            {
                Id = eduId, TenantId = tenantId, EmployeeId = empId,
                Institution = "Old University", Degree = "BSc", FieldOfStudy = "Physics",
                StartYear = "2008", EndYear = "2012",
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId).UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
            {
                RowVersion = await CurrentRowVersion(tenantId, empId),
                UpdateEducation = true,
                Education = new List<EducationInput>
                {
                    new()
                    {
                        Id = eduId,                    // ← REUSE the existing PK
                        Institution = "New University",
                        Degree = "MSc",
                        FieldOfStudy = "Computer Science",
                        StartYear = "2013",
                        EndYear = "2015",
                    },
                },
            });

            result.IsSuccess.Should().BeTrue(result.Error);
        }

        await using (var verify = Db(tenantId))
        {
            var rows = await verify.EmployeeEducation.AsNoTracking()
                .Where(e => e.EmployeeId == empId).ToListAsync();

            rows.Should().ContainSingle("the reused PK must be one row — a duplicate INSERT would 23505");
            var row = rows[0];
            row.Id.Should().Be(eduId, "the same PK was reused");
            row.Institution.Should().Be("New University");
            row.Degree.Should().Be("MSc");
            row.FieldOfStudy.Should().Be("Computer Science");
            row.StartYear.Should().Be("2013");
            row.EndYear.Should().Be("2015");
        }
    }

    // ══ Arm 1 — same operation for WorkHistory ══

    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task WorkHistory_DeleteThenReinsertSamePk_InOneSaveChanges_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;
        var whId = BaseEntity.NewUuidV7();

        await using (var seed = Db(tenantId))
        {
            empId = NewEmployee(seed, tenantId, "WH1");
            seed.EmployeeWorkHistory.Add(new EmployeeWorkHistory
            {
                Id = whId, TenantId = tenantId, EmployeeId = empId,
                Company = "Old Corp", Position = "Junior",
                FromDate = new DateOnly(2010, 1, 1), ToDate = new DateOnly(2012, 12, 31),
                Description = "old",
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId).UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
            {
                RowVersion = await CurrentRowVersion(tenantId, empId),
                UpdateWorkHistory = true,
                WorkHistory = new List<WorkHistoryInput>
                {
                    new()
                    {
                        Id = whId,                     // ← REUSE the existing PK
                        Company = "New Corp",
                        Position = "Senior",
                        FromDate = new DateOnly(2013, 6, 1),
                        ToDate = new DateOnly(2018, 3, 31),
                        Description = "new",
                    },
                },
            });

            result.IsSuccess.Should().BeTrue(result.Error);
        }

        await using (var verify = Db(tenantId))
        {
            var rows = await verify.EmployeeWorkHistory.AsNoTracking()
                .Where(e => e.EmployeeId == empId).ToListAsync();

            rows.Should().ContainSingle();
            var row = rows[0];
            row.Id.Should().Be(whId);
            row.Company.Should().Be("New Corp");
            row.Position.Should().Be("Senior");
            row.Description.Should().Be("new");
        }
    }

    // ══ Arm 1 — same operation for Dependents ══

    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task Dependent_DeleteThenReinsertSamePk_InOneSaveChanges_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;
        var depId = BaseEntity.NewUuidV7();

        await using (var seed = Db(tenantId))
        {
            empId = NewEmployee(seed, tenantId, "DEP1");
            seed.EmployeeDependents.Add(new EmployeeDependent
            {
                Id = depId, TenantId = tenantId, EmployeeId = empId,
                Name = "Old Name", Relationship = "Child", DateOfBirth = new DateOnly(2015, 5, 5),
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId).UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
            {
                RowVersion = await CurrentRowVersion(tenantId, empId),
                UpdateDependents = true,
                Dependents = new List<DependentInput>
                {
                    new()
                    {
                        Id = depId,                    // ← REUSE the existing PK
                        Name = "New Name",
                        Relationship = "Spouse",
                        DateOfBirth = new DateOnly(1990, 9, 9),
                    },
                },
            });

            result.IsSuccess.Should().BeTrue(result.Error);
        }

        await using (var verify = Db(tenantId))
        {
            var rows = await verify.EmployeeDependents.AsNoTracking()
                .Where(e => e.EmployeeId == empId).ToListAsync();

            rows.Should().ContainSingle();
            var row = rows[0];
            row.Id.Should().Be(depId);
            row.Name.Should().Be("New Name");
            row.Relationship.Should().Be("Spouse");
        }
    }

    // ══ Arm 2 — DateOnly -> `date` round-trip on Npgsql ══

    /// <summary>
    /// The DateOnly properties (WorkHistory.FromDate/ToDate, Dependent.DateOfBirth) map to Postgres `date`
    /// columns. Persist exact values through the real full-replace path and read them back byte-for-byte:
    /// proves the DateOnly↔`date` mapping (no time-of-day, no DateTimeKind/timestamptz drift) on Npgsql.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task DateOnly_Fields_RoundTripExactly_AsPostgresDate()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        var from = new DateOnly(2018, 3, 5);
        var to = new DateOnly(2021, 11, 20);
        var dob = new DateOnly(2015, 7, 14);

        await using (var seed = Db(tenantId))
        {
            empId = NewEmployee(seed, tenantId, "DATE1");
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId).UpdateProfileAsync(empId, new UpdateEmployeeProfileRequest
            {
                RowVersion = await CurrentRowVersion(tenantId, empId),
                UpdateWorkHistory = true,
                WorkHistory = new List<WorkHistoryInput>
                {
                    new() { Company = "Acme", Position = "Engineer", FromDate = from, ToDate = to },
                },
                UpdateDependents = true,
                Dependents = new List<DependentInput>
                {
                    new() { Name = "Kiddo", Relationship = "Child", DateOfBirth = dob },
                },
            });

            result.IsSuccess.Should().BeTrue(result.Error);
        }

        await using (var verify = Db(tenantId))
        {
            var wh = await verify.EmployeeWorkHistory.AsNoTracking().SingleAsync(e => e.EmployeeId == empId);
            wh.FromDate.Should().Be(from);
            wh.ToDate.Should().Be(to);

            var dep = await verify.EmployeeDependents.AsNoTracking().SingleAsync(e => e.EmployeeId == empId);
            dep.DateOfBirth.Should().Be(dob);
        }
    }

    // ══ Arm 3 — cross-tenant isolation on the sub-tables ══

    /// <summary>
    /// The three sub-tables are BaseEntity-tenant-scoped, so the EF global query filter must translate to
    /// SQL: a tenant-B context sees NONE of tenant A's education/work-history/dependent rows, while A still
    /// sees its own. IgnoreQueryFilters confirms the rows physically exist (so "empty for B" is the filter
    /// at work, not a failed seed).
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-335")]
    public async Task SubEntities_AreTenantIsolated_AcrossContexts()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid empA;

        await using (var seedA = Db(tenantA))
        {
            empA = NewEmployee(seedA, tenantA, "ISOA");
            seedA.EmployeeEducation.Add(new EmployeeEducation
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantA, EmployeeId = empA,
                Institution = "A-Uni", Degree = "BSc",
            });
            seedA.EmployeeWorkHistory.Add(new EmployeeWorkHistory
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantA, EmployeeId = empA,
                Company = "A-Corp", Position = "Eng",
            });
            seedA.EmployeeDependents.Add(new EmployeeDependent
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantA, EmployeeId = empA,
                Name = "A-Dep", Relationship = "Child",
            });
            await seedA.SaveChangesAsync();
        }

        // Tenant B sees nothing.
        await using (var b = Db(tenantB))
        {
            (await b.EmployeeEducation.AsNoTracking().CountAsync()).Should().Be(0);
            (await b.EmployeeWorkHistory.AsNoTracking().CountAsync()).Should().Be(0);
            (await b.EmployeeDependents.AsNoTracking().CountAsync()).Should().Be(0);
        }

        // Tenant A sees its own, and the rows physically exist for A.
        await using (var a = Db(tenantA))
        {
            (await a.EmployeeEducation.AsNoTracking().CountAsync(e => e.EmployeeId == empA)).Should().Be(1);
            (await a.EmployeeWorkHistory.AsNoTracking().CountAsync(e => e.EmployeeId == empA)).Should().Be(1);
            (await a.EmployeeDependents.AsNoTracking().CountAsync(e => e.EmployeeId == empA)).Should().Be(1);

            (await a.EmployeeEducation.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(e => e.TenantId == tenantA)).Should().Be(1);
        }
    }
}
