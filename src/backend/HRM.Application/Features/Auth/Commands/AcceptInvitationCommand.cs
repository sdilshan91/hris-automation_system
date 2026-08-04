using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// BUG-294: redeems a tenant user-invitation — verifies the one-time token, activates the membership with the
/// invited roles, and sets the invitee's first password.
/// </summary>
public sealed record AcceptInvitationCommand(
    string Token,
    string NewPassword,
    string? IpAddress = null,
    string? UserAgent = null
) : IRequest<Result>;
