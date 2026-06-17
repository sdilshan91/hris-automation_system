using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Queries;

/// <summary>
/// US-ONB-006 FR-4/FR-5: tenant-scoped aggregated exit-interview analytics over an optional date range.
/// Returns AGGREGATES ONLY (anonymized); <see cref="HasDetailPermission"/> is set from ExitInterview.ViewDetail
/// for the NFR-6 audit flag.
/// </summary>
public sealed record GetExitInterviewAnalyticsQuery(
    DateTime? FromDate, DateTime? ToDate, bool HasDetailPermission)
    : IRequest<Result<ExitInterviewAnalyticsDto>>;

public sealed class GetExitInterviewAnalyticsQueryHandler
    : IRequestHandler<GetExitInterviewAnalyticsQuery, Result<ExitInterviewAnalyticsDto>>
{
    private readonly IExitInterviewService _service;

    public GetExitInterviewAnalyticsQueryHandler(IExitInterviewService service) => _service = service;

    public Task<Result<ExitInterviewAnalyticsDto>> Handle(
        GetExitInterviewAnalyticsQuery request, CancellationToken cancellationToken)
        => _service.GetAnalyticsAsync(
            request.FromDate, request.ToDate, request.HasDetailPermission, cancellationToken);
}
