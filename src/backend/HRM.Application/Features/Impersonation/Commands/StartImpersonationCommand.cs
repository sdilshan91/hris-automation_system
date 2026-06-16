using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Impersonation.DTOs;
using MediatR;

namespace HRM.Application.Features.Impersonation.Commands;

/// <summary>
/// Starts a tenant-user impersonation session from the System Admin Console (US-ADM-003 AC-1/FR-1). Thin command;
/// the transactional work lives in <see cref="IImpersonationService"/>.
/// </summary>
public sealed record StartImpersonationCommand(
    Guid TargetUserId,
    Guid TargetTenantId,
    string Reason) : IRequest<Result<StartImpersonationResultDto>>;

public sealed class StartImpersonationCommandHandler
    : IRequestHandler<StartImpersonationCommand, Result<StartImpersonationResultDto>>
{
    private readonly IImpersonationService _service;

    public StartImpersonationCommandHandler(IImpersonationService service) => _service = service;

    public Task<Result<StartImpersonationResultDto>> Handle(
        StartImpersonationCommand request, CancellationToken cancellationToken)
        => _service.StartAsync(
            new StartImpersonationInput(request.TargetUserId, request.TargetTenantId, request.Reason),
            cancellationToken);
}
