using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Queries;

/// <summary>
/// Query to retrieve the current tenant's MFA policy settings.
/// </summary>
public sealed record GetTenantAuthSettingsQuery(Guid TenantId) : IRequest<Result<TenantAuthSettingsResponse>>;
