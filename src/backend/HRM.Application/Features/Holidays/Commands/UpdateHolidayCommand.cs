using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Commands;

/// <summary>
/// Updates an existing holiday (US-LV-007 FR-1).
/// </summary>
public sealed record UpdateHolidayCommand(
    Guid Id,
    string Name,
    DateOnly Date,
    string Type,
    Guid? LocationId,
    string? Description,
    bool IsRecurring
) : IRequest<Result<HolidayDto>>;
