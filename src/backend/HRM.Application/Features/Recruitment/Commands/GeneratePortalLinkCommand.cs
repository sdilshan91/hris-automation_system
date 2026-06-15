using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-008 (FR-1/FR-7): issues a candidate-portal magic-link token for an applicant email in the current
/// tenant and "sends" (log-only seam, FR-7) the portal link. Used both at application-confirmation time and
/// by the recruiter to (re)issue a link. Tenant-scoped.
/// </summary>
public sealed record GeneratePortalLinkCommand(string ApplicantEmail) : IRequest<Result<PortalLinkDto>>;

public sealed class GeneratePortalLinkCommandHandler
    : IRequestHandler<GeneratePortalLinkCommand, Result<PortalLinkDto>>
{
    private readonly IApplicantPortalTokenService _tokenService;
    private readonly ILogger<GeneratePortalLinkCommandHandler> _logger;

    public GeneratePortalLinkCommandHandler(
        IApplicantPortalTokenService tokenService,
        ILogger<GeneratePortalLinkCommandHandler> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<Result<PortalLinkDto>> Handle(GeneratePortalLinkCommand request, CancellationToken cancellationToken)
    {
        var issued = await _tokenService.IssueAsync(request.ApplicantEmail, cancellationToken);
        if (issued.IsFailure)
            return Result<PortalLinkDto>.Failure(issued.Error!, issued.StatusCode ?? 400, issued.ErrorCode);

        // FR-7: log-only "email sent" with the link (no real notification platform — DEFERRED). The raw token
        // is intentionally logged here ONLY because this is the dev/log seam that stands in for the email.
        _logger.LogInformation(
            "Portal magic-link generated for {Email}; link would be emailed (FR-7, log-only seam). Expires {ExpiresAt}.",
            request.ApplicantEmail, issued.Value!.ExpiresAt);

        return Result<PortalLinkDto>.Success(new PortalLinkDto
        {
            Token = issued.Value.Token,
            ExpiresAt = issued.Value.ExpiresAt,
        });
    }
}
