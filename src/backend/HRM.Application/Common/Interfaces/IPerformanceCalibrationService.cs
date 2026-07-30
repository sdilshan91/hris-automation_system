using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-PRF-011 §3: applies/adjusts a CALIBRATED rating on a manager review. The write path is deliberately
/// separate from the read-only <see cref="IPerformanceDashboardService"/>. A calibration NEVER mutates the
/// review's original <c>FinalScore</c> — it appends a <c>RatingCalibration</c> history row (who/when/why/from/to)
/// and writes an audit-log entry. Tenant-scoped (NFR-2). Reason is mandatory; the caller must be permitted
/// (enforced at the controller via [RequirePermission], defended again here via the ambient permission set).
/// </summary>
public interface IPerformanceCalibrationService
{
    /// <summary>
    /// Applies a calibrated rating for one employee in a cycle. Fails closed if calibration is not enabled for
    /// the cycle, the employee has no submitted manager review (no original score to calibrate), the reason is
    /// blank, or the score is outside [0, RatingScaleMax].
    /// </summary>
    Task<Result<CalibrationResultDto>> ApplyAsync(
        ApplyCalibrationInput input, CancellationToken cancellationToken = default);
}
