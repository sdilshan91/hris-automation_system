namespace HRM.Application.Features.Auth.DTOs;

/// <summary>
/// BUG-294: the body of a tenant user-invitation redemption. Carries only the one-time token from the emailed
/// link and the password the invitee chooses — the tenant comes from the subdomain and the identity from the
/// invitation row, so nothing else is needed or trusted from the caller.
/// </summary>
public sealed record AcceptInvitationRequest(string Token, string NewPassword);
