namespace HRM.Application.Features.Impersonation.DTOs;

/// <summary>
/// Input for starting an impersonation session (US-ADM-003 FR-1). <c>Reason</c> is mandatory (min 10, max 500
/// chars — BR-4) and stored verbatim.
/// </summary>
public sealed record StartImpersonationInput(
    Guid TargetUserId,
    Guid TargetTenantId,
    string Reason);

/// <summary>
/// Result of a successful impersonation start (US-ADM-003 AC-1, §7 Output). The <c>Token</c> is the separate
/// impersonation JWT (max 60 min, not refreshable); the System Admin's browser opens <c>RedirectUrl</c> (the
/// target tenant subdomain) in a new tab carrying the token. <c>IsReadOnly</c> tells the FE whether to render the
/// session as read-only (AC-5/AC-6).
/// </summary>
public sealed record StartImpersonationResultDto(
    Guid SessionId,
    string Token,
    string RedirectUrl,
    DateTime ExpiresAt,
    bool IsReadOnly);

/// <summary>Result of ending an impersonation session (US-ADM-003 AC-3).</summary>
public sealed record EndImpersonationResultDto(
    Guid SessionId,
    string Status,
    int ActionsCount,
    DateTime? EndedAt);

/// <summary>
/// A selectable impersonation target — an active tenant user shown in the picker (US-ADM-003 AC-1). System-admin /
/// system-tenant users are excluded (BR-2). <c>Roles</c> are the user's role names within the target tenant.
/// </summary>
public sealed record ImpersonationTargetDto(
    Guid UserId,
    string Email,
    string? DisplayName,
    IReadOnlyList<string> Roles);
