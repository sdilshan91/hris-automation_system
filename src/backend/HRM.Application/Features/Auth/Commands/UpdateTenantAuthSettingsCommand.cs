using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Command to update tenant MFA policy settings (FR-6).
/// Only Tenant Admin and System Admin roles can execute this.
/// </summary>
public sealed record UpdateTenantAuthSettingsCommand(
    Guid TenantId,
    TenantAuthSettingsRequest Request) : IRequest<Result>;
