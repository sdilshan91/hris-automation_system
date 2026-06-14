using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// Command for a manager to bulk-approve multiple pending regularizations in one action
/// (US-ATT-004 BR-7). Each id is independently authorized + payroll-lock-checked; per-item results
/// are returned. Comment is optional and applied to every item (BR-2).
/// </summary>
public sealed record BulkApproveRegularizationsCommand(
    IReadOnlyList<Guid> RegularizationIds,
    string? Comment) : IRequest<Result<BulkApproveRegularizationResult>>;
