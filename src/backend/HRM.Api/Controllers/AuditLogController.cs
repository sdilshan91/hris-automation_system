using HRM.Application.DTOs;
using HRM.Application.Features.AuditLog.Commands;
using HRM.Application.Features.AuditLog.DTOs;
using HRM.Application.Features.AuditLog.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// US-ADM-008: Tenant-scoped AUDIT LOG viewing + export for a Tenant Admin / Tenant Owner / Auditor (BR-1). All
/// operations target ONLY the current tenant — the service filters EXPLICITLY by ITenantContext.TenantId
/// (audit_logs has no EF global query filter; AC-1/FR-1). There is no tenant id in any route or body.
///
/// <para>Reads (list + detail) require <c>Audit.View</c> (Tenant Owner, Tenant Admin, AND the read-only
/// Auditor). Export requires <c>Audit.Export</c> (Tenant Owner + Tenant Admin ONLY — the Auditor is read-only
/// and CANNOT export, FR-7). The table is append-only by code convention (AC-5/NFR-3): there is intentionally NO
/// update/delete endpoint. PostgreSQL RLS + DB-role UPDATE/DELETE revocation are DEFERRED platform infra.</para>
/// </summary>
[ApiController]
[Route("api/v1/tenant/audit-logs")]
[Authorize]
public sealed class AuditLogController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuditLogController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET /api/v1/tenant/audit-logs — paginated, reverse-chronological, filterable audit list (AC-1/AC-2/FR-1).
    /// Filters (all optional, AND semantics): startDate, endDate, actorUserId, action, resourceType, search.
    /// </summary>
    [HttpGet]
    [RequirePermission("Audit.View")]
    [ProducesResponseType(typeof(ApiResponse<AuditLogPageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] Guid? actorUserId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? resourceType = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListAuditLogsQuery(page, pageSize, startDate, endDate, actorUserId, action, resourceType, search),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<AuditLogPageDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/audit-logs/{id} — full record incl. MASKED before/after JSON, IP, user agent, trace id
    /// (AC-3/FR-2/FR-4). The visual diff (FR-3) is computed on the FRONTEND from the masked before/after.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Audit.View")]
    [ProducesResponseType(typeof(ApiResponse<AuditLogDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAuditLogQuery(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<AuditLogDetailDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/audit-logs/export — export the SAME-filtered records as CSV (default) or JSON, masked
    /// (AC-4/§7). The export action itself is audited as "AuditLog.Export" (BR-4). Returns the file inline for
    /// immediate download; the async large-dataset email path is DEFERRED. Requires <c>Audit.Export</c> —
    /// Auditor CANNOT export (FR-7).
    /// </summary>
    [HttpGet("export")]
    [RequirePermission("Audit.Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Export(
        [FromQuery] string format = "csv",
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] Guid? actorUserId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? resourceType = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => ExecuteExport(format, startDate, endDate, actorUserId, action, resourceType, search, cancellationToken);

    /// <summary>
    /// POST /api/v1/tenant/audit-logs/export — same export as the GET, taking the filters + format in the body
    /// (the UI confirmation dialog uses this). Requires <c>Audit.Export</c>.
    /// </summary>
    [HttpPost("export")]
    [RequirePermission("Audit.Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportPost(
        [FromBody] AuditLogExportRequest request, CancellationToken cancellationToken)
        => ExecuteExport(
            request.Format, request.StartDate, request.EndDate, request.ActorUserId,
            request.Action, request.ResourceType, request.Search, cancellationToken);

    private async Task<IActionResult> ExecuteExport(
        string? format,
        DateTime? startDate,
        DateTime? endDate,
        Guid? actorUserId,
        string? action,
        string? resourceType,
        string? search,
        CancellationToken cancellationToken)
    {
        if (!TryParseFormat(format, out var exportFormat))
            return StatusCode(400, ApiResponse.Fail($"Unsupported export format '{format}'. Use 'csv' or 'json'.", "invalid_format"));

        var result = await _mediator.Send(
            new ExportAuditLogsCommand(
                exportFormat, startDate, endDate, actorUserId, action, resourceType, search),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var export = result.Value!;
        // FR-5: even on the (deferred) large-dataset path we currently stream synchronously; signal it via a
        // response header so the client can surface the "we would normally email this link" UX without changing
        // the immediate-download contract.
        Response.Headers["X-Audit-Export-Deferred"] = export.Deferred ? "true" : "false";
        return File(export.Content, export.ContentType, export.FileName);
    }

    private static bool TryParseFormat(string? format, out AuditLogExportFormat exportFormat)
    {
        switch ((format ?? "csv").Trim().ToLowerInvariant())
        {
            case "csv":
                exportFormat = AuditLogExportFormat.Csv;
                return true;
            case "json":
                exportFormat = AuditLogExportFormat.Json;
                return true;
            default:
                exportFormat = AuditLogExportFormat.Csv;
                return false;
        }
    }
}

/// <summary>US-ADM-008 AC-4: the POST /export body — filters + format (default CSV).</summary>
public sealed record AuditLogExportRequest(
    string Format,
    DateTime? StartDate,
    DateTime? EndDate,
    Guid? ActorUserId,
    string? Action,
    string? ResourceType,
    string? Search);
