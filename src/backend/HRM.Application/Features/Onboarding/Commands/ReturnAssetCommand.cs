using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>US-ONB-005 AC-2/BR-3: returns an asset against an offboarding task and completes the task.</summary>
public sealed record ReturnAssetCommand(
    Guid TaskId,
    Guid AssetId,
    AssetCondition Condition,
    bool Disposed
) : IRequest<Result<OffboardingInstanceDto>>;

public sealed class ReturnAssetCommandHandler
    : IRequestHandler<ReturnAssetCommand, Result<OffboardingInstanceDto>>
{
    private readonly IOffboardingService _service;

    public ReturnAssetCommandHandler(IOffboardingService service) => _service = service;

    public Task<Result<OffboardingInstanceDto>> Handle(
        ReturnAssetCommand request, CancellationToken cancellationToken)
        => _service.ReturnAssetAsync(
            new ReturnAssetInput(request.TaskId, request.AssetId, request.Condition, request.Disposed),
            cancellationToken);
}
