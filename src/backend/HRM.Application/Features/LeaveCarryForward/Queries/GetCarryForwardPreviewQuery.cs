using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveCarryForward.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveCarryForward.Queries;

/// <summary>
/// Read-only preview of year-end carry-forward / forfeiture for the closing
/// <paramref name="Year"/> (US-LV-008 FR-5, AC-5). Commits nothing.
/// </summary>
public sealed record GetCarryForwardPreviewQuery(int? Year)
    : IRequest<Result<IReadOnlyList<CarryForwardPreviewDto>>>;
