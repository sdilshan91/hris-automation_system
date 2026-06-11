using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Queries;

/// <summary>
/// Query to retrieve active sessions for a user in a tenant (US-AUTH-009 AC-4, AC-6).
/// Used by both admin (GET /api/v1/tenant/users/{id}/sessions) and self (GET /api/v1/auth/me/sessions).
/// </summary>
public sealed record GetUserSessionsQuery(
    Guid UserId,
    Guid TenantId,
    Guid? CurrentSessionId) : IRequest<Result<IReadOnlyList<SessionDto>>>;
