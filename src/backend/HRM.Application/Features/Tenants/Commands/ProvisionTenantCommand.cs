using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Tenants.DTOs;
using MediatR;

namespace HRM.Application.Features.Tenants.Commands;

/// <summary>
/// Provisions a new tenant from the System Admin Console (US-ADM-001). Thin command: the transactional work
/// lives in <see cref="ITenantProvisioningService"/>.
/// </summary>
public sealed record ProvisionTenantCommand(
    string Name,
    string Subdomain,
    Guid SubscriptionPlanId,
    string OwnerEmail,
    string Region,
    int? TrialDays) : IRequest<Result<ProvisionTenantResultDto>>;

public sealed class ProvisionTenantCommandHandler
    : IRequestHandler<ProvisionTenantCommand, Result<ProvisionTenantResultDto>>
{
    private readonly ITenantProvisioningService _service;

    public ProvisionTenantCommandHandler(ITenantProvisioningService service) => _service = service;

    public Task<Result<ProvisionTenantResultDto>> Handle(
        ProvisionTenantCommand request, CancellationToken cancellationToken)
        => _service.ProvisionAsync(
            new ProvisionTenantInput(
                request.Name, request.Subdomain, request.SubscriptionPlanId,
                request.OwnerEmail, request.Region, request.TrialDays),
            cancellationToken);
}
