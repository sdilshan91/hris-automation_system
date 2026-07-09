using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-006 Phase 5b (the FINAL seam): the real <see cref="IPerformanceNotificationService"/>
/// (US-PRF-001..009). Replaces the log-only stub — resolves recipients and dispatches performance notifications
/// (email + in-app) via <see cref="INotificationDispatcher"/>.
///
/// <para><b>All performance recipients are internal employees.</b> Each recipient is resolved to
/// <c>Employee.UserId</c> (in-app + email); when the employee has no linked user we fall back to
/// <c>Employee.Email</c> (email-only). Recipient rules per method:</para>
/// <list type="bullet">
/// <item><b>goal-changed / self-assessment-reminder / manager-review / cycle-event / review-signoff / reviewer /
/// pip / goal-comment / goal-stale-nudge</b> → a single employee (goal owner, participant, reviewer, or the passed
/// stakeholder).</item>
/// <item><b>self-assessment-submitted</b> → the submitter's <c>ReportsToEmployeeId</c> manager.</item>
/// <item><b>review-disputed</b> → the employee's manager + the HR pool.</item>
/// <item><b>review-auto-closed</b> and the <c>goal-blocked</c> HR broadcast (detail == "hr") → the HR pool: every
/// ACTIVE tenant user whose roles grant <c>Performance.Review.All</c>.</item>
/// </list>
///
/// <para>The tenant id comes from <see cref="ITenantContext"/>: every caller runs with the tenant resolved — the
/// request-scoped services via the middleware, and the Hangfire jobs (reminders / phase-transition / auto-close /
/// stale-nudge) restore it explicitly before invoking this service. <b>Never throws</b> — a delivery failure must not
/// break the committed performance write; every method is wrapped and per-recipient legs are individually guarded,
/// mirroring the LogOnly contract. Resolution queries are tenant-scoped with <c>IgnoreQueryFilters</c> so they are
/// correct even outside a request scope.</para>
/// </summary>
public sealed class RealPerformanceNotificationService : IPerformanceNotificationService
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<RealPerformanceNotificationService> _logger;

    public RealPerformanceNotificationService(
        AppDbContext db,
        INotificationDispatcher dispatcher,
        ITenantContext tenantContext,
        ILogger<RealPerformanceNotificationService> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task NotifyGoalChangedAsync(
        string eventType, Guid goalId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        var eventKey = eventType switch
        {
            "goal-assigned" => "goal_assigned",
            "goal-modified" => "goal_modified",
            "goal-removed" => "goal_removed",
            _ => string.Empty,
        };
        if (WarnUnknown(eventKey, eventType, goalId)) return;

        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var goalTitle = await LoadGoalTitleAsync(tenantId, goalId, cancellationToken);
            var cycleName = await LoadCycleNameAsync(tenantId, cycleId, cancellationToken);

            var (verb, article) = eventKey switch
            {
                "goal_assigned" => ("assigned you a new", "goal"),
                "goal_modified" => ("updated your", "goal"),
                _ => ("removed your", "goal"),
            };
            var payload = BuildGoalPayload(
                title: eventKey switch
                {
                    "goal_assigned" => "A new goal has been assigned to you",
                    "goal_modified" => "One of your goals has been updated",
                    _ => "One of your goals has been removed",
                },
                message: $"Your manager has {verb} {article} — {goalTitle} — for the {cycleName} cycle.",
                employee, cycleName, goalTitle);

            await DispatchToEmployeeAsync(tenantId, eventKey, payload, employee, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, eventKey, goalId);
        }
    }

    public async Task NotifySelfAssessmentSubmittedAsync(
        Guid selfAssessmentId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var submitter = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var cycleName = await LoadCycleNameAsync(tenantId, cycleId, cancellationToken);
            var name = FullName(submitter);

            var payload = BuildPerfPayload(
                title: "A self-assessment has been submitted for your review",
                message: $"{name} has submitted their self-assessment for the {cycleName} cycle.",
                submitter, cycleName);

            // Recipient is the submitter's manager (the reporting line).
            var manager = await LoadManagerAsync(tenantId, employeeId, cancellationToken);
            await DispatchToEmployeeAsync(tenantId, "self_assessment_submitted", payload, manager, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "self_assessment_submitted", selfAssessmentId);
        }
    }

    public async Task NotifySelfAssessmentReminderAsync(
        Guid employeeId, Guid cycleId, int daysUntilDeadline, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var cycleName = await LoadCycleNameAsync(tenantId, cycleId, cancellationToken);

            var payload = BuildPerfPayload(
                title: "Reminder: your self-assessment is due soon",
                message: $"Your self-assessment for the {cycleName} cycle is due in {daysUntilDeadline} day(s).",
                employee, cycleName,
                extra: d => d["reminder"] = new Dictionary<string, object?> { ["daysUntilDeadline"] = daysUntilDeadline });

            await DispatchToEmployeeAsync(tenantId, "self_assessment_reminder", payload, employee, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "self_assessment_reminder", employeeId);
        }
    }

    public async Task NotifyManagerReviewSubmittedAsync(
        Guid managerReviewId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var cycleName = await LoadCycleNameAsync(tenantId, cycleId, cancellationToken);

            var payload = BuildPerfPayload(
                title: "Your performance review has been submitted",
                message: $"Your manager has submitted your performance review for the {cycleName} cycle.",
                employee, cycleName);

            await DispatchToEmployeeAsync(tenantId, "manager_review_submitted", payload, employee, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "manager_review_submitted", managerReviewId);
        }
    }

    public async Task NotifyCycleEventAsync(
        string eventType, Guid cycleId, Guid employeeId, string? detail = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var cycleName = await LoadCycleNameAsync(tenantId, cycleId, cancellationToken);
            var detailText = detail ?? string.Empty;

            var payload = BuildPerfPayload(
                title: $"Update on the {cycleName} performance cycle",
                message: $"There's an update on the {cycleName} performance cycle: {eventType}. {detailText}".Trim(),
                employee, cycleName,
                extra: d => d["event"] = new Dictionary<string, object?>
                {
                    ["subtype"] = eventType, ["detail"] = detailText,
                });

            await DispatchToEmployeeAsync(tenantId, "cycle_event", payload, employee, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "cycle_event", cycleId);
        }
    }

    public async Task NotifyReviewerAssignedAsync(
        Guid reviewerEmployeeId, Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
        => await DispatchReviewerAsync(
            "reviewer_assigned", reviewerEmployeeId, revieweeEmployeeId, cycleId,
            title: "You've been asked to provide feedback",
            messageVerb: "You've been asked to provide 360-degree feedback for",
            cancellationToken);

    public async Task NotifyReviewerReminderAsync(
        Guid reviewerEmployeeId, Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
        => await DispatchReviewerAsync(
            "reviewer_reminder", reviewerEmployeeId, revieweeEmployeeId, cycleId,
            title: "Reminder: your feedback is still pending",
            messageVerb: "Reminder — please submit your 360-degree feedback for",
            cancellationToken);

    private async Task DispatchReviewerAsync(
        string eventKey, Guid reviewerEmployeeId, Guid revieweeEmployeeId, Guid cycleId,
        string title, string messageVerb, CancellationToken cancellationToken)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var reviewer = await LoadEmployeeAsync(tenantId, reviewerEmployeeId, cancellationToken);
            var reviewee = await LoadEmployeeAsync(tenantId, revieweeEmployeeId, cancellationToken);
            var cycleName = await LoadCycleNameAsync(tenantId, cycleId, cancellationToken);
            var revieweeName = FullName(reviewee);

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["title"] = title,
                ["message"] = $"{messageVerb} {revieweeName} in the {cycleName} cycle.",
                ["reviewer"] = new Dictionary<string, object?> { ["firstName"] = reviewer?.FirstName ?? string.Empty },
                ["reviewee"] = new Dictionary<string, object?>
                {
                    ["firstName"] = reviewee?.FirstName ?? string.Empty,
                    ["lastName"] = reviewee?.LastName ?? string.Empty,
                },
                ["cycle"] = new Dictionary<string, object?> { ["name"] = cycleName },
            });

            await DispatchToEmployeeAsync(tenantId, eventKey, payload, reviewer, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, eventKey, reviewerEmployeeId);
        }
    }

    public async Task NotifyReviewSignOffRequestedAsync(
        Guid managerReviewId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var cycleName = await LoadCycleNameAsync(tenantId, cycleId, cancellationToken);

            var payload = BuildPerfPayload(
                title: "Please sign off on your performance review",
                message: $"Your manager has requested your sign-off on your {cycleName} performance review.",
                employee, cycleName);

            await DispatchToEmployeeAsync(tenantId, "review_signoff_requested", payload, employee, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "review_signoff_requested", managerReviewId);
        }
    }

    public async Task NotifyReviewDisputedAsync(
        Guid managerReviewId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var cycleName = await LoadCycleNameAsync(tenantId, cycleId, cancellationToken);
            var name = FullName(employee);

            var payload = BuildPerfPayload(
                title: "A performance review has been disputed",
                message: $"{name} has disputed their {cycleName} performance review.",
                employee, cycleName);

            // Manager + HR pool.
            var manager = await LoadManagerAsync(tenantId, employeeId, cancellationToken);
            await DispatchToEmployeeAsync(tenantId, "review_disputed", payload, manager, cancellationToken);

            var hr = await ResolveHrUserIdsAsync(tenantId, cancellationToken);
            await DispatchToUsersAsync(tenantId, "review_disputed", payload, hr, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "review_disputed", managerReviewId);
        }
    }

    public async Task NotifyReviewAutoClosedAsync(
        Guid managerReviewId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var employee = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var cycleName = await LoadCycleNameAsync(tenantId, cycleId, cancellationToken);
            var name = FullName(employee);

            var payload = BuildPerfPayload(
                title: "A performance review was auto-closed",
                message: $"The {cycleName} performance review for {name} was auto-closed with No Response.",
                employee, cycleName);

            var hr = await ResolveHrUserIdsAsync(tenantId, cancellationToken);
            await DispatchToUsersAsync(tenantId, "review_auto_closed", payload, hr, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "review_auto_closed", managerReviewId);
        }
    }

    public async Task NotifyPipEventAsync(
        string eventType, Guid pipId, Guid employeeId, Guid? recipientEmployeeId, string? detail = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = _tenantContext.TenantId;
            var subject = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var subjectName = FullName(subject);
            var detailText = detail ?? string.Empty;

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["title"] = "Update on a performance improvement plan",
                ["message"] = $"There's an update on the improvement plan for {subjectName}: {eventType}. {detailText}"
                    .Trim(),
                ["employee"] = new Dictionary<string, object?>
                {
                    ["firstName"] = subject?.FirstName ?? string.Empty,
                    ["lastName"] = subject?.LastName ?? string.Empty,
                },
                ["pip"] = new Dictionary<string, object?> { ["subtype"] = eventType, ["detail"] = detailText },
            });

            // Recipient is the passed stakeholder (employee / manager / mentor); fall back to the PIP subject.
            var recipient = await LoadEmployeeAsync(tenantId, recipientEmployeeId ?? employeeId, cancellationToken);
            await DispatchToEmployeeAsync(tenantId, "pip_event", payload, recipient, cancellationToken);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "pip_event", pipId);
        }
    }

    public async Task NotifyGoalProgressAsync(
        string eventType, Guid goalId, Guid employeeId, Guid? recipientEmployeeId, string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var eventKey = eventType switch
        {
            "goal-progress-updated" => "goal_progress_updated",
            "goal-blocked" => "goal_blocked",
            "goal-stale-nudge" => "goal_stale_nudge",
            "goal-comment-added" => "goal_comment_added",
            _ => string.Empty,
        };
        if (WarnUnknown(eventKey, eventType, goalId)) return;

        try
        {
            var tenantId = _tenantContext.TenantId;
            var isHrBroadcast = string.Equals(detail, "hr", StringComparison.Ordinal);
            var owner = await LoadEmployeeAsync(tenantId, employeeId, cancellationToken);
            var goalTitle = await LoadGoalTitleAsync(tenantId, goalId, cancellationToken);
            var ownerName = FullName(owner);
            var progressDetail = isHrBroadcast ? string.Empty : (detail ?? string.Empty);

            var (title, message) = eventKey switch
            {
                "goal_progress_updated" => (
                    "A goal progress update has been posted",
                    $"{ownerName} posted a progress update on their goal — {goalTitle}: {progressDetail}.".Trim()),
                "goal_blocked" => (
                    "A goal has been marked as blocked",
                    $"{ownerName} has marked their goal — {goalTitle} — as Blocked."),
                "goal_stale_nudge" => (
                    "One of your goals needs an update",
                    $"Your goal — {goalTitle} — hasn't been updated recently. Please post a progress update."),
                _ => (
                    "A comment was added to your goal",
                    $"Your manager added a comment to your goal — {goalTitle}."),
            };

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["title"] = title,
                ["message"] = message,
                ["employee"] = new Dictionary<string, object?>
                {
                    ["firstName"] = owner?.FirstName ?? string.Empty,
                    ["lastName"] = owner?.LastName ?? string.Empty,
                },
                ["goal"] = new Dictionary<string, object?> { ["title"] = goalTitle },
                ["progress"] = new Dictionary<string, object?> { ["detail"] = progressDetail },
            });

            if (isHrBroadcast)
            {
                // BR-3: goal-blocked HR broadcast (recipient flows in as null, detail == "hr").
                var hr = await ResolveHrUserIdsAsync(tenantId, cancellationToken);
                await DispatchToUsersAsync(tenantId, eventKey, payload, hr, cancellationToken);
            }
            else
            {
                var recipient = await LoadEmployeeAsync(tenantId, recipientEmployeeId ?? employeeId, cancellationToken);
                await DispatchToEmployeeAsync(tenantId, eventKey, payload, recipient, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            LogFailure(ex, eventKey, goalId);
        }
    }

    // ── Recipient resolution ────────────────────────────────────────────────────────────────────────

    private async Task<EmployeeLite?> LoadEmployeeAsync(
        Guid tenantId, Guid employeeId, CancellationToken cancellationToken)
        => await _db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Id == employeeId && e.TenantId == tenantId)
            .Select(e => new EmployeeLite(e.UserId, e.Email, e.FirstName, e.LastName))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Loads the reporting-line manager of <paramref name="employeeId"/> (its ReportsToEmployeeId).</summary>
    private async Task<EmployeeLite?> LoadManagerAsync(
        Guid tenantId, Guid employeeId, CancellationToken cancellationToken)
    {
        var managerId = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Id == employeeId && e.TenantId == tenantId)
            .Select(e => e.ReportsToEmployeeId)
            .FirstOrDefaultAsync(cancellationToken);
        return managerId is { } m ? await LoadEmployeeAsync(tenantId, m, cancellationToken) : null;
    }

    /// <summary>
    /// Resolves the distinct ACTIVE tenant users whose assigned roles grant <c>Performance.Review.All</c> (the HR
    /// pool). Scoped by tenant id with IgnoreQueryFilters so it is correct outside a request scope; mirrors the
    /// payroll approver-pool query with the performance-review permission.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveHrUserIdsAsync(Guid tenantId, CancellationToken cancellationToken)
        => await (
                from ut in _db.UserTenants.IgnoreQueryFilters()
                where ut.TenantId == tenantId && ut.Status == Domain.Entities.UserTenantStatus.Active
                join utr in _db.UserTenantRoles.IgnoreQueryFilters() on ut.Id equals utr.UserTenantId
                join rp in _db.RolePermissions.IgnoreQueryFilters() on utr.RoleId equals rp.RoleId
                where rp.Permission == PermissionCatalog.Performance.ReviewAll
                select ut.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    private async Task<string> LoadGoalTitleAsync(Guid tenantId, Guid goalId, CancellationToken cancellationToken)
        => await _db.Goals.IgnoreQueryFilters().AsNoTracking()
            .Where(g => g.Id == goalId && g.TenantId == tenantId)
            .Select(g => g.Title)
            .FirstOrDefaultAsync(cancellationToken) ?? "your goal";

    private async Task<string> LoadCycleNameAsync(Guid tenantId, Guid cycleId, CancellationToken cancellationToken)
        => await _db.AppraisalCycles.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == cycleId && c.TenantId == tenantId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "the current";

    // ── Dispatch legs ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches to a single internal employee: in-app + email when the employee has a linked user id, else email-only
    /// against the employee's own address. Per-recipient guarded so a delivery failure can't break the flow.
    /// </summary>
    private async Task DispatchToEmployeeAsync(
        Guid tenantId, string eventKey, string payloadJson, EmployeeLite? recipient, CancellationToken cancellationToken)
    {
        if (recipient is null)
        {
            _logger.LogWarning(
                "RealPerformanceNotificationService: no recipient employee for {EventKey} (tenant {TenantId}); not sent.",
                eventKey, tenantId);
            return;
        }

        try
        {
            if (recipient.UserId is { } userId)
            {
                var request = new NotificationRequest(
                    TenantId: tenantId, EventKey: eventKey, PayloadJson: payloadJson,
                    RecipientUserId: userId, NotificationType: eventKey);
                await _dispatcher.SendInAppAsync(request, cancellationToken);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                var request = new NotificationRequest(
                    TenantId: tenantId, EventKey: eventKey, PayloadJson: payloadJson,
                    RecipientEmail: recipient.Email, NotificationType: eventKey);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "RealPerformanceNotificationService: recipient for {EventKey} has no user id or email (tenant " +
                    "{TenantId}); not sent.", eventKey, tenantId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RealPerformanceNotificationService: failed to dispatch {EventKey} to an employee (tenant {TenantId}).",
                eventKey, tenantId);
        }
    }

    private async Task DispatchToUsersAsync(
        Guid tenantId, string eventKey, string payloadJson, IReadOnlyList<Guid> recipientUserIds,
        CancellationToken cancellationToken)
    {
        if (recipientUserIds.Count == 0)
        {
            _logger.LogWarning(
                "RealPerformanceNotificationService: no recipients for {EventKey} (tenant {TenantId}); nothing delivered.",
                eventKey, tenantId);
            return;
        }

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
                    "RealPerformanceNotificationService: failed to dispatch {EventKey} to user {UserId}.", eventKey, userId);
            }
        }
    }

    // ── Payload builders + helpers ──────────────────────────────────────────────────────────────────

    private static string BuildGoalPayload(
        string title, string message, EmployeeLite? employee, string cycleName, string goalTitle) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = title,
            ["message"] = message,
            ["employee"] = new Dictionary<string, object?>
            {
                ["firstName"] = employee?.FirstName ?? string.Empty,
                ["lastName"] = employee?.LastName ?? string.Empty,
            },
            ["cycle"] = new Dictionary<string, object?> { ["name"] = cycleName },
            ["goal"] = new Dictionary<string, object?> { ["title"] = goalTitle },
        });

    private static string BuildPerfPayload(
        string title, string message, EmployeeLite? employee, string cycleName,
        Action<Dictionary<string, object?>>? extra = null)
    {
        var data = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["message"] = message,
            ["employee"] = new Dictionary<string, object?>
            {
                ["firstName"] = employee?.FirstName ?? string.Empty,
                ["lastName"] = employee?.LastName ?? string.Empty,
            },
            ["cycle"] = new Dictionary<string, object?> { ["name"] = cycleName },
        };
        extra?.Invoke(data);
        return JsonSerializer.Serialize(data);
    }

    private static string FullName(EmployeeLite? e)
        => e is null ? "An employee" : $"{e.FirstName} {e.LastName}".Trim();

    private bool WarnUnknown(string eventKey, string eventType, Guid subjectId)
    {
        if (!string.IsNullOrEmpty(eventKey)) return false;
        _logger.LogWarning(
            "RealPerformanceNotificationService: unknown event '{EventType}' for {SubjectId} (tenant {TenantId}); " +
            "nothing dispatched.", eventType, subjectId, _tenantContext.TenantId);
        return true;
    }

    private void LogFailure(Exception ex, string eventKey, Guid subjectId)
        => _logger.LogError(ex,
            "RealPerformanceNotificationService: failed to dispatch {EventKey} for {SubjectId} (tenant {TenantId}).",
            eventKey, subjectId, _tenantContext.TenantId);

    private sealed record EmployeeLite(Guid? UserId, string Email, string FirstName, string LastName);
}
