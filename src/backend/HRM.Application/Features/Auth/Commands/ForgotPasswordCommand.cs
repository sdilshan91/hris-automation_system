using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to initiate a password reset. Always returns success to prevent user enumeration.
/// </summary>
public sealed record ForgotPasswordCommand(string Email) : IRequest<Result>;
