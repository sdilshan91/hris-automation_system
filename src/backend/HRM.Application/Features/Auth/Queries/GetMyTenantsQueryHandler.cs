using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Queries;

public sealed class GetMyTenantsQueryHandler
    : IRequestHandler<GetMyTenantsQuery, Result<IReadOnlyList<TenantMembershipDto>>>
{
    private readonly IAuthService _authService;

    public GetMyTenantsQueryHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public Task<Result<IReadOnlyList<TenantMembershipDto>>> Handle(
        GetMyTenantsQuery request,
        CancellationToken cancellationToken)
    {
        return _authService.GetMyTenantsAsync(request.UserId, request.CurrentTenantId, cancellationToken);
    }
}
