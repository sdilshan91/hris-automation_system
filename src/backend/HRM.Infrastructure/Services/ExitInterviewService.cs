using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Exit-interview recording + analytics service (US-ONB-006). All queries are tenant-scoped via ITenantContext
/// + the EF global query filter (FR-6/NFR-2/AC-5); the tenant_id is stamped from the session, never user input.
/// Recording links the interview to its offboarding instance (FR-3), completes the exit-interview offboarding
/// task (AC-2), and — for self-service — writes an HR-notification outbox row reusing the onboarding outbox
/// pattern (FR-8). BR-1: one active interview per offboarding. BR-2: a submitted interview is immutable; an edit
/// creates a NEW version and supersedes the original (the original is preserved). FR-5: analytics returns
/// anonymized aggregates only. Audit columns (FR-7) are stamped by AuditInterceptor.
/// </summary>
public sealed class ExitInterviewService : IExitInterviewService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ExitInterviewService> _logger;

    public ExitInterviewService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<ExitInterviewService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── FR-1 / AC-1: template ────────────────────────────────────────────

    public async Task<Result<ExitInterviewTemplateDto>> GetTemplateAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ExitInterviewTemplateDto>.Failure("Tenant context is not resolved.", 400);

        var template = await _dbContext.ExitInterviewTemplates
            .Include(t => t.Questions)
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // FR-1/AC-1: no tenant template yet → seed + persist the built-in DEFAULT set for this tenant.
        if (template is null)
            template = await SeedDefaultTemplateAsync(cancellationToken);

        return Result<ExitInterviewTemplateDto>.Success(ToTemplateDto(template));
    }

    // ── FR-3: get by offboarding ─────────────────────────────────────────

    public async Task<Result<ExitInterviewDto>> GetByOffboardingAsync(
        Guid offboardingInstanceId, bool includeFreeText, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ExitInterviewDto>.Failure("Tenant context is not resolved.", 400);

        var interview = await _dbContext.ExitInterviews
            .AsNoTracking()
            .Include(i => i.Responses)
            .Where(i => i.OffboardingInstanceId == offboardingInstanceId && !i.IsSuperseded)
            .FirstOrDefaultAsync(cancellationToken);

        if (interview is null)
            return Result<ExitInterviewDto>.Failure(
                "No exit interview found for this offboarding.", 404, "exit_interview_not_found");

        // NFR-6: flag PII free-text access in the audit log when detail is surfaced.
        if (includeFreeText)
            _logger.LogInformation(
                "Exit-interview PII free-text accessed. InterviewId={InterviewId}, Offboarding={Offboarding}, " +
                "TenantId={TenantId}, By={User}",
                interview.Id, offboardingInstanceId, _tenantContext.TenantId, _currentUser.Email);

        return Result<ExitInterviewDto>.Success(ToInterviewDto(interview, includeFreeText));
    }

    // ── AC-2 / FR-3 / FR-8 / BR-1 / BR-2: record ─────────────────────────

    public async Task<Result<ExitInterviewDto>> RecordAsync(
        RecordExitInterviewInput input,
        bool isSelfService,
        bool allowEdit,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ExitInterviewDto>.Failure("Tenant context is not resolved.", 400);

        var offboarding = await _dbContext.OffboardingInstances
            .Include(o => o.Tasks)
            .FirstOrDefaultAsync(o => o.Id == input.OffboardingId, cancellationToken);
        if (offboarding is null)
            return Result<ExitInterviewDto>.Failure(
                "Offboarding not found.", 404, "offboarding_not_found");

        // Self-service: the caller may only record against their OWN offboarding (authorization scope). The
        // caller's employee record is resolved from the session (tenant-scoped via the global query filter).
        if (isSelfService)
        {
            var callerEmployeeId = await _dbContext.Employees
                .AsNoTracking()
                .Where(e => e.UserId == _currentUser.UserId)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (callerEmployeeId is null || offboarding.EmployeeId != callerEmployeeId.Value)
                return Result<ExitInterviewDto>.Failure(
                    "You can only submit an exit interview for your own offboarding.", 403, "not_own_offboarding");

            // BR-3: self-service must be submitted before the last working day.
            if (DateTime.UtcNow.Date > offboarding.LastWorkingDay.Date)
                return Result<ExitInterviewDto>.Failure(
                    "Self-service exit interviews must be submitted before your last working day.",
                    409, "after_last_working_day");
        }

        // Resolve the active template (seeding the default set if the tenant has none).
        var template = await _dbContext.ExitInterviewTemplates
            .Include(t => t.Questions)
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        template ??= await SeedDefaultTemplateAsync(cancellationToken);

        var questionsById = template.Questions.ToDictionary(q => q.Id);

        // Every supplied response must map to a question in the tenant's template (FR-1 §7).
        foreach (var r in input.Responses)
        {
            if (!questionsById.ContainsKey(r.QuestionId))
                return Result<ExitInterviewDto>.Failure(
                    $"Question {r.QuestionId} does not exist in the tenant's exit interview template.",
                    400, "unknown_question");
        }

        // BR-1 / BR-2: at most one ACTIVE interview per offboarding. A second submission edits it (new version)
        // only when explicitly permitted; otherwise it is rejected as a duplicate.
        var existing = await _dbContext.ExitInterviews
            .Where(i => i.OffboardingInstanceId == input.OffboardingId && !i.IsSuperseded)
            .FirstOrDefaultAsync(cancellationToken);

        var nextVersion = 1;
        Guid? supersedesId = null;
        if (existing is not null)
        {
            if (!allowEdit)
                return Result<ExitInterviewDto>.Failure(
                    "An exit interview has already been recorded for this offboarding.",
                    409, "exit_interview_exists");

            // BR-2: preserve the original; mark it superseded and create a new version.
            existing.IsSuperseded = true;
            nextVersion = existing.Version + 1;
            supersedesId = existing.Id;
        }

        var mode = isSelfService ? ExitInterviewMode.SelfService : ExitInterviewMode.HrConducted;

        var interviewId = BaseEntity.NewUuidV7();
        var interview = new ExitInterview
        {
            Id = interviewId,
            TenantId = _tenantContext.TenantId, // FR-6
            OffboardingInstanceId = offboarding.Id, // FR-3
            TemplateId = template.Id,
            InterviewMode = mode,
            ConductedByUserId = mode == ExitInterviewMode.HrConducted ? _currentUser.UserId : null,
            // BUG-289 class: interview_date is timestamptz — .Date is Kind=Unspecified (Npgsql rejects it). UTC-kind it.
            InterviewDate = DateTime.SpecifyKind(input.InterviewDate.Date, DateTimeKind.Utc),
            OverallExperienceRating = input.OverallExperienceRating,
            WouldRecommendEmployer = input.WouldRecommendEmployer,
            AdditionalComments = string.IsNullOrWhiteSpace(input.AdditionalComments)
                ? null
                : input.AdditionalComments.Trim(),
            Version = nextVersion,
            IsSuperseded = false,
            SupersedesId = supersedesId,
            IsDeleted = false,
        };

        foreach (var r in input.Responses)
        {
            var q = questionsById[r.QuestionId];
            interview.Responses.Add(new ExitInterviewResponse
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId, // FR-6
                ExitInterviewId = interviewId,
                QuestionId = r.QuestionId,
                Category = q.Category, // FR-4 analytics grouping snapshot
                Rating = r.Rating,
                SelectedOption = string.IsNullOrWhiteSpace(r.SelectedOption) ? null : r.SelectedOption.Trim(),
                FreeText = string.IsNullOrWhiteSpace(r.FreeText) ? null : r.FreeText.Trim(),
                IsDeleted = false,
            });
        }

        _dbContext.ExitInterviews.Add(interview);

        // AC-2: mark the exit-interview offboarding task complete (matched by title, if such a task exists).
        CompleteExitInterviewTask(offboarding);

        // FR-8: self-service submissions notify HR via the outbox (reuses the onboarding notification outbox).
        if (isSelfService)
            await WriteHrNotificationAsync(offboarding, interview, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Exit interview recorded. InterviewId={InterviewId}, Offboarding={Offboarding}, Mode={Mode}, " +
            "Version={Version}, Responses={Responses}, TenantId={TenantId}, By={User}",
            interview.Id, offboarding.Id, mode, interview.Version, interview.Responses.Count,
            _tenantContext.TenantId, _currentUser.Email);

        // The recorder always sees the full record (incl. free text) — they just submitted it.
        return Result<ExitInterviewDto>.Success(ToInterviewDto(interview, includeFreeText: true));
    }

    // ── FR-4 / FR-5: analytics (anonymized aggregates) ───────────────────

    public async Task<Result<ExitInterviewAnalyticsDto>> GetAnalyticsAsync(
        DateTime? fromDate,
        DateTime? toDate,
        bool hasDetailPermission,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ExitInterviewAnalyticsDto>.Failure("Tenant context is not resolved.", 400);

        var from = fromDate?.Date;
        var to = toDate?.Date;

        // Active (non-superseded) interviews in range — tenant-scoped via the global query filter (BR-4/AC-5).
        var interviewsQuery = _dbContext.ExitInterviews
            .AsNoTracking()
            .Where(i => !i.IsSuperseded);
        if (from is not null)
            interviewsQuery = interviewsQuery.Where(i => i.InterviewDate >= from.Value);
        if (to is not null)
            interviewsQuery = interviewsQuery.Where(i => i.InterviewDate <= to.Value);

        var interviews = await interviewsQuery
            .Select(i => new
            {
                i.Id,
                i.OffboardingInstanceId,
                i.InterviewDate,
                i.OverallExperienceRating,
            })
            .ToListAsync(cancellationToken);

        if (interviews.Count == 0)
            return Result<ExitInterviewAnalyticsDto>.Success(new ExitInterviewAnalyticsDto());

        var interviewIds = interviews.Select(i => i.Id).ToHashSet();
        var offboardingIds = interviews.Select(i => i.OffboardingInstanceId).Distinct().ToList();

        // Reason distribution: from the linked offboarding instances (tenant-scoped).
        var reasons = await _dbContext.OffboardingInstances
            .AsNoTracking()
            .Where(o => offboardingIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Reason })
            .ToListAsync(cancellationToken);
        var reasonByOffboarding = reasons.ToDictionary(r => r.Id, r => r.Reason);

        var reasonDistribution = interviews
            .Select(i => reasonByOffboarding.TryGetValue(i.OffboardingInstanceId, out var reason)
                ? reason.ToString()
                : "Unknown")
            .GroupBy(r => r)
            .Select(g => new ReasonCountDto { Reason = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Reason)
            .ToList();

        // Average rating per category: from rating-type responses, grouped by the category snapshot.
        var ratings = await _dbContext.ExitInterviewResponses
            .AsNoTracking()
            .Where(r => interviewIds.Contains(r.ExitInterviewId) && r.Rating != null)
            .Select(r => new { r.Category, Rating = r.Rating!.Value })
            .ToListAsync(cancellationToken);

        var averageRatingsPerCategory = ratings
            .GroupBy(r => r.Category)
            .Select(g => new CategoryAverageDto
            {
                Category = g.Key,
                AverageRating = Math.Round(g.Average(x => x.Rating), 2),
            })
            .OrderBy(c => c.Category)
            .ToList();

        // Trend over time: monthly interview count + average overall rating.
        var trend = interviews
            .GroupBy(i => i.InterviewDate.ToString("yyyy-MM"))
            .Select(g => new TrendPointDto
            {
                Period = g.Key,
                Count = g.Count(),
                AverageRating = g.Any(x => x.OverallExperienceRating != null)
                    ? Math.Round(g.Where(x => x.OverallExperienceRating != null)
                        .Average(x => x.OverallExperienceRating!.Value), 2)
                    : 0,
            })
            .OrderBy(t => t.Period)
            .ToList();

        if (hasDetailPermission)
            _logger.LogInformation(
                "Exit-interview analytics accessed with detail permission. TenantId={TenantId}, By={User}",
                _tenantContext.TenantId, _currentUser.Email);

        return Result<ExitInterviewAnalyticsDto>.Success(new ExitInterviewAnalyticsDto
        {
            ReasonDistribution = reasonDistribution,
            AverageRatingsPerCategory = averageRatingsPerCategory,
            Trend = trend,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>AC-2: complete the offboarding's exit-interview task (matched case-insensitively by title).</summary>
    private static void CompleteExitInterviewTask(OffboardingInstance offboarding)
    {
        var task = offboarding.Tasks.FirstOrDefault(t =>
            !t.IsDeleted
            && t.Status != OnboardingTaskStatus.Completed
            && t.Title.Contains("exit interview", StringComparison.OrdinalIgnoreCase));
        if (task is null)
            return;

        task.Status = OnboardingTaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;
    }

    /// <summary>FR-8: write an HR-notification outbox row for a self-service submission.</summary>
    private async Task WriteHrNotificationAsync(
        OffboardingInstance offboarding, ExitInterview interview, CancellationToken cancellationToken)
    {
        // Resolve an HR recipient: prefer the user who initiated the offboarding.
        var recipientUserId = offboarding.InitiatedByUserId ?? offboarding.CompletedByUserId;
        if (recipientUserId is null)
            return; // No resolvable HR recipient; the dispatcher worker has nothing to send.

        var payload = JsonSerializer.Serialize(new
        {
            exitInterviewId = interview.Id,
            offboardingInstanceId = offboarding.Id,
            employeeId = offboarding.EmployeeId,
            interviewDate = interview.InterviewDate,
            submittedAt = DateTime.UtcNow,
        });

        _dbContext.OnboardingNotificationOutbox.Add(new OnboardingNotificationOutbox
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            // The outbox is keyed by a checklist/offboarding instance; reuse the offboarding instance id.
            ChecklistInstanceId = offboarding.Id,
            RecipientUserId = recipientUserId.Value,
            RecipientRole = OnboardingResponsibleRole.HR,
            NotificationType = "exit_interview.self_service.submitted",
            Payload = payload,
            Status = OnboardingNotificationStatus.Pending,
            AttemptCount = 0,
            IsDeleted = false,
        });

        await Task.CompletedTask;
    }

    /// <summary>FR-1/AC-1: seed + persist the built-in DEFAULT questionnaire template for the tenant.</summary>
    private async Task<ExitInterviewTemplate> SeedDefaultTemplateAsync(CancellationToken cancellationToken)
    {
        var templateId = BaseEntity.NewUuidV7();
        var template = new ExitInterviewTemplate
        {
            Id = templateId,
            TenantId = _tenantContext.TenantId,
            TemplateName = "Default Exit Interview",
            Description = "Default exit interview questionnaire.",
            IsActive = true,
            IsDeleted = false,
        };

        var sort = 0;
        foreach (var (category, text, type, options, required) in DefaultQuestions())
        {
            template.Questions.Add(new ExitInterviewQuestion
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                TemplateId = templateId,
                Category = category,
                Text = text,
                Type = type,
                Options = options,
                Required = required,
                SortOrder = sort++,
                IsDeleted = false,
            });
        }

        _dbContext.ExitInterviewTemplates.Add(template);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return template;
    }

    /// <summary>The built-in DEFAULT questionnaire (AC-1 categories).</summary>
    private static IEnumerable<(string Category, string Text, ExitInterviewQuestionType Type, List<string> Options, bool Required)> DefaultQuestions() =>
    [
        ("Reason for Leaving", "What is the primary reason for your departure?", ExitInterviewQuestionType.MultipleChoice,
            ["Better opportunity", "Compensation", "Work-life balance", "Management", "Relocation", "Career change", "Other"], true),
        ("Reason for Leaving", "Please elaborate on your reason for leaving.", ExitInterviewQuestionType.FreeText, [], false),
        ("Work Environment", "How would you rate the overall work environment?", ExitInterviewQuestionType.Rating, [], true),
        ("Work Environment", "Did you have the tools and resources to do your job effectively?", ExitInterviewQuestionType.YesNo, [], true),
        ("Management", "How would you rate the support from your manager?", ExitInterviewQuestionType.Rating, [], true),
        ("Management", "Did you receive regular, useful feedback?", ExitInterviewQuestionType.YesNo, [], false),
        ("Recommendations", "What could the organization do to improve?", ExitInterviewQuestionType.FreeText, [], false),
        ("Recommendations", "Would you recommend this organization as a place to work?", ExitInterviewQuestionType.Rating, [], false),
    ];

    private static ExitInterviewTemplateDto ToTemplateDto(ExitInterviewTemplate template)
    {
        var categories = template.Questions
            .OrderBy(q => q.SortOrder)
            .GroupBy(q => q.Category)
            .Select(g => new ExitInterviewCategoryDto
            {
                Category = g.Key,
                Questions = g.OrderBy(q => q.SortOrder).Select(q => new ExitInterviewQuestionDto
                {
                    QuestionId = q.Id,
                    Text = q.Text,
                    Type = ToWireType(q.Type),
                    Options = q.Type == ExitInterviewQuestionType.MultipleChoice ? q.Options : null,
                    Required = q.Required,
                }).ToList(),
            })
            .ToList();

        return new ExitInterviewTemplateDto { TemplateId = template.Id, Categories = categories };
    }

    private static ExitInterviewDto ToInterviewDto(ExitInterview interview, bool includeFreeText) => new()
    {
        Id = interview.Id,
        OffboardingInstanceId = interview.OffboardingInstanceId,
        TemplateId = interview.TemplateId,
        InterviewMode = ToWireMode(interview.InterviewMode),
        ConductedByUserId = interview.ConductedByUserId,
        InterviewDate = interview.InterviewDate,
        OverallExperienceRating = interview.OverallExperienceRating,
        WouldRecommendEmployer = interview.WouldRecommendEmployer,
        // FR-5/NFR-6: comments are free-text PII — withheld unless the caller is detail-permitted.
        AdditionalComments = includeFreeText ? interview.AdditionalComments : null,
        Version = interview.Version,
        Responses = interview.Responses.Select(r => new ExitInterviewResponseDto
        {
            QuestionId = r.QuestionId,
            Category = r.Category,
            Rating = r.Rating,
            SelectedOption = r.SelectedOption,
            FreeText = includeFreeText ? r.FreeText : null,
        }).ToList(),
        CreatedAt = interview.CreatedAt,
    };

    private static string ToWireType(ExitInterviewQuestionType type) => type switch
    {
        ExitInterviewQuestionType.Rating => "rating",
        ExitInterviewQuestionType.MultipleChoice => "multiple_choice",
        ExitInterviewQuestionType.FreeText => "free_text",
        ExitInterviewQuestionType.YesNo => "yes_no",
        _ => "free_text",
    };

    private static string ToWireMode(ExitInterviewMode mode) => mode switch
    {
        ExitInterviewMode.SelfService => "self_service",
        _ => "hr_conducted",
    };
}
