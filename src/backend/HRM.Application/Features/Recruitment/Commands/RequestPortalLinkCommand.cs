using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-008 (BR-5/FR-8): a candidate whose link expired requests a fresh one by entering their email. A new
/// link is issued ONLY if an application exists for that email in the current tenant (email verification,
/// BR-5). To avoid leaking which emails have applied, the response is the SAME generic message regardless of
/// whether an application matched (anti-enumeration) — so this always returns success at the API boundary
/// when the input is well-formed and the tenant is resolved. Tenant-scoped.
/// </summary>
public sealed record RequestPortalLinkCommand(string Email) : IRequest<Result<PortalLinkRequestResultDto>>;

public sealed class RequestPortalLinkCommandHandler
    : IRequestHandler<RequestPortalLinkCommand, Result<PortalLinkRequestResultDto>>
{
    private readonly IApplicantPortalTokenService _tokenService;

    public RequestPortalLinkCommandHandler(IApplicantPortalTokenService tokenService)
        => _tokenService = tokenService;

    public async Task<Result<PortalLinkRequestResultDto>> Handle(RequestPortalLinkCommand request, CancellationToken cancellationToken)
    {
        var issued = await _tokenService.RequestLinkAsync(request.Email, cancellationToken);
        if (issued.IsFailure)
            return Result<PortalLinkRequestResultDto>.Failure(issued.Error!, issued.StatusCode ?? 400, issued.ErrorCode);

        // Generic, non-enumerable response regardless of whether a link was actually issued (BR-5).
        return Result<PortalLinkRequestResultDto>.Success(new PortalLinkRequestResultDto
        {
            Message = "If an application exists for this email, a new portal link has been sent.",
        });
    }
}
