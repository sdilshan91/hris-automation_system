using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Converts an accepted applicant into a Core HR employee record (US-REC-010). All queries are tenant-scoped
/// via ITenantContext + the EF global query filter (AC-5). The conversion is ATOMIC (NFR-3): employee
/// creation + applicant linkage + vacancy filled-count update either all commit or all roll back. On a
/// relational provider this is wrapped in a DB transaction; on the InMemory provider (tests) transactions
/// are unsupported, so a single SaveChanges is used and the BeginTransaction is guarded behind
/// Database.IsRelational() (mirrors how the rest of the codebase handles this).
///
/// REUSE: employee creation goes through <see cref="IEmployeeService.CreateAsync"/> — it owns email
/// uniqueness (BR-2 employee), department/job-title validation, the subscription-plan limit (BR-3), and the
/// auto-generated employee number (FR-4). The few Core HR fields the create request doesn't carry
/// (a manual employee-number override, the structured LocationId FK, and the reporting manager) are applied
/// on the tracked entity within the same transaction.
///
/// FR-5/BR-7 auto-create user account: IMPLEMENTED (ISSUE-140), gated on the per-tenant
/// <see cref="Tenant.AutoCreateUserOnHire"/> toggle (default OFF — opt-in). When ON, the conversion
/// provisions a passwordless User + Active UserTenant + built-in "Employee" role and links Employee.UserId,
/// atomically inside the same transaction/SaveChanges as the conversion (see TryProvisionUserAccountAsync).
/// An existing global User with the same email is REUSED (no duplicate account). Credential DELIVERY (welcome
/// email) is deferred to US-NTF-006 — the account is created passwordless.
///
/// DEFERRALS (Phase 1):
///  - FR-8 onboarding workflow trigger: there is no Onboarding module yet — DEFERRED (log-only seam below).
///  - FR-9 welcome email: log-only seam (real email + the Hangfire NFR-5 async queue are deferred, same as
///    the rest of recruitment).
///  - Postgres RLS (NFR-2): tenant isolation is the EF query filter + TenantInterceptor (US-PLT-002 defers RLS).
/// </summary>
public sealed class ApplicantConversionService : IApplicantConversionService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeService _employeeService;
    private readonly IRecruitmentNotificationService _notifications;
    private readonly ILogger<ApplicantConversionService> _logger;

    public ApplicantConversionService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IEmployeeService employeeService,
        IRecruitmentNotificationService notifications,
        ILogger<ApplicantConversionService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _employeeService = employeeService;
        _notifications = notifications;
        _logger = logger;
    }

    // ── Pre-fill (AC-1/FR-2) ───────────────────────────────────────────

    public async Task<Result<ConversionPrefillDto>> GetPrefillAsync(
        Guid applicantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ConversionPrefillDto>.Failure("Tenant context is not resolved.", 400);

        var applicant = await _dbContext.Applicants
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicantId, cancellationToken);
        if (applicant is null)
            return Result<ConversionPrefillDto>.Failure("Applicant not found.", 404, "applicant_not_found");

        // FR-1: convertible only when Hired with an accepted offer.
        var eligibility = await CheckEligibilityAsync(applicant, cancellationToken);
        if (eligibility.IsFailure)
            return Result<ConversionPrefillDto>.Failure(eligibility.Error!, eligibility.StatusCode ?? 409, eligibility.ErrorCode);

        var offer = eligibility.Value!;

        var vacancy = await _dbContext.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == applicant.VacancyId, cancellationToken);

        var departmentId = offer.DepartmentId ?? vacancy?.DepartmentId;
        var departmentName = departmentId is { } did
            ? await _dbContext.Departments.AsNoTracking().Where(d => d.Id == did).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        var managerName = offer.ReportingManagerEmployeeId is { } mid
            ? await _dbContext.Employees.AsNoTracking().Where(e => e.Id == mid).Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync(cancellationToken)
            : null;

        var jobTitleName = vacancy?.JobTitleId is { } jid
            ? await _dbContext.JobTitles.AsNoTracking().Where(j => j.Id == jid).Select(j => j.TitleName).FirstOrDefaultAsync(cancellationToken)
            : null;

        return Result<ConversionPrefillDto>.Success(new ConversionPrefillDto
        {
            ApplicantId = applicant.Id,
            VacancyId = applicant.VacancyId,
            VacancyTitle = vacancy?.Title,
            OfferId = offer.Id,
            OfferReferenceNumber = offer.OfferReferenceNumber,
            FirstName = applicant.FirstName,
            LastName = applicant.LastName,
            Email = applicant.Email,
            Phone = applicant.Phone,
            OfferedPosition = offer.OfferedPosition,
            DepartmentId = departmentId,
            DepartmentName = departmentName,
            ReportingManagerEmployeeId = offer.ReportingManagerEmployeeId,
            ReportingManagerName = managerName?.Trim(),
            SalaryAmount = offer.SalaryAmount,
            Currency = offer.Currency,
            SalaryFrequency = offer.SalaryFrequency,
            StartDate = offer.StartDate,
            ProbationMonths = offer.ProbationMonths,
            JobTitleId = vacancy?.JobTitleId,
            JobTitleName = jobTitleName,
            LocationId = vacancy?.LocationId,
            EmploymentType = vacancy?.EmploymentType ?? EmploymentType.FullTime,
            AlreadyConverted = applicant.ConvertedToEmployeeId is not null,
            ConvertedToEmployeeId = applicant.ConvertedToEmployeeId,
        });
    }

    // ── Convert (AC-2/AC-4/FR-1/FR-4/FR-6/FR-7/FR-10/BR-2/BR-3/BR-5/NFR-3) ──

    public async Task<Result<ConversionResultDto>> ConvertAsync(
        ConvertApplicantToEmployeeInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ConversionResultDto>.Failure("Tenant context is not resolved.", 400);

        // BUG-068: a user-initiated BeginTransactionAsync throws under Npgsql's retrying execution
        // strategy ("does not support user-initiated transactions") — the whole atomic unit (NFR-3)
        // must run *inside* the execution strategy delegate so it is retried as a single retriable
        // unit. Reads live inside the delegate too, so a transient retry re-reads from the DB rather
        // than re-using the state from the rolled-back attempt.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var applicant = await _dbContext.Applicants
                .FirstOrDefaultAsync(a => a.Id == input.ApplicantId, cancellationToken);
            if (applicant is null)
                return Result<ConversionResultDto>.Failure("Applicant not found.", 404, "applicant_not_found");

            // FR-10/BR-2: duplicate-conversion prevention.
            if (applicant.ConvertedToEmployeeId is not null)
                return Result<ConversionResultDto>.Failure(
                    "This applicant has already been converted to an employee.", 409, "already_converted");

            // FR-1: preconditions — Hired stage + an Accepted offer.
            var eligibility = await CheckEligibilityAsync(applicant, cancellationToken);
            if (eligibility.IsFailure)
                return Result<ConversionResultDto>.Failure(eligibility.Error!, eligibility.StatusCode ?? 409, eligibility.ErrorCode);

            // NFR-3: atomic. Guard BeginTransaction behind a relational provider (InMemory has no transactions).
            var useTransaction = _dbContext.Database.IsRelational();
            await using var transaction = useTransaction
                ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            // Hoisted so the catch can detach every entity this attempt touched on a rollback. The retrying
            // execution strategy re-invokes this whole delegate on a transient failure, and a rollback reverts
            // only the DB — the tracked mutations survive in the ChangeTracker. Unless they are detached the
            // retry would re-insert the Added AuditLog, re-create the Employee (double-create / second
            // employee-number), double-increment the Vacancy, and re-read the SAME tracked Applicant still
            // carrying attempt-1's ConvertedToEmployeeId (spurious `already_converted`). ISSUE-253/BUG-252/BUG-264.
            AuditLog? auditLog = null;
            Employee? employee = null;
            Vacancy? vacancy = null;
            // FR-5: entities the (optional) user-account provisioning added this attempt. Detached on rollback
            // alongside the others so the retrying execution strategy re-inserts nothing (BUG-264).
            var provisioned = new List<object>();
            try
            {
                // Reuse the Core HR create path: email uniqueness (BR-2 employee), dept/job-title validation,
                // subscription-plan limit (BR-3), and auto employee-number (FR-4). DateOfJoining defaults to the
                // offer start date (BR-4) but is supplied by the caller (overridable on the form).
                var createResult = await _employeeService.CreateAsync(new CreateEmployeeRequest
                {
                    FirstName = applicant.FirstName,
                    LastName = applicant.LastName,
                    Email = applicant.Email,
                    Phone = applicant.Phone,
                    DateOfBirth = input.DateOfBirth,
                    Gender = input.Gender,
                    DateOfJoining = input.DateOfJoining,
                    DepartmentId = input.DepartmentId,
                    JobTitleId = input.JobTitleId,
                    EmploymentType = input.EmploymentType,
                    Status = null, // Core HR defaults Active (probation handling is a separate Core HR concern).
                }, cancellationToken);

                if (createResult.IsFailure)
                {
                    if (transaction is not null)
                        await transaction.RollbackAsync(cancellationToken);
                    // Surface the Core HR failure verbatim (e.g. duplicate email 400, plan limit 403/BR-3).
                    return Result<ConversionResultDto>.Failure(
                        createResult.Error!, createResult.StatusCode ?? 400, createResult.ErrorCode);
                }

                var employeeId = createResult.Value!.Id;

                // Apply the Core HR fields the create request doesn't carry: manual employee-number override
                // (FR-4), the structured LocationId FK, and the reporting manager. Tracked fetch (no AsNoTracking).
                employee = await _dbContext.Employees.FirstAsync(e => e.Id == employeeId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(input.EmployeeNo))
                    employee.EmployeeNo = input.EmployeeNo.Trim();
                employee.LocationId = input.LocationId;
                employee.ReportsToEmployeeId = input.ReportsToEmployeeId;

                // FR-6: link the applicant to the new employee record (one-way, BR-6 — applicant not deleted).
                applicant.ConvertedToEmployeeId = employeeId;
                applicant.ConvertedAt = DateTime.UtcNow;
                applicant.ConvertedByUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null;

                // FR-7/BR-5: increment the vacancy filled-count; auto-close when fully filled.
                var vacancyClosed = false;
                var filledCount = 0;
                var headcount = 0;
                vacancy = await _dbContext.Vacancies.FirstOrDefaultAsync(v => v.Id == applicant.VacancyId, cancellationToken);
                if (vacancy is not null)
                {
                    vacancy.FilledCount += 1;
                    filledCount = vacancy.FilledCount;
                    headcount = vacancy.Headcount;
                    if (vacancy.FilledCount >= vacancy.Headcount &&
                        vacancy.Status is VacancyStatus.Open or VacancyStatus.OnHold)
                    {
                        vacancy.Status = VacancyStatus.Closed;
                        vacancy.ClosedAt = DateTime.UtcNow;
                        vacancyClosed = true;
                    }
                }

                // Audit trail for the conversion (security-relevant org change).
                auditLog = new AuditLog
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
                    EventType = "recruitment.applicant.converted",
                    Detail = $"Applicant {applicant.ApplicationReferenceNumber} converted to employee {employee.EmployeeNo} ({employeeId}).",
                };
                _dbContext.AuditLogs.Add(auditLog);

                // FR-5/BR-7: auto-create the login account when the tenant toggle is on (passwordless; credential
                // delivery is deferred to US-NTF-006). Runs inside the same atomic unit as the conversion.
                var userAccountCreated = await TryProvisionUserAccountAsync(applicant, employee, provisioned, cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Applicant converted to employee. ApplicantId={ApplicantId}, EmployeeId={EmployeeId}, " +
                    "EmployeeNo={EmployeeNo}, VacancyId={VacancyId}, FilledCount={FilledCount}/{Headcount}, " +
                    "VacancyClosed={VacancyClosed}, TenantId={TenantId}, By={User}",
                    applicant.Id, employeeId, employee.EmployeeNo, applicant.VacancyId, filledCount, headcount,
                    vacancyClosed, _tenantContext.TenantId, _currentUser.Email);

                // FR-7/BR-5 + FR-8/FR-9: log-only seams (no real email/onboarding module yet — deferred).
                await PostConversionNotificationsSafeAsync(applicant, employeeId, vacancyClosed, cancellationToken);

                return Result<ConversionResultDto>.Success(new ConversionResultDto
                {
                    EmployeeId = employeeId,
                    EmployeeNo = employee.EmployeeNo,
                    ApplicantId = applicant.Id,
                    VacancyId = applicant.VacancyId,
                    VacancyFilledCount = filledCount,
                    VacancyHeadcount = headcount,
                    VacancyClosed = vacancyClosed,
                    UserAccountCreated = userAccountCreated, // FR-5 — gated on Tenant.AutoCreateUserOnHire.
                });
            }
            catch
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(cancellationToken);
                // BUG-264: detach EVERY entity this failed attempt mutated so the retrying execution strategy's
                // re-invocation starts from clean, DB-committed state. Rollback reverts the DB but not the
                // ChangeTracker, so without this the retry would double-insert the audit row, re-create the
                // Employee (second employee-number), double-increment the Vacancy, and short-circuit on the
                // stale Applicant.ConvertedToEmployeeId with a spurious `already_converted`. The top-of-delegate
                // reads then re-materialize each entity fresh, making the conversion idempotent under retry.
                if (auditLog is not null)
                    _dbContext.Entry(auditLog).State = EntityState.Detached;
                if (employee is not null)
                    _dbContext.Entry(employee).State = EntityState.Detached;
                if (vacancy is not null)
                    _dbContext.Entry(vacancy).State = EntityState.Detached;
                foreach (var e in provisioned)
                    _dbContext.Entry(e).State = EntityState.Detached;
                _dbContext.Entry(applicant).State = EntityState.Detached;
                throw;
            }
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// FR-1: an applicant is convertible only when in the Hired stage with an Accepted offer. Returns the
    /// most recent (highest-version) accepted offer on success.
    /// </summary>
    private async Task<Result<Offer>> CheckEligibilityAsync(Applicant applicant, CancellationToken cancellationToken)
    {
        if (applicant.Stage != ApplicantStage.Hired)
            return Result<Offer>.Failure(
                "The applicant must be in the Hired stage to be converted.", 409, "applicant_not_hired");

        var acceptedOffer = await _dbContext.Offers
            .AsNoTracking()
            .Where(o => o.ApplicantId == applicant.Id && o.Status == OfferStatus.Accepted)
            .OrderByDescending(o => o.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (acceptedOffer is null)
            return Result<Offer>.Failure(
                "The applicant does not have an accepted offer.", 409, "no_accepted_offer");

        return Result<Offer>.Success(acceptedOffer);
    }

    /// <summary>
    /// FR-5/BR-7 (ISSUE-140): when the tenant's <see cref="Tenant.AutoCreateUserOnHire"/> toggle is on, provision
    /// a login account for the new employee — a passwordless <see cref="User"/> (credential delivery deferred to
    /// US-NTF-006), an Active <see cref="UserTenant"/> membership, and the built-in "Employee" role — then link
    /// <c>Employee.UserId</c>. An existing GLOBAL user with the same email is REUSED (no duplicate account). All
    /// entities are only Add()-ed here; the caller's single SaveChanges persists them in the same atomic unit
    /// (NFR-3), and the newly-Added rows are recorded in <paramref name="provisioned"/> so the retry rollback
    /// detaches them (BUG-264). Returns true when the toggle is on (an account was created or reused + linked).
    /// </summary>
    private async Task<bool> TryProvisionUserAccountAsync(
        Applicant applicant, Employee employee, List<object> provisioned, CancellationToken cancellationToken)
    {
        var autoCreate = await _dbContext.Tenants
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.AutoCreateUserOnHire)
            .FirstOrDefaultAsync(cancellationToken);
        if (!autoCreate) return false;

        var email = applicant.Email.Trim().ToLowerInvariant();

        // Users are global (not tenant-scoped) — IgnoreQueryFilters so an existing account under another tenant
        // is reused rather than duplicated (mirrors TenantProvisioningService's owner-link path).
        var user = await _dbContext.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = BaseEntity.NewUuidV7(),
                Email = email,
                DisplayName = $"{applicant.FirstName} {applicant.LastName}".Trim(),
                PasswordHash = null, // passwordless — credential delivery deferred (US-NTF-006).
                IsActive = true,
            };
            _dbContext.Users.Add(user);
            provisioned.Add(user);
        }

        var membership = await _dbContext.UserTenants
            .FirstOrDefaultAsync(ut => ut.UserId == user.Id && ut.TenantId == _tenantContext.TenantId, cancellationToken);
        if (membership is null)
        {
            membership = new UserTenant
            {
                Id = BaseEntity.NewUuidV7(),
                UserId = user.Id,
                TenantId = _tenantContext.TenantId,
                Status = UserTenantStatus.Active,
            };
            _dbContext.UserTenants.Add(membership);
            provisioned.Add(membership);
        }
        else if (membership.Status != UserTenantStatus.Active)
        {
            membership.Status = UserTenantStatus.Active;
        }

        var employeeRoleId = await _dbContext.Roles
            .Where(r => r.TenantId == _tenantContext.TenantId && r.Name == PermissionCatalog.BuiltInRoles.Employee)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (employeeRoleId != Guid.Empty &&
            !await _dbContext.UserTenantRoles.AnyAsync(
                x => x.UserTenantId == membership.Id && x.RoleId == employeeRoleId, cancellationToken))
        {
            var utr = new UserTenantRole
            {
                UserTenantId = membership.Id,
                RoleId = employeeRoleId,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = _currentUser.IsAuthenticated ? _currentUser.UserId.ToString() : null,
            };
            _dbContext.UserTenantRoles.Add(utr);
            provisioned.Add(utr);
        }

        employee.UserId = user.Id; // link the login account to the employee record.
        return true;
    }

    /// <summary>
    /// FR-7/BR-5 recruiter notification (vacancy auto-closed) + FR-8 onboarding trigger + FR-9 welcome email.
    /// All log-only seams (real delivery + the Onboarding module are deferred). Never fails the committed
    /// conversion (mirrors the other recruitment notification handling).
    /// </summary>
    private async Task PostConversionNotificationsSafeAsync(
        Applicant applicant, Guid employeeId, bool vacancyClosed, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.NotifyStageChangedAsync(
                applicant.Id, applicant.VacancyId, applicant.Email,
                ApplicantStage.Hired.ToString(), "Converted", cancellationToken);

            _logger.LogInformation(
                "Conversion seams (log-only). EmployeeId={EmployeeId}, ApplicantId={ApplicantId}, " +
                "VacancyClosed={VacancyClosed}: FR-9 welcome email DEFERRED (no email platform); " +
                "FR-8 onboarding trigger DEFERRED (no onboarding module); recruiter notify on auto-close DEFERRED.",
                employeeId, applicant.Id, vacancyClosed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Post-conversion notification failed (non-fatal). ApplicantId={ApplicantId}, TenantId={TenantId}",
                applicant.Id, _tenantContext.TenantId);
        }
    }
}
