using HRM.Application.DTOs;

namespace HRM.Application.Features.Users.DTOs;

/// <summary>US-ADM-005 AC-1/FR-1: a row in the tenant user list.</summary>
public sealed record TenantUserListItemDto(
    Guid UserTenantId,
    Guid UserId,
    string Email,
    string? DisplayName,
    string Status,
    IReadOnlyList<TenantUserRoleDto> Roles,
    DateTime? LastLoginAt,
    Guid? LinkedEmployeeId);

/// <summary>A role assigned to a user's membership.</summary>
public sealed record TenantUserRoleDto(Guid RoleId, string Name);

/// <summary>US-ADM-005 FR-6: the user detail view.</summary>
public sealed record TenantUserDetailDto(
    Guid UserTenantId,
    Guid UserId,
    string Email,
    string? DisplayName,
    string Status,
    IReadOnlyList<TenantUserRoleDto> Roles,
    DateTime? LastLoginAt,
    Guid? LinkedEmployeeId,
    IReadOnlyList<ActiveSessionDto> ActiveSessions,
    IReadOnlyList<InvitationDto> InvitationHistory);

/// <summary>An active session (refresh token) for the user in this tenant (FR-6).</summary>
public sealed record ActiveSessionDto(
    Guid Id,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    DateTime? LastActiveAt,
    string? UserAgent,
    string? IpAddress);

/// <summary>US-ADM-005 AC-2: a pending/closed invitation row (Pending Invitations tab).</summary>
public sealed record InvitationDto(
    Guid Id,
    string Email,
    string Status,
    IReadOnlyList<Guid> InvitedRoleIds,
    DateTime InvitedAt,
    DateTime ExpiresAt,
    bool IsExpired);

/// <summary>Result of an invite operation — single or bulk (AC-2/FR-3).</summary>
public sealed record InviteResultDto(
    IReadOnlyList<InvitationDto> Created,
    IReadOnlyList<InviteRowErrorDto> Errors);

/// <summary>A per-row error for a bulk invite (FR-3) — valid rows are not blocked by invalid ones.</summary>
public sealed record InviteRowErrorDto(string Email, string Error);

// ── Request DTOs (controller bodies) ───────────────────────────────────────

/// <summary>Single-email invite (AC-2/FR-2).</summary>
public sealed record InviteUserRequest(string Email, IReadOnlyList<Guid> RoleIds);

/// <summary>One row of a bulk/CSV invite (FR-3). Roles are referenced by NAME (CSV column).</summary>
public sealed record BulkInviteRow(string Email, IReadOnlyList<string> RoleNames);

/// <summary>Bulk invite (FR-3) — an array of {email, roleNames}.</summary>
public sealed record BulkInviteRequest(IReadOnlyList<BulkInviteRow> Rows);

/// <summary>Edit-roles request (AC-3/FR-4) — a new COMPLETE role set.</summary>
public sealed record EditUserRolesRequest(IReadOnlyList<Guid> RoleIds);
