using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// Command to submit an attendance regularization request for the acting employee
/// (US-ATT-003 FR-1/FR-2/FR-5/FR-6/FR-7, AC-1..AC-5, BR-2..BR-7). Carries the request shape; the
/// acting employee and tenant are resolved server-side.
/// </summary>
public sealed record SubmitRegularizationCommand(
    SubmitRegularizationRequest Request) : IRequest<Result<RegularizationDto>>;
