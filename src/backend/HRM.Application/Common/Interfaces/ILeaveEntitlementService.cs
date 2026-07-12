using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using HRM.Domain.Entities;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for leave entitlement CRUD, rule resolution, and accrual processing (US-LV-002).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface ILeaveEntitlementService
{
    // ── Rules CRUD ─────────────────────────────────────────────────

    Task<Result<LeaveEntitlementRuleDto>> CreateRuleAsync(
        UpsertLeaveEntitlementRuleRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<LeaveEntitlementRuleDto>> UpdateRuleAsync(
        Guid ruleId,
        UpsertLeaveEntitlementRuleRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteRuleAsync(
        Guid ruleId,
        CancellationToken cancellationToken = default);

    Task<Result<LeaveEntitlementRuleDto>> GetRuleByIdAsync(
        Guid ruleId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LeaveEntitlementRuleDto>>> GetRulesAsync(
        Guid? leaveTypeId = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LeaveEntitlementRuleDto>>> BulkCreateRulesAsync(
        IReadOnlyList<UpsertLeaveEntitlementRuleRequest> requests,
        CancellationToken cancellationToken = default);

    // ── Overrides CRUD ─────────────────────────────────────────────

    Task<Result<LeaveEntitlementOverrideDto>> UpsertOverrideAsync(
        UpsertLeaveEntitlementOverrideRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteOverrideAsync(
        Guid overrideId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LeaveEntitlementOverrideDto>>> GetOverridesAsync(
        Guid? employeeId = null,
        Guid? leaveTypeId = null,
        int? leaveYear = null,
        CancellationToken cancellationToken = default);

    // ── Entitlement Resolution ─────────────────────────────────────

    /// <summary>
    /// Computes the effective entitlement for an employee for a given leave type and year.
    /// Resolution order: override > most-specific matching rule > leave type default.
    /// </summary>
    Task<Result<EffectiveEntitlementDto>> ComputeEffectiveEntitlementAsync(
        Guid employeeId,
        Guid leaveTypeId,
        int leaveYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// BUG-124: batched, set-based resolution of the <b>prorated</b> effective entitlement for every
    /// (employee × leave type) pair, for reporting/analytics at scale (NFR-1: 5,000 employees × 13 types).
    /// Runs ONE query for overrides and ONE query for active rules, then resolves each pair in memory via
    /// the same pure entitlement engine that <see cref="ComputeEffectiveEntitlementAsync"/> uses — so the
    /// per-pair <c>ProratedEntitlementDays</c> is byte-identical to calling it N times, without the N+1
    /// round-trips (it deliberately does NOT compute the ledger balance, which reports discard).
    /// The <paramref name="employees"/> and <paramref name="leaveTypes"/> are supplied by the caller (the
    /// report has already materialised them) and are not re-queried.
    /// </summary>
    Task<Dictionary<(Guid EmployeeId, Guid LeaveTypeId), decimal>> ComputeProratedEntitlementsBatchAsync(
        IReadOnlyList<Employee> employees,
        IReadOnlyList<LeaveType> leaveTypes,
        int year,
        CancellationToken cancellationToken = default);

    // ── Accrual Processing ─────────────────────────────────────────

    /// <summary>
    /// Recalculates entitlements and writes accrual ledger entries for affected employees.
    /// Called by Hangfire on rule changes and as a scheduled accrual job (AC-5, FR-5).
    /// </summary>
    Task ProcessAccrualsAsync(
        int leaveYear,
        Guid? leaveTypeId = null,
        CancellationToken cancellationToken = default);
}
