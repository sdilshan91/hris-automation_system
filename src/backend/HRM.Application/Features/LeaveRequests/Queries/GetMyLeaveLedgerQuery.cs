using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

/// <summary>
/// Query for the current employee's full ledger transaction log for one leave type and year
/// (US-LV-006 FR-3, AC-2). Returns accruals, usages, adjustments, carry-forwards, and
/// expirations ordered chronologically. Year defaults to the current leave year when null.
/// </summary>
public sealed record GetMyLeaveLedgerQuery(Guid LeaveTypeId, int? Year)
    : IRequest<Result<IReadOnlyList<LeaveLedgerEntryDto>>>;
