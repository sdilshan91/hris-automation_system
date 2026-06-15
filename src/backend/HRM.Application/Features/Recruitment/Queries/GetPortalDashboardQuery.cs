using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-008 (FR-2/AC-1/AC-2/AC-3/FR-6): builds the sanitized candidate-portal dashboard for the email
/// behind a presented magic-link token. Anonymous — authenticated by the token, not JWT. Tenant-scoped to
/// the subdomain-resolved tenant + the token's tenant (AC-4).
/// </summary>
public sealed record GetPortalDashboardQuery(string Token) : IRequest<Result<PortalDashboardDto>>;

public sealed class GetPortalDashboardQueryHandler
    : IRequestHandler<GetPortalDashboardQuery, Result<PortalDashboardDto>>
{
    private readonly IApplicantPortalService _service;

    public GetPortalDashboardQueryHandler(IApplicantPortalService service) => _service = service;

    public Task<Result<PortalDashboardDto>> Handle(GetPortalDashboardQuery request, CancellationToken cancellationToken)
        => _service.GetDashboardAsync(request.Token, cancellationToken);
}
