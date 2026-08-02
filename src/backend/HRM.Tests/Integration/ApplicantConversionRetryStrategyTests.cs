// ============================================================================
// US-REC-010 / BUG-068 regression: converting an applicant must succeed under
// the Npgsql RETRYING execution strategy (production config,
// EnableRetryOnFailure). This is the one path the sibling InMemory integration
// test structurally cannot cover — InMemory has no execution strategy and no
// transactions, so a user-initiated BeginTransactionAsync never throws there.
//
// PROVIDER: a real PostgreSQL Testcontainer, with the AppDbContext configured
// EXACTLY like production (UseNpgsql + EnableRetryOnFailure + snake_case + the
// same SaveChanges interceptors) so the retrying execution strategy is active.
//
// Pre-fix: ConvertAsync called Database.BeginTransactionAsync() directly, which
// NpgsqlRetryingExecutionStrategy rejects with InvalidOperationException
// ("The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does
// not support user-initiated transactions...") → HTTP 500 on every convert.
// Post-fix: the atomic unit runs inside Database.CreateExecutionStrategy()
// .ExecuteAsync(...), so it commits normally.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
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

public sealed class ApplicantConversionRetryStrategyTests : IAsyncLifetime
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
        // Mirror production DI (DependencyInjection.AddInfrastructure): Npgsql + the RETRYING
        // execution strategy + snake_case + the SaveChanges interceptors. The retry strategy is
        // what makes a direct BeginTransactionAsync throw (BUG-068).
        var interceptors = new List<IInterceptor>
        {
            new TenantInterceptor(tenantContext),
            new AuditInterceptor(currentUser),
            new AuditCaptureInterceptor(tenantContext, currentUser),
        };
        // A test-only interceptor (e.g. inject a transient failure to force an execution-strategy retry).
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
            Substitute.For<ISalaryAssignmentService>(),
            NullLogger<ApplicantConversionService>.Instance);
    }

    [Fact]
    public async Task Convert_UnderRetryingExecutionStrategy_Succeeds()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = MakeCurrentUser();

        await using var db = CreateContext(tenantContext, currentUser);
        await db.Database.MigrateAsync();

        var (applicantId, deptId, jobTitleId) = SeedConvertiblePipeline(db, _tenantId);
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

        // Pre-fix this threw InvalidOperationException (retrying strategy rejects the user-initiated
        // BeginTransactionAsync). Post-fix the conversion commits.
        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeId.Should().NotBeEmpty();
        result.Value.EmployeeNo.Should().NotBeNullOrWhiteSpace();

        var applicant = await db.Applicants.AsNoTracking().FirstAsync(a => a.Id == applicantId);
        applicant.ConvertedToEmployeeId.Should().Be(result.Value.EmployeeId);
    }

    // ── ISSUE-253 (BUG-252 class): audit row must be detached on the rollback path ──────────
    //
    // ConvertAsync runs the atomic unit inside strategy.ExecuteAsync(...). If the first attempt
    // fails AFTER the explicit "recruitment.applicant.converted" AuditLog has been Add()-ed, the
    // catch rolls back the DB transaction but the Added audit row stays tracked on the DbContext.
    // On a transient RETRY the still-tracked stale row would be inserted again (duplicate audit).
    // The fix detaches it in the catch (mirrors TenantDataDeletionService, BUG-252).
    //
    // Here we inject a one-shot TRANSIENT failure (TimeoutException — classified transient by
    // NpgsqlTransientExceptionDetector) on the save that carries the conversion audit, forcing the
    // execution strategy to re-invoke the delegate. We then assert NO stale Added conversion-audit
    // row lingers on the change tracker: pre-fix exactly one lingers (would double-insert), post-fix
    // zero. This test binds to the audit-detach MECHANISM specifically; the broader end-to-end
    // idempotency guarantee (the retry completes to a real success rather than a spurious
    // `already_converted`, with exactly one Employee / conversion audit) is covered by
    // Convert_FailsTransientlyOnFirstAttempt_SucceedsOnRetry_ExactlyOnce (BUG-264).
    [Fact]
    public async Task Convert_FailsAfterAuditAdded_DetachesAuditRow_SoRetryCannotDoubleInsert()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = MakeCurrentUser();

        var failOnce = new FailOnceOnConversionAuditInterceptor();

        await using var db = CreateContext(tenantContext, currentUser, failOnce);
        await db.Database.MigrateAsync();

        var (applicantId, deptId, jobTitleId) = SeedConvertiblePipeline(db, _tenantId);
        await db.SaveChangesAsync();

        var service = BuildService(db, tenantContext, currentUser);

        var input = new ConvertApplicantToEmployeeInput
        {
            ApplicantId = applicantId,
            DepartmentId = deptId,
            JobTitleId = jobTitleId,
            EmploymentType = EmploymentType.FullTime,
            DateOfJoining = DateTime.UtcNow.Date,
        };

        // The first attempt fails at the conversion-audit save; the strategy retries the delegate.
        // Whether it ends by returning `already_converted` (transient → retried) or by rethrowing
        // (defensive: if classification ever changes), the load-bearing assertion below is the same.
        try
        {
            await service.ConvertAsync(input);
        }
        catch
        {
            // Non-transient fallthrough — still exercises the rollback/detach path.
        }

        // Guard against a vacuous test: the injected failure must have actually fired on the save
        // that carried the conversion AuditLog (i.e. attempt 1 reached the Add()).
        failOnce.ThrowCount.Should().Be(1);

        // THE REGRESSION: the failed attempt's conversion AuditLog must NOT remain tracked as Added.
        // Pre-fix it lingers (count 1) and a retry re-inserts it; post-fix the catch detaches it (0).
        var staleAddedConversionAudits = db.ChangeTracker
            .Entries<AuditLog>()
            .Count(e => e.State == EntityState.Added
                     && e.Entity.EventType == "recruitment.applicant.converted");

        staleAddedConversionAudits.Should().Be(0,
            "the failed attempt's conversion AuditLog must be detached in the catch so a strategy "
            + "retry cannot re-insert it (ISSUE-253)");
    }

    // ── BUG-264 (ISSUE-253/BUG-252 class): the conversion must be IDEMPOTENT under a transient retry ──
    //
    // The broader sibling of the audit-detach gap. When the first attempt fails transiently AFTER the
    // Employee has been created (EmployeeService.CreateAsync does its own SaveChanges) and the Applicant
    // mutated (ConvertedToEmployeeId set), the retrying execution strategy re-invokes the whole delegate.
    // Pre-fix the rolled-back attempt's mutations survived in the ChangeTracker, so the retry either:
    //   (a) re-read the SAME tracked Applicant still carrying attempt-1's ConvertedToEmployeeId and
    //       returned a spurious `already_converted`, or
    //   (b) re-created the Employee (second employee-number) / double-inserted the audit row.
    // Post-fix the catch detaches the Employee, Vacancy, Applicant, and AuditLog, so the retry re-reads
    // clean DB-committed state and completes the conversion exactly once.
    //
    // This asserts the end-to-end idempotency guarantee (not just the change-tracker mechanism): the
    // conversion SUCCEEDS on the retry, and the DB ends with exactly ONE employee, the applicant converted
    // once, and exactly one conversion audit row. ThrowCount == 1 proves the retry actually fired (guards
    // against a vacuous pass). Pre-fix this FAILS: ConvertAsync returns `already_converted` (spurious).
    [Fact]
    public async Task Convert_FailsTransientlyOnFirstAttempt_SucceedsOnRetry_ExactlyOnce()
    {
        var tenantContext = new MutableTenantContext { TenantId = _tenantId };
        var currentUser = MakeCurrentUser();

        var failOnce = new FailOnceOnConversionAuditInterceptor();

        await using var db = CreateContext(tenantContext, currentUser, failOnce);
        await db.Database.MigrateAsync();

        var (applicantId, deptId, jobTitleId) = SeedConvertiblePipeline(db, _tenantId);
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

        // The injected transient failure must have fired exactly once (attempt 1) — proves the retry
        // path was genuinely exercised, not a happy-path single attempt.
        failOnce.ThrowCount.Should().Be(1);

        // Idempotency guarantee #1: the retry COMPLETES the conversion — it must NOT short-circuit with
        // the spurious `already_converted` produced pre-fix by the stale in-memory Applicant mutation.
        result.IsSuccess.Should().BeTrue(
            "the transient first attempt must be retried to a real success, not a spurious already_converted");
        result.ErrorCode.Should().NotBe("already_converted");
        result.Value!.EmployeeId.Should().NotBeEmpty();

        // Idempotency guarantee #2: exactly ONE Employee exists (no double-create / no second employee-number).
        var employees = await db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Email == "ada@acme.com").ToListAsync();
        employees.Should().HaveCount(1, "the created Employee must be detached on the failed attempt so the retry does not re-create it");
        employees[0].Id.Should().Be(result.Value.EmployeeId);

        // Idempotency guarantee #3: the applicant is Converted exactly once, linked to that single employee.
        var applicant = await db.Applicants.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == applicantId);
        applicant.ConvertedToEmployeeId.Should().Be(result.Value.EmployeeId);

        // Idempotency guarantee #4: exactly ONE conversion audit row committed (the detach prevents a double-insert).
        var conversionAudits = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(a => a.EventType == "recruitment.applicant.converted");
        conversionAudits.Should().Be(1);
    }

    /// <summary>
    /// Seeds the minimal convertible pipeline: a Hired applicant with an Accepted offer on an open
    /// vacancy, plus the Core HR master data EmployeeService validates. Returns the ids the caller needs.
    /// </summary>
    private static (Guid applicantId, Guid deptId, Guid jobTitleId) SeedConvertiblePipeline(
        AppDbContext db, Guid tenantId)
    {
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        var vacancyId = Guid.NewGuid();
        var applicantId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = "acme", Name = "Acme Corp" });
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

        return (applicantId, deptId, jobTitleId);
    }

    /// <summary>
    /// Test-only SaveChanges interceptor: throws a TRANSIENT <see cref="TimeoutException"/> exactly
    /// once, on the first save whose change set contains the explicit "recruitment.applicant.converted"
    /// AuditLog (the final ConvertAsync save, after the audit Add). NpgsqlTransientExceptionDetector
    /// classifies TimeoutException as transient, so the retrying execution strategy re-invokes the
    /// delegate — reproducing the ISSUE-253 retry path.
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
