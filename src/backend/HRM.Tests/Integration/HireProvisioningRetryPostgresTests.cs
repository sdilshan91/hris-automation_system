// ============================================================================
// DF-15 / US-REC-010 FR-5 / BR-7 (ISSUE-140, BUG-264): auto-creating the login account on hire, on REAL
// PostgreSQL. When Tenant.AutoCreateUserOnHire is on, ApplicantConversionService.ConvertAsync provisions a
// passwordless User + Active UserTenant + built-in "Employee" UserTenantRole and links Employee.UserId —
// ALL inside the single BeginTransactionAsync()-gated atomic unit (NFR-3). That transaction path is only
// reachable when Database.IsRelational() is true, so the provisioning-commits-atomically guarantee is
// structurally uncoverable on the InMemory provider the unit tests use.
//
// The sibling ApplicantConversionRetryStrategyTests already proves the RETRY/idempotency mechanism for the
// Employee/Vacancy/Applicant/AuditLog set, but with AutoCreateUserOnHire OFF (default) and no "Employee" role
// seeded — so the USER-ACCOUNT provisioning path (TryProvisionUserAccountAsync) is never exercised there. THIS
// suite turns the toggle ON and seeds the role, then asserts (1) the account rows commit in the same
// transaction, and (2) a transient first-attempt failure + execution-strategy retry re-inserts NO duplicate
// User/UserTenant/UserTenantRole (BUG-264 rollback-detach).
//
// Harness + BuildService + SeedConvertiblePipeline + FailOnceOnConversionAuditInterceptor are mirrored EXACTLY
// from ApplicantConversionRetryStrategyTests (Npgsql + EnableRetryOnFailure retrying strategy + snake_case +
// the same SaveChanges interceptors + AuditCaptureInterceptor). The one-shot transient interceptor fires on the
// SAME final SaveChanges that carries both the conversion audit AND the provisioned account rows, so it forces
// the exact retry the detach path guards.
//
// ⚠ POSTGRES ENFORCES FKs InMemory IGNORES: the seed adds the Tenant row (FK target for every tenant_id), and
// the provisioning creates User/UserTenant(UserId FKs users)/UserTenantRole itself, so no orphan-FK seeding is
// needed for the account rows — but the built-in "Employee" Role (TenantId FKs tenants) IS seeded so
// TryProvisionUserAccountAsync finds a role to assign.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class HireProvisioningRetryPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
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
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private AppDbContext CreateContext(
        ITenantContext tenantContext, ICurrentUser currentUser, IInterceptor? extraInterceptor = null)
    {
        var interceptors = new List<IInterceptor>
        {
            new TenantInterceptor(tenantContext),
            new AuditInterceptor(currentUser),
            new AuditCaptureInterceptor(tenantContext, currentUser),
        };
        if (extraInterceptor is not null)
            interceptors.Add(extraInterceptor);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptors)
            .Options;

        return new AppDbContext(options, tenantContext);
    }

    private ICurrentUser MakeCurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Email.Returns("hr@acme.com");
        currentUser.TenantId.Returns(_tenantId);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Roles.Returns(new List<string>());
        currentUser.Permissions.Returns(new List<string>());
        return currentUser;
    }

    private ApplicantConversionService BuildService(AppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        var virusScanner = Substitute.For<IVirusScanner>();
        var customFields = Substitute.For<ICustomFieldService>();
        customFields.ValidateCustomFieldValuesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var fileStorage = Substitute.For<IFileStorage>();

        var employeeService = new EmployeeService(
            db, tenantContext, currentUser, fileStorage, virusScanner, customFields,
            Substitute.For<IPayrollAuditLogger>(), NullLogger<EmployeeService>.Instance);

        return new ApplicantConversionService(
            db, tenantContext, currentUser, employeeService,
            new LogOnlyRecruitmentNotificationService(NullLogger<LogOnlyRecruitmentNotificationService>.Instance),
            NullLogger<ApplicantConversionService>.Instance);
    }

    // ══ Arm 1 — the login account is provisioned in the SAME committed transaction as the conversion ══

    /// <summary>
    /// With AutoCreateUserOnHire on and the built-in "Employee" role seeded, converting an applicant provisions
    /// a passwordless User + Active UserTenant + Employee UserTenantRole and links Employee.UserId — all inside
    /// the IsRelational()-gated BeginTransactionAsync that only exists on a real relational provider. This arm
    /// asserts every account row committed and is correctly wired (unreachable on InMemory).
    /// </summary>
    [Fact]
    [Trait("TC", "TC-REC-010-15")]
    public async Task Convert_WithAutoCreateOn_ProvisionsUserAccount_InSameCommittedTransaction()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = MakeCurrentUser();

        await using var db = CreateContext(tenantContext, currentUser);
        await db.Database.MigrateAsync();

        var (applicantId, deptId, jobTitleId, employeeRoleId) =
            SeedConvertiblePipeline(db, _tenantId, autoCreateUserOnHire: true);
        await db.SaveChangesAsync();

        var service = BuildService(db, tenantContext, currentUser);

        var result = await service.ConvertAsync(new ConvertApplicantToEmployeeInput
        {
            ApplicantId = applicantId,
            DepartmentId = deptId,
            JobTitleId = jobTitleId,
            EmploymentType = EmploymentType.FullTime,
            DateOfJoining = DateTime.UtcNow.Date,
        });

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.UserAccountCreated.Should().BeTrue("AutoCreateUserOnHire is on");
        var employeeId = result.Value.EmployeeId;

        // A fresh context reads DB-COMMITTED state only — proves the account rows are in the committed transaction.
        await using var verify = CreateContext(new MutableTenantContext { TenantId = _tenantId }, MakeCurrentUser());

        var employee = await verify.Employees.IgnoreQueryFilters().AsNoTracking().FirstAsync(e => e.Id == employeeId);
        employee.UserId.Should().NotBeNull("Employee.UserId must be linked to the provisioned account");

        var user = await verify.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == employee.UserId!.Value);
        user.Email.Should().Be("ada@acme.com");
        user.PasswordHash.Should().BeNull("the account is passwordless — credential delivery is deferred (US-NTF-006)");

        var membership = await verify.UserTenants.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(ut => ut.UserId == user.Id && ut.TenantId == _tenantId);
        membership.Status.Should().Be(UserTenantStatus.Active);

        var roleAssigned = await verify.UserTenantRoles.AsNoTracking()
            .AnyAsync(utr => utr.UserTenantId == membership.Id && utr.RoleId == employeeRoleId);
        roleAssigned.Should().BeTrue("the built-in Employee role must be assigned to the provisioned membership");
    }

    // ══ Arm 2 — a transient retry re-inserts NO duplicate account rows (BUG-264) ══

    /// <summary>
    /// The first convert attempt fails TRANSIENTLY on the final SaveChanges that carries both the conversion
    /// audit AND the just-Add()-ed User/UserTenant/UserTenantRole; the retrying execution strategy re-invokes the
    /// whole delegate. Pre-fix the rolled-back-but-still-tracked provisioned rows would re-insert on the retry
    /// (duplicate User / duplicate membership). Post-fix the catch detaches them (provisioned list) and the retry
    /// re-reads clean DB state, so the account exists EXACTLY ONCE. ThrowCount == 1 guards against a vacuous pass.
    ///
    /// Retry-injection IS feasible from the service surface: FailOnceOnConversionAuditInterceptor (mirrored from
    /// ApplicantConversionRetryStrategyTests) throws a TimeoutException — classified transient by
    /// NpgsqlTransientExceptionDetector — exactly once on the save carrying the conversion audit, which is the
    /// same atomic SaveChanges that persists the provisioned account rows.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-REC-010-15")]
    public async Task Convert_WithAutoCreateOn_FailsTransientlyThenRetries_ProvisionsAccountExactlyOnce()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = MakeCurrentUser();

        var failOnce = new FailOnceOnConversionAuditInterceptor();

        await using var db = CreateContext(tenantContext, currentUser, failOnce);
        await db.Database.MigrateAsync();

        var (applicantId, deptId, jobTitleId, employeeRoleId) =
            SeedConvertiblePipeline(db, _tenantId, autoCreateUserOnHire: true);
        await db.SaveChangesAsync();

        var service = BuildService(db, tenantContext, currentUser);

        var result = await service.ConvertAsync(new ConvertApplicantToEmployeeInput
        {
            ApplicantId = applicantId,
            DepartmentId = deptId,
            JobTitleId = jobTitleId,
            EmploymentType = EmploymentType.FullTime,
            DateOfJoining = DateTime.UtcNow.Date,
        });

        // The injected transient failure fired exactly once (attempt 1) — proves the retry path was exercised.
        failOnce.ThrowCount.Should().Be(1);

        // The retry completes to a real success (not a spurious already_converted from stale tracked state).
        result.IsSuccess.Should().BeTrue(
            "the transient first attempt must be retried to a real success, not a spurious already_converted");
        result.ErrorCode.Should().NotBe("already_converted");
        result.Value!.UserAccountCreated.Should().BeTrue();

        // Idempotency: EXACTLY ONE User for the hired applicant's email (the rollback-detach prevents a re-insert).
        var users = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Email == "ada@acme.com").ToListAsync();
        users.Should().HaveCount(1, "the provisioned User must be detached on the failed attempt so the retry does not re-create it");

        // Idempotency: EXACTLY ONE Active UserTenant membership, and EXACTLY ONE Employee-role assignment.
        var memberships = await db.UserTenants.IgnoreQueryFilters().AsNoTracking()
            .Where(ut => ut.UserId == users[0].Id && ut.TenantId == _tenantId).ToListAsync();
        memberships.Should().HaveCount(1, "no duplicate UserTenant membership after the retry");

        var roleAssignments = await db.UserTenantRoles.AsNoTracking()
            .Where(utr => utr.UserTenantId == memberships[0].Id && utr.RoleId == employeeRoleId).ToListAsync();
        roleAssignments.Should().HaveCount(1, "no duplicate UserTenantRole after the retry");

        // The employee is linked to that single account.
        var employee = await db.Employees.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(e => e.Id == result.Value.EmployeeId);
        employee.UserId.Should().Be(users[0].Id);
    }

    /// <summary>
    /// Seeds the minimal convertible pipeline (Hired applicant + Accepted offer + open Vacancy + Core HR master
    /// data) plus — for DF-15 — the tenant toggle and the built-in "Employee" role the provisioning path needs.
    /// Returns the ids the caller asserts against, including the seeded Employee-role id.
    /// </summary>
    private static (Guid applicantId, Guid deptId, Guid jobTitleId, Guid employeeRoleId) SeedConvertiblePipeline(
        AppDbContext db, Guid tenantId, bool autoCreateUserOnHire)
    {
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        var vacancyId = Guid.NewGuid();
        var applicantId = Guid.NewGuid();
        var employeeRoleId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = "acme", Name = "Acme Corp",
            AutoCreateUserOnHire = autoCreateUserOnHire, // DF-15: gate for TryProvisionUserAccountAsync.
        });
        // FR-5/BR-7: the built-in "Employee" role the provisioning path assigns (seeded per tenant in prod).
        db.Roles.Add(new Role
        {
            Id = employeeRoleId, TenantId = tenantId,
            Name = PermissionCatalog.BuiltInRoles.Employee, IsBuiltIn = true,
        });
        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Engineering", Code = "ENG", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = jobTitleId, TenantId = tenantId, TitleName = "Backend Engineer", IsActive = true });
        db.Vacancies.Add(new Vacancy
        {
            Id = vacancyId,
            TenantId = tenantId,
            ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer",
            Status = VacancyStatus.Open,
            EmploymentType = EmploymentType.FullTime,
            Headcount = 2,
            FilledCount = 0,
            Description = "Build things.",
        });
        db.Applicants.Add(new Applicant
        {
            Id = applicantId,
            TenantId = tenantId,
            VacancyId = vacancyId,
            ApplicationReferenceNumber = "APP-2026-0001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@acme.com",
            Phone = "+15551234567",
            ResumeStorageKey = $"recruitment/{vacancyId}/x/z.pdf",
            ResumeFileName = "resume.pdf",
            Stage = ApplicantStage.Hired,
            Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow,
        });
        db.Offers.Add(new Offer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ApplicantId = applicantId,
            VacancyId = vacancyId,
            OfferReferenceNumber = "OFR-2026-0001",
            Status = OfferStatus.Accepted,
            Response = "Accepted",
            OfferedPosition = "Backend Engineer",
            DepartmentId = deptId,
            SalaryAmount = 120000m,
            Currency = "USD",
            SalaryFrequency = SalaryFrequency.Annual,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            ProbationMonths = 3,
            Version = 1,
            RespondedAt = DateTime.UtcNow,
        });

        return (applicantId, deptId, jobTitleId, employeeRoleId);
    }

    /// <summary>
    /// Test-only SaveChanges interceptor: throws a TRANSIENT <see cref="TimeoutException"/> exactly once, on the
    /// first save whose change set contains the explicit "recruitment.applicant.converted" AuditLog (the final
    /// ConvertAsync save — the same atomic SaveChanges that also persists the provisioned account rows).
    /// NpgsqlTransientExceptionDetector classifies TimeoutException as transient, so the retrying execution
    /// strategy re-invokes the delegate — reproducing the BUG-264 retry path over the provisioning rows.
    /// Mirrored from ApplicantConversionRetryStrategyTests.
    /// </summary>
    private sealed class FailOnceOnConversionAuditInterceptor : SaveChangesInterceptor
    {
        public int ThrowCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var carriesConversionAudit = eventData.Context is not null
                && eventData.Context.ChangeTracker
                    .Entries<AuditLog>()
                    .Any(e => e.State == EntityState.Added
                           && e.Entity.EventType == "recruitment.applicant.converted");

            if (carriesConversionAudit && ThrowCount == 0)
            {
                ThrowCount++;
                throw new TimeoutException(
                    "Simulated transient failure on the first convert attempt (forces an execution-strategy retry).");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
