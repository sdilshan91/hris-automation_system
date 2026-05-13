namespace HRM.Application.Features.Auth.DTOs;

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
