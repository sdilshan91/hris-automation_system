namespace HRM.Application.Features.Auth.DTOs;

/// <summary>BUG-295: token-only — the reset token alone identifies the user (see <c>IAuthService</c>).</summary>
public sealed record ResetPasswordRequest(string Token, string NewPassword);
