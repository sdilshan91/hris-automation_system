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
/// Candidate-portal application service (US-REC-008). Serves the anonymous, magic-link-authenticated portal:
/// the sanitized dashboard (FR-2/AC-1/AC-2/AC-3/FR-6), offer accept/decline (FR-5/AC-3 — reuses
/// <see cref="IOfferService"/> so accept advances the applicant to Hired, BR-3), and the resume + offer-letter
/// downloads (FR-4). Every operation validates the presented token via <see cref="IApplicantPortalTokenService"/>
/// against the CURRENT (subdomain-resolved) tenant (AC-4/BR-4); the EF global query filter is the second
/// isolation layer. The portal DTOs deliberately EXCLUDE rejection reasons, scorecards, and internal notes
/// (NFR-5/BR-3).
///
/// DEFERRALS (Phase 1): real email delivery of the magic link + status notifications (log-only seam, FR-7);
/// a real distributed rate-limiter on link generation (basic recent-issue guard only, NFR-6); tenant branding
/// payload (logo/primary color, §8) — the FE reads it from the tenant-resolution response, not this service;
/// short-lived signed blob URLs (FR-4) — downloads stream through the API instead (same as US-REC-003/007).
/// </summary>
public sealed class ApplicantPortalService : IApplicantPortalService
{
    private readonly AppDbContext _dbContext;
    private readonly IApplicantPortalTokenService _tokenService;
    private readonly IOfferService _offerService;
    private readonly IFileStorage _fileStorage;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ApplicantPortalService> _logger;

    /// <summary>The funnel order for the step indicator (AC-1). Rejected is terminal and handled separately.</summary>
    private static readonly ApplicantStage[] FunnelStages =
    [
        ApplicantStage.Applied,
        ApplicantStage.Screening,
        ApplicantStage.Interview,
        ApplicantStage.Offer,
        ApplicantStage.Hired,
    ];

    public ApplicantPortalService(
        AppDbContext dbContext,
        IApplicantPortalTokenService tokenService,
        IOfferService offerService,
        IFileStorage fileStorage,
        ITenantContext tenantContext,
        ILogger<ApplicantPortalService> logger)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _offerService = offerService;
        _fileStorage = fileStorage;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ── Dashboard (FR-2/AC-1/AC-2/AC-3/FR-6) ──────────────────────────

    public async Task<Result<PortalDashboardDto>> GetDashboardAsync(
        string token, CancellationToken cancellationToken = default)
    {
        var validation = await _tokenService.ValidateAsync(token, cancellationToken);
        if (validation.IsFailure)
            return Result<PortalDashboardDto>.Failure(validation.Error!, validation.StatusCode ?? 401, validation.ErrorCode);

        var email = validation.Value!.ApplicantEmail;

        // All applications for this email in THIS tenant (global filter + tenant-bound token = AC-4). Email
        // matching is case-insensitive against the lower-cased token email.
        var applicants = await _dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Email.ToLower() == email)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync(cancellationToken);

        if (applicants.Count == 0)
        {
            return Result<PortalDashboardDto>.Success(new PortalDashboardDto
            {
                ApplicantName = string.Empty,
                ApplicantEmail = email,
                Applications = [],
            });
        }

        var applications = new List<PortalApplicationDto>(applicants.Count);
        foreach (var applicant in applicants)
            applications.Add(await BuildApplicationAsync(applicant, cancellationToken));

        var latest = applicants[0];
        return Result<PortalDashboardDto>.Success(new PortalDashboardDto
        {
            ApplicantName = $"{latest.FirstName} {latest.LastName}".Trim(),
            ApplicantEmail = email,
            Applications = applications,
        });
    }

    private async Task<PortalApplicationDto> BuildApplicationAsync(Applicant applicant, CancellationToken cancellationToken)
    {
        var vacancy = await _dbContext.Vacancies
            .AsNoTracking()
            .Where(v => v.Id == applicant.VacancyId)
            .Select(v => new { v.Title, v.DepartmentId })
            .FirstOrDefaultAsync(cancellationToken);

        string? departmentName = null;
        if (vacancy?.DepartmentId is { } deptId)
        {
            departmentName = await _dbContext.Departments
                .AsNoTracking()
                .Where(d => d.Id == deptId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new PortalApplicationDto
        {
            ApplicantId = applicant.Id,
            ApplicationReferenceNumber = applicant.ApplicationReferenceNumber,
            VacancyId = applicant.VacancyId,
            VacancyTitle = vacancy?.Title ?? string.Empty,
            DepartmentName = departmentName,
            AppliedAt = applicant.AppliedAt,
            CurrentStage = applicant.Stage,
            CurrentStageName = applicant.Stage.ToString(),
            Steps = BuildSteps(applicant.Stage),
            UpcomingInterview = await BuildUpcomingInterviewAsync(applicant.Id, cancellationToken),
            ActiveOffer = await BuildActiveOfferAsync(applicant.Id, cancellationToken),
            Timeline = await BuildTimelineAsync(applicant, cancellationToken),
        };
    }

    /// <summary>AC-1: ordered step indicator with completed/current flags. Rejected gets a terminal step.</summary>
    private static IReadOnlyList<PortalStageStepDto> BuildSteps(ApplicantStage current)
    {
        var steps = new List<PortalStageStepDto>(FunnelStages.Length + 1);

        if (current == ApplicantStage.Rejected)
        {
            // Show the funnel as not-current, then a terminal Rejected step (no internal reason — BR-3).
            for (var i = 0; i < FunnelStages.Length; i++)
            {
                steps.Add(new PortalStageStepDto
                {
                    Stage = FunnelStages[i],
                    StageName = FunnelStages[i].ToString(),
                    Order = i,
                    IsCompleted = false,
                    IsCurrent = false,
                });
            }
            steps.Add(new PortalStageStepDto
            {
                Stage = ApplicantStage.Rejected,
                StageName = ApplicantStage.Rejected.ToString(),
                Order = FunnelStages.Length,
                IsCompleted = false,
                IsCurrent = true,
            });
            return steps;
        }

        var currentIndex = Array.IndexOf(FunnelStages, current);
        for (var i = 0; i < FunnelStages.Length; i++)
        {
            steps.Add(new PortalStageStepDto
            {
                Stage = FunnelStages[i],
                StageName = FunnelStages[i].ToString(),
                Order = i,
                IsCompleted = currentIndex >= 0 && i < currentIndex,
                IsCurrent = i == currentIndex,
            });
        }
        return steps;
    }

    private async Task<PortalInterviewDto?> BuildUpcomingInterviewAsync(Guid applicantId, CancellationToken cancellationToken)
    {
        var nowDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var nowTime = TimeOnly.FromDateTime(DateTime.UtcNow);

        // The next Scheduled interview that has not yet started (FR-3/AC-2). Tenant-scoped via global filter.
        var interview = await _dbContext.Interviews
            .AsNoTracking()
            .Include(i => i.Interviewers)
            .Where(i => i.ApplicantId == applicantId
                        && i.Status == InterviewStatus.Scheduled
                        && (i.ScheduledDate > nowDate
                            || (i.ScheduledDate == nowDate && i.StartTime >= nowTime)))
            .OrderBy(i => i.ScheduledDate).ThenBy(i => i.StartTime)
            .FirstOrDefaultAsync(cancellationToken);

        if (interview is null)
            return null;

        var interviewerIds = interview.Interviewers.Select(ii => ii.EmployeeId).ToList();
        var names = interviewerIds.Count == 0
            ? new List<string>()
            : await _dbContext.Employees
                .AsNoTracking()
                .Where(e => interviewerIds.Contains(e.Id))
                .Select(e => (e.FirstName + " " + e.LastName).Trim())
                .ToListAsync(cancellationToken);

        return new PortalInterviewDto
        {
            Id = interview.Id,
            RoundNumber = interview.RoundNumber,
            InterviewType = interview.InterviewType,
            InterviewTypeName = interview.InterviewType.ToString(),
            ScheduledDate = interview.ScheduledDate,
            StartTime = interview.StartTime,
            DurationMinutes = interview.DurationMinutes,
            Location = interview.Location,
            VideoLink = interview.VideoLink,
            InterviewerNames = names,
            // NOTE: interview.Notes is INTERNAL — deliberately NOT exposed (NFR-5/BR-3).
        };
    }

    private async Task<PortalOfferDto?> BuildActiveOfferAsync(Guid applicantId, CancellationToken cancellationToken)
    {
        // The active (Sent) offer the candidate can act on (FR-2/AC-3). Newest version first.
        var offer = await _dbContext.Offers
            .AsNoTracking()
            .Where(o => o.ApplicantId == applicantId && o.Status == OfferStatus.Sent)
            .OrderByDescending(o => o.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (offer is null)
            return null;

        return ToPortalOffer(offer);
    }

    private static PortalOfferDto ToPortalOffer(Offer offer) => new()
    {
        Id = offer.Id,
        OfferReferenceNumber = offer.OfferReferenceNumber,
        Status = offer.Status,
        StatusName = offer.Status.ToString(),
        OfferedPosition = offer.OfferedPosition,
        SalaryAmount = offer.SalaryAmount,
        Currency = offer.Currency,
        SalaryFrequency = offer.SalaryFrequency,
        SalaryFrequencyName = offer.SalaryFrequency.ToString(),
        StartDate = offer.StartDate,
        ExpiryDate = offer.ExpiryDate,
        IsDocumentAvailable = !string.IsNullOrWhiteSpace(offer.PdfStorageKey),
        CanRespond = offer.Status == OfferStatus.Sent,
    };

    private async Task<IReadOnlyList<PortalTimelineEventDto>> BuildTimelineAsync(Applicant applicant, CancellationToken cancellationToken)
    {
        var history = await _dbContext.ApplicantStageHistories
            .AsNoTracking()
            .Where(h => h.ApplicantId == applicant.Id)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync(cancellationToken);

        var events = new List<PortalTimelineEventDto>(history.Count + 1);

        foreach (var h in history)
        {
            // SANITIZED (BR-3/NFR-5): label + stage + timestamp only. NEVER h.Reason / h.RejectionReason /
            // h.Notes.
            events.Add(new PortalTimelineEventDto
            {
                Label = SanitizedLabel(h.ToStage),
                Stage = h.ToStage,
                StageName = h.ToStage.ToString(),
                OccurredAt = h.ChangedAt,
            });
        }

        // FR-6: always anchor the timeline with the "Application received" event (oldest).
        events.Add(new PortalTimelineEventDto
        {
            Label = "Application received",
            Stage = ApplicantStage.Applied,
            StageName = ApplicantStage.Applied.ToString(),
            OccurredAt = applicant.AppliedAt,
        });

        return events;
    }

    /// <summary>FR-6 sanitized stage labels — never leaks the internal reason/notes (BR-3).</summary>
    private static string SanitizedLabel(ApplicantStage toStage) => toStage switch
    {
        ApplicantStage.Applied => "Application received",
        ApplicantStage.Rejected => "Application closed",
        _ => $"Moved to {toStage}",
    };

    // ── Offer respond via portal (FR-5/AC-3/BR-2) ─────────────────────

    public async Task<Result<PortalOfferDto>> RespondToOfferAsync(
        string token, Guid offerId, bool accept, CancellationToken cancellationToken = default)
    {
        var ownership = await ResolveOfferForTokenAsync(token, offerId, cancellationToken);
        if (ownership.IsFailure)
            return Result<PortalOfferDto>.Failure(ownership.Error!, ownership.StatusCode ?? 401, ownership.ErrorCode);

        // Reuse the canonical offer-respond path (BR-3 accept→Hired, BR-2 one-time, lifecycle checks). The
        // offer write is tenant-scoped through the same EF filter.
        var responded = await _offerService.RespondAsync(
            offerId, new RespondToOfferInput { Accepted = accept }, cancellationToken);
        if (responded.IsFailure)
            return Result<PortalOfferDto>.Failure(responded.Error!, responded.StatusCode ?? 400, responded.ErrorCode);

        var offer = await _dbContext.Offers.AsNoTracking().FirstAsync(o => o.Id == offerId, cancellationToken);
        _logger.LogInformation(
            "Portal offer response recorded. OfferId={OfferId}, Accept={Accept}, TenantId={TenantId}",
            offerId, accept, _tenantContext.TenantId);

        return Result<PortalOfferDto>.Success(ToPortalOffer(offer));
    }

    // ── Downloads (FR-4) ──────────────────────────────────────────────

    public async Task<Result<ResumeDownloadDto>> DownloadResumeAsync(
        string token, Guid applicantId, CancellationToken cancellationToken = default)
    {
        var validation = await _tokenService.ValidateAsync(token, cancellationToken);
        if (validation.IsFailure)
            return Result<ResumeDownloadDto>.Failure(validation.Error!, validation.StatusCode ?? 401, validation.ErrorCode);

        var applicant = await _dbContext.Applicants
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicantId, cancellationToken);
        if (applicant is null)
            return Result<ResumeDownloadDto>.Failure("Application not found.", 404, "applicant_not_found");

        // Token-scoping: the applicant must belong to the token's email (BR-4 / token-scoped to that applicant).
        if (!string.Equals(applicant.Email, validation.Value!.ApplicantEmail, StringComparison.OrdinalIgnoreCase))
            return Result<ResumeDownloadDto>.Failure("You do not have access to this document.", 403, "forbidden");

        if (string.IsNullOrWhiteSpace(applicant.ResumeStorageKey))
            return Result<ResumeDownloadDto>.Failure("No resume is on file for this application.", 404, "resume_not_found");

        await using var stream = await _fileStorage.OpenReadAsync(
            _tenantContext.TenantId, applicant.ResumeStorageKey, cancellationToken);
        if (stream is null)
            return Result<ResumeDownloadDto>.Failure("The resume could not be found in storage.", 404, "resume_not_found");

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return Result<ResumeDownloadDto>.Success(new ResumeDownloadDto
        {
            Content = buffer.ToArray(),
            FileName = string.IsNullOrWhiteSpace(applicant.ResumeFileName) ? "resume.pdf" : applicant.ResumeFileName,
            ContentType = "application/octet-stream",
        });
    }

    public async Task<Result<OfferDocumentDto>> DownloadOfferDocumentAsync(
        string token, Guid offerId, CancellationToken cancellationToken = default)
    {
        var ownership = await ResolveOfferForTokenAsync(token, offerId, cancellationToken);
        if (ownership.IsFailure)
            return Result<OfferDocumentDto>.Failure(ownership.Error!, ownership.StatusCode ?? 401, ownership.ErrorCode);

        return await _offerService.GetDocumentAsync(offerId, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates the token AND that the offer belongs to the token-holder's applicant (BR-4 token-scoping).
    /// Returns the offer's applicant email on success; 401 invalid token, 404 offer gone, 403 not the holder's.
    /// </summary>
    private async Task<Result<string>> ResolveOfferForTokenAsync(
        string token, Guid offerId, CancellationToken cancellationToken)
    {
        var validation = await _tokenService.ValidateAsync(token, cancellationToken);
        if (validation.IsFailure)
            return Result<string>.Failure(validation.Error!, validation.StatusCode ?? 401, validation.ErrorCode);

        var offer = await _dbContext.Offers
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);
        if (offer is null)
            return Result<string>.Failure("Offer not found.", 404, "offer_not_found");

        var applicantEmail = await _dbContext.Applicants
            .AsNoTracking()
            .Where(a => a.Id == offer.ApplicantId)
            .Select(a => a.Email)
            .FirstOrDefaultAsync(cancellationToken);

        if (applicantEmail is null
            || !string.Equals(applicantEmail, validation.Value!.ApplicantEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.Failure("You do not have access to this offer.", 403, "forbidden");
        }

        return Result<string>.Success(applicantEmail);
    }
}
