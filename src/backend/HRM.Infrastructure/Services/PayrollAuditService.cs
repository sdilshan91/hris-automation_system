using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Read side of US-PAY-012 — payroll run history (FR-1/AC-1), the payroll audit trail (FR-4/AC-4), the
/// per-run audit timeline (FR-6/AC-2) and audit export (FR-5).
///
/// <para>TENANT ISOLATION (AC-5): the run history reads <c>PayrollRuns</c> (a BaseEntity, so the EF global
/// query filter scopes it). The <c>audit_log</c> table is NOT a BaseEntity and has no global filter, so every
/// audit query here applies an EXPLICIT <c>TenantId == _tenantContext.TenantId</c> predicate — a user from
/// Tenant B can never see Tenant A's audit rows.</para>
///
/// <para>SCOPE: the audit trail is restricted to PAYROLL actions (resource types in the payroll catalog) so a
/// tenant's non-payroll audit entries (auth, employee, recruitment, …) do not leak into this payroll view.</para>
///
/// <para>IMMUTABILITY (NFR-4/BR-2): this service exposes only reads + export — no update/delete path exists.</para>
/// </summary>
public sealed class PayrollAuditService : IPayrollAuditService
{
    private const int MaxPageSize = 200;

    /// <summary>The payroll resource types that scope the audit trail (FR-4) — keeps non-payroll rows out.</summary>
    private static readonly string[] PayrollResourceTypes =
    {
        PayrollAuditAction.ResourceType.SalaryComponent,
        PayrollAuditAction.ResourceType.SalaryStructure,
        PayrollAuditAction.ResourceType.EmployeeSalary,
        PayrollAuditAction.ResourceType.StatutoryRule,
        PayrollAuditAction.ResourceType.PayrollRun,
        PayrollAuditAction.ResourceType.PayrollAdjustment,
        PayrollAuditAction.ResourceType.PayrollSlip,
    };

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PayrollAuditService> _logger;

    public PayrollAuditService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<PayrollAuditService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ── FR-1 / AC-1: payroll run history ─────────────────────────────────────

    public async Task<Result<PayrollRunHistoryPageDto>> GetRunHistoryAsync(
        PayrollRunHistoryFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollRunHistoryPageDto>.Failure("Tenant context is not resolved.", 400);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > MaxPageSize ? 25 : filter.PageSize;

        // PayrollRuns is a BaseEntity → tenant-scoped + soft-delete-scoped by the EF global query filter (AC-5).
        var query = _dbContext.PayrollRuns.AsNoTracking().AsQueryable();

        if (filter.Year is { } year)
            query = query.Where(r => r.PayYear == year);
        if (filter.Month is { } month)
            query = query.Where(r => r.PayMonth == month);
        if (!string.IsNullOrWhiteSpace(filter.Status)
            && Enum.TryParse<HRM.Domain.Enums.PayrollRunStatus>(filter.Status, ignoreCase: true, out var st))
            query = query.Where(r => r.Status == st);

        var totalCount = await query.CountAsync(cancellationToken);

        query = ApplySort(query, filter.SortBy, filter.Descending);

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new PayrollRunHistoryItemDto(
            r.Id, r.PayMonth, r.PayYear, $"{r.PayYear:D4}-{r.PayMonth:D2}", r.Status.ToString(),
            r.ProcessedEmployees, r.TotalNet, r.TotalGross, r.TotalDeductions,
            r.InitiatedBy, r.InitiatedAt, r.ApprovedBy, r.ApprovedAt, r.FinalizedAt)).ToList();

        return Result<PayrollRunHistoryPageDto>.Success(
            new PayrollRunHistoryPageDto(items, totalCount, page, pageSize));
    }

    private static IQueryable<PayrollRun> ApplySort(IQueryable<PayrollRun> query, string? sortBy, bool desc)
    {
        switch ((sortBy ?? "period").Trim().ToLowerInvariant())
        {
            case "status":
                return desc ? query.OrderByDescending(r => r.Status) : query.OrderBy(r => r.Status);
            case "net":
                return desc ? query.OrderByDescending(r => r.TotalNet) : query.OrderBy(r => r.TotalNet);
            case "initiated":
                return desc ? query.OrderByDescending(r => r.InitiatedAt) : query.OrderBy(r => r.InitiatedAt);
            case "finalized":
                return desc ? query.OrderByDescending(r => r.FinalizedAt) : query.OrderBy(r => r.FinalizedAt);
            default: // period
                return desc
                    ? query.OrderByDescending(r => r.PayYear).ThenByDescending(r => r.PayMonth).ThenByDescending(r => r.InitiatedAt)
                    : query.OrderBy(r => r.PayYear).ThenBy(r => r.PayMonth).ThenBy(r => r.InitiatedAt);
        }
    }

    // ── FR-4 / AC-4: payroll audit trail ─────────────────────────────────────

    public async Task<Result<PayrollAuditPageDto>> GetAuditTrailAsync(
        PayrollAuditTrailFilter filter, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollAuditPageDto>.Failure("Tenant context is not resolved.", 400);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > MaxPageSize ? 50 : filter.PageSize;

        var query = BuildTrailQuery(filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result<PayrollAuditPageDto>.Success(
            new PayrollAuditPageDto(rows.Select(ToDto).ToList(), totalCount, page, pageSize));
    }

    /// <summary>
    /// Builds the tenant-scoped, payroll-only audit-trail query (AC-5). The audit_log table has no EF global
    /// query filter, so tenant scoping is an EXPLICIT predicate. Restricted to the payroll resource catalog.
    /// </summary>
    private IQueryable<AuditLog> BuildTrailQuery(PayrollAuditTrailFilter filter)
    {
        var tenantId = _tenantContext.TenantId;

        var query = _dbContext.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .Where(a => a.ResourceType != null && PayrollResourceTypes.Contains(a.ResourceType));

        if (filter.FromUtc is { } from)
            query = query.Where(a => a.CreatedAt >= from);
        if (filter.ToUtc is { } to)
            query = query.Where(a => a.CreatedAt <= to);
        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(a => a.Action == action);
        }
        if (filter.ActorUserId is { } actor)
            query = query.Where(a => a.UserId == actor);
        if (!string.IsNullOrWhiteSpace(filter.ResourceType))
        {
            var rt = filter.ResourceType.Trim();
            query = query.Where(a => a.ResourceType == rt);
        }
        if (!string.IsNullOrWhiteSpace(filter.ResourceId))
        {
            var rid = filter.ResourceId.Trim();
            query = query.Where(a => a.ResourceId == rid);
        }

        return query;
    }

    // ── FR-6 / AC-2: per-run audit timeline ──────────────────────────────────

    public async Task<Result<IReadOnlyList<PayrollAuditEntryDto>>> GetRunTimelineAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<PayrollAuditEntryDto>>.Failure("Tenant context is not resolved.", 400);

        // Tenant-scoped existence check via the global filter — a cross-tenant run is invisible ⇒ 404 (AC-5).
        var runExists = await _dbContext.PayrollRuns.AsNoTracking().AnyAsync(r => r.Id == runId, cancellationToken);
        if (!runExists)
            return Result<IReadOnlyList<PayrollAuditEntryDto>>.Failure("Payroll run not found.", 404, "run_not_found");

        // The run's own payslip ids (tenant-scoped) — PayslipPDF.Generated / PayslipEmail.Sent reference slips.
        var slipIds = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .Select(s => s.Id.ToString())
            .ToListAsync(cancellationToken);

        var runIdStr = runId.ToString();
        var tenantId = _tenantContext.TenantId;

        var rows = await _dbContext.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .Where(a =>
                (a.ResourceType == PayrollAuditAction.ResourceType.PayrollRun && a.ResourceId == runIdStr)
                || (a.ResourceType == PayrollAuditAction.ResourceType.PayrollSlip
                    && a.ResourceId != null && slipIds.Contains(a.ResourceId)))
            .OrderBy(a => a.CreatedAt) // AC-2: chronological order (oldest first).
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<PayrollAuditEntryDto>>.Success(rows.Select(ToDto).ToList());
    }

    // ── FR-5: audit export (reuses the US-PAY-009 PayrollReportRenderer) ─────

    public async Task<Result<PayrollAuditExportResult>> ExportAuditTrailAsync(
        PayrollAuditTrailFilter filter, PayrollExportFormat format, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollAuditExportResult>.Failure("Tenant context is not resolved.", 400);

        // Export the full filtered set (no paging), capped defensively so a runaway filter can't OOM.
        var rows = await BuildTrailQuery(filter)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50_000)
            .ToListAsync(cancellationToken);

        // Reuse the pure US-PAY-009 PayrollReportRenderer (CSV/Excel/PDF) by projecting the audit rows into the
        // generic columnar PayrollReportResult — no new export infra introduced (FR-5).
        var report = new PayrollReportResult
        {
            ReportType = "PayrollAuditTrail",
            Title = "Payroll Audit Trail",
            PayMonth = filter.FromUtc?.Month ?? DateTime.UtcNow.Month,
            PayYear = filter.FromUtc?.Year ?? DateTime.UtcNow.Year,
            // ISSUE-186: include the Before/After JSON (FR-5/BR-4) so the exported file conveys WHAT changed,
            // not merely that something changed — the whole point of an offline audit artifact. The columns are
            // already on the entity and in the JSON API's PayrollAuditEntryDto; they were just missing here.
            Columns = new[] { "Timestamp (UTC)", "Action", "Resource Type", "Resource Id", "Actor User Id", "Actor Emp No", "IP Address", "Trace Id", "Before", "After" },
            Rows = rows.Select(a => new PayrollReportRow
            {
                Cells = new[]
                {
                    a.CreatedAt.ToString("u"),
                    a.Action ?? a.EventType,
                    a.ResourceType ?? string.Empty,
                    a.ResourceId ?? string.Empty,
                    a.UserId?.ToString() ?? string.Empty,
                    a.ActorEmployeeNo ?? string.Empty,
                    a.IpAddress ?? string.Empty,
                    a.TraceId ?? string.Empty,
                    a.Before ?? string.Empty,
                    a.After ?? string.Empty,
                },
            }).ToList(),
            TotalCount = rows.Count,
        };

        var (content, fileName, contentType) = PayrollReportRenderer.Render(format, report);
        return Result<PayrollAuditExportResult>.Success(new PayrollAuditExportResult(content, fileName, contentType));
    }

    private static PayrollAuditEntryDto ToDto(AuditLog a) => new(
        a.Id,
        a.TenantId,
        a.CreatedAt,
        a.UserId,
        a.ActorEmployeeNo,
        a.Action ?? a.EventType,
        a.ResourceType,
        a.ResourceId,
        a.Before,
        a.After,
        a.IpAddress,
        a.UserAgent,
        a.TraceId);
}
