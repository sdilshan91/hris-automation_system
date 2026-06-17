using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.DataExport.DTOs;
using HRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// US-ADM-010: Tenant-Admin on-demand DATA EXPORT (GDPR Article 20 portability + termination-grace extraction).
/// All operations target ONLY the current tenant — the service runs in the resolved-tenant context and the export
/// is always scoped to <c>ITenantContext.TenantId</c> (AC-5); a client-supplied foreign tenant id is ignored.
///
/// <para>Initiation + download + history require <c>Tenant.ExportData</c> (Tenant Owner + Tenant Admin). The
/// System-Admin cross-tenant variant lives on <see cref="AdminDataExportController"/> under
/// <c>api/v1/system/...</c> and requires the system-level <c>Tenant.ExportDataSystem</c>.</para>
/// </summary>
[ApiController]
[Route("api/v1/tenant/data-exports")]
[Authorize]
public sealed class DataExportController : ControllerBase
{
    private readonly ITenantDataExportService _service;

    public DataExportController(ITenantDataExportService service) => _service = service;

    /// <summary>
    /// POST /api/v1/tenant/data-exports — initiate a full or partial export of the CURRENT tenant's data (AC-1).
    /// Body: { scope?, entities?, delimiter?, dateFormat? }. Returns the export id + the "you'll get an email" note.
    /// </summary>
    [HttpPost]
    [RequirePermission("Tenant.ExportData")]
    [ProducesResponseType(typeof(ApiResponse<ExportInitiatedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Initiate(
        [FromBody] ExportInitiateRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.InitiateAsync(request, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status202Accepted, ApiResponse<ExportInitiatedDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/data-exports — export history for the current tenant (date, scope, status, size,
    /// download availability) (FR-9 / §8).
    /// </summary>
    [HttpGet]
    [RequirePermission("Tenant.ExportData")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExportRequestDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _service.ListAsync(cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<ExportRequestDto>>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/data-exports/{id} — single export status (FR-9 in-progress status).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("Tenant.ExportData")]
    [ProducesResponseType(typeof(ApiResponse<ExportRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(id, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ExportRequestDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/data-exports/{id}/download — serves the bundle ZIP if Completed and within the 72h
    /// window (AC-3/FR-7). After expiry → 410 Gone (the file is considered deleted). The download is audited.
    /// </summary>
    [HttpGet("{id:guid}/download")]
    [RequirePermission("Tenant.ExportData")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.DownloadAsync(id, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var download = result.Value!;
        return File(download.Content, download.ContentType, download.FileName);
    }
}

/// <summary>
/// US-ADM-010 (AC-6): System Admin cross-tenant export initiation. Runs in the SYSTEM/admin context and targets
/// an EXPLICIT tenant id; a Suspended tenant is allowed here (BR-2). Gated by the system-level
/// <c>Tenant.ExportDataSystem</c> — only the platform SystemAdmin role holds it.
/// </summary>
[ApiController]
[Route("api/v1/system/tenants/{tenantId:guid}/data-exports")]
[Authorize]
public sealed class AdminDataExportController : ControllerBase
{
    private readonly ITenantDataExportService _service;

    public AdminDataExportController(ITenantDataExportService service) => _service = service;

    /// <summary>
    /// POST /api/v1/system/tenants/{tenantId}/data-exports — initiate an export for ANY tenant (AC-6/BR-2).
    /// </summary>
    [HttpPost]
    [RequirePermission("Tenant.ExportDataSystem")]
    [ProducesResponseType(typeof(ApiResponse<ExportInitiatedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> InitiateForTenant(
        [FromRoute] Guid tenantId, [FromBody] ExportInitiateRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.InitiateForTenantAsync(tenantId, request, cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status202Accepted, ApiResponse<ExportInitiatedDto>.Ok(result.Value!));
    }
}
