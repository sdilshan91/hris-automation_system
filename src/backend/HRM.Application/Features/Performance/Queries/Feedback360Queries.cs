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

/// <summary>BUG-244: config-screen LOAD — gets the reviewer config for an employee in the active 360-enabled cycle.</summary>
public sealed record GetActiveReviewerConfigurationQuery(Guid EmployeeId) : IRequest<Result<ReviewerConfigurationDto>>;

public sealed class GetActiveReviewerConfigurationQueryHandler
    : IRequestHandler<GetActiveReviewerConfigurationQuery, Result<ReviewerConfigurationDto>>
{
    private readonly IReviewerAssignmentService _service;
    public GetActiveReviewerConfigurationQueryHandler(IReviewerAssignmentService service) => _service = service;

    public Task<Result<ReviewerConfigurationDto>> Handle(GetActiveReviewerConfigurationQuery request, CancellationToken cancellationToken)
        => _service.GetConfigurationForActiveCycleAsync(request.EmployeeId, cancellationToken);
}

/// <summary>BUG-244 #3: gets the 360 completion tracker for an employee in the active 360-enabled cycle.</summary>
public sealed record GetTrackerQuery(Guid EmployeeId) : IRequest<Result<CompletionTrackerDto>>;

public sealed class GetTrackerQueryHandler : IRequestHandler<GetTrackerQuery, Result<CompletionTrackerDto>>
{
    private readonly IReviewerAssignmentService _service;
    public GetTrackerQueryHandler(IReviewerAssignmentService service) => _service = service;

    public Task<Result<CompletionTrackerDto>> Handle(GetTrackerQuery request, CancellationToken cancellationToken)
        => _service.GetTrackerAsync(request.EmployeeId, cancellationToken);
}

/// <summary>BUG-244 #2: gets one reviewer's feedback form for a single assignment (US-PRF-005 FR-4/AC-2).</summary>
public sealed record GetFeedbackFormQuery(Guid AssignmentId) : IRequest<Result<FeedbackFormDto>>;

public sealed class GetFeedbackFormQueryHandler : IRequestHandler<GetFeedbackFormQuery, Result<FeedbackFormDto>>
{
    private readonly IFeedback360Service _service;
    public GetFeedbackFormQueryHandler(IFeedback360Service service) => _service = service;

    public Task<Result<FeedbackFormDto>> Handle(GetFeedbackFormQuery request, CancellationToken cancellationToken)
        => _service.GetFeedbackFormAsync(request.AssignmentId, cancellationToken);
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

/// <summary>Gets the CALLER's OWN released 360 results for a cycle (US-PRF-005 FR-3/FR-5). Self-scoped.</summary>
public sealed record GetMyFeedback360ResultsQuery(Guid CycleId) : IRequest<Result<Feedback360ResultsDto>>;

public sealed class GetMyFeedback360ResultsQueryHandler
    : IRequestHandler<GetMyFeedback360ResultsQuery, Result<Feedback360ResultsDto>>
{
    private readonly IFeedback360Service _service;
    public GetMyFeedback360ResultsQueryHandler(IFeedback360Service service) => _service = service;

    public Task<Result<Feedback360ResultsDto>> Handle(GetMyFeedback360ResultsQuery request, CancellationToken cancellationToken)
        => _service.GetMyResultsAsync(request.CycleId, cancellationToken);
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

/// <summary>Renders the 360 summary report as a branded PDF download (US-PRF-005 FR-7).</summary>
public sealed record ExportFeedback360ReportQuery(Guid CycleId, Guid RevieweeEmployeeId, string? Format)
    : IRequest<Result<PerformanceExportFile>>;

public sealed class ExportFeedback360ReportQueryHandler
    : IRequestHandler<ExportFeedback360ReportQuery, Result<PerformanceExportFile>>
{
    private readonly IFeedback360Service _service;
    public ExportFeedback360ReportQueryHandler(IFeedback360Service service) => _service = service;

    public Task<Result<PerformanceExportFile>> Handle(ExportFeedback360ReportQuery request, CancellationToken cancellationToken)
        => _service.ExportReportAsync(request.RevieweeEmployeeId, request.CycleId, request.Format, cancellationToken);
}
