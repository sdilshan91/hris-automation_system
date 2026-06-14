using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using MediatR;

namespace HRM.Application.Features.Holidays.Queries;

/// <summary>
/// Gets a single holiday by ID (US-LV-007 FR-1).
/// </summary>
public sealed record GetHolidayByIdQuery(Guid Id) : IRequest<Result<HolidayDto>>;
