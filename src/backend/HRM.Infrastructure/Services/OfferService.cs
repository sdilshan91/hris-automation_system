using System.Globalization;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Offer-letter generation / send / response / withdrawal + reads (US-REC-007). All queries are
/// tenant-scoped via ITenantContext + the EF global query filter (AC-5). Generation enforces one active
/// offer per applicant/vacancy (BR-2) by superseding any prior active offer (it is withdrawn) and bumping
/// the version (FR-9); it renders the default offer-letter template with variable substitution
/// (<see cref="OfferLetterTemplate"/>), produces a 2-page PDF via <see cref="OfferPdfRenderer"/>, and
/// stores it under the tenant-scoped path {tenantId}/recruitment/{vacancyId}/{applicantId}/offers/{uuid}.pdf
/// via IFileStorage (FR-2/FR-3) → status Draft. Sending logs the applicant email (log-only seam, FR-5) and
/// schedules the Hangfire expiry job (FR-7) when the seam is available. Acceptance advances the applicant
/// to Hired (BR-3/AC-3); decline leaves them in Offer. A withdrawn offer cannot be re-sent (BR-7).
///
/// DEFERRALS (Phase 1): configurable per-tenant templates (S35.2.9) — one built-in default template is
/// used; the approval workflow (S34/FR-10/BR-5); the candidate self-service portal (AC-3) — the recruiter
/// records the response; PDF encryption-at-rest (NFR-3).
/// </summary>
public sealed class OfferService : IOfferService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorage _fileStorage;
    private readonly IRecruitmentNotificationService _notifications;
    private readonly IOfferExpiryScheduler? _expiryScheduler;
    private readonly IOfferExpiryReminderScheduler? _expiryReminderScheduler;
    private readonly ICurrentUser? _currentUser;
    private readonly IWorkflowRuntime? _workflowRuntime;
    private readonly ILogger<OfferService> _logger;

    /// <summary>BR-6: default offer-validity window (days) when no expiry date is supplied.</summary>
    private const int DefaultExpiryDays = 7;

    /// <summary>
    /// ISSUE-262 / FR-7: how many days BEFORE the expiry date the reminder nudge fires. A documented
    /// constant default; making it tenant-configurable (a recruitment-settings surface) is a SEPARATE
    /// concern and is intentionally NOT built here.
    /// </summary>
    private const int ExpiryReminderDaysBefore = 3;

    public OfferService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IFileStorage fileStorage,
        IRecruitmentNotificationService notifications,
        ILogger<OfferService> logger,
        IOfferExpiryScheduler? expiryScheduler = null,
        IOfferExpiryReminderScheduler? expiryReminderScheduler = null,
        ICurrentUser? currentUser = null,
        IWorkflowRuntime? workflowRuntime = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
        _notifications = notifications;
        _logger = logger;
        _expiryScheduler = expiryScheduler;
        _expiryReminderScheduler = expiryReminderScheduler;
        // US-ADM-011c FR-11 / US-REC-007 FR-10/BR-5: optional so existing US-REC-007 tests keep compiling; DI
        // always supplies them. When the tenant has an Active Offer workflow, GenerateAsync instantiates it and
        // SendAsync is gated on the instance being Approved (AC-11 legacy send when there is no definition).
        _currentUser = currentUser;
        _workflowRuntime = workflowRuntime;
    }

    // ── Generate (AC-1/FR-2/FR-3/FR-9/BR-2/BR-6) ─────────────────────

    public async Task<Result<OfferDto>> GenerateAsync(
        GenerateOfferInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OfferDto>.Failure("Tenant context is not resolved.", 400);

        // Defensive re-validation (the FluentValidation validator is the primary gate).
        if (input.SalaryAmount <= 0)
            return Result<OfferDto>.Failure("The salary amount must be greater than zero.", 400, "salary_invalid");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expiryDate = input.ExpiryDate ?? today.AddDays(DefaultExpiryDays); // BR-6 default +7d.
        if (expiryDate <= today)
            return Result<OfferDto>.Failure("The offer expiry date must be in the future.", 400, "expiry_in_past");

        // Applicant must exist in this tenant (global filter ⇒ found ⇒ in-tenant). 404 otherwise so a
        // guessed cross-tenant id never generates against another tenant's applicant (AC-5).
        var applicant = await _dbContext.Applicants
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == input.ApplicantId, cancellationToken);
        if (applicant is null)
            return Result<OfferDto>.Failure("Applicant not found.", 404, "applicant_not_found");

        // BR-2/FR-9: supersede any prior active offer (Draft/Sent) for this applicant/vacancy. The new
        // offer's version = max existing version + 1.
        var existingOffers = await _dbContext.Offers
            .Where(o => o.ApplicantId == applicant.Id && o.VacancyId == applicant.VacancyId)
            .ToListAsync(cancellationToken);

        var maxVersion = existingOffers.Count == 0 ? 0 : existingOffers.Max(o => o.Version);

        foreach (var prior in existingOffers.Where(o => o.IsActive))
        {
            prior.Status = OfferStatus.Withdrawn;
            _expiryScheduler?.Cancel(prior.ReminderJobId);
            prior.ReminderJobId = null;
            _expiryReminderScheduler?.Cancel(prior.ExpiryReminderJobId);
            prior.ExpiryReminderJobId = null;
            _logger.LogInformation(
                "Superseded prior active offer {OfferId} (v{Version}) for applicant {ApplicantId} (BR-2/FR-9). TenantId={TenantId}",
                prior.Id, prior.Version, applicant.Id, _tenantContext.TenantId);
        }

        var offerId = BaseEntity.NewUuidV7();
        var reference = await GenerateReferenceNumberAsync(cancellationToken);

        var tenantName = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Our Company";

        var departmentName = input.DepartmentId is { } deptId
            ? await _dbContext.Departments.AsNoTracking()
                .Where(d => d.Id == deptId).Select(d => d.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        var offer = new Offer
        {
            Id = offerId,
            TenantId = _tenantContext.TenantId,
            ApplicantId = applicant.Id,
            VacancyId = applicant.VacancyId,
            OfferReferenceNumber = reference,
            Status = OfferStatus.Draft,
            OfferedPosition = input.OfferedPosition.Trim(),
            DepartmentId = input.DepartmentId,
            ReportingManagerEmployeeId = input.ReportingManagerEmployeeId,
            SalaryAmount = input.SalaryAmount,
            Currency = input.Currency.Trim().ToUpperInvariant(),
            SalaryFrequency = input.SalaryFrequency,
            BenefitsSummary = Trim(input.BenefitsSummary),
            StartDate = input.StartDate,
            ExpiryDate = expiryDate,
            ProbationMonths = input.ProbationMonths,
            CustomClauses = Trim(input.CustomClauses),
            Version = maxVersion + 1,
            IsDeleted = false,
        };

        // FR-2: render the default template with variable substitution → a 2-page PDF document.
        var applicantName = $"{applicant.FirstName} {applicant.LastName}".Trim();
        var variables = OfferLetterTemplate.BuildVariables(offer, applicantName, tenantName, departmentName);
        var pdfBytes = OfferPdfRenderer.Render(offer, variables);

        // FR-3: store under the tenant-isolated path (IFileStorage prefixes {tenantId}).
        var relativePath = $"recruitment/{offer.VacancyId}/{offer.ApplicantId}/offers/{offerId:N}.pdf";
        using (var pdfStream = new MemoryStream(pdfBytes))
        {
            await _fileStorage.UploadAsync(
                _tenantContext.TenantId, relativePath, pdfStream, "application/pdf", cancellationToken);
        }

        offer.PdfStorageKey = relativePath;

        _dbContext.Offers.Add(offer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // US-ADM-011c FR-11 / US-REC-007 FR-10/BR-5: if the tenant has an Active Offer approval workflow,
        // instantiate it now so the offer must be approved before it can be sent (gated in SendAsync). No active
        // definition → WorkflowInstanceId stays null and the offer can be sent freely (AC-11).
        if (_workflowRuntime is not null)
        {
            var requesterEmployeeId = await ResolveRequesterEmployeeIdAsync(cancellationToken);
            var requestData = new Dictionary<string, object?>
            {
                ["salary"] = offer.SalaryAmount,
                ["position"] = offer.OfferedPosition,
            };
            var wf = await _workflowRuntime.CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Offer, offer.Id, requesterEmployeeId, requestData, cancellationToken);
            if (wf.InstanceCreated)
            {
                offer.WorkflowInstanceId = wf.InstanceId;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        _logger.LogInformation(
            "Offer generated (Draft). OfferId={OfferId}, Ref={Ref}, Version={Version}, ApplicantId={ApplicantId}, " +
            "VacancyId={VacancyId}, StorageKey={StorageKey}, TenantId={TenantId}",
            offer.Id, offer.OfferReferenceNumber, offer.Version, offer.ApplicantId, offer.VacancyId,
            relativePath, _tenantContext.TenantId);

        return await BuildDtoResultAsync(offer.Id, cancellationToken);
    }

    // ── Send (AC-2/FR-5/FR-7/BR-7) ───────────────────────────────────

    public async Task<Result<OfferDto>> SendAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OfferDto>.Failure("Tenant context is not resolved.", 400);

        var offer = await _dbContext.Offers.FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);
        if (offer is null)
            return Result<OfferDto>.Failure("Offer not found.", 404, "offer_not_found");

        // Only a Draft offer can be sent. BR-7: a withdrawn offer cannot be re-sent.
        if (offer.Status == OfferStatus.Withdrawn)
            return Result<OfferDto>.Failure("A withdrawn offer cannot be sent; generate a new offer.", 409, "offer_withdrawn");
        if (offer.Status != OfferStatus.Draft)
            return Result<OfferDto>.Failure($"Only a draft offer can be sent (current status: {offer.Status}).", 409, "offer_not_draft");

        // US-ADM-011c FR-11 / US-REC-007 FR-10/BR-5: when the offer is workflow-driven, it can only be sent once
        // its approval-workflow instance is Approved. A still-in-flight (or rejected) instance blocks the send. A
        // legacy offer (no WorkflowInstanceId) sends freely (AC-11).
        if (offer.WorkflowInstanceId is { } instanceId)
        {
            var instanceStatus = await _dbContext.WorkflowInstances
                .AsNoTracking()
                .Where(i => i.Id == instanceId)
                .Select(i => (WorkflowInstanceStatus?)i.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (instanceStatus != WorkflowInstanceStatus.Approved)
                return Result<OfferDto>.Failure(
                    "This offer is pending approval and cannot be sent yet.", 409, "offer_approval_pending");
        }

        offer.Status = OfferStatus.Sent;
        offer.SentAt = DateTime.UtcNow;

        // FR-7/AC-4: schedule the tenant-aware expiry follow-up job (no-op if the seam is absent — tests —
        // or if the fire-time is already past).
        offer.ReminderJobId = ScheduleExpiry(offer);

        // ISSUE-262 / FR-7/AC-4: also schedule the "N days before expiry" reminder nudge (no-op if the seam
        // is absent). Stored on a SEPARATE field so cancelling the reminder never clobbers the expiry job.
        offer.ExpiryReminderJobId = ScheduleExpiryReminder(offer);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Offer sent. OfferId={OfferId}, ExpiryDate={ExpiryDate}, ReminderJobId={ReminderJobId}, " +
            "ExpiryReminderJobId={ExpiryReminderJobId}, TenantId={TenantId}",
            offer.Id, offer.ExpiryDate, offer.ReminderJobId, offer.ExpiryReminderJobId, _tenantContext.TenantId);

        await NotifyOfferSafeAsync("offer-sent", offer, cancellationToken);

        return await BuildDtoResultAsync(offer.Id, cancellationToken);
    }

    // ── Respond (AC-3/BR-3) ──────────────────────────────────────────

    public async Task<Result<OfferDto>> RespondAsync(
        Guid offerId, RespondToOfferInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OfferDto>.Failure("Tenant context is not resolved.", 400);

        var offer = await _dbContext.Offers.FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);
        if (offer is null)
            return Result<OfferDto>.Failure("Offer not found.", 404, "offer_not_found");

        // A response can only be recorded against a Sent offer (AC-3).
        if (offer.Status != OfferStatus.Sent)
            return Result<OfferDto>.Failure($"Only a sent offer can be responded to (current status: {offer.Status}).", 409, "offer_not_sent");

        offer.Status = input.Accepted ? OfferStatus.Accepted : OfferStatus.Declined;
        offer.Response = offer.Status.ToString();
        offer.RespondedAt = DateTime.UtcNow;

        // The offer is answered → cancel the expiry follow-up + the expiry-reminder nudge.
        _expiryScheduler?.Cancel(offer.ReminderJobId);
        offer.ReminderJobId = null;
        _expiryReminderScheduler?.Cancel(offer.ExpiryReminderJobId);
        offer.ExpiryReminderJobId = null;

        if (input.Accepted)
        {
            // BR-3/AC-3: acceptance advances the applicant to the Hired stage. Decline leaves the
            // applicant in the Offer stage (no change). Tenant-scoped fetch (global filter).
            var applicant = await _dbContext.Applicants.FirstOrDefaultAsync(a => a.Id == offer.ApplicantId, cancellationToken);
            if (applicant is not null && applicant.Stage != ApplicantStage.Hired)
            {
                var from = applicant.Stage;
                applicant.Stage = ApplicantStage.Hired;
                applicant.RejectionReason = null;

                _dbContext.ApplicantStageHistories.Add(new ApplicantStageHistory
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    ApplicantId = applicant.Id,
                    FromStage = from,
                    ToStage = ApplicantStage.Hired,
                    Reason = $"Offer {offer.OfferReferenceNumber} accepted.",
                    ChangedAt = DateTime.UtcNow,
                    IsDeleted = false,
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Offer response recorded. OfferId={OfferId}, Response={Response}, TenantId={TenantId}",
            offer.Id, offer.Response, _tenantContext.TenantId);

        return await BuildDtoResultAsync(offer.Id, cancellationToken);
    }

    // ── Withdraw (FR-8/BR-7) ─────────────────────────────────────────

    public async Task<Result<OfferDto>> WithdrawAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OfferDto>.Failure("Tenant context is not resolved.", 400);

        var offer = await _dbContext.Offers.FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);
        if (offer is null)
            return Result<OfferDto>.Failure("Offer not found.", 404, "offer_not_found");

        // FR-8: withdrawal is allowed before acceptance (Draft/Sent). A terminal offer can't be withdrawn.
        if (!offer.IsActive)
            return Result<OfferDto>.Failure($"Only a draft or sent offer can be withdrawn (current status: {offer.Status}).", 409, "offer_not_active");

        offer.Status = OfferStatus.Withdrawn;
        _expiryScheduler?.Cancel(offer.ReminderJobId);
        offer.ReminderJobId = null;
        _expiryReminderScheduler?.Cancel(offer.ExpiryReminderJobId);
        offer.ExpiryReminderJobId = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Offer withdrawn. OfferId={OfferId}, TenantId={TenantId}", offer.Id, _tenantContext.TenantId);

        await NotifyOfferSafeAsync("offer-withdrawn", offer, cancellationToken);

        return await BuildDtoResultAsync(offer.Id, cancellationToken);
    }

    // ── Decision (US-ADM-011c FR-11 / US-REC-007 FR-10/BR-5) ─────────

    public async Task<Result<OfferApprovalDecisionDto>> DecideApprovalAsync(
        Guid offerId, WorkflowDecisionAction action, string? comment, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OfferApprovalDecisionDto>.Failure("Tenant context is not resolved.", 400);
        if (_workflowRuntime is null)
            return Result<OfferApprovalDecisionDto>.Failure(
                "Offer approval workflow is not available.", 400, "workflow_unavailable");

        var offer = await _dbContext.Offers.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);
        if (offer is null)
            return Result<OfferApprovalDecisionDto>.Failure("Offer not found.", 404, "offer_not_found");
        if (offer.WorkflowInstanceId is null)
            return Result<OfferApprovalDecisionDto>.Failure(
                "This offer is not governed by an approval workflow.", 400, "offer_not_workflow_driven");

        // Offer approval has NO domain side-effect at approval time — Send (5.3) reads the instance status — so
        // no staged onApproved/onRejected callbacks are needed. The runtime advances/completes the instance and
        // audits the transition.
        var decision = await _workflowRuntime.DecideAsync(
            WorkflowEntityType.Offer, offerId, action, comment,
            onApproved: null, onRejected: null, cancellationToken: cancellationToken);

        if (decision.IsFailure)
            return Result<OfferApprovalDecisionDto>.Failure(
                decision.Error!, decision.StatusCode ?? 400, decision.ErrorCode);

        var outcome = decision.Value!;
        var status = outcome.Outcome switch
        {
            WorkflowDecisionOutcome.InstanceApproved => "Approved",
            WorkflowDecisionOutcome.InstanceRejected => "Rejected",
            _ => "Pending", // StepAdvanced / StepRecorded — still in flight
        };

        return Result<OfferApprovalDecisionDto>.Success(
            new OfferApprovalDecisionDto(offerId, status, outcome.NextStepOrder));
    }

    // ── Reads ─────────────────────────────────────────────────────────

    public async Task<Result<OfferDto>> GetByIdAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OfferDto>.Failure("Tenant context is not resolved.", 400);

        var offer = await _dbContext.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);
        if (offer is null)
            return Result<OfferDto>.Failure("Offer not found.", 404, "offer_not_found");

        return Result<OfferDto>.Success(await ToDtoAsync(offer, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<OfferDto>>> ListByApplicantAsync(
        Guid applicantId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<OfferDto>>.Failure("Tenant context is not resolved.", 400);

        var offers = await _dbContext.Offers
            .AsNoTracking()
            .Where(o => o.ApplicantId == applicantId)
            .OrderByDescending(o => o.Version)
            .ToListAsync(cancellationToken);

        var dtos = new List<OfferDto>(offers.Count);
        foreach (var offer in offers)
            dtos.Add(await ToDtoAsync(offer, cancellationToken));

        return Result<IReadOnlyList<OfferDto>>.Success(dtos);
    }

    public async Task<Result<OfferDocumentDto>> GetDocumentAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OfferDocumentDto>.Failure("Tenant context is not resolved.", 400);

        var offer = await _dbContext.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);
        if (offer is null)
            return Result<OfferDocumentDto>.Failure("Offer not found.", 404, "offer_not_found");

        if (string.IsNullOrWhiteSpace(offer.PdfStorageKey))
            return Result<OfferDocumentDto>.Failure("This offer has no generated document.", 404, "offer_document_not_found");

        await using var stream = await _fileStorage.OpenReadAsync(
            _tenantContext.TenantId, offer.PdfStorageKey, cancellationToken);
        if (stream is null)
            return Result<OfferDocumentDto>.Failure("The offer document could not be found in storage.", 404, "offer_document_not_found");

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return Result<OfferDocumentDto>.Success(new OfferDocumentDto
        {
            Content = buffer.ToArray(),
            FileName = $"offer-{offer.OfferReferenceNumber}.pdf",
            ContentType = "application/pdf",
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// FR-7/AC-4: schedule the tenant-aware expiry job via the seam. Fires at the START of the day AFTER
    /// the expiry date (UTC) — the offer is valid through the expiry date. Returns the job id, or null when
    /// the scheduler is unavailable (tests) or the fire-time is already past.
    /// </summary>
    private string? ScheduleExpiry(Offer offer)
    {
        if (_expiryScheduler is null)
            return null;

        // Valid through the end of ExpiryDate; the job fires at the next day's UTC start.
        var fireAtUtc = DateTime.SpecifyKind(
            offer.ExpiryDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        return _expiryScheduler.Schedule(_tenantContext.TenantId, offer.Id, fireAtUtc);
    }

    /// <summary>
    /// ISSUE-262 / FR-7/AC-4: schedule the tenant-aware expiry-reminder job via the seam. Fires at the START
    /// of the day <see cref="ExpiryReminderDaysBefore"/> days BEFORE the expiry date (UTC). Returns the job
    /// id, or null when the scheduler is unavailable (tests). If the computed fire time is already in the
    /// past (offer sent close to expiry), the reminder is scheduled anyway — Hangfire runs a past-dated job
    /// immediately and the job's IsActive guard keeps it safe.
    /// </summary>
    private string? ScheduleExpiryReminder(Offer offer)
    {
        if (_expiryReminderScheduler is null)
            return null;

        var fireAtUtc = DateTime.SpecifyKind(
            offer.ExpiryDate.AddDays(-ExpiryReminderDaysBefore).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        return _expiryReminderScheduler.Schedule(_tenantContext.TenantId, offer.Id, fireAtUtc);
    }

    /// <summary>
    /// FR-5/FR-8: notify the applicant via the log-only seam. Never let a notification failure fail a
    /// committed offer write (mirrors the other recruitment notification handling).
    /// </summary>
    private async Task NotifyOfferSafeAsync(string eventType, Offer offer, CancellationToken cancellationToken)
    {
        try
        {
            var applicantEmail = await _dbContext.Applicants
                .AsNoTracking()
                .Where(a => a.Id == offer.ApplicantId)
                .Select(a => a.Email)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

            await _notifications.NotifyOfferAsync(
                eventType, offer.Id, offer.ApplicantId, offer.VacancyId, applicantEmail, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Offer notification failed (non-fatal). EventType={EventType}, OfferId={OfferId}, TenantId={TenantId}",
                eventType, offer.Id, _tenantContext.TenantId);
        }
    }

    private async Task<Result<OfferDto>> BuildDtoResultAsync(Guid offerId, CancellationToken cancellationToken)
    {
        var offer = await _dbContext.Offers.AsNoTracking().FirstAsync(o => o.Id == offerId, cancellationToken);
        return Result<OfferDto>.Success(await ToDtoAsync(offer, cancellationToken));
    }

    private async Task<OfferDto> ToDtoAsync(Offer offer, CancellationToken cancellationToken)
    {
        var vacancyTitle = await _dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.Id == offer.VacancyId)
            .Select(v => v.Title)
            .FirstOrDefaultAsync(cancellationToken);

        var applicantName = await _dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Id == offer.ApplicantId)
            .Select(a => $"{a.FirstName} {a.LastName}")
            .FirstOrDefaultAsync(cancellationToken);

        return new OfferDto
        {
            Id = offer.Id,
            ApplicantId = offer.ApplicantId,
            VacancyId = offer.VacancyId,
            VacancyTitle = vacancyTitle,
            ApplicantName = applicantName?.Trim(),
            OfferReferenceNumber = offer.OfferReferenceNumber,
            Status = offer.Status,
            StatusName = offer.Status.ToString(),
            OfferedPosition = offer.OfferedPosition,
            DepartmentId = offer.DepartmentId,
            ReportingManagerEmployeeId = offer.ReportingManagerEmployeeId,
            SalaryAmount = offer.SalaryAmount,
            Currency = offer.Currency,
            SalaryFrequency = offer.SalaryFrequency,
            SalaryFrequencyName = offer.SalaryFrequency.ToString(),
            BenefitsSummary = offer.BenefitsSummary,
            StartDate = offer.StartDate,
            ExpiryDate = offer.ExpiryDate,
            ProbationMonths = offer.ProbationMonths,
            CustomClauses = offer.CustomClauses,
            Version = offer.Version,
            SentAt = offer.SentAt,
            RespondedAt = offer.RespondedAt,
            Response = offer.Response,
            CreatedAt = offer.CreatedAt,
        };
    }

    private async Task<string> GenerateReferenceNumberAsync(CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"OFR-{year}-";

        // Highest existing suffix for this tenant+year. IgnoreQueryFilters scopes explicitly to the tenant
        // (and includes soft-deleted rows so a deleted ref number is never reused — collision-safe with
        // the partial unique index). Mirrors ApplicantService.GenerateReferenceNumberAsync.
        var existing = await _dbContext.Offers
            .IgnoreQueryFilters()
            .Where(o => o.TenantId == _tenantContext.TenantId && o.OfferReferenceNumber.StartsWith(prefix))
            .Select(o => o.OfferReferenceNumber)
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
    /// US-ADM-011c: resolves the current user's employee id (for LineManager/DepartmentHead approver resolution).
    /// Null when no user context / no linked employee record — the workflow then simply cannot resolve those
    /// relative approver types for the offer (Offer approvers are typically NamedUser / Role).
    /// </summary>
    private async Task<Guid?> ResolveRequesterEmployeeIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUser is null || !_currentUser.IsAuthenticated)
            return null;
        return await _dbContext.Employees.AsNoTracking()
            .Where(e => e.UserId == _currentUser.UserId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
