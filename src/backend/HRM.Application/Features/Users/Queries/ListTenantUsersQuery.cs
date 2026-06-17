using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Users.DTOs;
using MediatR;

namespace HRM.Application.Features.Users.Queries;

/// <summary>US-ADM-005 AC-1/FR-1: paginated, searchable, filterable tenant user list.</summary>
public sealed record ListTenantUsersQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Status,
    Guid? RoleId
) : IRequest<Result<PagedResult<TenantUserListItemDto>>>;

public sealed class ListTenantUsersQueryHandler
    : IRequestHandler<ListTenantUsersQuery, Result<PagedResult<TenantUserListItemDto>>>
{
    private readonly IUserManagementService _service;

    public ListTenantUsersQueryHandler(IUserManagementService service) => _service = service;

    public Task<Result<PagedResult<TenantUserListItemDto>>> Handle(
        ListTenantUsersQuery request, CancellationToken cancellationToken)
        => _service.ListUsersAsync(
            new ListTenantUsersInput(request.Page, request.PageSize, request.Search, request.Status, request.RoleId),
            cancellationToken);
}
