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
/// US-PRF-011 §3: applies a calibrated rating to a manager review WITHOUT ever mutating the review's original
/// <see cref="ManagerReview.FinalScore"/>. Each call appends an APPEND-ONLY <see cref="RatingCalibration"/>
/// history row (original snapshot + previous + new + reason + actor) and stages a structured audit-log entry
/// via the shared <see cref="IPayrollAuditLogger"/> (a generic audit writer over the audit_log table — its
/// payroll-flavoured name is incidental; the entry is committed atomically with the calibration row by the one
/// SaveChanges here). Tenant-scoped via <see cref="ITenantContext"/> + the EF global query filter (NFR-2).
/// </summary>
public sealed class PerformanceCalibrationService : IPerformanceCalibrationService
{
    private const string CalibrationAction = "RatingCalibration.Applied";
    private const string CalibrationResource = "RatingCalibration";

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPayrollAuditLogger _auditLogger;
    private readonly ILogger<PerformanceCalibrationService> _logger;

    public PerformanceCalibrationService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPayrollAuditLogger auditLogger,
        ILogger<PerformanceCalibrationService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<Result<CalibrationResultDto>> ApplyAsync(
        ApplyCalibrationInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CalibrationResultDto>.Failure("Tenant context is not resolved.", 400);

        // Defence-in-depth authorization (the controller [RequirePermission] is the primary gate): only a
        // caller who can publish/manage cycles OR review org-wide may calibrate.
        var perms = _currentUser.Permissions;
        // F3: Calibrate is OR-ed IN, not swapped for the other three — see the catalog entry. Granting
        // Calibrate alone is now sufficient, which is what makes delegation to a facilitator possible.
        var permitted = perms.Contains(PermissionCatalog.Performance.Calibrate)
            || perms.Contains(PermissionCatalog.Performance.PublishAll)
            || perms.Contains(PermissionCatalog.Performance.Manage)
            || perms.Contains(PermissionCatalog.Performance.ReviewAll);
        if (!permitted)
            return Result<CalibrationResultDto>.Failure(
                "You do not have permission to calibrate ratings.", 403, "forbidden");

        if (string.IsNullOrWhiteSpace(input.Reason))
            return Result<CalibrationResultDto>.Failure(
                "A calibration reason is required.", 422, "reason_required");

        var cycle = await _dbContext.AppraisalCycles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == input.CycleId, cancellationToken);
        if (cycle is null)
            return Result<CalibrationResultDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        if (!cycle.IsCalibrationEnabled)
            return Result<CalibrationResultDto>.Failure(
                "Calibration is not enabled for this cycle.", 409, "calibration_disabled");

        // The employee must have a SUBMITTED manager review carrying a final score — that final score is the
        // ORIGINAL rating (never overwritten). Without it there is nothing to calibrate.
        var review = await _dbContext.ManagerReviews
            .FirstOrDefaultAsync(
                r => r.CycleId == input.CycleId && r.EmployeeId == input.EmployeeId, cancellationToken);
        if (review is null)
            return Result<CalibrationResultDto>.Failure(
                "No manager review exists for this employee in this cycle.", 404, "review_not_found");
        if (review.Status != ManagerReviewStatus.Submitted || review.FinalScore is null)
            return Result<CalibrationResultDto>.Failure(
                "The manager review has no submitted final score to calibrate.", 422, "no_original_score");

        if (input.CalibratedScore < 0 || input.CalibratedScore > cycle.RatingScaleMax)
            return Result<CalibrationResultDto>.Failure(
                $"The calibrated score must be between 0 and {cycle.RatingScaleMax}.", 422, "score_out_of_range");

        var originalScore = review.FinalScore.Value;

        // Previous calibrated value = most-recent existing calibration row for this employee/cycle (repeated rounds).
        var previous = await _dbContext.RatingCalibrations.AsNoTracking()
            .Where(c => c.CycleId == input.CycleId && c.EmployeeId == input.EmployeeId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (decimal?)c.CalibratedScore)
            .FirstOrDefaultAsync(cancellationToken);

        var calibration = new RatingCalibration
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            CycleId = input.CycleId,
            EmployeeId = input.EmployeeId,
            ManagerReviewId = review.Id,
            OriginalScore = originalScore,
            PreviousCalibratedScore = previous,
            CalibratedScore = input.CalibratedScore,
            Reason = input.Reason.Trim(),
            CalibratedByUserId = _currentUser.UserId,
            IsDeleted = false,
        };
        _dbContext.RatingCalibrations.Add(calibration);

        // Structured audit entry (who/when/why/from/to) staged on the same context — committed atomically below.
        _auditLogger.Log(
            CalibrationAction,
            CalibrationResource,
            calibration.Id.ToString(),
            before: new { OriginalScore = originalScore, CalibratedScore = previous },
            after: new { calibration.CalibratedScore, calibration.Reason, calibration.EmployeeId, calibration.CycleId });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Rating calibration applied. Cycle={CycleId}, Employee={EmployeeId}, Original={Original}, Calibrated={Calibrated}, Tenant={TenantId}, By={User}",
            input.CycleId, input.EmployeeId, originalScore, input.CalibratedScore, _tenantContext.TenantId, _currentUser.Email);

        return Result<CalibrationResultDto>.Success(new CalibrationResultDto
        {
            CalibrationId = calibration.Id,
            CycleId = input.CycleId,
            EmployeeId = input.EmployeeId,
            ManagerReviewId = review.Id,
            OriginalScore = originalScore,
            PreviousCalibratedScore = previous,
            CalibratedScore = input.CalibratedScore,
            Reason = calibration.Reason,
            CalibratedAt = calibration.CreatedAt,
        });
    }

    /// <inheritdoc />
    public async Task<Result<PhaseClosureDto>> CompleteCalibrationPhaseAsync(
        Guid cycleId, CancellationToken cancellationToken = default)
    {
        // Same OR-ed permission set as ApplyAsync — completing the phase is the last act of calibrating, so
        // anyone who may calibrate may close it. See the PermissionCatalog.Performance.Calibrate remarks.
        var perms = _currentUser.Permissions;
        var permitted = perms.Contains(PermissionCatalog.Performance.Calibrate)
            || perms.Contains(PermissionCatalog.Performance.PublishAll)
            || perms.Contains(PermissionCatalog.Performance.Manage)
            || perms.Contains(PermissionCatalog.Performance.ReviewAll);
        if (!permitted)
            return Result<PhaseClosureDto>.Failure(
                "You do not have permission to complete the calibration phase.", 403, "forbidden");

        var cycle = await _dbContext.AppraisalCycles
            .Include(c => c.Phases)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle is null)
            return Result<PhaseClosureDto>.Failure("Cycle not found.", 404, "cycle_not_found");

        if (!cycle.IsCalibrationEnabled)
            return Result<PhaseClosureDto>.Failure(
                "Calibration is not enabled for this cycle.", 409, "calibration_not_enabled");

        var phase = cycle.Phases.FirstOrDefault(p => p.PhaseType == CyclePhaseType.Calibration);
        if (phase is null)
            return Result<PhaseClosureDto>.Failure(
                "This cycle has no calibration phase.", 404, "calibration_phase_not_found");

        // Rejected, not silently re-stamped: overwriting CompletedOn/CompletedByUserId would quietly rewrite
        // who closed the phase and when, and that record is the point of storing them.
        if (phase.IsComplete)
            return Result<PhaseClosureDto>.Failure(
                "The calibration phase is already complete.", 409, "phase_already_complete");

        var now = DateTime.UtcNow;

        // The window must have STARTED. Predecessor phases are deliberately NOT required to be complete:
        // only Calibration has a completion fact today, so requiring predecessors would gate a real phase on
        // state that does not exist for the others and could never be satisfied. Revisit when the remaining
        // phases gain their own CompletedOn.
        if (now < phase.StartDate)
            return Result<PhaseClosureDto>.Failure(
                "The calibration phase has not started yet.", 422, "phase_not_started");

        phase.CompletedOn = now;
        phase.CompletedByUserId = _currentUser.UserId;

        // Same shared audit writer + same context as ApplyAsync, so the phase closure and the row it stamps
        // commit atomically. "Who closed calibration and when" is the record AC-3 exists to produce.
        _auditLogger.Log(
            "CalibrationPhase.Completed",
            CalibrationResource,
            cycle.Id.ToString(),
            before: new { CompletedOn = (DateTime?)null },
            after: new { CompletedOn = now, CompletedByUserId = _currentUser.UserId });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<PhaseClosureDto>.Success(new PhaseClosureDto(
            cycle.Id, phase.PhaseType.ToString(), now, phase.CompletedByUserId));
    }
}
