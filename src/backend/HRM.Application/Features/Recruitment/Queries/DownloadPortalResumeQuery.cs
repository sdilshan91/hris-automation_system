using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-008 (FR-4): streams the candidate's own resume through the portal. Anonymous — authenticated by the
/// magic-link token, which must own the applicant. Tenant-scoped.
/// </summary>
public sealed record DownloadPortalResumeQuery(string Token, Guid ApplicantId)
    : IRequest<Result<ResumeDownloadDto>>;

public sealed class DownloadPortalResumeQueryHandler
    : IRequestHandler<DownloadPortalResumeQuery, Result<ResumeDownloadDto>>
{
    private readonly IApplicantPortalService _service;

    public DownloadPortalResumeQueryHandler(IApplicantPortalService service) => _service = service;

    public Task<Result<ResumeDownloadDto>> Handle(DownloadPortalResumeQuery request, CancellationToken cancellationToken)
        => _service.DownloadResumeAsync(request.Token, request.ApplicantId, cancellationToken);
}
