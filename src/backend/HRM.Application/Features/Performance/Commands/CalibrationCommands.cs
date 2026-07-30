using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

/// <summary>
/// US-PRF-011 §3: applies/adjusts a calibrated rating for one employee in a cycle, with a mandatory reason.
/// Thin MediatR wrapper over <see cref="IPerformanceCalibrationService"/> (which owns the append-only history +
/// audit write). Permission-gated at the controller via [RequirePermission].
/// </summary>
public sealed record ApplyCalibrationCommand(ApplyCalibrationInput Input) : IRequest<Result<CalibrationResultDto>>;

public sealed class ApplyCalibrationCommandHandler
    : IRequestHandler<ApplyCalibrationCommand, Result<CalibrationResultDto>>
{
    private readonly IPerformanceCalibrationService _service;
    public ApplyCalibrationCommandHandler(IPerformanceCalibrationService service) => _service = service;

    public Task<Result<CalibrationResultDto>> Handle(
        ApplyCalibrationCommand request, CancellationToken cancellationToken)
        => _service.ApplyAsync(request.Input, cancellationToken);
}
