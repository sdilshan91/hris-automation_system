using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Auth.DTOs;
using MediatR;

namespace HRM.Application.Features.Auth.Commands;

/// <summary>
/// Handles <see cref="BeginSsoAdminConsentCommand"/>: marks the tenant's onboarding <c>consent_pending</c> (and
/// resolves its subdomain), then builds the Microsoft admin-consent URL. Composes the two SSO services — no cycle
/// (neither service references this handler).
/// </summary>
public sealed class BeginSsoAdminConsentCommandHandler
    : IRequestHandler<BeginSsoAdminConsentCommand, Result<AdminConsentUrlResponse>>
{
    private readonly IAuthService _authService;
    private readonly IEntraSsoService _sso;

    public BeginSsoAdminConsentCommandHandler(IAuthService authService, IEntraSsoService sso)
    {
        _authService = authService;
        _sso = sso;
    }

    public async Task<Result<AdminConsentUrlResponse>> Handle(
        BeginSsoAdminConsentCommand request, CancellationToken cancellationToken)
    {
        if (!_sso.IsAdminConsentConfigured)
        {
            return Result<AdminConsentUrlResponse>.Failure(
                "Microsoft SSO admin-consent is not configured for this deployment.", 400, "sso_not_configured");
        }

        var pending = await _authService.MarkAdminConsentPendingAsync(request.TenantId, cancellationToken);
        if (pending.IsFailure)
        {
            return Result<AdminConsentUrlResponse>.Failure(pending.Error!, pending.StatusCode ?? 404);
        }

        var subdomain = pending.Value!;
        var url = await _sso.BuildAdminConsentUrlAsync(subdomain, request.ReturnOrigin, cancellationToken);
        if (url.IsFailure)
        {
            return Result<AdminConsentUrlResponse>.Failure(
                "Could not start the admin-consent flow.", 400, url.Error);
        }

        return Result<AdminConsentUrlResponse>.Success(new AdminConsentUrlResponse(url.Value!));
    }
}
