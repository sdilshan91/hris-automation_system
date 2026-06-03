using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to disable MFA for the current user.
/// Will be rejected if the tenant policy requires MFA for the user's roles (BR-3).
/// </summary>
public sealed record MfaDisableCommand(Guid UserId, Guid TenantId) : IRequest<Result>;
