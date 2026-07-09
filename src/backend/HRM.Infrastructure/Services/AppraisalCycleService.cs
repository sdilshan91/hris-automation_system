using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Appraisal cycle management service (US-PRF-004). Every query is tenant-scoped via ITenantContext + the EF
/// global query filter (NFR-2). It owns the domain rules the FluentValidation layer cannot check from a single
/// command's fields: participant scoping (FR-3), BR-4 (no two ACTIVE same-type cycles per employee), the FR-7
/// status state machine, BR-5 (rating-scale locks on Active), BR-6 (cancel needs a reason + notifies), and
/// BR-2 (delete only a Draft with no review activity). The GoalSetting/SelfAssessment/ManagerReview phases are
/// kept in sync with the cycle's legacy window columns, so US-PRF-001/002/003 windows are now phase-driven.
/// Hangfire scheduling (FR-5) is an OPTIONAL seam (<see cref="ICyclePhaseScheduler"/>) — absent in tests.
/// </summary>
public sealed class AppraisalCycleService : IAppraisalCycleService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ICyclePhaseScheduler? _scheduler;
    private readonly ILogger<AppraisalCycleService> _logger;

    public AppraisalCycleService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPerformanceNotificationService notifications,
        ILogger<AppraisalCycleService> logger,
        ICyclePhaseScheduler? scheduler = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _notifications = notifications;
        _logger = logger;
        _scheduler = scheduler;
    }

    // ── Create (AC-1/AC-2) ───────────────────────────────────────────────────

    public async Task<Result<CycleDto>> CreateAsync(CreateCycleInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CycleDto>.Failure("Tenant context is not resolved.", 400);

        // FR-3: resolve participants from the scope (snapshot).
        var participantResult = await ResolveParticipantsAsync(input.Scope, cancellationToken);
        if (participantResult.IsFailure)
            return Result<CycleDto>.Failure(participantResult.Error!, participantResult.StatusCode ?? 400, participantResult.ErrorCode);
        var employeeIds = participantResult.Value!;

        // BR-4: none of the resolved employees may already be in an ACTIVE cycle of the same type.
        var conflict = await FindActiveSameTypeConflictAsync(input.Type, employeeIds, excludeCycleId: null, cancellationToken);
        if (conflict is not null)
            return Result<CycleDto>.Failure(
                $"Employee {conflict} is already a participant in an active {input.Type} cycle.", 409, "active_same_type_conflict");

        var cycleId = BaseEntity.NewUuidV7();
        var cycle = new AppraisalCycle
        {
            Id = cycleId,
            TenantId = _tenantContext.TenantId,
            Name = input.Name.Trim(),
            Type = input.Type,
            Status = AppraisalCycleStatus.Draft,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            RatingScaleMax = input.RatingScaleMax,
            SelfWeightPercent = input.SelfWeightPercent,
            Is360Enabled = input.Is360Enabled,
            IsCalibrationEnabled = input.IsCalibrationEnabled,
            IsAnonymousFeedback = input.IsAnonymousFeedback,
            ParticipantScope = input.Scope.ScopeType,
            // BUG-260: persist the department selection (prefill only) whenever the scope is Departments; clear it
            // otherwise. This lives alongside the resolved participant snapshot and does not affect resolution.
            ScopeDepartmentIds = input.Scope.ScopeType == ParticipantScopeType.Departments
                ? (input.Scope.DepartmentIds ?? []).Distinct().ToList()
                : [],
            Phases = BuildPhases(cycleId, input.Phases),
            IsDeleted = false,
        };
        cycle.SyncLegacyWindowsFromPhases();

        cycle.Participants = employeeIds.Select(eid => new CycleParticipant
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            CycleId = cycleId,
            EmployeeId = eid,
            IsDeleted = false,
        }).ToList();

        _dbContext.AppraisalCycles.Add(cycle);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-5/AC-2: schedule phase-transition + reminder jobs (no-op when the scheduler seam is absent).
        _scheduler?.ScheduleCycleJobs(_tenantContext.TenantId, cycleId);

        _logger.LogInformation(
            "Appraisal cycle created. Id={CycleId}, Type={Type}, Participants={Count}, TenantId={TenantId}, By={User}",
            cycleId, input.Type, employeeIds.Count, _tenantContext.TenantId, _currentUser.Email);

        return Result<CycleDto>.Success(ToDto(cycle, employeeIds.Count, employeeIds));
    }

    // ── Update / extend phase (AC-5) ─────────────────────────────────────────

    public async Task<Result<CycleDto>> UpdateAsync(Guid cycleId, UpdateCycleInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CycleDto>.Failure("Tenant context is not resolved.", 400);

        var cycle = await _dbContext.AppraisalCycles
            .Include(c => c.Phases)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<CycleDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        if (cycle.IsTerminal)
            return Result<CycleDto>.Failure(
                $"A {cycle.Status} cycle can no longer be edited.", 409, "cycle_terminal");

        // ISSUE-118 / BR-5: the 360 anonymity flag freezes once any 360 feedback has been submitted for this
        // cycle — flipping it would retroactively de-anonymize (or expose) already-submitted feedback. The check
        // is tenant-scoped via the EF global query filter on Feedback360. Other cycle fields stay editable.
        if (input.IsAnonymousFeedback != cycle.IsAnonymousFeedback)
        {
            var hasFeedback = await _dbContext.Feedback360s
                .AnyAsync(f => f.CycleId == cycle.Id, cancellationToken);
            if (hasFeedback)
                return Result<CycleDto>.Failure(
                    "Anonymity cannot be changed after feedback has been submitted.", 409, "anonymity_locked");
        }

        // BUG-261: the participant scope is editable — but ONLY while the cycle is still in Draft. Once it leaves
        // Draft, review activity (Feedback360 / SelfAssessment / ManagerReview / Goal rows keyed by
        // CycleId+EmployeeId, NOT by CycleParticipant.Id) may exist and re-resolving participants would orphan it.
        // So a scope change is Draft-only — mirroring the BR-5 scale lock and the anonymity freeze above. A scope
        // change on a non-Draft cycle is rejected (scope_locked); an unchanged (or omitted) scope is a no-op so we
        // never churn the participant set. When the scope differs we re-resolve the set BEFORE any mutation, so a
        // resolution/BR-4 failure short-circuits cleanly.
        List<Guid>? rescopedParticipantIds = null;
        if (input.Scope is not null && await ScopeDiffersAsync(input.Scope, cycle, cancellationToken))
        {
            if (cycle.Status != AppraisalCycleStatus.Draft)
                return Result<CycleDto>.Failure(
                    "The participant scope cannot be changed once the cycle leaves Draft.", 409, "scope_locked");

            // FR-3: re-resolve the participant set from the new scope (snapshot).
            var rescoped = await ResolveParticipantsAsync(input.Scope, cancellationToken);
            if (rescoped.IsFailure)
                return Result<CycleDto>.Failure(rescoped.Error!, rescoped.StatusCode ?? 400, rescoped.ErrorCode);
            rescopedParticipantIds = rescoped.Value!;

            // BR-4: none of the newly-resolved employees may already be in an ACTIVE cycle of the same type — but
            // EXCLUDE this cycle so its own (about-to-be-replaced) participants can't falsely self-conflict.
            var conflict = await FindActiveSameTypeConflictAsync(
                cycle.Type, rescopedParticipantIds, excludeCycleId: cycle.Id, cancellationToken);
            if (conflict is not null)
                return Result<CycleDto>.Failure(
                    $"Employee {conflict} is already a participant in an active {cycle.Type} cycle.",
                    409, "active_same_type_conflict");
        }

        cycle.Name = input.Name.Trim();
        cycle.StartDate = input.StartDate;
        cycle.EndDate = input.EndDate;
        cycle.Is360Enabled = input.Is360Enabled;
        cycle.IsCalibrationEnabled = input.IsCalibrationEnabled;
        cycle.IsAnonymousFeedback = input.IsAnonymousFeedback;

        // BR-5: the rating scale (and self/manager weights) lock once the cycle is Active.
        if (cycle.Status == AppraisalCycleStatus.Draft)
        {
            if (input.RatingScaleMax.HasValue) cycle.RatingScaleMax = input.RatingScaleMax.Value;
            if (input.SelfWeightPercent.HasValue) cycle.SelfWeightPercent = input.SelfWeightPercent.Value;
        }
        else if ((input.RatingScaleMax.HasValue && input.RatingScaleMax.Value != cycle.RatingScaleMax) ||
                 (input.SelfWeightPercent.HasValue && input.SelfWeightPercent.Value != cycle.SelfWeightPercent))
        {
            return Result<CycleDto>.Failure(
                "The rating scale and weights are locked once the cycle is active.", 409, "rating_scale_locked");
        }

        // Replace the phase set (re-validated by the validator before this point). Remove the old phase rows
        // and add the new ones via the DbSet WITHOUT reassigning/clearing the tracked navigation collection —
        // mutating cycle.Phases leaves the just-deleted children referenced by the parent relationship, and EF's
        // cascade fixup then flips a deleted child into a modified state on save (which surfaces as a
        // DbUpdateConcurrencyException). This mirrors the ScorecardService child-replace pattern. The newly
        // Added phases fix up into cycle.Phases automatically via their CycleId FK.
        _dbContext.CyclePhases.RemoveRange(cycle.Phases.ToList());
        var newPhases = BuildPhases(cycle.Id, input.Phases);
        _dbContext.CyclePhases.AddRange(newPhases);

        // Keep the legacy window columns in sync off the freshly built phases (US-PRF-001/002/003 windows).
        cycle.SyncLegacyWindowsFromPhases(newPhases);

        // BUG-261: apply the re-resolved scope. This only runs in Draft (guarded above), so there is no review
        // activity keyed to these participants by construction ⇒ it is safe to REPLACE the participant set. Use
        // the same replace-set pattern as the phases (remove the tracked rows via the DbSet, add the new ones by
        // CycleId FK) WITHOUT touching the cycle.Participants navigation, so EF's cascade fixup can't resurrect a
        // deleted child as a modified row on save.
        if (rescopedParticipantIds is not null)
        {
            cycle.ParticipantScope = input.Scope!.ScopeType;
            // Mirror create (:84-86): persist the department selection only for a Departments scope, distinct.
            cycle.ScopeDepartmentIds = input.Scope.ScopeType == ParticipantScopeType.Departments
                ? (input.Scope.DepartmentIds ?? []).Distinct().ToList()
                : [];

            var oldParticipants = await _dbContext.CycleParticipants
                .Where(p => p.CycleId == cycle.Id)
                .ToListAsync(cancellationToken);
            _dbContext.CycleParticipants.RemoveRange(oldParticipants);
            _dbContext.CycleParticipants.AddRange(rescopedParticipantIds.Select(eid => new CycleParticipant
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                CycleId = cycle.Id,
                EmployeeId = eid,
                IsDeleted = false,
            }));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AC-5: reschedule jobs + notify affected participants.
        _scheduler?.ScheduleCycleJobs(_tenantContext.TenantId, cycle.Id);
        await NotifyParticipantsAsync(cycle.Id, "cycle-updated", "phase dates changed", cancellationToken);

        // BUG-260/BUG-261: reload the resolved participant ids (not just a count) so the returned DTO's Scope
        // reflects the post-update set — either the preserved snapshot (no scope change) or the newly re-resolved
        // participants (Draft-only scope edit above). AsNoTracking read after SaveChanges ⇒ authoritative.
        var participantEmployeeIds = await _dbContext.CycleParticipants
            .AsNoTracking()
            .Where(p => p.CycleId == cycle.Id)
            .Select(p => p.EmployeeId)
            .ToListAsync(cancellationToken);
        _logger.LogInformation(
            "Appraisal cycle updated. Id={CycleId}, TenantId={TenantId}, By={User}",
            cycle.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result<CycleDto>.Success(ToDto(cycle, participantEmployeeIds.Count, participantEmployeeIds));
    }

    // ── Clone (FR-8) ─────────────────────────────────────────────────────────

    public async Task<Result<CycleDto>> CloneAsync(CloneCycleInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CycleDto>.Failure("Tenant context is not resolved.", 400);

        var source = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .Include(c => c.Phases)
            .FirstOrDefaultAsync(c => c.Id == input.SourceCycleId, cancellationToken);
        if (source is null)
            return Result<CycleDto>.Failure("Source cycle not found.", 404, "cycle_not_found");

        // Shift the phase windows proportionally into the new cycle window (FR-8: copy settings, new dates).
        var srcSpan = (source.EndDate - source.StartDate).Ticks;
        var newSpan = (input.EndDate - input.StartDate).Ticks;

        DateTime Shift(DateTime original)
        {
            if (srcSpan <= 0) return input.StartDate;
            var fraction = (double)(original - source.StartDate).Ticks / srcSpan;
            return input.StartDate.AddTicks((long)(fraction * newSpan));
        }

        var newCycleId = BaseEntity.NewUuidV7();
        var clone = new AppraisalCycle
        {
            Id = newCycleId,
            TenantId = _tenantContext.TenantId,
            Name = input.Name.Trim(),
            Type = source.Type,
            Status = AppraisalCycleStatus.Draft, // a clone always starts as a Draft template.
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            RatingScaleMax = source.RatingScaleMax,
            SelfWeightPercent = source.SelfWeightPercent,
            Is360Enabled = source.Is360Enabled,
            IsCalibrationEnabled = source.IsCalibrationEnabled,
            IsAnonymousFeedback = source.IsAnonymousFeedback,
            ParticipantScope = source.ParticipantScope,
            // BUG-260: a clone copies the department selection as a SETTING (participants are re-resolved on edit).
            ScopeDepartmentIds = source.ScopeDepartmentIds.ToList(),
            IsDeleted = false,
            Phases = source.Phases
                .OrderBy(p => p.Sequence)
                .Select((p, i) => new CyclePhase
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    CycleId = newCycleId,
                    PhaseType = p.PhaseType,
                    Sequence = i,
                    StartDate = Shift(p.StartDate),
                    EndDate = Shift(p.EndDate),
                    IsDeleted = false,
                }).ToList(),
        };
        clone.SyncLegacyWindowsFromPhases();

        // A clone copies SETTINGS only, not the participant snapshot — HR re-resolves participants on the new
        // period (the FR-8 "template" intent). Participants stay empty until the cycle is activated/edited.
        _dbContext.AppraisalCycles.Add(clone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Appraisal cycle cloned. SourceId={SourceId}, NewId={NewId}, TenantId={TenantId}, By={User}",
            source.Id, newCycleId, _tenantContext.TenantId, _currentUser.Email);

        return Result<CycleDto>.Success(ToDto(clone, participantCount: 0));
    }

    // ── Status transition (FR-7) ─────────────────────────────────────────────

    public async Task<Result<CycleDto>> TransitionStatusAsync(
        Guid cycleId, AppraisalCycleStatus targetStatus, string? reason, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CycleDto>.Failure("Tenant context is not resolved.", 400);

        var cycle = await _dbContext.AppraisalCycles
            .Include(c => c.Phases)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<CycleDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        if (!AppraisalCycle.IsValidTransition(cycle.Status, targetStatus))
            return Result<CycleDto>.Failure(
                $"Cannot transition a {cycle.Status} cycle to {targetStatus}.", 409, "invalid_status_transition");

        // BR-6: cancellation requires a reason.
        if (targetStatus == AppraisalCycleStatus.Cancelled && string.IsNullOrWhiteSpace(reason))
            return Result<CycleDto>.Failure("A cancellation reason is required.", 422, "cancellation_reason_required");

        cycle.Status = targetStatus;
        if (targetStatus == AppraisalCycleStatus.Cancelled)
            cycle.CancellationReason = reason!.Trim();

        // ISSUE-254: stamp the real "final ratings published" timestamp the first time the cycle completes. Only
        // set it when currently null so a (defensive) re-entry can never overwrite the original publish time.
        if (targetStatus == AppraisalCycleStatus.Completed && cycle.RatingsPublishedOn is null)
            cycle.RatingsPublishedOn = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // FR-5/FR-7: (re)schedule on Active/resume; cancel scheduled jobs on terminal/paused states.
        if (targetStatus == AppraisalCycleStatus.Active)
            _scheduler?.ScheduleCycleJobs(_tenantContext.TenantId, cycle.Id);
        else
            _scheduler?.CancelCycleJobs(cycle.Id);

        // BR-6: cancellation notifies all participants.
        if (targetStatus == AppraisalCycleStatus.Cancelled)
            await NotifyParticipantsAsync(cycle.Id, "cycle-cancelled", reason!.Trim(), cancellationToken);

        // BUG-260: load the resolved participant ids so the returned DTO's Scope is populated.
        var participantEmployeeIds = await _dbContext.CycleParticipants
            .AsNoTracking()
            .Where(p => p.CycleId == cycle.Id)
            .Select(p => p.EmployeeId)
            .ToListAsync(cancellationToken);
        _logger.LogInformation(
            "Appraisal cycle status changed. Id={CycleId}, To={Status}, TenantId={TenantId}, By={User}",
            cycle.Id, targetStatus, _tenantContext.TenantId, _currentUser.Email);

        return Result<CycleDto>.Success(ToDto(cycle, participantEmployeeIds.Count, participantEmployeeIds));
    }

    // ── Delete (BR-2) ────────────────────────────────────────────────────────

    public async Task<Result> DeleteAsync(Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var cycle = await _dbContext.AppraisalCycles
            .Include(c => c.Phases)
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        // BR-2: cannot delete if any review activity exists — cancel only.
        var hasReviewActivity =
            await _dbContext.SelfAssessments.AnyAsync(s => s.CycleId == cycleId, cancellationToken) ||
            await _dbContext.ManagerReviews.AnyAsync(m => m.CycleId == cycleId, cancellationToken);
        if (hasReviewActivity)
            return Result.Failure(
                "This cycle has review activity and cannot be deleted; cancel it instead.", 409, "cycle_has_reviews");

        // Only a Draft cycle can be deleted; anything active/terminal must be cancelled.
        if (cycle.Status != AppraisalCycleStatus.Draft)
            return Result.Failure(
                "Only a draft cycle can be deleted; cancel an active or completed cycle instead.", 409, "cycle_not_draft");

        cycle.IsDeleted = true;
        foreach (var p in cycle.Phases) p.IsDeleted = true;
        foreach (var p in cycle.Participants) p.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _scheduler?.CancelCycleJobs(cycle.Id);

        _logger.LogInformation(
            "Appraisal cycle deleted (soft). Id={CycleId}, TenantId={TenantId}, By={User}",
            cycle.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    // ── Reads ────────────────────────────────────────────────────────────────

    public async Task<Result<CycleDto>> GetByIdAsync(Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CycleDto>.Failure("Tenant context is not resolved.", 400);

        var cycle = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .Include(c => c.Phases)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<CycleDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        // BUG-260: load the resolved participant ids (not just a count) so the DTO's Scope.EmployeeIds is
        // populated for the edit-form prefill. Tenant-scoped via the global query filter.
        var participantEmployeeIds = await _dbContext.CycleParticipants
            .AsNoTracking()
            .Where(p => p.CycleId == cycleId)
            .Select(p => p.EmployeeId)
            .ToListAsync(cancellationToken);
        return Result<CycleDto>.Success(ToDto(cycle, participantEmployeeIds.Count, participantEmployeeIds));
    }

    public async Task<Result<IReadOnlyList<CycleSummaryDto>>> ListAsync(
        AppraisalCycleStatus? status, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<CycleSummaryDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.AppraisalCycles.AsNoTracking().AsQueryable();
        if (status.HasValue)
            query = query.Where(c => c.Status == status.Value);

        var cycles = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Name, c.Type, c.Status, c.StartDate, c.EndDate, c.RatingsPublishedOn })
            .ToListAsync(cancellationToken);

        var counts = await _dbContext.CycleParticipants
            .AsNoTracking()
            .GroupBy(p => p.CycleId)
            .Select(g => new { CycleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CycleId, x => x.Count, cancellationToken);

        var summaries = cycles.Select(c => new CycleSummaryDto
        {
            Id = c.Id,
            Name = c.Name,
            Type = c.Type,
            TypeName = c.Type.ToString(),
            Status = c.Status,
            StatusName = c.Status.ToString(),
            StartDate = c.StartDate,
            EndDate = c.EndDate,
            ParticipantCount = counts.GetValueOrDefault(c.Id),
            RatingsPublishedOn = c.RatingsPublishedOn,
        }).ToList();

        return Result<IReadOnlyList<CycleSummaryDto>>.Success(summaries);
    }

    public async Task<Result<ActiveCycleDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ActiveCycleDto>.Failure("Tenant context is not resolved.", 400);

        var now = DateTime.UtcNow;

        // The canonical active cycle: most recently started Active cycle (NFR — there is at most one per type,
        // but multiple types may be active; the most recent start wins for the single-active contract).
        var cycle = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .Include(c => c.Phases)
            .Where(c => c.Status == AppraisalCycleStatus.Active)
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (cycle is null)
            return Result<ActiveCycleDto>.Failure("No active appraisal cycle.", 404, "no_active_cycle");

        var current = cycle.Phases.FirstOrDefault(p => p.ContainsInstant(now));

        return Result<ActiveCycleDto>.Success(new ActiveCycleDto
        {
            Id = cycle.Id,
            Name = cycle.Name,
            Type = cycle.Type,
            TypeName = cycle.Type.ToString(),
            Status = cycle.Status,
            StatusName = cycle.Status.ToString(),
            StartDate = cycle.StartDate,
            EndDate = cycle.EndDate,
            RatingScaleMax = cycle.RatingScaleMax,
            SelfWeightPercent = cycle.SelfWeightPercent,
            ManagerWeightPercent = cycle.ManagerWeightPercent,
            Is360Enabled = cycle.Is360Enabled,
            CurrentPhase = current?.PhaseType,
            CurrentPhaseName = current?.PhaseType.ToString(),
            GoalSettingOpen = cycle.IsGoalSettingOpen(now),
            SelfAssessmentOpen = cycle.IsSelfAssessmentOpen(now),
            ManagerReviewOpen = cycle.IsManagerReviewOpen(now),
            Phases = cycle.Phases.OrderBy(p => p.Sequence).Select(p => ToPhaseDto(p, now)).ToList(),
        });
    }

    public async Task<Result<CycleDashboardDto>> GetDashboardAsync(Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CycleDashboardDto>.Failure("Tenant context is not resolved.", 400);

        var now = DateTime.UtcNow;

        var cycle = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .Include(c => c.Phases)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<CycleDashboardDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        var participantIds = await _dbContext.CycleParticipants
            .AsNoTracking()
            .Where(p => p.CycleId == cycleId)
            .Select(p => p.EmployeeId)
            .ToListAsync(cancellationToken);
        var total = participantIds.Count;

        // Completion sets per phase (AC-3): goal-setting = has ≥1 goal; self-assessment = submitted;
        // manager-review = submitted. Calibration/Publish have no per-participant signal yet → 0 completed.
        var goalDoneIds = await _dbContext.Goals
            .AsNoTracking()
            .Where(g => g.CycleId == cycleId)
            .Select(g => g.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var selfDoneIds = await _dbContext.SelfAssessments
            .AsNoTracking()
            .Where(s => s.CycleId == cycleId && s.Status == SelfAssessmentStatus.Submitted)
            .Select(s => s.EmployeeId)
            .ToListAsync(cancellationToken);

        var mgrDoneIds = await _dbContext.ManagerReviews
            .AsNoTracking()
            .Where(m => m.CycleId == cycleId && m.Status == ManagerReviewStatus.Submitted)
            .Select(m => m.EmployeeId)
            .ToListAsync(cancellationToken);

        int CompletedFor(CyclePhaseType type) => type switch
        {
            CyclePhaseType.GoalSetting => goalDoneIds.Intersect(participantIds).Count(),
            CyclePhaseType.SelfAssessment => selfDoneIds.Intersect(participantIds).Count(),
            CyclePhaseType.ManagerReview => mgrDoneIds.Intersect(participantIds).Count(),
            _ => 0,
        };

        var phaseStats = cycle.Phases.OrderBy(p => p.Sequence).Select(p =>
        {
            var completed = CompletedFor(p.PhaseType);
            var pct = total == 0 ? 0 : (int)Math.Round(completed * 100.0 / total);
            // Overdue: phase end has passed and the participant hasn't completed it (only meaningful for
            // the three measurable phases).
            var overdue = p.EndDate < now && p.PhaseType is CyclePhaseType.GoalSetting
                or CyclePhaseType.SelfAssessment or CyclePhaseType.ManagerReview
                ? Math.Max(0, total - completed)
                : 0;

            return new PhaseCompletionDto
            {
                PhaseType = p.PhaseType,
                PhaseTypeName = p.PhaseType.ToString(),
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                IsCurrent = p.ContainsInstant(now),
                TotalParticipants = total,
                CompletedCount = completed,
                CompletionPercent = pct,
                OverdueCount = overdue,
            };
        }).ToList();

        var currentPhase = cycle.Phases.FirstOrDefault(p => p.ContainsInstant(now));

        return Result<CycleDashboardDto>.Success(new CycleDashboardDto
        {
            CycleId = cycle.Id,
            Name = cycle.Name,
            Status = cycle.Status,
            StatusName = cycle.Status.ToString(),
            StartDate = cycle.StartDate,
            EndDate = cycle.EndDate,
            ParticipantCount = total,
            CurrentPhase = currentPhase?.PhaseType,
            CurrentPhaseName = currentPhase?.PhaseType.ToString(),
            Phases = phaseStats,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private List<CyclePhase> BuildPhases(Guid cycleId, IReadOnlyList<CyclePhaseInput> phases)
        => phases
            .OrderBy(p => p.StartDate)
            .Select((p, i) => new CyclePhase
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                CycleId = cycleId,
                PhaseType = p.PhaseType,
                Sequence = i,
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                IsDeleted = false,
            }).ToList();

    /// <summary>FR-3: resolve the participant employee-id set from the chosen scope (tenant-scoped).</summary>
    private async Task<Result<List<Guid>>> ResolveParticipantsAsync(
        ParticipantScopeInput scope, CancellationToken cancellationToken)
    {
        switch (scope.ScopeType)
        {
            case ParticipantScopeType.AllEmployees:
            {
                var ids = await _dbContext.Employees
                    .AsNoTracking()
                    .Where(e => e.Status == EmployeeStatus.Active)
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken);
                return Result<List<Guid>>.Success(ids);
            }
            case ParticipantScopeType.Departments:
            {
                var deptIds = scope.DepartmentIds ?? [];
                var ids = await _dbContext.Employees
                    .AsNoTracking()
                    .Where(e => e.Status == EmployeeStatus.Active && deptIds.Contains(e.DepartmentId))
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken);
                return Result<List<Guid>>.Success(ids);
            }
            case ParticipantScopeType.CustomList:
            {
                var requested = (scope.EmployeeIds ?? []).Distinct().ToList();
                // Validate every requested employee exists in-tenant (the global filter scopes the query),
                // rejecting cross-department/cross-tenant ids the caller doesn't own.
                var existing = await _dbContext.Employees
                    .AsNoTracking()
                    .Where(e => requested.Contains(e.Id))
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken);
                var missing = requested.Except(existing).ToList();
                if (missing.Count > 0)
                    return Result<List<Guid>>.Failure(
                        "One or more selected employees do not exist in this tenant.", 422, "invalid_participant");
                return Result<List<Guid>>.Success(existing);
            }
            case ParticipantScopeType.Grades:
                // SEAM: Core HR has no grade/band field yet (see vault). Resolve to an empty set rather than
                // failing, so the contract is honored; HR uses CustomList until US-CHR adds grades.
                return Result<List<Guid>>.Success([]);
            default:
                return Result<List<Guid>>.Failure("Unsupported participant scope.", 400, "invalid_scope");
        }
    }

    /// <summary>
    /// BUG-261: true when the supplied edit scope actually differs from the cycle's persisted scope (so the
    /// participant set needs re-resolving). A different scope TYPE always differs. Within the same type: a
    /// Departments scope compares the (distinct) department-id selection; a CustomList scope compares the
    /// (distinct) requested employee-ids against the currently-persisted participant set. AllEmployees / Grades
    /// carry no id selection, so the same type ⇒ unchanged (no churn).
    /// </summary>
    private async Task<bool> ScopeDiffersAsync(
        ParticipantScopeInput scope, AppraisalCycle cycle, CancellationToken cancellationToken)
    {
        if (scope.ScopeType != cycle.ParticipantScope)
            return true;

        switch (scope.ScopeType)
        {
            case ParticipantScopeType.Departments:
            {
                var requested = (scope.DepartmentIds ?? []).Distinct().OrderBy(x => x);
                var persisted = cycle.ScopeDepartmentIds.Distinct().OrderBy(x => x);
                return !requested.SequenceEqual(persisted);
            }
            case ParticipantScopeType.CustomList:
            {
                var requested = (scope.EmployeeIds ?? []).Distinct().OrderBy(x => x).ToList();
                var current = await _dbContext.CycleParticipants
                    .AsNoTracking()
                    .Where(p => p.CycleId == cycle.Id)
                    .Select(p => p.EmployeeId)
                    .ToListAsync(cancellationToken);
                return !requested.SequenceEqual(current.Distinct().OrderBy(x => x));
            }
            default:
                // AllEmployees / Grades: no id selection to compare ⇒ same type means unchanged.
                return false;
        }
    }

    /// <summary>BR-4: returns the first employee already in an ACTIVE cycle of the same type, or null.</summary>
    private async Task<Guid?> FindActiveSameTypeConflictAsync(
        CycleType type, List<Guid> employeeIds, Guid? excludeCycleId, CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0)
            return null;

        var activeCycleIds = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .Where(c => c.Type == type && c.Status == AppraisalCycleStatus.Active &&
                        (excludeCycleId == null || c.Id != excludeCycleId))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (activeCycleIds.Count == 0)
            return null;

        var conflict = await _dbContext.CycleParticipants
            .AsNoTracking()
            .Where(p => activeCycleIds.Contains(p.CycleId) && employeeIds.Contains(p.EmployeeId))
            .Select(p => (Guid?)p.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        return conflict;
    }

    private async Task NotifyParticipantsAsync(Guid cycleId, string eventType, string? detail, CancellationToken cancellationToken)
    {
        var participantIds = await _dbContext.CycleParticipants
            .AsNoTracking()
            .Where(p => p.CycleId == cycleId)
            .Select(p => p.EmployeeId)
            .ToListAsync(cancellationToken);

        foreach (var employeeId in participantIds)
            await _notifications.NotifyCycleEventAsync(eventType, cycleId, employeeId, detail, cancellationToken);
    }

    private static CyclePhaseDto ToPhaseDto(CyclePhase p, DateTime now) => new()
    {
        Id = p.Id,
        PhaseType = p.PhaseType,
        PhaseTypeName = p.PhaseType.ToString(),
        Sequence = p.Sequence,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        IsCurrent = p.ContainsInstant(now),
    };

    private static CycleDto ToDto(AppraisalCycle c, int participantCount, IReadOnlyList<Guid>? participantEmployeeIds = null)
    {
        var now = DateTime.UtcNow;
        return new CycleDto
        {
            Id = c.Id,
            Name = c.Name,
            Type = c.Type,
            TypeName = c.Type.ToString(),
            Status = c.Status,
            StatusName = c.Status.ToString(),
            StartDate = c.StartDate,
            EndDate = c.EndDate,
            RatingScaleMax = c.RatingScaleMax,
            SelfWeightPercent = c.SelfWeightPercent,
            ManagerWeightPercent = c.ManagerWeightPercent,
            Is360Enabled = c.Is360Enabled,
            IsCalibrationEnabled = c.IsCalibrationEnabled,
            IsAnonymousFeedback = c.IsAnonymousFeedback,
            ParticipantScope = c.ParticipantScope,
            // BUG-260: the full scope. DepartmentIds is the persisted selection (empty for non-Departments
            // scopes / pre-existing cycles); EmployeeIds is the resolved participant set (empty when not loaded).
            Scope = new CycleScopeDto(
                c.ParticipantScope,
                c.ScopeDepartmentIds.ToList(),
                participantEmployeeIds ?? []),
            CancellationReason = c.CancellationReason,
            ParticipantCount = participantCount,
            Phases = c.Phases.OrderBy(p => p.Sequence).Select(p => ToPhaseDto(p, now)).ToList(),
        };
    }
}
