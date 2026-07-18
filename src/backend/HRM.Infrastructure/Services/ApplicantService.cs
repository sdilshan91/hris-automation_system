using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.DTOs;
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
    private readonly IHtmlSanitizer _sanitizer;
    private readonly ILogger<ApplicantService> _logger;

    private const int MaxPageSize = 100;

    public ApplicantService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IVirusScanner virusScanner,
        IRecruitmentNotificationService notifications,
        IHtmlSanitizer sanitizer,
        ILogger<ApplicantService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _virusScanner = virusScanner;
        _notifications = notifications;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    public async Task<Result<ApplicationConfirmationDto>> SubmitAsync(
        SubmitApplicationInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ApplicationConfirmationDto>.Failure("Tenant context is not resolved.", 400);

        // ISSUE-095 / US-REC-002 BR-5: the anonymous public path is only open when the tenant's
        // PublicCareersEnabled toggle is on. Mirrors the public list/detail (which return empty/404 when
        // off) — return 404 rather than disclose that a vacancy exists. The internal path is unaffected.
        if (input.Source == ApplicationSource.Public)
        {
            var careersEnabled = await _dbContext.Tenants
                .AsNoTracking()
                .Where(t => t.Id == _tenantContext.TenantId)
                .Select(t => t.PublicCareersEnabled)
                .FirstOrDefaultAsync(cancellationToken);
            if (!careersEnabled)
                return Result<ApplicationConfirmationDto>.Failure("Vacancy not found.", 404, "vacancy_not_found");
        }

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

        // BUG-058: sniff the real magic bytes BEFORE the virus scan — the allow-list above only checks the
        // client-supplied Content-Type, so a renamed .exe with a PDF/DOCX MIME string would otherwise pass.
        // Reject (400 invalid_file_type) when the bytes don't match the declared type. Resets the stream.
        var signature = await FileSignatureValidator.ValidateStreamAsync(
            input.ResumeContentType, input.ResumeStream, cancellationToken);
        if (signature.IsFailure)
            return Result<ApplicationConfirmationDto>.Failure(
                "Resume must be a PDF, DOC, or DOCX file.", 400, FileSignatureValidator.ErrorCode);

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
            // ISSUE-103: sanitize applicant free-text (name, cover letter) server-side, matching the sibling
            // VacancyService.Description path — an injected <script>/<img onerror> payload is stripped on write
            // so it can never be stored to be rendered later in a recruiter UI, email, or exported PDF/CSV.
            FirstName = _sanitizer.Sanitize(input.FirstName.Trim()) ?? string.Empty,
            LastName = _sanitizer.Sanitize(input.LastName.Trim()) ?? string.Empty,
            Email = normalizedEmail,
            Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            CoverLetter = string.IsNullOrWhiteSpace(input.CoverLetter) ? null : _sanitizer.Sanitize(input.CoverLetter.Trim()),
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
        // ISSUE-104 (US-REC-005 audit): write a queryable tenant audit row for the public application
        // submission (with an after-snapshot), not just an ILogger line. Public/anonymous path ⇒ UserId is
        // null (system/applicant actor); TenantId is stamped from the resolved tenant context (the guard at
        // the top of SubmitAsync already required _tenantContext.IsResolved).
        AddApplicantAudit("Application.Submitted", applicant.Id, after: SnapshotApplicant(applicant),
            $"Application {applicant.ApplicationReferenceNumber} submitted to vacancy {applicant.VacancyId}.");
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
            ConvertedToEmployeeId = a.ConvertedToEmployeeId,
            IsConverted = a.ConvertedToEmployeeId != null,
            ConvertedAt = a.ConvertedAt,
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
        // every column). FR-5: per-stage count + overall total. The Unknown sentinel (ISSUE-231) is not a real
        // pipeline stage — it only ever appears when a corrupt row is read through the tolerant converter — so
        // it never gets a column; such a card is logged at read time and simply not shown on the board.
        var stages = Enum.GetValues<ApplicantStage>()
            .Where(s => s != ApplicantStage.Unknown)
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
            RejectionReason = h.RejectionReason,
            RejectionReasonName = h.RejectionReason?.ToString(),
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
        uint expectedRowVersion = 0,
        RejectionReason? rejectionReason = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<MoveApplicantStageResultDto>.Failure("Tenant context is not resolved.", 400);

        var applicant = await _dbContext.Applicants
            .FirstOrDefaultAsync(a => a.Id == applicantId, cancellationToken);

        if (applicant is null)
            return Result<MoveApplicantStageResultDto>.Failure("Applicant not found.", 404, "applicant_not_found");

        // ISSUE-109 (NFR): guard the stage move with the client's read-time concurrency token. Without
        // this, EF's OriginalValue equals the just-read DB xmin, so the guarded UPDATE always matches and
        // a concurrent stage move silently clobbers. Setting OriginalValue to the client's token makes a
        // stale write raise DbUpdateConcurrencyException -> 409. Mirrors EmployeeService.UpdateProfileAsync.
        _dbContext.Entry(applicant).Property(a => a.RowVersion).OriginalValue = expectedRowVersion;

        // FR-8/BR-4: load the owning vacancy (tenant-scoped) for the Closed/Cancelled gate and the
        // headcount-filled warning.
        var vacancy = await _dbContext.Vacancies
            .FirstOrDefaultAsync(v => v.Id == applicant.VacancyId, cancellationToken);

        var hiredCount = await CountHiredAsync(applicant.VacancyId, cancellationToken);

        // US-REC-006 BR-6: Offer-gate input — does the applicant have ≥1 scorecard?
        var hasScorecard = await HasAnyScorecardAsync(applicant.Id, cancellationToken);
        // ISSUE-108 / BR-1: Interview-gate input — does the applicant have ≥1 interview on record?
        var hasScheduledInterview = await HasAnyInterviewAsync(applicant.Id, cancellationToken);

        var ruleCheck = ApplyStageMove(
            applicant, toStage, reason, notes, rejectionReason, vacancy, hiredCount, hasScorecard,
            hasScheduledInterview, out var historyRow, out var warnings);
        if (ruleCheck.IsFailure)
            return Result<MoveApplicantStageResultDto>.Failure(ruleCheck.Error!, ruleCheck.StatusCode ?? 400, ruleCheck.ErrorCode);

        WriteStageChangeAuditLog(historyRow!);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // ISSUE-109: a concurrent stage move changed the applicant's xmin since we read it.
            return Result<MoveApplicantStageResultDto>.Failure(
                "This applicant was modified by another session. Reload and try again.", 409, "concurrency_conflict");
        }

        await NotifyStageChangedSafeAsync(applicant, historyRow!, cancellationToken);

        return Result<MoveApplicantStageResultDto>.Success(ToMoveResult(historyRow!, warnings));
    }

    public async Task<Result<BulkMoveApplicantStageResultDto>> BulkMoveStageAsync(
        IReadOnlyList<Guid> applicantIds, ApplicantStage toStage, string? reason, string? notes,
        RejectionReason? rejectionReason = null,
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

        // Pre-load the distinct owning vacancies + per-vacancy Hired counts for the FR-8 gate / BR-4
        // warning (tenant-scoped). Hired counts are snapshot before the batch; a single bulk move to
        // Offer/Hired therefore warns based on the pre-move count (the soft gate is advisory).
        var vacancyIds = applicants.Select(a => a.VacancyId).Distinct().ToList();
        var vacancies = await _dbContext.Vacancies
            .Where(v => vacancyIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);
        var hiredCounts = new Dictionary<Guid, int>();
        foreach (var vid in vacancyIds)
            hiredCounts[vid] = await CountHiredAsync(vid, cancellationToken);

        // US-REC-006 BR-6: Offer-gate input per applicant — which applicants have ≥1 scorecard?
        var applicantIdsWithScorecards = await ApplicantIdsWithScorecardsAsync(distinctIds, cancellationToken);
        // ISSUE-108 / BR-1: Interview-gate input per applicant — which applicants have ≥1 interview?
        var applicantIdsWithInterviews = await ApplicantIdsWithInterviewsAsync(distinctIds, cancellationToken);

        // All-or-nothing: validate every move first, collect (history row + warnings), then persist
        // together so the caller never gets a partial move.
        var staged = new List<(ApplicantStageHistory Row, Applicant Applicant, IReadOnlyList<string> Warnings)>(applicants.Count);
        foreach (var applicant in applicants)
        {
            vacancies.TryGetValue(applicant.VacancyId, out var vacancy);
            hiredCounts.TryGetValue(applicant.VacancyId, out var hiredCount);

            var hasScorecard = applicantIdsWithScorecards.Contains(applicant.Id);
            var hasScheduledInterview = applicantIdsWithInterviews.Contains(applicant.Id);

            var ruleCheck = ApplyStageMove(
                applicant, toStage, reason, notes, rejectionReason, vacancy, hiredCount, hasScorecard,
                hasScheduledInterview, out var historyRow, out var warnings);
            if (ruleCheck.IsFailure)
                return Result<BulkMoveApplicantStageResultDto>.Failure(ruleCheck.Error!, ruleCheck.StatusCode ?? 400, ruleCheck.ErrorCode);

            staged.Add((historyRow!, applicant, warnings));
        }

        foreach (var (row, _, _) in staged)
            WriteStageChangeAuditLog(row);

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var (row, applicant, _) in staged)
            await NotifyStageChangedSafeAsync(applicant, row, cancellationToken);

        return Result<BulkMoveApplicantStageResultDto>.Success(new BulkMoveApplicantStageResultDto
        {
            MovedCount = staged.Count,
            Results = staged.Select(s => ToMoveResult(s.Row, s.Warnings)).ToList(),
        });
    }

    /// <summary>Tenant-scoped count of Hired applicants for a vacancy (BR-4 headcount warning).</summary>
    private Task<int> CountHiredAsync(Guid vacancyId, CancellationToken cancellationToken) =>
        _dbContext.Applicants.CountAsync(
            a => a.VacancyId == vacancyId && a.Stage == ApplicantStage.Hired, cancellationToken);

    /// <summary>
    /// US-REC-006 BR-6: true when the applicant has ≥1 interview scorecard (the Offer-stage gate criterion).
    /// Tenant-scoped — joins the applicant's interviews to their scorecards through the EF query filters.
    /// </summary>
    private async Task<bool> HasAnyScorecardAsync(Guid applicantId, CancellationToken cancellationToken)
    {
        var interviewIds = await _dbContext.Interviews
            .Where(i => i.ApplicantId == applicantId)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        if (interviewIds.Count == 0)
            return false;

        return await _dbContext.InterviewScorecards
            .AnyAsync(s => interviewIds.Contains(s.InterviewId), cancellationToken);
    }

    // ISSUE-108 / BR-1: does the applicant have ≥1 interview on record (the Interview soft-gate input)?
    private Task<bool> HasAnyInterviewAsync(Guid applicantId, CancellationToken cancellationToken)
        => _dbContext.Interviews.AnyAsync(i => i.ApplicantId == applicantId, cancellationToken);

    // ISSUE-108 / BR-1: bulk-move counterpart — which of these applicants have ≥1 interview (one query, no N+1)?
    private async Task<HashSet<Guid>> ApplicantIdsWithInterviewsAsync(
        IReadOnlyList<Guid> applicantIds, CancellationToken cancellationToken)
        => (await _dbContext.Interviews
            .Where(i => applicantIds.Contains(i.ApplicantId))
            .Select(i => i.ApplicantId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

    /// <summary>
    /// US-REC-006 BR-6: of the supplied applicants, the subset that have ≥1 interview scorecard (the
    /// Offer-stage gate). Tenant-scoped. Used by the bulk-move path.
    /// </summary>
    private async Task<HashSet<Guid>> ApplicantIdsWithScorecardsAsync(
        IReadOnlyList<Guid> applicantIds, CancellationToken cancellationToken)
    {
        // applicantId -> interviewIds
        var interviewLinks = await _dbContext.Interviews
            .Where(i => applicantIds.Contains(i.ApplicantId))
            .Select(i => new { i.ApplicantId, i.Id })
            .ToListAsync(cancellationToken);

        if (interviewLinks.Count == 0)
            return [];

        var interviewIds = interviewLinks.Select(l => l.Id).ToList();
        var scoredInterviewIds = (await _dbContext.InterviewScorecards
            .Where(s => interviewIds.Contains(s.InterviewId))
            .Select(s => s.InterviewId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        return interviewLinks
            .Where(l => scoredInterviewIds.Contains(l.Id))
            .Select(l => l.ApplicantId)
            .ToHashSet();
    }

    /// <summary>
    /// FR-6/NFR-5: fire the per-transition applicant notification (log-only seam). Never let a
    /// notification failure fail a committed move — mirrors the submit-path notification handling. The
    /// async-queue (Hangfire) delivery is deferred.
    /// </summary>
    private async Task NotifyStageChangedSafeAsync(
        Applicant applicant, ApplicantStageHistory row, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.NotifyStageChangedAsync(
                applicant.Id, applicant.VacancyId, applicant.Email,
                row.FromStage.ToString(), row.ToStage.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Stage-change notification failed (non-fatal). ApplicantId={ApplicantId}, TenantId={TenantId}",
                applicant.Id, _tenantContext.TenantId);
        }
    }

    /// <summary>
    /// Applies the stage-move business rules to a tracked applicant, mutates its stage, and stages an
    /// <see cref="ApplicantStageHistory"/> row in the change tracker (BR-5). Does NOT call SaveChanges —
    /// the caller batches the save (so bulk moves are atomic). Hard rules (failure):
    /// <list type="bullet">
    /// <item>A no-op move (same stage) is rejected (409).</item>
    /// <item>BUG-059 (US-REC-004): Hired is a terminal stage — any move OUT of Hired is rejected (400,
    /// <c>hired_is_terminal</c>).</item>
    /// <item>BUG-060 (US-REC-004, BR-2): a reactivation out of Rejected may only re-enter at an early
    /// pipeline stage (Applied/Screening); a forward leap to Interview/Offer/Hired is rejected (400,
    /// <c>invalid_reactivation_target</c>).</item>
    /// <item>US-REC-004 AC-4/FR-3: moving to Rejected requires a structured <paramref name="rejectionReason"/>
    /// AND the free-text reason (BR-3).</item>
    /// <item>BR-4 / BR-2: a backward move, OR moving OUT of Rejected to an active stage (reactivation),
    /// requires a reason.</item>
    /// <item>FR-8: a forward/active move (anything other than Rejected) is blocked (409) when the owning
    /// vacancy is Closed or Cancelled. Rejection is always allowed.</item>
    /// </list>
    /// Soft, overridable warnings (success, never blocking) are returned for: BR-4 headcount-filled when
    /// moving to Offer/Hired at/over capacity, the BR-1 Interview gate (ISSUE-108: moving to Interview with
    /// no interview on record), and the BR-6 Offer gate (moving to Offer with no scorecard). Moving INTO
    /// Hired is allowed but is terminal thereafter
    /// (convert-to-employee is US-REC-010, out of scope). Backward/forward permission (BR-2) is enforced at
    /// the API layer (the endpoint requires
    /// Recruitment.Manage), so any caller here already holds Manage.
    /// </summary>
    private Result ApplyStageMove(
        Applicant applicant, ApplicantStage toStage, string? reason, string? notes,
        RejectionReason? rejectionReason, Vacancy? vacancy, int hiredCount, bool hasScorecard,
        bool hasScheduledInterview,
        out ApplicantStageHistory? historyRow, out IReadOnlyList<string> warnings)
    {
        historyRow = null;
        warnings = [];

        var fromStage = applicant.Stage;

        if (fromStage == toStage)
            return Result.Failure("The applicant is already in this stage.", 409, "stage_unchanged");

        // BUG-059 (US-REC-004): Hired is a HARD terminal stage. Once an applicant is Hired they cannot
        // change stage in either direction — the pipeline is finished. (Onward handling of a hire is the
        // convert-to-employee flow, US-REC-010, not a pipeline move.) Mirrors the other hard-rule guards.
        if (fromStage == ApplicantStage.Hired)
            return Result.Failure("A hired applicant cannot change stage.", 400, "hired_is_terminal");

        var isBackward = (int)toStage < (int)fromStage;
        var isReactivation = fromStage == ApplicantStage.Rejected && toStage != ApplicantStage.Rejected;
        var isRejection = toStage == ApplicantStage.Rejected;

        // BUG-060 (US-REC-004, BR-2): reactivating a Rejected applicant is a CONTROLLED RE-ENTRY, not a
        // forward jump. A rejected applicant may only re-enter the funnel at its early stages (Applied or
        // Screening) and must then progress normally; leaping straight to Interview/Offer/Hired — skipping
        // the flow — is rejected. (The pre-rejection stage is not tracked on the applicant, only in the
        // history rows, so the defensible re-entry point is the early screening portion of the pipeline.)
        if (isReactivation && toStage >= ApplicantStage.Interview)
            return Result.Failure(
                "A reactivated applicant must re-enter the pipeline at Applied or Screening, not a later stage.",
                400, "invalid_reactivation_target");

        // BR-3: moving to Rejected requires the free-text reason (the audit trail/email carry context).
        if (isRejection && string.IsNullOrWhiteSpace(reason))
            return Result.Failure("A reason is required when rejecting an applicant.", 400, "reason_required");

        // US-REC-004 AC-4/FR-3: moving to Rejected ALSO requires a structured rejection reason.
        if (isRejection && rejectionReason is null)
            return Result.Failure(
                "A rejection reason is required when rejecting an applicant.", 400, "rejection_reason_required");

        // BR-4 / BR-2: a backward move, or reactivation out of Rejected, requires a reason (the
        // Recruitment.Manage permission is enforced at the API layer — every mover here already holds it).
        if ((isBackward || isReactivation) && string.IsNullOrWhiteSpace(reason))
            return Result.Failure("A reason is required to move an applicant to an earlier stage.", 400, "reason_required");

        // FR-8: block a forward/active move when the vacancy is no longer accepting progress. Rejection is
        // always allowed (you can reject regardless of vacancy state).
        if (!isRejection && vacancy is not null &&
            (vacancy.Status == VacancyStatus.Closed || vacancy.Status == VacancyStatus.Cancelled))
            return Result.Failure(
                "The vacancy is closed or cancelled; applicants cannot be advanced.", 409, "vacancy_not_active");

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        // ── Soft, overridable warnings (US-REC-004) — never block the move ──────────────────────────
        var warn = new List<string>();

        // BR-4: warn when moving to Offer/Hired and the vacancy headcount is already filled.
        if ((toStage == ApplicantStage.Offer || toStage == ApplicantStage.Hired) &&
            vacancy is not null && hiredCount >= vacancy.Headcount)
        {
            warn.Add(
                $"The vacancy headcount ({vacancy.Headcount}) is already filled ({hiredCount} hired); " +
                "advancing more applicants to Offer/Hired may exceed the planned headcount.");
        }

        // ISSUE-108 / BR-1 (the Interview gate, FR-1): advancing to Interview expects ≥1 interview on
        // record for the applicant. Like the Offer gate below it is SOFT (advisory, never blocking) —
        // the recruiter may advance a candidate before the interview is booked and override. Completes
        // the BR-1 default-gate set (Screening→notes, Interview→≥1 interview, Offer→≥1 scorecard).
        if (toStage == ApplicantStage.Interview && !hasScheduledInterview)
        {
            warn.Add(
                "No interview has been scheduled for this applicant; " +
                "advancing to the Interview stage without a scheduled interview is not recommended.");
        }

        // US-REC-006 BR-6 (the Offer gate, FR-1): advancing to Offer requires ≥1 scorecard for the
        // applicant. The gate is SOFT (advisory warning, never blocking) to match the rest of the
        // REC-004 gate framework — the recruiter may override (the story sets no minimum-scorecard hard
        // rule). The data authority is US-REC-006; this surfaces it on the move.
        if (toStage == ApplicantStage.Offer && !hasScorecard)
        {
            warn.Add(
                "No interview scorecard has been submitted for this applicant; " +
                "advancing to Offer without a scorecard is not recommended.");
        }

        applicant.Stage = toStage;
        // Track structured rejection state on the applicant (set on reject, clear on reactivation, BR-2).
        applicant.RejectionReason = isRejection ? rejectionReason : null;

        historyRow = new ApplicantStageHistory
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            ApplicantId = applicant.Id,
            FromStage = fromStage,
            ToStage = toStage,
            ChangedByUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            Reason = trimmedReason,
            RejectionReason = isRejection ? rejectionReason : null,
            Notes = trimmedNotes,
            ChangedAt = DateTime.UtcNow,
            IsDeleted = false,
        };

        _dbContext.ApplicantStageHistories.Add(historyRow);

        _logger.LogInformation(
            "Applicant stage moved. ApplicantId={ApplicantId}, From={From}, To={To}, " +
            "RejectionReason={RejectionReason}, Warnings={WarningCount}, ChangedByUserId={UserId}, TenantId={TenantId}",
            applicant.Id, fromStage, toStage, historyRow.RejectionReason, warn.Count,
            historyRow.ChangedByUserId, _tenantContext.TenantId);

        warnings = warn;
        return Result.Success();
    }

    private static readonly JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// ISSUE-104 (US-REC-005): appends a queryable tenant audit row (structured Action/ResourceType/before/
    /// after) to the shared audit_log table. Tenant is stamped from context; the actor is the authenticated
    /// user when present and null on the public/anonymous application-submission path. Mirrors
    /// <c>LeaveTypeService.AddLeaveTypeAudit</c>; the row is added to the change tracker and persisted by the
    /// caller's SaveChanges (same transaction as the write).
    /// </summary>
    private void AddApplicantAudit(string action, Guid resourceId, string? after, string detail, string? before = null)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "Applicant",
            ResourceId = resourceId.ToString(),
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Serializes the audit-relevant fields of an applicant/application to a JSON snapshot.</summary>
    private static string SnapshotApplicant(Applicant a) => JsonSerializer.Serialize(new
    {
        a.ApplicationReferenceNumber,
        a.VacancyId,
        a.FirstName,
        a.LastName,
        a.Email,
        a.Phone,
        Stage = a.Stage.ToString(),
        Source = a.Source.ToString(),
        a.IsInternal,
        a.LinkedEmployeeId,
        a.ResumeFileName,
        a.AppliedAt,
    }, AuditJsonOptions);

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

    private static MoveApplicantStageResultDto ToMoveResult(
        ApplicantStageHistory row, IReadOnlyList<string> warnings) => new()
    {
        ApplicantId = row.ApplicantId,
        FromStage = row.FromStage,
        FromStageName = row.FromStage.ToString(),
        ToStage = row.ToStage,
        ToStageName = row.ToStage.ToString(),
        StageHistoryId = row.Id,
        ChangedAt = row.ChangedAt,
        RejectionReason = row.RejectionReason,
        Warnings = warnings,
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
        ConvertedToEmployeeId = a.ConvertedToEmployeeId,
        IsConverted = a.ConvertedToEmployeeId != null,
        ConvertedAt = a.ConvertedAt,
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
        Stage = a.Stage,
        StageName = a.Stage.ToString(),
        Source = a.Source,
        SourceName = a.Source.ToString(),
        IsInternal = a.IsInternal,
        LinkedEmployeeId = a.LinkedEmployeeId,
        ConvertedToEmployeeId = a.ConvertedToEmployeeId,
        IsConverted = a.ConvertedToEmployeeId != null,
        ConvertedAt = a.ConvertedAt,
        AppliedAt = a.AppliedAt,
        CreatedAt = a.CreatedAt,
        RowVersion = a.RowVersion,
    };
}
