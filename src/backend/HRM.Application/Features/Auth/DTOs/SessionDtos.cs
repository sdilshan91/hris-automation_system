namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Represents an active session for display in session lists (US-AUTH-009 AC-4, AC-6).
/// Device/browser/os are parsed from the stored user_agent string.
/// </summary>
public sealed record SessionDto
{
    public Guid SessionId { get; init; }
    public string? Device { get; init; }
    public string? Browser { get; init; }
    public string? Os { get; init; }
    public string? IpAddress { get; init; }
    public DateTime IssuedAt { get; init; }
    public DateTime? LastActiveAt { get; init; }
    public bool IsCurrent { get; init; }
}

/// <summary>
/// Request body for admin session revocation (US-AUTH-009 AC-5).
/// If SessionId is null, all sessions for the user are revoked.
/// </summary>
public sealed record SessionRevokeRequest
{
    public Guid? SessionId { get; init; }
}
