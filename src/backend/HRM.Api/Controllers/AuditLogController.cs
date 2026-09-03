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
/// (AC-1/FR-1), with the GAP-006 global query filter as a second layer beneath it. There is no tenant id in
/// any route or body.
///
/// <para>Reads (list + detail) require <c>Audit.View</c> (Tenant Owner, Tenant Admin, AND the read-only
/// Auditor). Export requires <c>Audit.Export</c> (Tenant Owner + Tenant Admin ONLY — the Auditor is read-only
/// and CANNOT export, FR-7).</para>
///
/// <para>APPEND-ONLY (AC-5/NFR-3) — what is true, and where. UNCONDITIONALLY: there is intentionally NO
/// update or delete endpoint here, so no API request can rewrite audit history in ANY environment, and the
/// retention purge — the one legitimate deleter — runs on the privileged connection.</para>
///
/// <para>CONDITIONALLY, GAP-005 adds a second line of defence at the DATABASE layer, which does not depend on
/// this code staying correct: <c>Rls/roles.sql</c> revokes UPDATE and DELETE on <c>audit_logs</c> and
/// <c>employee_field_audit_logs</c> from the runtime <c>hrm_app</c> role, so a SQL-injection foothold or a
/// stray <c>ExecuteDelete</c> on the runtime connection is refused by Postgres (SQLSTATE 42501). This process
/// CANNOT verify that defence is present, so do not assume it. It holds in an environment only where BOTH:
/// <list type="number">
///   <item><description><c>roles.sql</c> has actually been applied to that database. It is an ops/bootstrap
///     script run once by a DBA and deliberately NOT executed by the app or by EF migrations — the app must
///     never own role DDL — so whether it ran is an operational fact, not a code fact.</description></item>
///   <item><description><c>ConnectionStrings:DefaultConnection</c> really authenticates as <c>hrm_app</c>.
///     The revoke binds to that role and nothing else: a superuser bypasses privilege checks entirely, and
///     <c>hrm_owner</c> deliberately KEEPS DELETE so the purge works.</description></item>
/// </list>
/// Condition (2) demonstrably does NOT hold on the committed defaults: <c>appsettings.Development.json</c>
/// points <c>DefaultConnection</c> at <c>Username=developer</c>, not <c>hrm_app</c>, so the revoke cannot
/// apply there whatever the DBA did (and per <c>appsettings.json</c> that local role is additionally a
/// superuser, which bypasses privilege checks outright).
/// Both conditions are switched on together by the increment-3 flip documented in <c>appsettings.json</c>
/// (<c>_comment_PrivilegedConnection</c>), which also sets <c>Rls:Enabled=true</c> — note that flag is an
/// INDICATOR of the flip, not the mechanism: it gates the tenant GUC, not table privileges.</para>
///
/// <para>So: append-only is ENFORCED by the database wherever the platform is fully provisioned, and
/// CONVENTIONAL (no mutating endpoint + a privileged-only purge path) everywhere else.
/// <c>RlsIsolationPostgresTests.AuditTables_AreAppendOnly_ForTheRuntimeRole_ButPurgeableByOwner_GAP005</c>
/// proves the revoke actually bites on a real Postgres, and — because that fixture EXTRACTS the REVOKE
/// statements from <c>roles.sql</c> rather than restating them — that the shipped script still contains them.
/// No test can prove a given environment was bootstrapped.</para>
///
/// <para>A <c>tenant_isolation</c> RLS policy also exists on the table (shipped dormant; enabled by the same
/// increment-3 flip).</para>
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
    /// Filters (all optional, AND across filter types): startDate, endDate, actorUserId, action, resourceType,
    /// search. US-NTF-005 FR-2 adds the repeatable multi-select params <c>actions</c> and <c>resourceTypes</c>
    /// (IN-list, OR within a group); the singular <c>action</c>/<c>resourceType</c> still work (back-compat) and
    /// are folded into their group.
    ///
    /// <para>SIDE EFFECT (US-NTF-005 FR-9/BR-5): viewing the list writes ONE Action="AuditLog.View" meta-audit
    /// row recording the actor + tenant + request IP/UA/trace + applied filters.</para>
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
        [FromQuery] string[]? actions = null,
        [FromQuery] string[]? resourceTypes = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new ListAuditLogsQuery(
                page, pageSize, startDate, endDate, actorUserId, action, resourceType, search,
                actions, resourceTypes),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<AuditLogPageDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/audit-logs/actors?search={q} — US-NTF-005 FR-2: distinct actors (user id + name +
    /// email) that appear in THIS tenant's audit log, matching the type-ahead query, capped at 20. Tenant-scoped.
    /// </summary>
    [HttpGet("actors")]
    [RequirePermission("Audit.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AuditLogActorDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Actors(
        [FromQuery] string? search = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new SearchAuditLogActorsQuery(search, limit), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<AuditLogActorDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/audit-logs/filter-options — US-NTF-005 FR-2: distinct action names + resource types
    /// present in THIS tenant's audit log, to populate the multi-select filter dropdowns. Tenant-scoped.
    /// </summary>
    [HttpGet("filter-options")]
    [RequirePermission("Audit.View")]
    [ProducesResponseType(typeof(ApiResponse<AuditLogFilterOptionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FilterOptions(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAuditLogFilterOptionsQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<AuditLogFilterOptionsDto>.Ok(result.Value!));
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
