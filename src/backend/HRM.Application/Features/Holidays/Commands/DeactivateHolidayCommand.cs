using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Holidays.Commands;

/// <summary>
/// Soft-deactivates a holiday (US-LV-007 BR-4).
/// </summary>
public sealed record DeactivateHolidayCommand(Guid Id) : IRequest<Result>;
