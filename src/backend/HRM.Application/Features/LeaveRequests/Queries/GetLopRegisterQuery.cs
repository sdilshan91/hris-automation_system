using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Read-only cross-employee LOP register for the HR management screen (US-LV-011 FR-5). Lists every
/// effective LOP occurrence in the period across employees, each row carrying the employee's identity.
/// Optionally narrowed by <paramref name="EmployeeIds"/>. Delegates to <c>ILopService.GetLopRegisterAsync</c>.
/// </summary>
public sealed record GetLopRegisterQuery(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? EmployeeIds = null)
    : IRequest<Result<IReadOnlyList<LopRegisterEntryDto>>>;
