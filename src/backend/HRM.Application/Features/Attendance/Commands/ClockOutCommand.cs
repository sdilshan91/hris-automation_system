using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// Command to clock the current employee out and auto-calculate work hours
/// (US-ATT-002 FR-1..FR-4/FR-6/FR-7, AC-1..AC-5, BR-1..BR-4/BR-6). Coordinates are conditional on
/// the tenant geolocation policy (AC-5). The source IP and user agent are captured by the controller
/// from the HTTP context (§7) — the service layer has no HttpContext access.
/// </summary>
public sealed record ClockOutCommand(
    decimal? Latitude,
    decimal? Longitude,
    string? IpAddress,
    string? UserAgent) : IRequest<Result<ClockOutResultDto>>;
