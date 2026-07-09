using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 5a: the real <see cref="IRecruitmentNotificationService"/> (US-REC-002/004/005/006/007).
/// Replaces the log-only stub — resolves recipients and dispatches recruitment notifications (email + in-app) via
/// <see cref="INotificationDispatcher"/>. Recipient rules:
/// <list type="bullet">
/// <item><b>External candidates</b> (application/interview/offer confirmations) → <c>RecipientEmail</c> (email-only;
/// they have no <c>User</c> row).</item>
/// <item><b>Interviewers</b> arrive as employee ids → resolved to <c>Employee.UserId</c> (in-app + email) when
/// the interviewer has a linked user account, else <c>Employee.Email</c> (email-only fallback) — ISSUE-263.</item>
/// <item><b>Hiring manager</b> → the vacancy's <c>HiringManagerId</c> → <c>Employee.UserId</c> (in-app + email),
/// falling back to <c>Employee.Email</c> (email-only) when the employee has no linked user.</item>
/// <item><b>Recruiter / hiring-team pool</b> → every ACTIVE tenant user whose roles grant
/// <c>Recruitment.Manage</c> (in-app + email).</item>
/// </list>
///
/// <para>The tenant id comes from <see cref="ITenantContext"/>: every caller runs with the tenant resolved — the
/// request-scoped services (Applicant/Interview/Scorecard/Offer) via the middleware, and the Hangfire jobs
/// (InterviewReminderJob/OfferExpiryJob) restore it explicitly before invoking this service. <b>Never throws</b> — a
/// delivery failure must not break the committed recruitment write; every method is wrapped and per-recipient legs
/// are individually guarded, mirroring the LogOnly contract. Resolution queries are tenant-scoped with
/// <c>IgnoreQueryFilters</c> so they are correct even outside a request scope.</para>
///
/// <para><b>Offer-letter PDF (offer_sent):</b> the candidate leg sends the stored offer PDF INLINE via the generic
/// <see cref="IEmailSender"/> + <see cref="EmailAttachment"/> (NOT the dispatcher — the multi-MB blob must not be
/// persisted as a Hangfire argument), mirroring the Phase-4 payslip pattern. If the PDF cannot be read it falls back
/// to a dispatcher email with no attachment (never throws).</para>
/// </summary>
public sealed class RealRecruitmentNotificationService : IRecruitmentNotificationService
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEmailSender _emailSender;
    private readonly IFileStorage _fileStorage;
    private readonly IEmailTemplateService _templateService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RealRecruitmentNotificationService> _logger;

    public RealRecruitmentNotificationService(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        IEmailSender emailSender,
        IFileStorage fileStorage,
        IEmailTemplateService templateService,
        ITenantContext tenantContext,
        ILogger<RealRecruitmentNotificationService> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _emailSender = emailSender;
        _fileStorage = fileStorage;
        _templateService = templateService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task NotifyApplicationReceivedAsync(
        Guid applicantId, Guid vacancyId, string applicantEmail, string applicationReferenceNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var applicant = await LoadApplicantAsync(tenantId, applicantId, cancellationToken);
            var vacancyTitle = await LoadVacancyTitleAsync(tenantId, vacancyId, cancellationToken);

            var payload = BuildApplicantPayload(
                title: $"Application received for {vacancyTitle}",
                message: $"We've received the application for {vacancyTitle}.",
                applicant, applicantEmail, vacancyTitle, applicationReferenceNumber);

            await DispatchEmailOnlyAsync(tenantId, "application_received", payload,
                FirstNonEmpty(applicantEmail, applicant?.Email), cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "application_received", applicantId);
        }
    }

    public async Task NotifyNewApplicationAsync(
        Guid applicantId, Guid vacancyId, Guid? hiringManagerEmployeeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var applicant = await LoadApplicantAsync(tenantId, applicantId, cancellationToken);
            var vacancyTitle = await LoadVacancyTitleAsync(tenantId, vacancyId, cancellationToken);
            var reference = applicant?.ApplicationReferenceNumber ?? string.Empty;

            var payload = BuildApplicantPayload(
                title: $"New application for {vacancyTitle}",
                message: $"A new application was received for {vacancyTitle}.",
                applicant, applicant?.Email, vacancyTitle, reference);

            // Recruiter/hiring-team pool → in-app + email.
            var userIds = new HashSet<Guid>(await ResolveRecruiterUserIdsAsync(tenantId, cancellationToken));

            // Hiring manager → UserId (in-app + email) when linked, else email-only fallback.
            if (hiringManagerEmployeeId is { } hmId)
            {
                var hm = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
                    .Where(e => e.Id == hmId && e.TenantId == tenantId)
                    .Select(e => new { e.UserId, e.Email })
                    .FirstOrDefaultAsync(cancellationToken);

                if (hm?.UserId is { } hmUserId)
                    userIds.Add(hmUserId);
                else if (!string.IsNullOrWhiteSpace(hm?.Email))
                    await DispatchEmailOnlyAsync(tenantId, "application_new", payload, hm.Email, cancellationToken);
            }

            await DispatchToUsersAsync(tenantId, "application_new", payload, userIds, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "application_new", applicantId);
        }
    }

    public async Task NotifyStageChangedAsync(
        Guid applicantId, Guid vacancyId, string applicantEmail, string fromStage, string toStage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var applicant = await LoadApplicantAsync(tenantId, applicantId, cancellationToken);
            var vacancyTitle = await LoadVacancyTitleAsync(tenantId, vacancyId, cancellationToken);

            var payload = BuildApplicantPayload(
                title: $"Application update for {vacancyTitle}",
                message: $"The application for {vacancyTitle} moved from {fromStage} to {toStage}.",
                applicant, applicantEmail, vacancyTitle, applicant?.ApplicationReferenceNumber ?? string.Empty,
                fromStage: fromStage, toStage: toStage);

            await DispatchEmailOnlyAsync(tenantId, "application_stage_changed", payload,
                FirstNonEmpty(applicantEmail, applicant?.Email), cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "application_stage_changed", applicantId);
        }
    }

    public async Task NotifyInterviewAsync(
        string eventType, Guid interviewId, Guid applicantId, Guid vacancyId, string applicantEmail,
        IReadOnlyList<Guid> interviewerEmployeeIds, CancellationToken cancellationToken = default)
    {
        var eventKey = eventType switch
        {
            "interview-scheduled" => "interview_scheduled",
            "interview-updated" => "interview_updated",
            "interview-cancelled" => "interview_cancelled",
            _ => string.Empty,
        };
        if (string.IsNullOrEmpty(eventKey))
        {
            _logger.LogWarning(
                "RealRecruitmentNotificationService: unknown interview event '{EventType}' for interview {InterviewId}; nothing dispatched.",
                eventType, interviewId);
            return;
        }

        await DispatchInterviewAsync(eventKey, interviewId, vacancyId, applicantEmail, interviewerEmployeeIds, cancellationToken);
    }

    public Task NotifyInterviewReminderAsync(
        Guid interviewId, Guid applicantId, Guid vacancyId, string applicantEmail,
        IReadOnlyList<Guid> interviewerEmployeeIds, CancellationToken cancellationToken = default)
        => DispatchInterviewAsync("interview_reminder", interviewId, vacancyId, applicantEmail, interviewerEmployeeIds, cancellationToken);

    private async Task DispatchInterviewAsync(
        string eventKey, Guid interviewId, Guid vacancyId, string applicantEmail,
        IReadOnlyList<Guid> interviewerEmployeeIds, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var vacancyTitle = await LoadVacancyTitleAsync(tenantId, vacancyId, cancellationToken);
            var interview = await _db.Interviews.IgnoreQueryFilters().AsNoTracking()
                .Where(i => i.Id == interviewId && i.TenantId == tenantId)
                .Select(i => new { i.ScheduledDate, i.StartTime, i.InterviewType, i.Location, i.VideoLink })
                .FirstOrDefaultAsync(cancellationToken);

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["applicant"] = new Dictionary<string, object?> { ["email"] = applicantEmail },
                ["vacancy"] = new Dictionary<string, object?> { ["title"] = vacancyTitle },
                ["interview"] = new Dictionary<string, object?>
                {
                    ["date"] = interview?.ScheduledDate.ToString("yyyy-MM-dd") ?? string.Empty,
                    ["time"] = interview?.StartTime.ToString("HH:mm") ?? string.Empty,
                    ["type"] = interview?.InterviewType.ToString() ?? string.Empty,
                    ["location"] = FirstNonEmpty(interview?.VideoLink, interview?.Location),
                },
            });

            // Candidate is external → email-only (no User row).
            await DispatchEmailOnlyAsync(tenantId, eventKey, payload, applicantEmail, cancellationToken);

            // Interviewers are Employees: resolve {UserId, Email}. Linked account → in-app + email;
            // account-less → email-only fallback (ISSUE-263). Mirrors the hiring-manager resolution above.
            if (interviewerEmployeeIds.Count > 0)
            {
                var distinctIds = interviewerEmployeeIds.Distinct().ToList();
                var interviewers = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
                    .Where(e => e.TenantId == tenantId && distinctIds.Contains(e.Id))
                    .Select(e => new { e.UserId, e.Email })
                    .ToListAsync(cancellationToken);

                var userIds = new HashSet<Guid>();
                foreach (var iv in interviewers)
                {
                    if (iv.UserId is { } uid)
                        userIds.Add(uid);
                    else if (!string.IsNullOrWhiteSpace(iv.Email))
                        await DispatchEmailOnlyAsync(tenantId, eventKey, payload, iv.Email, cancellationToken);
                }

                await DispatchToUsersAsync(tenantId, eventKey, payload, userIds, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            LogFailure(ex, eventKey, interviewId);
        }
    }

    public async Task NotifyScorecardSubmittedAsync(
        Guid scorecardId, Guid interviewId, Guid applicantId, Guid vacancyId, Guid interviewerEmployeeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var applicant = await LoadApplicantAsync(tenantId, applicantId, cancellationToken);
            var vacancyTitle = await LoadVacancyTitleAsync(tenantId, vacancyId, cancellationToken);
            var name = $"{applicant?.FirstName} {applicant?.LastName}".Trim();

            var payload = BuildApplicantPayload(
                title: $"Scorecard submitted for {vacancyTitle}",
                message: $"An interviewer submitted a scorecard for {name} ({vacancyTitle}).",
                applicant, applicant?.Email, vacancyTitle, applicant?.ApplicationReferenceNumber ?? string.Empty);

            var userIds = await ResolveRecruiterUserIdsAsync(tenantId, cancellationToken);
            await DispatchToUsersAsync(tenantId, "scorecard_submitted", payload, userIds, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "scorecard_submitted", scorecardId);
        }
    }

    public async Task NotifyOfferAsync(
        string eventType, Guid offerId, Guid applicantId, Guid vacancyId, string applicantEmail,
        CancellationToken cancellationToken = default)
    {
        var (eventKey, toRecruiterPool) = eventType switch
        {
            "offer-sent" => ("offer_sent", false),
            "offer-withdrawn" => ("offer_withdrawn", false),
            "offer-expiry-reminder" => ("offer_expiry_reminder", true),
            "offer-expired" => ("offer_expired", true),
            _ => (string.Empty, false),
        };
        if (string.IsNullOrEmpty(eventKey))
        {
            _logger.LogWarning(
                "RealRecruitmentNotificationService: unknown offer event '{EventType}' for offer {OfferId}; nothing dispatched.",
                eventType, offerId);
            return;
        }

        try
        {
            var tenantId = _tenantContext.TenantId;
            var applicant = await LoadApplicantAsync(tenantId, applicantId, cancellationToken);
            var vacancyTitle = await LoadVacancyTitleAsync(tenantId, vacancyId, cancellationToken);
            var offer = await _db.Offers.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.Id == offerId && o.TenantId == tenantId)
                .Select(o => new { o.OfferReferenceNumber, o.OfferedPosition, o.StartDate, o.ExpiryDate, o.PdfStorageKey })
                .FirstOrDefaultAsync(cancellationToken);

            var candidateEmail = FirstNonEmpty(applicantEmail, applicant?.Email);
            var candidateName = $"{applicant?.FirstName} {applicant?.LastName}".Trim();
            var (inAppTitle, inAppMessage) = eventKey switch
            {
                "offer_expiry_reminder" => (
                    $"Offer expiring soon for {vacancyTitle}",
                    $"The offer for {candidateName} ({vacancyTitle}) is expiring soon."),
                "offer_expired" => (
                    $"Offer expired for {vacancyTitle}",
                    $"The offer for {candidateName} ({vacancyTitle}) has expired without a response."),
                _ => (string.Empty, string.Empty),
            };
            var payloadData = new Dictionary<string, object?>
            {
                ["title"] = inAppTitle,
                ["message"] = inAppMessage,
                ["applicant"] = new Dictionary<string, object?>
                {
                    ["firstName"] = applicant?.FirstName ?? string.Empty,
                    ["email"] = candidateEmail,
                },
                ["vacancy"] = new Dictionary<string, object?> { ["title"] = vacancyTitle },
                ["offer"] = new Dictionary<string, object?>
                {
                    ["reference"] = offer?.OfferReferenceNumber ?? string.Empty,
                    ["position"] = offer?.OfferedPosition ?? vacancyTitle,
                    ["startDate"] = offer?.StartDate.ToString("yyyy-MM-dd") ?? string.Empty,
                    ["expiryDate"] = offer?.ExpiryDate.ToString("yyyy-MM-dd") ?? string.Empty,
                },
            };
            var payload = JsonSerializer.Serialize(payloadData);

            // offer_sent → candidate leg carries the offer PDF inline (IEmailSender), NOT via the dispatcher.
            if (eventKey == "offer_sent")
            {
                await SendOfferSentWithPdfAsync(
                    tenantId, offerId, vacancyId, applicantId, candidateEmail, offer?.OfferReferenceNumber,
                    offer?.PdfStorageKey, payloadData, payload, cancellationToken);
            }
            else
            {
                await DispatchEmailOnlyAsync(tenantId, eventKey, payload, candidateEmail, cancellationToken);
            }

            // Expiry-reminder / expired also notify the recruiter pool (in-app + email).
            if (toRecruiterPool)
            {
                var userIds = await ResolveRecruiterUserIdsAsync(tenantId, cancellationToken);
                await DispatchToUsersAsync(tenantId, eventKey, payload, userIds, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            LogFailure(ex, eventKey, offerId);
        }
    }

    /// <summary>
    /// Sends the offer_sent candidate email with the stored offer-letter PDF attached INLINE via
    /// <see cref="IEmailSender"/> (the Phase-4 payslip pattern — the multi-MB blob must not be persisted as a Hangfire
    /// argument). Falls back to a dispatcher email with no attachment if the PDF cannot be read (never throws).
    /// </summary>
    private async Task SendOfferSentWithPdfAsync(
        Guid tenantId, Guid offerId, Guid vacancyId, Guid applicantId, string candidateEmail, string? offerReference,
        string? pdfStorageKey, IReadOnlyDictionary<string, object?> payloadData, string payloadJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidateEmail))
        {
            _logger.LogWarning(
                "RealRecruitmentNotificationService: no candidate email for offer_sent (offer {OfferId}); not sent.",
                offerId);
            return;
        }

        byte[]? pdfBytes = null;
        var storagePath = string.IsNullOrWhiteSpace(pdfStorageKey)
            ? $"recruitment/{vacancyId}/{applicantId}/offers/{offerId:N}.pdf"
            : pdfStorageKey;
        try
        {
            await using var stream = await _fileStorage.OpenReadAsync(tenantId, storagePath, cancellationToken);
            if (stream is not null)
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, cancellationToken);
                pdfBytes = ms.ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RealRecruitmentNotificationService: could not read offer PDF at {Path} (offer {OfferId}); " +
                "falling back to an email without attachment.", storagePath, offerId);
        }

        // No PDF → fall back to the plain dispatcher email (still delivers, just without the attachment).
        if (pdfBytes is null || pdfBytes.Length == 0)
        {
            await DispatchEmailOnlyAsync(tenantId, "offer_sent", payloadJson, candidateEmail, cancellationToken);
            return;
        }

        // Render the offer_sent template (tenant override → system default) against the payload, then send inline.
        var resolved = await _templateService.ResolveAsync("offer_sent", language: null, cancellationToken);
        if (resolved.IsFailure || resolved.Value is null)
        {
            await DispatchEmailOnlyAsync(tenantId, "offer_sent", payloadJson, candidateEmail, cancellationToken);
            return;
        }

        var t = resolved.Value;
        var rendered = _templateService.Render(t.Subject, t.BodyHtml, t.BodyText, payloadData);
        var fileName = string.IsNullOrWhiteSpace(offerReference) ? $"offer-{offerId:N}.pdf" : $"offer-{offerReference}.pdf";

        var message = new EmailMessage(
            TenantId: tenantId,
            RecipientEmail: candidateEmail,
            Subject: rendered.Subject,
            BodyHtml: rendered.BodyHtml,
            BodyText: rendered.BodyText,
            Attachments: [new EmailAttachment(fileName, pdfBytes, "application/pdf")]);

        await _emailSender.SendAsync(message, cancellationToken);
    }

    // ── Recipient resolution ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the distinct ACTIVE tenant users whose assigned roles grant <c>Recruitment.Manage</c> (the recruiter /
    /// hiring-team pool). Scoped by tenant id with IgnoreQueryFilters so it is correct outside a request scope; mirrors
    /// the payroll approver-pool query with the recruitment permission.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveRecruiterUserIdsAsync(Guid tenantId, CancellationToken cancellationToken)
        => await (
                from ut in _db.UserTenants.IgnoreQueryFilters()
                where ut.TenantId == tenantId && ut.Status == UserTenantStatus.Active
                join utr in _db.UserTenantRoles.IgnoreQueryFilters() on ut.Id equals utr.UserTenantId
                join rp in _db.RolePermissions.IgnoreQueryFilters() on utr.RoleId equals rp.RoleId
                where rp.Permission == PermissionCatalog.Recruitment.Manage
                select ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    // ── Dispatch legs ───────────────────────────────────────────────────────────────────────────────

    private async Task DispatchToUsersAsync(
        Guid tenantId, string eventKey, string payloadJson, IEnumerable<Guid> recipientUserIds,
        CancellationToken cancellationToken)
    {
        foreach (var userId in recipientUserIds.Distinct())
        {
            try
            {
                var request = new NotificationRequest(
                    TenantId: tenantId, EventKey: eventKey, PayloadJson: payloadJson,
                    RecipientUserId: userId, NotificationType: eventKey);
                await _dispatcher.SendInAppAsync(request, cancellationToken);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "RealRecruitmentNotificationService: failed to dispatch {EventKey} to user {UserId}.", eventKey, userId);
            }
        }
    }

    private async Task DispatchEmailOnlyAsync(
        Guid tenantId, string eventKey, string payloadJson, string? recipientEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogWarning(
                "RealRecruitmentNotificationService: no email address for {EventKey} (tenant {TenantId}); not sent.",
                eventKey, tenantId);
            return;
        }

        try
        {
            var request = new NotificationRequest(
                TenantId: tenantId, EventKey: eventKey, PayloadJson: payloadJson,
                RecipientEmail: recipientEmail, NotificationType: eventKey);
            await _dispatcher.SendEmailAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RealRecruitmentNotificationService: failed to dispatch email {EventKey} to {Email}.", eventKey, recipientEmail);
        }
    }

    // ── Loaders + helpers ───────────────────────────────────────────────────────────────────────────

    private async Task<ApplicantLite?> LoadApplicantAsync(Guid tenantId, Guid applicantId, CancellationToken cancellationToken)
        => await _db.Applicants.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Id == applicantId && a.TenantId == tenantId)
            .Select(a => new ApplicantLite(a.FirstName, a.LastName, a.Email, a.ApplicationReferenceNumber))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<string> LoadVacancyTitleAsync(Guid tenantId, Guid vacancyId, CancellationToken cancellationToken)
        => await _db.Vacancies.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Id == vacancyId && v.TenantId == tenantId)
            .Select(v => v.Title)
            .FirstOrDefaultAsync(cancellationToken) ?? "the position";

    private static string BuildApplicantPayload(
        string title, string message, ApplicantLite? applicant, string? email, string vacancyTitle,
        string reference, string? fromStage = null, string? toStage = null)
    {
        var application = new Dictionary<string, object?> { ["reference"] = reference };
        if (fromStage is not null) application["fromStage"] = fromStage;
        if (toStage is not null) application["toStage"] = toStage;

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = title,
            ["message"] = message,
            ["applicant"] = new Dictionary<string, object?>
            {
                ["firstName"] = applicant?.FirstName ?? string.Empty,
                ["lastName"] = applicant?.LastName ?? string.Empty,
                ["email"] = FirstNonEmpty(email, applicant?.Email),
            },
            ["vacancy"] = new Dictionary<string, object?> { ["title"] = vacancyTitle },
            ["application"] = application,
        });
    }

    private static string FirstNonEmpty(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) ? a : (b ?? string.Empty);

    private void LogFailure(Exception ex, string eventKey, Guid subjectId)
        => _logger.LogError(ex,
            "RealRecruitmentNotificationService: failed to dispatch {EventKey} for {SubjectId} (tenant {TenantId}).",
            eventKey, subjectId, _tenantContext.TenantId);

    private sealed record ApplicantLite(string FirstName, string LastName, string Email, string ApplicationReferenceNumber);
}
