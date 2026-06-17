using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Users.DTOs;
using MediatR;

namespace HRM.Application.Features.Users.Queries;

/// <summary>US-ADM-005 FR-6: the user detail view (profile + roles + sessions + invitation history).</summary>
public sealed record GetTenantUserDetailQuery(Guid UserTenantId) : IRequest<Result<TenantUserDetailDto>>;

public sealed class GetTenantUserDetailQueryHandler
    : IRequestHandler<GetTenantUserDetailQuery, Result<TenantUserDetailDto>>
{
    private readonly IUserManagementService _service;

    public GetTenantUserDetailQueryHandler(IUserManagementService service) => _service = service;

    public Task<Result<TenantUserDetailDto>> Handle(GetTenantUserDetailQuery request, CancellationToken cancellationToken)
        => _service.GetUserDetailAsync(request.UserTenantId, cancellationToken);
}
