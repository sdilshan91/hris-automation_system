using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// Command to clock the current employee in (US-ATT-001 FR-1..FR-5, AC-1..AC-5, BR-1..BR-6).
/// Coordinates / photo are conditional on the tenant policy. The source IP, user agent, and
/// optional Idempotency-Key are captured by the controller from the HTTP context (FR-5, NFR-4)
/// — the service layer has no HttpContext access.
/// </summary>
public sealed record ClockInCommand(
    decimal? Latitude,
    decimal? Longitude,
    string? PhotoUrl,
    string? Source,
    string? IpAddress,
    string? UserAgent,
    string? IdempotencyKey) : IRequest<Result<AttendanceLogDto>>;
