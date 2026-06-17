using HRM.Application.Common.Models;
using HRM.Application.Features.Users.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-005: Tenant-scoped user + role-assignment management for a Tenant Admin. Every read/write runs in
/// the NORMAL resolved-tenant context and relies on the EF Core global query filters for isolation (AC-6) —
/// the one deliberate exception is force-password-reset, which revokes refresh tokens across ALL tenants
/// (the password is global) scoped strictly by UserId.
/// </summary>
public interface IUserManagementService
{
    Task<Result<PagedResult<TenantUserListItemDto>>> ListUsersAsync(
        ListTenantUsersInput input, CancellationToken cancellationToken = default);

    Task<Result<TenantUserDetailDto>> GetUserDetailAsync(
        Guid userTenantId, CancellationToken cancellationToken = default);

    Task<Result<InviteResultDto>> InviteAsync(
        string email, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default);

    Task<Result<InviteResultDto>> BulkInviteAsync(
        IReadOnlyList<BulkInviteRow> rows, CancellationToken cancellationToken = default);

    Task<Result> EditRolesAsync(
        Guid userTenantId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(Guid userTenantId, CancellationToken cancellationToken = default);

    Task<Result> ForcePasswordResetAsync(Guid userTenantId, CancellationToken cancellationToken = default);

    Task<Result> EndAllSessionsAsync(Guid userTenantId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InvitationDto>>> ListInvitationsAsync(CancellationToken cancellationToken = default);

    Task<Result<InvitationDto>> ResendInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default);

    Task<Result> RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default);
}

/// <summary>Inputs for the tenant user list (FR-1): pagination + search + status/role filters.</summary>
public sealed record ListTenantUsersInput(
    int Page,
    int PageSize,
    string? Search,
    string? Status,
    Guid? RoleId);
