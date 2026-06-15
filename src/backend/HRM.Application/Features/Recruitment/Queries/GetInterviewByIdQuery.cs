using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>US-REC-005: gets a single interview by id (tenant-scoped, with interviewer details).</summary>
public sealed record GetInterviewByIdQuery(Guid InterviewId) : IRequest<Result<InterviewDto>>;

public sealed class GetInterviewByIdQueryHandler
    : IRequestHandler<GetInterviewByIdQuery, Result<InterviewDto>>
{
    private readonly IInterviewService _service;

    public GetInterviewByIdQueryHandler(IInterviewService service) => _service = service;

    public Task<Result<InterviewDto>> Handle(GetInterviewByIdQuery request, CancellationToken cancellationToken)
        => _service.GetByIdAsync(request.InterviewId, cancellationToken);
}
