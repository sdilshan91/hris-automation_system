using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Queries;

/// <summary>
/// Lists holidays for the current tenant (US-LV-007 FR-6 / AC-4).
/// Supports a date range (from/to) or a year-based list, plus optional location/active filters.
/// </summary>
public sealed record GetHolidaysQuery(
    DateOnly? From,
    DateOnly? To,
    int? Year,
    Guid? LocationId,
    bool? ActiveOnly
) : IRequest<Result<IReadOnlyList<HolidayDto>>>;
