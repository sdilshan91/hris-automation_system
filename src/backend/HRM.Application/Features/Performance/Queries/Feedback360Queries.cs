using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

/// <summary>Gets the reviewer-configuration view for a reviewee + cycle (US-PRF-005 AC-1).</summary>
public sealed record GetReviewerConfigurationQuery(Guid CycleId, Guid RevieweeEmployeeId)
    : IRequest<Result<ReviewerConfigurationDto>>;

public sealed class GetReviewerConfigurationQueryHandler
    : IRequestHandler<GetReviewerConfigurationQuery, Result<ReviewerConfigurationDto>>
{
    private readonly IReviewerAssignmentService _service;
    public GetReviewerConfigurationQueryHandler(IReviewerAssignmentService service) => _service = service;

    public Task<Result<ReviewerConfigurationDto>> Handle(GetReviewerConfigurationQuery request, CancellationToken cancellationToken)
        => _service.GetConfigurationAsync(request.RevieweeEmployeeId, request.CycleId, cancellationToken);
}

/// <summary>Gets the aggregated 360 results for a reviewee + cycle (US-PRF-005 AC-4/FR-6).</summary>
public sealed record GetFeedback360ResultsQuery(Guid CycleId, Guid RevieweeEmployeeId)
    : IRequest<Result<Feedback360ResultsDto>>;

public sealed class GetFeedback360ResultsQueryHandler
    : IRequestHandler<GetFeedback360ResultsQuery, Result<Feedback360ResultsDto>>
{
    private readonly IFeedback360Service _service;
    public GetFeedback360ResultsQueryHandler(IFeedback360Service service) => _service = service;

    public Task<Result<Feedback360ResultsDto>> Handle(GetFeedback360ResultsQuery request, CancellationToken cancellationToken)
        => _service.GetResultsAsync(request.RevieweeEmployeeId, request.CycleId, cancellationToken);
}

/// <summary>Gets the 360 summary report data for a reviewee + cycle, suitable for PDF export (US-PRF-005 FR-7).</summary>
public sealed record GetFeedback360ReportQuery(Guid CycleId, Guid RevieweeEmployeeId)
    : IRequest<Result<Feedback360ResultsDto>>;

public sealed class GetFeedback360ReportQueryHandler
    : IRequestHandler<GetFeedback360ReportQuery, Result<Feedback360ResultsDto>>
{
    private readonly IFeedback360Service _service;
    public GetFeedback360ReportQueryHandler(IFeedback360Service service) => _service = service;

    public Task<Result<Feedback360ResultsDto>> Handle(GetFeedback360ReportQuery request, CancellationToken cancellationToken)
        => _service.GetReportDataAsync(request.RevieweeEmployeeId, request.CycleId, cancellationToken);
}
