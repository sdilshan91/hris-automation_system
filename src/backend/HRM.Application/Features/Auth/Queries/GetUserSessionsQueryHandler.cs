using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Queries;

/// <summary>
/// Handles user sessions retrieval by delegating to IAuthService (US-AUTH-009).
/// </summary>
public sealed class GetUserSessionsQueryHandler
    : IRequestHandler<GetUserSessionsQuery, Result<IReadOnlyList<SessionDto>>>
{
    private readonly IAuthService _authService;

    public GetUserSessionsQueryHandler(IAuthService authService)
    {
        _authService = authService;
    }

    public async Task<Result<IReadOnlyList<SessionDto>>> Handle(
        GetUserSessionsQuery request,
        CancellationToken cancellationToken)
    {
        return await _authService.GetUserSessionsAsync(
            request.UserId,
            request.TenantId,
            request.CurrentSessionId,
            cancellationToken);
    }
}
