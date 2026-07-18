namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// Authenticated self-service change-password request (US-AUTH-004, ISSUE-248). The user id is taken from the
/// JWT (never the body), so only the current + new passwords are supplied.
/// </summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
