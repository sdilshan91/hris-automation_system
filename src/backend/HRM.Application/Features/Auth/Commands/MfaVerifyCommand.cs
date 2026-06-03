using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to verify a TOTP code during MFA enrollment (step 2).
/// Confirms enrollment by validating the code against the stored secret.
/// </summary>
public sealed record MfaVerifyCommand(Guid UserId, string Code) : IRequest<Result<MfaVerifyResponse>>;
