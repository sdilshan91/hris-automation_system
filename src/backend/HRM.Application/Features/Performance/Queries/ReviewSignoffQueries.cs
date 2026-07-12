using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

/// <summary>Gets the meeting notes + sign-off state for a review (US-PRF-006 AC-1).</summary>
public sealed record GetMeetingNotesQuery(Guid EmployeeId, Guid CycleId)
    : IRequest<Result<ReviewMeetingNotesDto>>;

public sealed class GetMeetingNotesQueryHandler
    : IRequestHandler<GetMeetingNotesQuery, Result<ReviewMeetingNotesDto>>
{
    private readonly IReviewSignoffService _service;
    public GetMeetingNotesQueryHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewMeetingNotesDto>> Handle(GetMeetingNotesQuery request, CancellationToken cancellationToken)
        => _service.GetNotesAsync(request.EmployeeId, request.CycleId, cancellationToken);
}

/// <summary>
/// Gets the CALLER's own meeting notes + sign-off state for the active cycle (US-PRF-006 AC-1, ISSUE-288).
/// Resolves employeeId + cycleId server-side from the caller — the employee self-view carries neither.
/// </summary>
public sealed record GetMyMeetingNotesQuery : IRequest<Result<ReviewMeetingNotesDto>>;

public sealed class GetMyMeetingNotesQueryHandler
    : IRequestHandler<GetMyMeetingNotesQuery, Result<ReviewMeetingNotesDto>>
{
    private readonly IReviewSignoffService _service;
    public GetMyMeetingNotesQueryHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewMeetingNotesDto>> Handle(GetMyMeetingNotesQuery request, CancellationToken cancellationToken)
        => _service.GetMyMeetingNotesAsync(cancellationToken);
}

/// <summary>Gets the complete review record for export (US-PRF-006 AC-4/FR-6) — PDF rendering is a seam.</summary>
public sealed record GetReviewExportQuery(Guid EmployeeId, Guid CycleId)
    : IRequest<Result<ReviewExportDto>>;

public sealed class GetReviewExportQueryHandler
    : IRequestHandler<GetReviewExportQuery, Result<ReviewExportDto>>
{
    private readonly IReviewSignoffService _service;
    public GetReviewExportQueryHandler(IReviewSignoffService service) => _service = service;

    public Task<Result<ReviewExportDto>> Handle(GetReviewExportQuery request, CancellationToken cancellationToken)
        => _service.GetExportRecordAsync(request.EmployeeId, request.CycleId, cancellationToken);
}
