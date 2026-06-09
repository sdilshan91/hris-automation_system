using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Exposes the tenant context resolved from the request host/header before login.
/// </summary>
[ApiController]
[Route("api/v1/tenant/context")]
[AllowAnonymous]
public sealed class TenantContextController : ControllerBase
{
    private readonly ITenantContext _tenantContext;

    public TenantContextController(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<TenantContextResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public IActionResult Get()
    {
        if (!_tenantContext.IsResolved)
        {
            return BadRequest(ApiResponse.Fail("Tenant context is not resolved."));
        }

        var response = new TenantContextResponse(
            _tenantContext.TenantId,
            _tenantContext.Subdomain,
            _tenantContext.Status,
            _tenantContext.Plan,
            _tenantContext.EnabledModules,
            _tenantContext.IsSystemContext,
            _tenantContext.LogoUrl,
            _tenantContext.PrimaryColor);

        return Ok(ApiResponse<TenantContextResponse>.Ok(response));
    }
}

public sealed record TenantContextResponse(
    Guid TenantId,
    string Subdomain,
    TenantStatus Status,
    string? Plan,
    IReadOnlyCollection<string> EnabledModules,
    bool IsSystemContext,
    string? LogoUrl,
    string? PrimaryColor);
