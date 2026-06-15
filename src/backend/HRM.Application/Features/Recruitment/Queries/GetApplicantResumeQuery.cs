using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-003 (AC-3/FR-7/NFR-5): streams the applicant's stored resume through the API (no direct blob
/// URL). Returns the bytes + filename + content type; tenant-scoped. 404 if the applicant or file is gone.
/// </summary>
public sealed record GetApplicantResumeQuery(Guid ApplicantId) : IRequest<Result<ResumeDownloadDto>>;

public sealed class GetApplicantResumeQueryHandler
    : IRequestHandler<GetApplicantResumeQuery, Result<ResumeDownloadDto>>
{
    private readonly IApplicantService _service;

    public GetApplicantResumeQueryHandler(IApplicantService service) => _service = service;

    public Task<Result<ResumeDownloadDto>> Handle(GetApplicantResumeQuery request, CancellationToken cancellationToken)
        => _service.GetResumeAsync(request.ApplicantId, cancellationToken);
}
