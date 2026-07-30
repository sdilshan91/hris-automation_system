using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

/// <summary>
/// US-PRF-011 §2: the calibration cohort for a cycle — each in-scope employee's original + calibrated rating,
/// reviewer and department. Delegates to <see cref="IPerformanceDashboardService"/> (reuses the dashboard's
/// scope + population + filter logic).
/// </summary>
public sealed record GetCalibrationCohortQuery(PerformanceDashboardFilter Filter)
    : IRequest<Result<CalibrationCohortDto>>;

public sealed class GetCalibrationCohortQueryHandler
    : IRequestHandler<GetCalibrationCohortQuery, Result<CalibrationCohortDto>>
{
    private readonly IPerformanceDashboardService _service;
    public GetCalibrationCohortQueryHandler(IPerformanceDashboardService service) => _service = service;

    public Task<Result<CalibrationCohortDto>> Handle(
        GetCalibrationCohortQuery request, CancellationToken cancellationToken)
        => _service.GetCalibrationCohortAsync(request.Filter, cancellationToken);
}
