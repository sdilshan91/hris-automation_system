using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// US-AUTH-016 FR-5/AC-4: starts the admin-consent onboarding flow for the current tenant — builds the Microsoft
/// admin-consent URL and marks onboarding <c>consent_pending</c>. <paramref name="ReturnOrigin"/> is the SPA origin
/// the callback should return the browser to.
/// </summary>
public sealed record BeginSsoAdminConsentCommand(
    Guid TenantId,
    string? ReturnOrigin
) : IRequest<Result<AdminConsentUrlResponse>>;
