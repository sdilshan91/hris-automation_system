using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Read side of the payroll history + audit trail (US-PAY-012). Provides the payroll run history (FR-1/AC-1),
/// the payroll-specific audit trail with filtering (FR-4/AC-4), the per-run audit timeline (FR-6/AC-2) and
/// audit export to CSV/Excel/PDF (FR-5). Every read is tenant-scoped via ITenantContext + the EF global query
/// filter on the run/slip entities and an explicit tenant predicate on the audit_log table (AC-5).
///
/// <para>IMMUTABILITY (NFR-4/BR-2): there is intentionally NO update/delete operation here or on any audit
/// controller — audit records are append-only and tamper-proof.</para>
/// </summary>
public interface IPayrollAuditService
{
    /// <summary>FR-1/AC-1: paginated, filterable, sortable payroll run history.</summary>
    Task<Result<PayrollRunHistoryPageDto>> GetRunHistoryAsync(
        PayrollRunHistoryFilter filter, CancellationToken cancellationToken = default);

    /// <summary>FR-4/AC-4: the payroll audit trail filtered by date range / action / actor / resource.</summary>
    Task<Result<PayrollAuditPageDto>> GetAuditTrailAsync(
        PayrollAuditTrailFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-6/AC-2: the per-run audit timeline — every audit entry whose resource is this run (or a payslip of
    /// this run) in chronological order (oldest first).
    /// </summary>
    Task<Result<IReadOnlyList<PayrollAuditEntryDto>>> GetRunTimelineAsync(
        Guid runId, CancellationToken cancellationToken = default);

    /// <summary>FR-5: exports the filtered audit trail to CSV / Excel / PDF.</summary>
    Task<Result<PayrollAuditExportResult>> ExportAuditTrailAsync(
        PayrollAuditTrailFilter filter, PayrollExportFormat format, CancellationToken cancellationToken = default);
}
