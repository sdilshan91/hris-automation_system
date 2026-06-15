using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Validators;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Applicant submission + recruiter reads (US-REC-002). All queries are tenant-scoped via
/// ITenantContext + the EF global query filter (AC-5 cross-tenant isolation). The resume is virus-scanned
/// BEFORE its storage key is persisted (FR-3/NFR-4), stored under the tenant-scoped path
/// {tenantId}/recruitment/{vacancyId}/{applicantId}/{uuid-filename} (FR-2/BR-3) via the existing
/// IFileStorage seam, and duplicate submissions (same email, same vacancy) are rejected (BR-1/AC-3).
/// Confirmation (FR-5) + new-application (FR-7) notifications are fired via the log-only seam.
/// </summary>
public sealed class ApplicantService : IApplicantService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _virusScanner;
    private readonly IRecruitmentNotificationService _notifications;
    private readonly ILogger<ApplicantService> _logger;

    private const int MaxPageSize = 100;

    public ApplicantService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IVirusScanner virusScanner,
        IRecruitmentNotificationService notifications,
        ILogger<ApplicantService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _virusScanner = virusScanner;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Result<ApplicationConfirmationDto>> SubmitAsync(
        SubmitApplicationInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ApplicationConfirmationDto>.Failure("Tenant context is not resolved.", 400);

        // Defensive re-validation of the file (the FluentValidation validator is the primary gate, but
        // the anonymous public path and tests can call the service directly). AC-2 / BR-4.
        if (input.ResumeFileSize <= 0 || string.IsNullOrWhiteSpace(input.ResumeFileName))
            return Result<ApplicationConfirmationDto>.Failure("A resume file is required.", 400, "resume_required");
        if (input.ResumeFileSize > SubmitApplicationValidator.MaxResumeSizeBytes)
            return Result<ApplicationConfirmationDto>.Failure("Resume exceeds the 25 MB limit.", 400, "resume_too_large");
        if (input.ResumeContentType is null || !SubmitApplicationValidator.AllowedResumeMimeTypes.Contains(input.ResumeContentType))
            return Result<ApplicationConfirmationDto>.Failure("Resume must be a PDF, DOC, or DOCX file.", 400, "resume_type_not_allowed");

        // BR-6: vacancy must exist (in this tenant — the global filter scopes the lookup), be Open, and
        // be before the application deadline (if set).
        var vacancy = await _dbContext.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == input.VacancyId, cancellationToken);

        if (vacancy is null)
            return Result<ApplicationConfirmationDto>.Failure("Vacancy not found.", 404, "vacancy_not_found");

        if (vacancy.Status != VacancyStatus.Open)
            return Result<ApplicationConfirmationDto>.Failure(
                "This vacancy is not accepting applications.", 409, "vacancy_not_open");

        if (vacancy.ApplicationDeadline is { } deadline &&
            DateOnly.FromDateTime(DateTime.UtcNow) > deadline)
            return Result<ApplicationConfirmationDto>.Failure(
                "The application deadline for this vacancy has passed.", 409, "deadline_passed");

        // FR-8/BR-5: an internal application must link to an existing employee in this tenant.
        if (input.Source == ApplicationSource.Internal)
        {
            if (input.LinkedEmployeeId is not { } empId || empId == Guid.Empty)
                return Result<ApplicationConfirmationDto>.Failure(
                    "An internal application must be linked to an employee.", 400, "linked_employee_required");

            var employeeExists = await _dbContext.Employees.AnyAsync(e => e.Id == empId, cancellationToken);
            if (!employeeExists)
                return Result<ApplicationConfirmationDto>.Failure(
                    "The linked employee does not exist.", 404, "linked_employee_not_found");
        }

        var normalizedEmail = input.Email.Trim().ToLowerInvariant();

        // BR-1/AC-3: reject a duplicate application (same email, same vacancy). Case-insensitive match.
        var duplicate = await _dbContext.Applicants
            .AsNoTracking()
            .AnyAsync(a => a.VacancyId == input.VacancyId && a.Email.ToLower() == normalizedEmail, cancellationToken);

        if (duplicate)
            return Result<ApplicationConfirmationDto>.Failure(
                "You have already applied to this vacancy with this email address.", 409, "duplicate_application");

        // FR-3/NFR-4: virus scan BEFORE persisting the storage key.
        var scan = await _virusScanner.ScanAsync(input.ResumeStream, input.ResumeFileName, cancellationToken);
        if (!scan.IsClean)
        {
            _logger.LogWarning(
                "Application resume rejected by virus scanner. FileName={FileName}, Threat={Threat}, VacancyId={VacancyId}, TenantId={TenantId}",
                input.ResumeFileName, scan.ThreatName, input.VacancyId, _tenantContext.TenantId);

            return Result<ApplicationConfirmationDto>.Failure(
                $"Resume rejected by malware scanner: {scan.ThreatName}.", 400, "resume_infected");
        }

        if (input.ResumeStream.CanSeek)
            input.ResumeStream.Position = 0;

        // NOTE(FR-4 EXIF): resumes are PDF/DOC(X), not images, so there is no image EXIF to strip.
        // TODO(prod): if image attachments are ever allowed, reuse the EmployeeDocumentService EXIF seam.

        var applicantId = BaseEntity.NewUuidV7();
        var reference = await GenerateReferenceNumberAsync(cancellationToken);

        // BR-3: sanitize + UUID-rename the stored file name to prevent path traversal / collisions. The
        // original name is preserved in ResumeFileName for display.
        var storedFileName = BuildStoredFileName(input.ResumeFileName);
        var relativePath = $"recruitment/{input.VacancyId}/{applicantId}/{storedFileName}";

        // FR-2: store under the tenant-isolated path (IFileStorage prefixes {tenantId}).
        var storageKey = await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, input.ResumeStream, input.ResumeContentType, cancellationToken);

        var applicant = new Applicant
        {
            Id = applicantId,
            TenantId = _tenantContext.TenantId,
            VacancyId = input.VacancyId,
            ApplicationReferenceNumber = reference,
            FirstName = input.FirstName.Trim(),
            LastName = input.LastName.Trim(),
            Email = normalizedEmail,
            Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            CoverLetter = string.IsNullOrWhiteSpace(input.CoverLetter) ? null : input.CoverLetter.Trim(),
            ResumeStorageKey = relativePath,
            ResumeFileName = input.ResumeFileName,
            Stage = ApplicantStage.Applied,
            Source = input.Source,
            IsInternal = input.Source == ApplicationSource.Internal,
            LinkedEmployeeId = input.Source == ApplicationSource.Internal ? input.LinkedEmployeeId : null,
            AppliedAt = DateTime.UtcNow,
            IsDeleted = false,
        };

        _dbContext.Applicants.Add(applicant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Application submitted (Applied). ApplicantId={ApplicantId}, Ref={Ref}, VacancyId={VacancyId}, " +
            "Source={Source}, IsInternal={IsInternal}, StorageKey={StorageKey}, TenantId={TenantId}",
            applicant.Id, applicant.ApplicationReferenceNumber, applicant.VacancyId, applicant.Source,
            applicant.IsInternal, storageKey, _tenantContext.TenantId);

        // FR-5 + FR-7: fire notifications (log-only seam). Never let a notification failure fail the submit.
        try
        {
            await _notifications.NotifyApplicationReceivedAsync(
                applicant.Id, applicant.VacancyId, applicant.Email, applicant.ApplicationReferenceNumber, cancellationToken);
            await _notifications.NotifyNewApplicationAsync(
                applicant.Id, applicant.VacancyId, vacancy.HiringManagerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Application notifications failed (non-fatal). ApplicantId={ApplicantId}, TenantId={TenantId}",
                applicant.Id, _tenantContext.TenantId);
        }

        return Result<ApplicationConfirmationDto>.Success(new ApplicationConfirmationDto
        {
            ApplicationReferenceNumber = applicant.ApplicationReferenceNumber,
            VacancyTitle = vacancy.Title,
            Email = applicant.Email,
            AppliedAt = applicant.AppliedAt,
        });
    }

    public async Task<Result<ApplicantDto>> GetByIdAsync(Guid applicantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ApplicantDto>.Failure("Tenant context is not resolved.", 400);

        var applicant = await _dbContext.Applicants
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicantId, cancellationToken);

        if (applicant is null)
            return Result<ApplicantDto>.Failure("Applicant not found.", 404);

        var vacancyTitle = await _dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.Id == applicant.VacancyId)
            .Select(v => v.Title)
            .FirstOrDefaultAsync(cancellationToken);

        return Result<ApplicantDto>.Success(ToDto(applicant, vacancyTitle));
    }

    public async Task<Result<PagedResult<ApplicantListItemDto>>> ListByVacancyAsync(
        Guid vacancyId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PagedResult<ApplicantListItemDto>>.Failure("Tenant context is not resolved.", 400);

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? Math.Clamp(pageSize, 1, MaxPageSize) : pageSize;

        // Confirm the vacancy is in this tenant (global filter ⇒ a found row is in-tenant). 404 otherwise
        // so we never surface another tenant's applicants for a guessed vacancy id (AC-5).
        var vacancyExists = await _dbContext.Vacancies.AnyAsync(v => v.Id == vacancyId, cancellationToken);
        if (!vacancyExists)
            return Result<PagedResult<ApplicantListItemDto>>.Failure("Vacancy not found.", 404, "vacancy_not_found");

        var query = _dbContext.Applicants.AsNoTracking().Where(a => a.VacancyId == vacancyId);

        var totalCount = await query.CountAsync(cancellationToken);

        var applicants = await query
            .OrderByDescending(a => a.AppliedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = applicants.Select(a => new ApplicantListItemDto
        {
            Id = a.Id,
            VacancyId = a.VacancyId,
            ApplicationReferenceNumber = a.ApplicationReferenceNumber,
            FirstName = a.FirstName,
            LastName = a.LastName,
            Email = a.Email,
            Phone = a.Phone,
            ResumeFileName = a.ResumeFileName,
            Stage = a.Stage,
            StageName = a.Stage.ToString(),
            Source = a.Source,
            SourceName = a.Source.ToString(),
            IsInternal = a.IsInternal,
            AppliedAt = a.AppliedAt,
        }).ToList();

        return Result<PagedResult<ApplicantListItemDto>>.Success(new PagedResult<ApplicantListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    // ── Reference number + file-name helpers ─────────────────────────

    private async Task<string> GenerateReferenceNumberAsync(CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"APP-{year}-";

        // Highest existing suffix for this tenant+year. IgnoreQueryFilters scopes explicitly to the
        // tenant (and includes soft-deleted rows so a deleted ref number is never reused — collision-safe
        // with the partial unique index). Mirrors VacancyService.GenerateReferenceNumberAsync.
        var existing = await _dbContext.Applicants
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantContext.TenantId && a.ApplicationReferenceNumber.StartsWith(prefix))
            .Select(a => a.ApplicationReferenceNumber)
            .ToListAsync(cancellationToken);

        var maxSeq = 0;
        foreach (var refNo in existing)
        {
            var tail = refNo[prefix.Length..];
            if (int.TryParse(tail, out var seq) && seq > maxSeq)
                maxSeq = seq;
        }

        return $"{prefix}{(maxSeq + 1):D4}";
    }

    /// <summary>
    /// BR-3: produce a collision-free, path-traversal-safe stored file name: a fresh UUID plus the
    /// sanitized original extension (e.g. "0193abc...-7def.pdf"). The original name is kept separately
    /// for display.
    /// </summary>
    private static string BuildStoredFileName(string originalFileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(originalFileName));
        var safeExt = string.Empty;
        if (!string.IsNullOrEmpty(extension))
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                extension = extension.Replace(c, '_');
            // Keep a conservative extension (leading dot + alnum only).
            safeExt = "." + new string(extension.TrimStart('.').Where(char.IsLetterOrDigit).ToArray());
            if (safeExt == ".") safeExt = string.Empty;
        }

        return $"{Guid.NewGuid():N}{safeExt}";
    }

    private static ApplicantDto ToDto(Applicant a, string? vacancyTitle) => new()
    {
        Id = a.Id,
        VacancyId = a.VacancyId,
        VacancyTitle = vacancyTitle,
        ApplicationReferenceNumber = a.ApplicationReferenceNumber,
        FirstName = a.FirstName,
        LastName = a.LastName,
        Email = a.Email,
        Phone = a.Phone,
        CoverLetter = a.CoverLetter,
        ResumeFileName = a.ResumeFileName,
        ResumeStorageKey = a.ResumeStorageKey,
        Stage = a.Stage,
        StageName = a.Stage.ToString(),
        Source = a.Source,
        SourceName = a.Source.ToString(),
        IsInternal = a.IsInternal,
        LinkedEmployeeId = a.LinkedEmployeeId,
        AppliedAt = a.AppliedAt,
        CreatedAt = a.CreatedAt,
    };
}
