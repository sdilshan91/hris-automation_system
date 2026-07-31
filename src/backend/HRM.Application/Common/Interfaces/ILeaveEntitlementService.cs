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

    // ── BUG-291 exposure report (READ-ONLY) ────────────────────────

    /// <summary>
    /// BUG-291 exposure report. Returns, for the current tenant only, every (employee × leave type) whose
    /// LEGACY full-year accrual (an <c>Accrual</c> ledger row with <c>AccrualPeriod == NULL</c>) over-credited
    /// relative to what its configured <c>AccrualFrequency</c> should have accrued as of
    /// <paramref name="asOfDate"/> — i.e. <c>proratedAnnual × elapsedPeriods / periodsPerYear</c>, using the
    /// SAME period maths as the merged BUG-291 fix. Strictly read-only: it writes nothing and adjusts no
    /// balances (correcting an over-credit downward is an employee-detriment decision made case-by-case).
    /// Only Monthly/Quarterly types can appear; Yearly/Upfront credit the whole year in one period and are
    /// excluded. Employees whose accrual is already period-tagged (post-fix) are excluded — the fix already
    /// handles them, so counting them would double-report. Only rows with a strictly positive over-credit are
    /// returned. Tenant-scoped under the normal EF query filters (no <c>IgnoreQueryFilters</c>).
    /// </summary>
    Task<Result<AccrualOverCreditExposureReportDto>> GetAccrualOverCreditExposureAsync(
        DateOnly asOfDate,
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

    /// <summary>
    /// BUG-118 (US-LV-002 AC-5): recalculates ALREADY-ACCRUED employees' balances after an entitlement rule is
    /// edited, writing an <c>Adjusted</c> ledger delta (new-rule target − already-granted) per employee×type.
    /// Unlike <see cref="ProcessAccrualsAsync"/> (insert-only; skips already-accrued employees), this moves
    /// existing balances. Idempotent, override-safe, and leaves manual adjustments + not-yet-accrued employees
    /// untouched. Runs inside a tenant scope (enqueued via <see cref="ILeaveEntitlementRecalcJobScheduler"/>).
    /// </summary>
    Task RecalculateEntitlementsAsync(
        int leaveYear,
        Guid? leaveTypeId = null,
        CancellationToken cancellationToken = default);
}
