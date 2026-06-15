using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using MediatR;

namespace HRM.Application.Features.Recruitment.Queries;

/// <summary>
/// US-REC-005 (FR-5): calendar view — lists interviews for the tenant, filterable by interviewer /
/// vacancy / date-range / status. Tenant-scoped.
/// </summary>
public sealed record ListInterviewsQuery(InterviewCalendarFilter Filter)
    : IRequest<Result<IReadOnlyList<InterviewDto>>>;

public sealed class ListInterviewsQueryHandler
    : IRequestHandler<ListInterviewsQuery, Result<IReadOnlyList<InterviewDto>>>
{
    private readonly IInterviewService _service;

    public ListInterviewsQueryHandler(IInterviewService service) => _service = service;

    public Task<Result<IReadOnlyList<InterviewDto>>> Handle(ListInterviewsQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(request.Filter, cancellationToken);
}
