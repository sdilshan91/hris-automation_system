using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// US-ATT-008 §8: the acting employee's own monthly lateness score ("X of N allowed lates").
/// </summary>
public sealed record GetMyLatenessScoreQuery(int Year, int Month) : IRequest<Result<LatenessScoreDto>>;

public sealed class GetMyLatenessScoreQueryHandler
    : IRequestHandler<GetMyLatenessScoreQuery, Result<LatenessScoreDto>>
{
    private readonly ILateEarlyService _service;

    public GetMyLatenessScoreQueryHandler(ILateEarlyService service)
    {
        _service = service;
    }

    public Task<Result<LatenessScoreDto>> Handle(
        GetMyLatenessScoreQuery request, CancellationToken cancellationToken)
        => _service.GetMyScoreAsync(request.Year, request.Month, cancellationToken);
}
