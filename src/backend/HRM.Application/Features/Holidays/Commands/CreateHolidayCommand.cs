using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Commands;

/// <summary>
/// Creates a new holiday in the current tenant (US-LV-007 AC-1).
/// </summary>
public sealed record CreateHolidayCommand(
    string Name,
    DateOnly Date,
    string Type,
    Guid? LocationId,
    string? Description,
    bool IsRecurring
) : IRequest<Result<HolidayDto>>;
