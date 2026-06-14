using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// Query for the acting employee's current clock-in status (US-ATT-001). Returns whether the
/// employee has an open AttendanceLog, the tenant's RequireGeolocation flag (BR-2), and null shift
/// fields (US-ATT-005 not built). Tenant-scoped via the resolved tenant context; the acting
/// employee is resolved from the JWT. Carries no parameters.
/// </summary>
public sealed record GetClockStatusQuery : IRequest<Result<ClockStatusDto>>;
