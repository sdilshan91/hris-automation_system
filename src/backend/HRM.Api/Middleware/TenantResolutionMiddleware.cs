using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HRM.Api.Middleware;

/// <summary>
/// Middleware that resolves the tenant from the request subdomain.
/// Runs early in the pipeline before authentication/authorization.
/// Populates ITenantContext for downstream use.
///
/// Supports:
/// - Standard subdomain: acme.yourhrm.com -> tenant "acme"
/// - System context: admin.yourhrm.com -> IsSystemContext = true
/// - Reserved subdomains: www, api, etc. -> skip tenant resolution
/// - Dev mode: acme.localhost:5001 -> tenant "acme"
/// - Unknown subdomains -> 404
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private const string DevTenantHeader = "X-Tenant-Subdomain";
    private const string TenantCacheKeyPrefix = "t:subdomain:";
    private const string WorkspaceNotFoundHtml = """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Workspace not found</title></head>
        <body><main><h1>This workspace does not exist.</h1></main></body>
        </html>
        """;

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly HashSet<string> _reservedSubdomains;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
        _reservedSubdomains = _configuration
            .GetSection("Platform:ReservedSubdomains")
            .Get<string[]>()?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? ["www", "api", "admin", "app", "mail", "status", "docs", "help", "support", "static", "cdn", "dev", "stage", "prod", "test", "qa"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip tenant resolution for health check and swagger
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/hangfire", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var host = context.Request.Host.Host;
        var baseDomain = _configuration["Platform:BaseDomain"] ?? "yourhrm.com";

        var subdomain = ExtractSubdomain(host, baseDomain);

        // Dev-only fallback: allow the SPA running on plain "localhost" to declare its
        // tenant via X-Tenant-Subdomain header, so developers don't need a hosts-file
        // entry for *.localhost. Disabled outside Development.
        if (string.IsNullOrEmpty(subdomain)
            && _environment.IsDevelopment()
            && context.Request.Headers.TryGetValue(DevTenantHeader, out var headerValue))
        {
            var headerSubdomain = headerValue.ToString().Trim();
            if (!string.IsNullOrEmpty(headerSubdomain))
            {
                subdomain = headerSubdomain;
            }
        }

        // No subdomain (e.g., yourhrm.com or localhost)
        if (string.IsNullOrEmpty(subdomain))
        {
            await _next(context);
            return;
        }

        // System context
        if (subdomain.Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
            tenantContext.SetSystemContext();
            await _next(context);
            return;
        }

        // Reserved subdomains - skip tenant resolution
        if (_reservedSubdomains.Contains(subdomain))
        {
            await _next(context);
            return;
        }

        // Validate subdomain format (3-63 chars, lowercase alphanumeric + hyphens, no leading/trailing hyphens)
        if (!IsValidSubdomain(subdomain))
        {
            await WriteWorkspaceNotFoundAsync(context);
            return;
        }

        var tenant = await ResolveTenantAsync(context, subdomain.ToLowerInvariant());

        if (tenant is null)
        {
            _logger.LogWarning("Tenant not found for subdomain: {Subdomain}", subdomain);
            await WriteWorkspaceNotFoundAsync(context);
            return;
        }

        // D3 (ISSUE-358): CustomDomain feature seam. A host that is NOT under the platform base domain (and not
        // localhost/*.localhost) is a CUSTOM DOMAIN — it may only serve a tenant whose plan includes the
        // CustomDomain flag, otherwise refuse. Fail-open: a tenant whose plan/flags can't be read (null) is never
        // locked out. Subdomain hosts (under the base domain), plain localhost, the dev X-Tenant-Subdomain header
        // path on localhost, reserved subdomains and the admin.* system context are NOT custom-domain hosts (or
        // short-circuit above), so their behaviour is untouched. Today the only way a tenant resolves on a custom
        // host is the dev header, so this is inert in production until the custom-domain feature adds host→tenant
        // mapping — exactly the pre-registered-seam pattern.
        if (IsCustomDomainHost(host, baseDomain)
            && !PlanFeatureFlagKeys.IsFeatureEnabled(tenant.FeatureFlags, PlanFeatureFlagKeys.CustomDomain))
        {
            _logger.LogInformation(
                "Refused custom-domain host {Host} for tenant {TenantId}: plan lacks the CustomDomain feature.",
                host, tenant.Id);
            await EntitlementResponse.WriteForbiddenAsync(context,
                "This workspace's plan does not include custom domains.", "custom_domain_not_entitled");
            return;
        }

        // Populate tenant context
        var tenantCtx = context.RequestServices.GetRequiredService<ITenantContext>();
        tenantCtx.SetTenant(
            tenant.Id,
            tenant.Subdomain,
            tenant.Status,
            tenant.Plan,
            tenant.EnabledModules,
            tenant.LogoUrl,
            tenant.PrimaryColor);
        // D3: carry the plan feature flags on the same resolved context (cached path + DB path agree — both go
        // through ResolveTenantAsync/ResolvedTenant). null = fail-open.
        tenantCtx.SetFeatureFlags(tenant.FeatureFlags);

        // US-PLT-004 (item 2): stamp the current trace span with tenant tags so every downstream span in the
        // request carries them. Null-safe: Activity.Current is null when OTel is inert (the default) — the
        // null-conditional IS the whole guard, so this never throws and never branches on OTel state.
        System.Diagnostics.Activity.Current?.SetTag("tenant.id", tenant.Id);
        System.Diagnostics.Activity.Current?.SetTag("tenant.subdomain", tenant.Subdomain);

        // Add tenant_id to Serilog log context
        using (Serilog.Context.LogContext.PushProperty("TenantId", tenant.Id))
        using (Serilog.Context.LogContext.PushProperty("TenantSubdomain", tenant.Subdomain))
        using (Serilog.Context.LogContext.PushProperty("tenant_id", tenant.Id))
        using (Serilog.Context.LogContext.PushProperty("tenant_subdomain", tenant.Subdomain))
        {
            await _next(context);
        }
    }

    private static string? ExtractSubdomain(string host, string baseDomain)
    {
        // Handle localhost with port (acme.localhost:5001)
        host = host.Split(':')[0];

        if (baseDomain.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            // Dev mode: acme.localhost -> "acme"
            if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            {
                return host[..^".localhost".Length];
            }
            return null;
        }

        // Production: acme.yourhrm.com -> "acme"
        var suffix = $".{baseDomain}";
        if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return host[..^suffix.Length];
        }

        return null;
    }

    // D3 (ISSUE-358): true when the request host is a CUSTOM DOMAIN — a real host that is neither the platform
    // base domain, a subdomain of it, nor a localhost/loopback dev host. Kept deliberately conservative so the
    // existing resolution paths (subdomain, plain localhost, *.localhost, IP/loopback) are never treated as custom.
    private static bool IsCustomDomainHost(string host, string baseDomain)
    {
        host = host.Split(':')[0];

        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return false;

        // Apex or subdomain of the platform base domain ⇒ NOT custom (this is our own domain).
        if (host.Equals(baseDomain, StringComparison.OrdinalIgnoreCase)) return false;
        if (host.EndsWith($".{baseDomain}", StringComparison.OrdinalIgnoreCase)) return false;

        // Loopback / bare IP hosts (used by tests and probes) are not custom domains.
        if (System.Net.IPAddress.TryParse(host, out _)) return false;

        return true;
    }

    private static bool IsValidSubdomain(string subdomain)
    {
        if (subdomain.Length < 3 || subdomain.Length > 63) return false;
        if (subdomain.StartsWith('-') || subdomain.EndsWith('-')) return false;
        return subdomain.All(c => c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '-');
    }

    private async Task<ResolvedTenant?> ResolveTenantAsync(HttpContext context, string subdomain)
    {
        var cache = context.RequestServices.GetService<IDistributedCache>();
        var cacheKey = $"{TenantCacheKeyPrefix}{subdomain}";

        if (cache is not null)
        {
            try
            {
                var cached = await cache.GetStringAsync(cacheKey, context.RequestAborted);
                if (!string.IsNullOrWhiteSpace(cached))
                {
                    var resolvedTenant = JsonSerializer.Deserialize<ResolvedTenant>(cached);
                    if (resolvedTenant is not null)
                    {
                        return resolvedTenant;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tenant cache read failed for {Subdomain}; falling back to database", subdomain);
            }
        }

        var dbContext = context.RequestServices.GetRequiredService<AppDbContext>();
        var row = await dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Where(t => t.Subdomain == subdomain)
            .Select(t => new
            {
                t.Id,
                t.Subdomain,
                t.Status,
                t.PlanId,
                t.EnabledModules,
                t.LogoUrl,
                t.PrimaryColor,
            })
            .FirstOrDefaultAsync(context.RequestAborted);

        if (row is null)
            return null;

        // D3 (ISSUE-358): resolve the plan's feature flags (subscription_plans is a SYSTEM table, not
        // tenant-scoped). Materialize the plan entity (not a projection of the value-converted FeatureFlags
        // column, which need not translate on every provider). A missing plan row leaves the derived set null ⇒
        // fail-open (no seam denies). Only on the cache-miss path — the resolved value is cached below, so the
        // cached path carries flags identically.
        var plan = await dbContext.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == row.PlanId, context.RequestAborted);

        var tenant = new ResolvedTenant(
            row.Id,
            row.Subdomain,
            row.Status,
            row.PlanId,
            row.EnabledModules,
            row.LogoUrl,
            row.PrimaryColor,
            PlanFeatureFlagKeys.Derive(plan?.FeatureFlags));

        if (cache is not null)
        {
            try
            {
                var ttlMinutes = _configuration.GetValue("Platform:TenantCacheTtlMinutes", 5);
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ttlMinutes)
                };

                await cache.SetStringAsync(
                    cacheKey,
                    JsonSerializer.Serialize(tenant),
                    options,
                    context.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tenant cache write failed for {Subdomain}; continuing without cache", subdomain);
            }
        }

        return tenant;
    }

    private static async Task WriteWorkspaceNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(WorkspaceNotFoundHtml);
    }

    private sealed record ResolvedTenant(
        Guid Id,
        string Subdomain,
        TenantStatus Status,
        string? Plan,
        IReadOnlyCollection<string> EnabledModules,
        string? LogoUrl,
        string? PrimaryColor,
        // D3 (ISSUE-358): derived plan feature-flag set; null = fail-open (no plan resolved). Cached with the rest
        // so the Redis-hit path and the DB path agree.
        IReadOnlyCollection<string>? FeatureFlags);
}
