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

    // ── US-REC-003: pipeline board, applicant detail, stage moves ────

    public async Task<Result<ApplicantPipelineBoardDto>> GetPipelineBoardAsync(
        Guid vacancyId, PipelineFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ApplicantPipelineBoardDto>.Failure("Tenant context is not resolved.", 400);

        // Confirm the vacancy is in this tenant (global filter ⇒ a found row is in-tenant). 404 otherwise
        // so a guessed cross-tenant vacancy id never surfaces another tenant's board (AC-5).
        var vacancy = await _dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.Id == vacancyId)
            .Select(v => new { v.Id, v.Title })
            .FirstOrDefaultAsync(cancellationToken);

        if (vacancy is null)
            return Result<ApplicantPipelineBoardDto>.Failure("Vacancy not found.", 404, "vacancy_not_found");

        var query = _dbContext.Applicants.AsNoTracking().Where(a => a.VacancyId == vacancyId);

        // FR-6/AC-4 filters. The board returns ALL matching applicants (no paging — NFR-1 targets ≤200
        // per vacancy, which is the Kanban use case).
        if (filter.Stage is { } stage)
            query = query.Where(a => a.Stage == stage);

        if (filter.Source is { } source)
            query = query.Where(a => a.Source == source);

        if (filter.AppliedFrom is { } from)
            query = query.Where(a => a.AppliedAt >= from);

        if (filter.AppliedTo is { } to)
            query = query.Where(a => a.AppliedAt <= to);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(a =>
                a.FirstName.ToLower().Contains(term) ||
                a.LastName.ToLower().Contains(term) ||
                a.Email.ToLower().Contains(term));
        }

        var applicants = await query
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync(cancellationToken);

        // FR-1: one column per stage, in pipeline (enum) order, even when empty (so the Kanban renders
        // every column). FR-5: per-stage count + overall total.
        var stages = Enum.GetValues<ApplicantStage>()
            .OrderBy(s => (int)s)
            .Select((s, order) =>
            {
                var cards = applicants
                    .Where(a => a.Stage == s)
                    .Select(ToCard)
                    .ToList();

                return new PipelineStageColumnDto
                {
                    Stage = s,
                    StageName = s.ToString(),
                    Order = order,
                    Count = cards.Count,
                    Applicants = cards,
                };
            })
            .ToList();

        return Result<ApplicantPipelineBoardDto>.Success(new ApplicantPipelineBoardDto
        {
            VacancyId = vacancy.Id,
            VacancyTitle = vacancy.Title,
            Stages = stages,
            Total = applicants.Count,
        });
    }

    public async Task<Result<ApplicantDetailDto>> GetDetailAsync(
        Guid applicantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ApplicantDetailDto>.Failure("Tenant context is not resolved.", 400);

        var applicant = await _dbContext.Applicants
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicantId, cancellationToken);

        if (applicant is null)
            return Result<ApplicantDetailDto>.Failure("Applicant not found.", 404);

        var vacancyTitle = await _dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.Id == applicant.VacancyId)
            .Select(v => v.Title)
            .FirstOrDefaultAsync(cancellationToken);

        // BR-5/AC-3: stage-transition timeline, newest first. Tenant-scoped by the global filter.
        var history = await _dbContext.ApplicantStageHistories
            .AsNoTracking()
            .Where(h => h.ApplicantId == applicantId)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync(cancellationToken);

        var timeline = history.Select(h => new ApplicantStageHistoryDto
        {
            Id = h.Id,
            FromStage = h.FromStage,
            FromStageName = h.FromStage.ToString(),
            ToStage = h.ToStage,
            ToStageName = h.ToStage.ToString(),
            ChangedByUserId = h.ChangedByUserId,
            Reason = h.Reason,
            Notes = h.Notes,
            ChangedAt = h.ChangedAt,
        }).ToList();

        return Result<ApplicantDetailDto>.Success(new ApplicantDetailDto
        {
            Profile = ToDto(applicant, vacancyTitle),
            StageHistory = timeline,
            ResumeFileName = applicant.ResumeFileName,
            // NFR-5: do NOT expose the raw blob path. A short-lived signed URL is deferred (local-dev
            // file-storage stub); the FE downloads via this authenticated route instead.
            ResumeDownloadUrl =
                $"/api/v1/recruitment/applicants/{applicant.Id}/resume",
            Interviews = [], // DEFERRED: no interview module yet (US-REC-005/006).
        });
    }

    public async Task<Result<MoveApplicantStageResultDto>> MoveStageAsync(
        Guid applicantId, ApplicantStage toStage, string? reason, string? notes,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<MoveApplicantStageResultDto>.Failure("Tenant context is not resolved.", 400);

        var applicant = await _dbContext.Applicants
            .FirstOrDefaultAsync(a => a.Id == applicantId, cancellationToken);

        if (applicant is null)
            return Result<MoveApplicantStageResultDto>.Failure("Applicant not found.", 404, "applicant_not_found");

        var ruleCheck = ApplyStageMove(applicant, toStage, reason, notes, out var historyRow);
        if (ruleCheck.IsFailure)
            return Result<MoveApplicantStageResultDto>.Failure(ruleCheck.Error!, ruleCheck.StatusCode ?? 400, ruleCheck.ErrorCode);

        WriteStageChangeAuditLog(historyRow!);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<MoveApplicantStageResultDto>.Success(ToMoveResult(historyRow!));
    }

    public async Task<Result<BulkMoveApplicantStageResultDto>> BulkMoveStageAsync(
        IReadOnlyList<Guid> applicantIds, ApplicantStage toStage, string? reason, string? notes,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BulkMoveApplicantStageResultDto>.Failure("Tenant context is not resolved.", 400);

        if (applicantIds is null || applicantIds.Count == 0)
            return Result<BulkMoveApplicantStageResultDto>.Failure("At least one applicant must be selected.", 400, "no_applicants");

        var distinctIds = applicantIds.Distinct().ToList();

        // Tenant-scoped fetch of all targets (global filter ⇒ only this tenant's rows; cross-tenant ids
        // simply don't load — AC-5).
        var applicants = await _dbContext.Applicants
            .Where(a => distinctIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        if (applicants.Count != distinctIds.Count)
            return Result<BulkMoveApplicantStageResultDto>.Failure(
                "One or more applicants were not found.", 404, "applicant_not_found");

        // All-or-nothing: validate every move first, collect history rows, then persist together so the
        // caller never gets a partial move.
        var historyRows = new List<ApplicantStageHistory>(applicants.Count);
        foreach (var applicant in applicants)
        {
            var ruleCheck = ApplyStageMove(applicant, toStage, reason, notes, out var historyRow);
            if (ruleCheck.IsFailure)
                return Result<BulkMoveApplicantStageResultDto>.Failure(ruleCheck.Error!, ruleCheck.StatusCode ?? 400, ruleCheck.ErrorCode);

            historyRows.Add(historyRow!);
        }

        foreach (var row in historyRows)
            WriteStageChangeAuditLog(row);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<BulkMoveApplicantStageResultDto>.Success(new BulkMoveApplicantStageResultDto
        {
            MovedCount = historyRows.Count,
            Results = historyRows.Select(ToMoveResult).ToList(),
        });
    }

    /// <summary>
    /// Applies the stage-move business rules to a tracked applicant, mutates its stage, and stages an
    /// <see cref="ApplicantStageHistory"/> row in the change tracker (BR-5). Does NOT call SaveChanges —
    /// the caller batches the save (so bulk moves are atomic). Rules:
    /// BR-3 Rejected requires a reason; BR-4 a backward move (lower stage index) requires a reason;
    /// BR-6 Hired is allowed (terminal — convert-to-employee is US-REC-010, out of scope). A no-op move
    /// (same stage) is rejected. Backward/forward permission (BR-1/BR-2/BR-4) is enforced at the API
    /// layer (the move endpoint requires Recruitment.Manage), so any caller here already holds Manage.
    /// </summary>
    private Result ApplyStageMove(
        Applicant applicant, ApplicantStage toStage, string? reason, string? notes,
        out ApplicantStageHistory? historyRow)
    {
        historyRow = null;

        var fromStage = applicant.Stage;

        if (fromStage == toStage)
            return Result.Failure("The applicant is already in this stage.", 409, "stage_unchanged");

        var isBackward = (int)toStage < (int)fromStage;

        // BR-3: moving to Rejected requires a reason.
        if (toStage == ApplicantStage.Rejected && string.IsNullOrWhiteSpace(reason))
            return Result.Failure("A reason is required when rejecting an applicant.", 400, "reason_required");

        // BR-4: a backward move requires a reason (the Recruitment.Manage permission is enforced at the
        // API layer — every mover here already holds it).
        if (isBackward && string.IsNullOrWhiteSpace(reason))
            return Result.Failure("A reason is required to move an applicant to an earlier stage.", 400, "reason_required");

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        applicant.Stage = toStage;

        historyRow = new ApplicantStageHistory
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            ApplicantId = applicant.Id,
            FromStage = fromStage,
            ToStage = toStage,
            ChangedByUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            Reason = trimmedReason,
            Notes = trimmedNotes,
            ChangedAt = DateTime.UtcNow,
            IsDeleted = false,
        };

        _dbContext.ApplicantStageHistories.Add(historyRow);

        _logger.LogInformation(
            "Applicant stage moved. ApplicantId={ApplicantId}, From={From}, To={To}, " +
            "ChangedByUserId={UserId}, TenantId={TenantId}",
            applicant.Id, fromStage, toStage, historyRow.ChangedByUserId, _tenantContext.TenantId);

        return Result.Success();
    }

    /// <summary>AC-2: record a tenant-scoped audit-log entry for the stage transition.</summary>
    private void WriteStageChangeAuditLog(ApplicantStageHistory row)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = "recruitment.applicant.stage_changed",
            Detail =
                $"Applicant {row.ApplicantId} moved {row.FromStage} -> {row.ToStage}" +
                (row.Reason is null ? string.Empty : $". Reason: {row.Reason}"),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static MoveApplicantStageResultDto ToMoveResult(ApplicantStageHistory row) => new()
    {
        ApplicantId = row.ApplicantId,
        FromStage = row.FromStage,
        FromStageName = row.FromStage.ToString(),
        ToStage = row.ToStage,
        ToStageName = row.ToStage.ToString(),
        StageHistoryId = row.Id,
        ChangedAt = row.ChangedAt,
    };

    private static PipelineApplicantCardDto ToCard(Applicant a) => new()
    {
        Id = a.Id,
        ApplicationReferenceNumber = a.ApplicationReferenceNumber,
        FirstName = a.FirstName,
        LastName = a.LastName,
        FullName = $"{a.FirstName} {a.LastName}".Trim(),
        Email = a.Email,
        Source = a.Source,
        SourceName = a.Source.ToString(),
        IsInternal = a.IsInternal,
        AppliedAt = a.AppliedAt,
    };

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

    public async Task<Result<ResumeDownloadDto>> GetResumeAsync(
        Guid applicantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ResumeDownloadDto>.Failure("Tenant context is not resolved.", 400);

        // Tenant-scoped by the global query filter (AC-5).
        var applicant = await _dbContext.Applicants
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicantId, cancellationToken);

        if (applicant is null)
            return Result<ResumeDownloadDto>.Failure("Applicant not found.", 404, "applicant_not_found");

        if (string.IsNullOrWhiteSpace(applicant.ResumeStorageKey))
            return Result<ResumeDownloadDto>.Failure("This applicant has no resume on file.", 404, "resume_not_found");

        await using var stream = await _fileStorage.OpenReadAsync(
            _tenantContext.TenantId, applicant.ResumeStorageKey, cancellationToken);

        if (stream is null)
            return Result<ResumeDownloadDto>.Failure("The resume file could not be found in storage.", 404, "resume_not_found");

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return Result<ResumeDownloadDto>.Success(new ResumeDownloadDto
        {
            Content = buffer.ToArray(),
            FileName = string.IsNullOrWhiteSpace(applicant.ResumeFileName) ? "resume" : applicant.ResumeFileName,
            ContentType = InferContentType(applicant.ResumeFileName),
        });
    }

    // FR-7: resumes are PDF/DOC/DOCX (BR-4); infer the response content type from the original filename.
    private static string InferContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            _ => "application/octet-stream",
        };

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
