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

/// <summary>
/// F3 / GAP-021 AC-3: marks a cycle's Calibration phase complete. Thin MediatR wrapper over
/// <see cref="IPerformanceCalibrationService"/>, matching <see cref="ApplyCalibrationCommand"/>.
/// </summary>
/// <remarks>
/// This is the write that makes BR-2 meaningful. Before it, <c>RecommendationService</c> gated on whether a
/// manager review had been submitted and returned <c>calibration_incomplete</c> — an error code naming a
/// condition it never tested. The gate now reads <c>CyclePhase.CompletedOn</c>, and this is what sets it.
/// </remarks>
public sealed record CompleteCalibrationPhaseCommand(Guid CycleId) : IRequest<Result<PhaseClosureDto>>;

public sealed class CompleteCalibrationPhaseCommandHandler
    : IRequestHandler<CompleteCalibrationPhaseCommand, Result<PhaseClosureDto>>
{
    private readonly IPerformanceCalibrationService _service;
    public CompleteCalibrationPhaseCommandHandler(IPerformanceCalibrationService service) => _service = service;

    public Task<Result<PhaseClosureDto>> Handle(
        CompleteCalibrationPhaseCommand request, CancellationToken cancellationToken)
        => _service.CompleteCalibrationPhaseAsync(request.CycleId, cancellationToken);
}
