using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

public sealed record SwitchTenantCommand(
    Guid UserId,
    Guid SourceTenantId,
    Guid TargetTenantId,
    string? IpAddress,
    string? UserAgent
) : IRequest<Result<SwitchTenantResponse>>;
