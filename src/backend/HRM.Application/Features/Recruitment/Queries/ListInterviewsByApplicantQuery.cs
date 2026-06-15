using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-005 (FR-2): lists all interviews for a single applicant, newest round first. Tenant-scoped.
/// </summary>
public sealed record ListInterviewsByApplicantQuery(Guid ApplicantId)
    : IRequest<Result<IReadOnlyList<InterviewDto>>>;

public sealed class ListInterviewsByApplicantQueryHandler
    : IRequestHandler<ListInterviewsByApplicantQuery, Result<IReadOnlyList<InterviewDto>>>
{
    private readonly IInterviewService _service;

    public ListInterviewsByApplicantQueryHandler(IInterviewService service) => _service = service;

    public Task<Result<IReadOnlyList<InterviewDto>>> Handle(ListInterviewsByApplicantQuery request, CancellationToken cancellationToken)
        => _service.ListByApplicantAsync(request.ApplicantId, cancellationToken);
}
