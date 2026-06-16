using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Performance Improvement Plan service (US-PRF-008). Every read/write is tenant-scoped via ITenantContext + the
/// EF global query filter (NFR-2). Enforces: HR-only create/extend/close/escalate (BR-1); one non-terminal PIP
/// per employee (BR-2); ≥30-day duration (BR-3); the immutable acknowledgement + append-only checkpoint/event
/// history (BR-4/FR-5, mirroring the US-PRF-006 ReviewSignoff pattern); and FR-8 visibility (only the employee,
/// their manager, HR, or the assigned mentor can view a PIP). Managers may record checkpoints but cannot
/// close/extend (BR-1). PIP data lives only in its own tables and is intentionally NOT surfaced in the general
/// performance dashboard (BR-5 — the dashboard service does not query these entities).
///
/// ENCRYPTION SEAM (NFR-4): Reason / EscalationNotes / FinalOutcomeNotes are stored as plain text. The codebase
/// has no field/PII encryption mechanism; building one is an infra concern out of scope here (see Pip xmldoc).
/// </summary>
public sealed class PipService : IPipService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;
    private readonly IPipCheckpointScheduler? _scheduler;
    private readonly ILogger<PipService> _logger;

    /// <summary>Minimum PIP duration in days (BR-3).</summary>
    internal const int MinDurationDays = 30;

    public PipService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPerformanceNotificationService notifications,
        ILogger<PipService> logger,
        IPipCheckpointScheduler? scheduler = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _logger = logger;
        _scheduler = scheduler;
    }

    // ── Create (AC-1/AC-2/FR-1, HR-only BR-1) ───────────────────────────

    public async Task<Result<PipDto>> CreateAsync(CreatePipInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PipDto>.Failure("Tenant context is not resolved.", 400);

        // BR-1: only HR (Review.All) can create a PIP.
        if (!_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result<PipDto>.Failure("Only HR can create a Performance Improvement Plan.", 403, "forbidden");

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == input.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<PipDto>.Failure("Employee not found.", 404, "employee_not_found");

        // BR-3: duration must be ≥ 30 days.
        if (input.EndDate.DayNumber - input.StartDate.DayNumber < MinDurationDays)
            return Result<PipDto>.Failure(
                $"PIP duration must be at least {MinDurationDays} days.", 422, "duration_too_short");

        if (input.Objectives.Count == 0)
            return Result<PipDto>.Failure("At least one improvement objective is required.", 422, "objectives_required");

        // BR-2: no second non-terminal PIP for the employee.
        var hasActive = await _dbContext.Pips.AsNoTracking().AnyAsync(
            p => p.EmployeeId == input.EmployeeId
                 && (p.Status == PipStatus.Draft || p.Status == PipStatus.Active || p.Status == PipStatus.Extended),
            cancellationToken);
        if (hasActive)
            return Result<PipDto>.Failure(
                "This employee already has an active Performance Improvement Plan.", 409, "active_pip_exists");

        // Validate optional mentor exists in-tenant (the global filter prevents cross-tenant references).
        if (input.MentorEmployeeId is { } mentorId)
        {
            var mentorExists = await _dbContext.Employees.AsNoTracking().AnyAsync(e => e.Id == mentorId, cancellationToken);
            if (!mentorExists)
                return Result<PipDto>.Failure("Assigned mentor not found.", 404, "mentor_not_found");
        }

        var now = DateTime.UtcNow;
        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        var actorName = SignerDisplayName(actor) ?? _currentUser.Email;

        var pip = new Pip
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = input.EmployeeId,
            ManagerEmployeeId = employee.ReportsToEmployeeId,
            MentorEmployeeId = input.MentorEmployeeId,
            OriginManagerReviewId = input.OriginManagerReviewId,
            Reason = input.Reason.Trim(),
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            ScheduledCheckpointDates = input.CheckpointDates.OrderBy(d => d).ToList(),
            Status = PipStatus.Active, // AC-2: created + initiated in one step.
            EscalationAction = input.EscalationAction,
            InitiatedAt = now,
            AcknowledgementStatus = PipAcknowledgementStatus.Pending,
            IsDeleted = false,
        };

        var order = 0;
        foreach (var o in input.Objectives)
        {
            pip.Objectives.Add(new PipObjective
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                PipId = pip.Id,
                Title = o.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(o.Description) ? null : o.Description.Trim(),
                SuccessCriteria = o.SuccessCriteria.Trim(),
                DueDate = o.DueDate,
                SortOrder = order++,
                AddedAtExtension = false,
                IsDeleted = false,
            });
        }

        AppendEvent(pip, PipEventType.Created, actor, actorName, input.ClientIpAddress, detail: null);
        AppendEvent(pip, PipEventType.Initiated, actor, actorName, input.ClientIpAddress,
            detail: $"Duration {input.StartDate:yyyy-MM-dd} to {input.EndDate:yyyy-MM-dd}");

        _dbContext.Pips.Add(pip);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PIP created. PipId={PipId}, EmployeeId={EmployeeId}, MentorId={MentorId}, Escalation={Escalation}, " +
            "TenantId={TenantId}, By={User}",
            pip.Id, pip.EmployeeId, pip.MentorEmployeeId, pip.EscalationAction, _tenantContext.TenantId, _currentUser.Email);

        // AC-2: notify employee + manager + mentor.
        await _notifications.NotifyPipEventAsync("pip-initiated", pip.Id, pip.EmployeeId, pip.EmployeeId, cancellationToken: cancellationToken);
        if (pip.ManagerEmployeeId is { } mgr)
            await _notifications.NotifyPipEventAsync("pip-initiated", pip.Id, pip.EmployeeId, mgr, cancellationToken: cancellationToken);
        if (pip.MentorEmployeeId is { } men)
            await _notifications.NotifyPipEventAsync("pip-initiated", pip.Id, pip.EmployeeId, men, cancellationToken: cancellationToken);

        // FR-3: schedule Hangfire checkpoint reminders (optional seam — absent in tests).
        _scheduler?.ScheduleCheckpointReminders(_tenantContext.TenantId, pip.Id, input.CheckpointDates);

        return await ReloadAsync(pip.Id, cancellationToken);
    }

    // ── List + Get (FR-8 visibility) ────────────────────────────────────

    public async Task<Result<IReadOnlyList<PipSummaryDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PipSummaryDto>>.Failure("Tenant context is not resolved.", 400);

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        var isHr = _currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll);

        var pips = await _dbContext.Pips.AsNoTracking()
            .Include(p => p.Objectives)
            .Include(p => p.Checkpoints)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        // FR-8/BR-1: HR sees all; otherwise only PIPs where the caller is the employee, the manager or the mentor.
        var visible = isHr
            ? pips
            : pips.Where(p => me is not null &&
                (p.EmployeeId == me.Id || p.ManagerEmployeeId == me.Id || p.MentorEmployeeId == me.Id)).ToList();

        var empIds = visible.Select(p => p.EmployeeId)
            .Concat(visible.Where(p => p.MentorEmployeeId is not null).Select(p => p.MentorEmployeeId!.Value))
            .Distinct().ToList();
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var rows = visible.Select(p =>
        {
            employees.TryGetValue(p.EmployeeId, out var emp);
            string? mentorName = null;
            if (p.MentorEmployeeId is { } mid && employees.TryGetValue(mid, out var men))
                mentorName = FullName(men);
            return new PipSummaryDto
            {
                Id = p.Id,
                EmployeeId = p.EmployeeId,
                EmployeeName = emp is null ? string.Empty : FullName(emp),
                EmployeeNo = emp?.EmployeeNo ?? string.Empty,
                Status = p.Status,
                StatusName = p.Status.ToString(),
                AcknowledgementStatus = p.AcknowledgementStatus,
                AcknowledgementStatusName = p.AcknowledgementStatus.ToString(),
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                MentorName = mentorName,
                ObjectiveCount = p.Objectives.Count(o => !o.IsDeleted),
                CheckpointCount = p.Checkpoints.Count(c => !c.IsDeleted),
            };
        }).ToList();

        return Result<IReadOnlyList<PipSummaryDto>>.Success(rows);
    }

    public async Task<Result<PipDto>> GetAsync(Guid pipId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PipDto>.Failure("Tenant context is not resolved.", 400);

        var pip = await LoadPipAsync(pipId, tracking: false, cancellationToken);
        if (pip is null)
            return Result<PipDto>.Failure("PIP not found.", 404, "pip_not_found");

        var authz = await AuthorizeViewAsync(pip, cancellationToken);
        if (authz.IsFailure)
            return Result<PipDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        return Result<PipDto>.Success(await BuildDtoAsync(pip, cancellationToken));
    }

    // ── Employee acknowledge (BR-4) ─────────────────────────────────────

    public async Task<Result<PipDto>> AcknowledgeAsync(AcknowledgePipInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PipDto>.Failure("Tenant context is not resolved.", 400);

        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        if (actor is null)
            return Result<PipDto>.Failure("The current user is not linked to an employee record.", 403, "no_employee_record");

        var pip = await LoadPipAsync(input.PipId, tracking: true, cancellationToken);
        if (pip is null)
            return Result<PipDto>.Failure("PIP not found.", 404, "pip_not_found");

        // BR-4: only the PIP's own employee acknowledges.
        if (pip.EmployeeId != actor.Id)
            return Result<PipDto>.Failure("Only the employee on the PIP can acknowledge it.", 403, "not_pip_employee");

        if (pip.AcknowledgementStatus == PipAcknowledgementStatus.Acknowledged)
            return Result<PipDto>.Failure("This PIP has already been acknowledged.", 409, "already_acknowledged");

        pip.AcknowledgementStatus = PipAcknowledgementStatus.Acknowledged;
        pip.AcknowledgedAt = DateTime.UtcNow;
        AppendEvent(pip, PipEventType.Acknowledged, actor, FullName(actor), input.ClientIpAddress, detail: null);

        var fail = await SaveAsync(cancellationToken);
        if (fail is not null) return fail;

        _logger.LogInformation("PIP acknowledged. PipId={PipId}, EmployeeId={EmployeeId}, TenantId={TenantId}",
            pip.Id, pip.EmployeeId, _tenantContext.TenantId);

        return await ReloadAsync(pip.Id, cancellationToken);
    }

    // ── Record checkpoint (AC-3/FR-4, manager OR HR) ────────────────────

    public async Task<Result<PipDto>> RecordCheckpointAsync(RecordCheckpointInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PipDto>.Failure("Tenant context is not resolved.", 400);

        var pip = await LoadPipAsync(input.PipId, tracking: true, cancellationToken);
        if (pip is null)
            return Result<PipDto>.Failure("PIP not found.", 404, "pip_not_found");

        // AC-3: the recorder must be the employee's manager OR HR (NOT the employee, NOT an unrelated user).
        var authz = await AuthorizeManagerOrHrAsync(pip, cancellationToken);
        if (authz.IsFailure)
            return Result<PipDto>.Failure(authz.Error!, authz.StatusCode ?? 403, authz.ErrorCode);

        if (pip.Status is not (PipStatus.Active or PipStatus.Extended))
            return Result<PipDto>.Failure("Checkpoints can only be recorded on an active PIP.", 409, "pip_not_active");

        if (string.IsNullOrWhiteSpace(input.EvidenceNotes))
            return Result<PipDto>.Failure("Evidence notes are required.", 422, "evidence_required");

        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var checkpoint = new PipCheckpoint
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            PipId = pip.Id,
            CheckpointDate = input.CheckpointDate,
            ProgressStatus = input.ProgressStatus,
            EvidenceNotes = input.EvidenceNotes.Trim(),
            ReviewerEmployeeId = actor?.Id,
            ReviewerName = SignerDisplayName(actor) ?? _currentUser.Email,
            RecordedAt = now,
            AttachmentStorageKey = input.AttachmentStorageKey,
            AttachmentFileName = input.AttachmentFileName,
            AttachmentContentType = input.AttachmentContentType,
            AttachmentSizeBytes = input.AttachmentSizeBytes,
            IsDeleted = false,
        };
        pip.Checkpoints.Add(checkpoint);
        _dbContext.PipCheckpoints.Add(checkpoint);

        AppendEvent(pip, PipEventType.CheckpointRecorded, actor, checkpoint.ReviewerName, input.ClientIpAddress,
            detail: $"Status: {input.ProgressStatus}");

        var fail = await SaveAsync(cancellationToken);
        if (fail is not null) return fail;

        _logger.LogInformation(
            "PIP checkpoint recorded. PipId={PipId}, Status={Status}, By={User}, TenantId={TenantId}",
            pip.Id, input.ProgressStatus, _currentUser.Email, _tenantContext.TenantId);

        // AC-3: notify the employee of the checkpoint outcome.
        await _notifications.NotifyPipEventAsync("pip-checkpoint-recorded", pip.Id, pip.EmployeeId, pip.EmployeeId,
            input.ProgressStatus.ToString(), cancellationToken);

        return await ReloadAsync(pip.Id, cancellationToken);
    }

    // ── Set final outcome (AC-4, HR-only BR-1) ──────────────────────────

    public async Task<Result<PipDto>> SetOutcomeAsync(SetPipOutcomeInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PipDto>.Failure("Tenant context is not resolved.", 400);

        // BR-1: only HR can close/extend/mark-not-met — a manager cannot.
        if (!_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result<PipDto>.Failure(
                "Only HR can set the final PIP outcome. Managers can record checkpoints but cannot close or extend a PIP.",
                403, "forbidden");

        var pip = await LoadPipAsync(input.PipId, tracking: true, cancellationToken);
        if (pip is null)
            return Result<PipDto>.Failure("PIP not found.", 404, "pip_not_found");

        if (pip.Status is not (PipStatus.Active or PipStatus.Extended))
            return Result<PipDto>.Failure("Only an active or extended PIP can have an outcome set.", 409, "pip_not_active");

        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        var actorName = SignerDisplayName(actor) ?? _currentUser.Email;
        var now = DateTime.UtcNow;
        var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();

        switch (input.Outcome)
        {
            case PipOutcomeKind.SuccessfullyCompleted:
                pip.Status = PipStatus.SuccessfullyCompleted;
                pip.OutcomeSetAt = now;
                pip.FinalOutcomeNotes = notes;
                AppendEvent(pip, PipEventType.CompletedSuccessfully, actor, actorName, input.ClientIpAddress, notes);
                break;

            case PipOutcomeKind.Extended:
                if (input.NewEndDate is not { } newEnd)
                    return Result<PipDto>.Failure("A new end date is required to extend a PIP.", 422, "new_end_date_required");
                // BR-3 applies to the extension too: the extended window must remain ≥30 days from the start.
                if (newEnd.DayNumber - pip.StartDate.DayNumber < MinDurationDays)
                    return Result<PipDto>.Failure(
                        $"The extended PIP duration must be at least {MinDurationDays} days from the start date.",
                        422, "duration_too_short");
                if (newEnd <= pip.EndDate)
                    return Result<PipDto>.Failure("The new end date must be after the current end date.", 422, "new_end_date_invalid");

                pip.Status = PipStatus.Extended;
                pip.EndDate = newEnd;
                // FR-6: optional additional objectives.
                if (input.NewObjectives is { Count: > 0 })
                {
                    var order = pip.Objectives.Count;
                    foreach (var o in input.NewObjectives)
                    {
                        if (string.IsNullOrWhiteSpace(o.Title)) continue;
                        var obj = new PipObjective
                        {
                            Id = BaseEntity.NewUuidV7(),
                            TenantId = _tenantContext.TenantId,
                            PipId = pip.Id,
                            Title = o.Title.Trim(),
                            Description = string.IsNullOrWhiteSpace(o.Description) ? null : o.Description.Trim(),
                            SuccessCriteria = o.SuccessCriteria.Trim(),
                            DueDate = o.DueDate,
                            SortOrder = order++,
                            AddedAtExtension = true,
                            IsDeleted = false,
                        };
                        pip.Objectives.Add(obj);
                        _dbContext.PipObjectives.Add(obj);
                    }
                }
                AppendEvent(pip, PipEventType.Extended, actor, actorName, input.ClientIpAddress,
                    $"New end date {newEnd:yyyy-MM-dd}{(notes is null ? "" : $". {notes}")}");
                break;

            case PipOutcomeKind.NotMet:
                pip.Status = PipStatus.NotMet;
                pip.OutcomeSetAt = now;
                pip.FinalOutcomeNotes = notes;
                AppendEvent(pip, PipEventType.MarkedNotMet, actor, actorName, input.ClientIpAddress, notes);
                break;

            default:
                return Result<PipDto>.Failure("Unknown PIP outcome.", 422, "invalid_outcome");
        }

        var fail = await SaveAsync(cancellationToken);
        if (fail is not null) return fail;

        _logger.LogInformation(
            "PIP outcome set. PipId={PipId}, Outcome={Outcome}, NewStatus={Status}, By={User}, TenantId={TenantId}",
            pip.Id, input.Outcome, pip.Status, _currentUser.Email, _tenantContext.TenantId);

        var ev = input.Outcome switch
        {
            PipOutcomeKind.SuccessfullyCompleted => "pip-completed",
            PipOutcomeKind.Extended => "pip-extended",
            _ => "pip-not-met",
        };
        await _notifications.NotifyPipEventAsync(ev, pip.Id, pip.EmployeeId, pip.EmployeeId, cancellationToken: cancellationToken);

        return await ReloadAsync(pip.Id, cancellationToken);
    }

    // ── Confirm escalation (AC-5/BR-6, HR-only) ─────────────────────────

    public async Task<Result<PipDto>> ConfirmEscalationAsync(ConfirmEscalationInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PipDto>.Failure("Tenant context is not resolved.", 400);

        // BR-1: HR-only.
        if (!_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result<PipDto>.Failure("Only HR can confirm a PIP escalation.", 403, "forbidden");

        var pip = await LoadPipAsync(input.PipId, tracking: true, cancellationToken);
        if (pip is null)
            return Result<PipDto>.Failure("PIP not found.", 404, "pip_not_found");

        // AC-5: escalation only applies to a Not-Met PIP.
        if (pip.Status != PipStatus.NotMet)
            return Result<PipDto>.Failure("Escalation can only be confirmed for a Not-Met PIP.", 409, "pip_not_marked_not_met");

        if (pip.EscalationConfirmed)
            return Result<PipDto>.Failure("The escalation for this PIP has already been confirmed.", 409, "already_escalated");

        if (input.EscalationAction == PipEscalationAction.None)
            return Result<PipDto>.Failure("An escalation action must be selected.", 422, "escalation_action_required");

        var actor = await GetCurrentEmployeeAsync(cancellationToken);
        var actorName = SignerDisplayName(actor) ?? _currentUser.Email;

        pip.EscalationAction = input.EscalationAction;
        pip.EscalationConfirmed = true;
        pip.EscalationConfirmedAt = DateTime.UtcNow;
        pip.EscalationNotes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();

        // FR-5: immutable escalation audit record (the action is recorded in the Detail without the sensitive notes).
        AppendEvent(pip, PipEventType.EscalationConfirmed, actor, actorName, input.ClientIpAddress,
            $"Action: {input.EscalationAction}");

        var fail = await SaveAsync(cancellationToken);
        if (fail is not null) return fail;

        _logger.LogInformation(
            "PIP escalation confirmed. PipId={PipId}, Action={Action}, By={User}, TenantId={TenantId}",
            pip.Id, input.EscalationAction, _currentUser.Email, _tenantContext.TenantId);

        // AC-5: notify stakeholders (employee + manager).
        await _notifications.NotifyPipEventAsync("pip-escalation-confirmed", pip.Id, pip.EmployeeId, pip.EmployeeId,
            input.EscalationAction.ToString(), cancellationToken);
        if (pip.ManagerEmployeeId is { } mgr)
            await _notifications.NotifyPipEventAsync("pip-escalation-confirmed", pip.Id, pip.EmployeeId, mgr,
                input.EscalationAction.ToString(), cancellationToken);

        return await ReloadAsync(pip.Id, cancellationToken);
    }

    // ── Authorization (FR-8/BR-1) ───────────────────────────────────────

    /// <summary>
    /// FR-8 visibility: a PIP is viewable only by the employee, their manager, HR (Review.All) or the assigned
    /// mentor. Anyone else → 403. The tenant global filter already prevents cross-tenant reads (NFR-2).
    /// </summary>
    private async Task<Result> AuthorizeViewAsync(Pip pip, CancellationToken cancellationToken)
    {
        if (_currentUser.Permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result.Success();

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        if (me is null)
            return Result.Failure("You do not have permission to view this PIP.", 403, "pip_forbidden");

        if (pip.EmployeeId == me.Id || pip.ManagerEmployeeId == me.Id || pip.MentorEmployeeId == me.Id)
            return Result.Success();

        return Result.Failure("You do not have permission to view this PIP.", 403, "pip_forbidden");
    }

    /// <summary>
    /// AC-3 recorder authorization: HR (Review.All) for any PIP, or a manager (Review.Team) who is the PIP's
    /// manager. The reviewed employee and unrelated users are refused.
    /// </summary>
    private async Task<Result> AuthorizeManagerOrHrAsync(Pip pip, CancellationToken cancellationToken)
    {
        var permissions = _currentUser.Permissions;

        if (permissions.Contains(PermissionCatalog.Performance.ReviewAll))
            return Result.Success();

        if (!permissions.Contains(PermissionCatalog.Performance.ReviewTeam))
            return Result.Failure("You do not have permission to record a PIP checkpoint.", 403, "forbidden");

        var me = await GetCurrentEmployeeAsync(cancellationToken);
        if (me is null)
            return Result.Failure("The current user is not linked to an employee record.", 403, "no_employee_record");

        if (pip.ManagerEmployeeId != me.Id)
            return Result.Failure("You can only record checkpoints for your direct reports.", 403, "not_direct_report");

        return Result.Success();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private Task<Pip?> LoadPipAsync(Guid pipId, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<Pip> query = _dbContext.Pips
            .Include(p => p.Objectives)
            .Include(p => p.Checkpoints)
            .Include(p => p.Events);
        if (!tracking)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(p => p.Id == pipId, cancellationToken);
    }

    private async Task<Result<PipDto>> ReloadAsync(Guid pipId, CancellationToken cancellationToken)
    {
        var pip = await LoadPipAsync(pipId, tracking: false, cancellationToken);
        if (pip is null)
            return Result<PipDto>.Failure("PIP not found.", 404, "pip_not_found");
        return Result<PipDto>.Success(await BuildDtoAsync(pip, cancellationToken));
    }

    private void AppendEvent(
        Pip pip, PipEventType type, Employee? actor, string actorName, string? clientIp, string? detail)
    {
        var ev = new PipEvent
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            PipId = pip.Id,
            EventType = type,
            ActorUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            ActorEmployeeId = actor?.Id,
            ActorName = actorName,
            OccurredAt = DateTime.UtcNow,
            ClientIpAddress = clientIp,
            Detail = detail,
            IsDeleted = false,
        };
        pip.Events.Add(ev);
        // For tracked-create the cascade adds it; for tracked-update we add explicitly so it's inserted.
        if (_dbContext.Entry(pip).State != EntityState.Added)
            _dbContext.PipEvents.Add(ev);
    }

    private async Task<Result<PipDto>?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PipDto>.Failure(
                "This PIP was modified by another session. Reload and try again.", 409, "concurrency_conflict");
        }
    }

    private async Task<PipDto> BuildDtoAsync(Pip pip, CancellationToken cancellationToken)
    {
        var ids = new List<Guid> { pip.EmployeeId };
        if (pip.ManagerEmployeeId is { } m) ids.Add(m);
        if (pip.MentorEmployeeId is { } men) ids.Add(men);
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        employees.TryGetValue(pip.EmployeeId, out var emp);
        string? managerName = pip.ManagerEmployeeId is { } mid && employees.TryGetValue(mid, out var mgr) ? FullName(mgr) : null;
        string? mentorName = pip.MentorEmployeeId is { } meId && employees.TryGetValue(meId, out var menEmp) ? FullName(menEmp) : null;

        return new PipDto
        {
            Id = pip.Id,
            EmployeeId = pip.EmployeeId,
            EmployeeName = emp is null ? string.Empty : FullName(emp),
            EmployeeNo = emp?.EmployeeNo ?? string.Empty,
            ManagerEmployeeId = pip.ManagerEmployeeId,
            ManagerName = managerName,
            MentorEmployeeId = pip.MentorEmployeeId,
            MentorName = mentorName,
            OriginManagerReviewId = pip.OriginManagerReviewId,
            Reason = pip.Reason,
            StartDate = pip.StartDate,
            EndDate = pip.EndDate,
            Status = pip.Status,
            StatusName = pip.Status.ToString(),
            EscalationAction = pip.EscalationAction,
            EscalationActionName = pip.EscalationAction.ToString(),
            InitiatedAt = pip.InitiatedAt,
            AcknowledgementStatus = pip.AcknowledgementStatus,
            AcknowledgementStatusName = pip.AcknowledgementStatus.ToString(),
            AcknowledgedAt = pip.AcknowledgedAt,
            OutcomeSetAt = pip.OutcomeSetAt,
            FinalOutcomeNotes = pip.FinalOutcomeNotes,
            EscalationConfirmed = pip.EscalationConfirmed,
            EscalationConfirmedAt = pip.EscalationConfirmedAt,
            EscalationNotes = pip.EscalationNotes,
            Objectives = pip.Objectives.Where(o => !o.IsDeleted).OrderBy(o => o.SortOrder)
                .Select(o => new PipObjectiveDto
                {
                    Id = o.Id,
                    Title = o.Title,
                    Description = o.Description,
                    SuccessCriteria = o.SuccessCriteria,
                    DueDate = o.DueDate,
                    SortOrder = o.SortOrder,
                    AddedAtExtension = o.AddedAtExtension,
                }).ToList(),
            Checkpoints = pip.Checkpoints.Where(c => !c.IsDeleted).OrderBy(c => c.RecordedAt)
                .Select(c => new PipCheckpointDto
                {
                    Id = c.Id,
                    CheckpointDate = c.CheckpointDate,
                    ProgressStatus = c.ProgressStatus,
                    ProgressStatusName = c.ProgressStatus.ToString(),
                    EvidenceNotes = c.EvidenceNotes,
                    ReviewerEmployeeId = c.ReviewerEmployeeId,
                    ReviewerName = c.ReviewerName,
                    RecordedAt = c.RecordedAt,
                    AttachmentFileName = c.AttachmentFileName,
                    AttachmentSizeBytes = c.AttachmentSizeBytes,
                }).ToList(),
            Events = pip.Events.Where(e => !e.IsDeleted).OrderBy(e => e.OccurredAt)
                .Select(e => new PipEventDto
                {
                    Id = e.Id,
                    EventType = e.EventType,
                    EventTypeName = e.EventType.ToString(),
                    ActorName = e.ActorName,
                    OccurredAt = e.OccurredAt,
                    Detail = e.Detail,
                }).ToList(),
        };
    }

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    private static string FullName(Employee e) => $"{e.FirstName} {e.LastName}".Trim();
    private static string? SignerDisplayName(Employee? e) => e is null ? null : FullName(e);
}
