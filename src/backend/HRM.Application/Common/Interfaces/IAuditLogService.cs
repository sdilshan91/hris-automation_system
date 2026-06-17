using HRM.Application.Common.Models;
using HRM.Application.Features.AuditLog.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-008: Tenant-Admin audit-log READ + EXPORT service. Every method operates ONLY on the CURRENT tenant.
/// The <c>audit_logs</c> table has NO EF global query filter (TenantId is nullable and shared with system rows),
/// so this service filters EXPLICITLY by <see cref="ITenantContext.TenantId"/> on every query (AC-1/FR-1).
///
/// <para>The table is APPEND-ONLY by code convention (AC-5/NFR-3): there is intentionally NO update/delete
/// method here. DB-role UPDATE/DELETE revocation + PostgreSQL RLS are DEFERRED platform infra. Sensitive field
/// values are masked on read (FR-4) via <see cref="HRM.Application.Features.AuditLog.SensitiveFieldMasker"/>.</para>
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// AC-1/AC-2/FR-1: tenant-scoped, reverse-chronological, paginated, filterable audit list. Each row's
    /// before/after summary and the change summary are masked (FR-4). The page envelope also carries the
    /// tenant's read-only retention window (BR-5).
    /// </summary>
    Task<Result<AuditLogPageDto>> ListAsync(
        AuditLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-3/FR-2/FR-3/FR-4: full record incl. masked before/after JSON, IP, user agent, trace id. The visual
    /// diff (FR-3) is computed on the FRONTEND from the masked before/after — not server-side.
    /// </summary>
    Task<Result<AuditLogDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-NTF-005 FR-2: distinct actors (user id + name + email) that appear in THIS tenant's audit log and
    /// match <paramref name="search"/> (matched against name/email), capped at <paramref name="limit"/>. Powers
    /// the actor type-ahead. Tenant-scoped.
    /// </summary>
    Task<Result<IReadOnlyList<AuditLogActorDto>>> SearchActorsAsync(
        string? search, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// US-NTF-005 FR-2: the distinct action names + resource types present in THIS tenant's audit log, to
    /// populate the multi-select filter dropdowns. Tenant-scoped.
    /// </summary>
    Task<Result<AuditLogFilterOptionsDto>> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-4/BR-4/FR-5: export the SAME-filtered records as CSV or JSON (masked). The export action itself writes
    /// an AuditLog row with Action="AuditLog.Export" (BR-4). The synchronous small-dataset path is fully
    /// implemented; for datasets above the large-export threshold the async Hangfire+email path is DEFERRED, so
    /// the result still returns synchronously with <see cref="AuditLogExportResult.Deferred"/> flagged.
    /// </summary>
    Task<Result<AuditLogExportResult>> ExportAsync(
        AuditLogFilter filter, AuditLogExportFormat format, CancellationToken cancellationToken = default);
}
