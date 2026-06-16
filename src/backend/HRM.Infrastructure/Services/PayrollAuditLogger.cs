using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Writes structured payroll audit entries (US-PAY-012) into the shared <c>audit_log</c> table via the
/// existing <see cref="AuditLog"/> entity. The entry is staged on the current <see cref="AppDbContext"/>; the
/// caller's <c>SaveChanges</c> commits it atomically with the business change (so a rolled-back write leaves
/// no orphan audit row). Tenant id comes from <see cref="ITenantContext"/>; actor from
/// <see cref="ICurrentUser"/> (or the system actor for job-driven actions, BR-7); IP/user-agent from the
/// ambient request when not supplied explicitly (BR-5).
///
/// <para>NFR-1 (async/outbox) is deferred — writes are synchronous. trace_id is taken from the ambient
/// HTTP <c>TraceIdentifier</c> when available.</para>
/// </summary>
public sealed class PayrollAuditLogger : IPayrollAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<PayrollAuditLogger> _logger;

    public PayrollAuditLogger(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<PayrollAuditLogger> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public void Log(
        string action,
        string resourceType,
        string resourceId,
        object? before = null,
        object? after = null,
        string? ipAddress = null,
        string? userAgent = null,
        bool systemActor = false)
    {
        _dbContext.AuditLogs.Add(Build(action, resourceType, resourceId, before, after, ipAddress, userAgent, systemActor));
    }

    public async Task LogAndSaveAsync(
        string action,
        string resourceType,
        string resourceId,
        object? before = null,
        object? after = null,
        string? ipAddress = null,
        string? userAgent = null,
        bool systemActor = false,
        CancellationToken cancellationToken = default)
    {
        Log(action, resourceType, resourceId, before, after, ipAddress, userAgent, systemActor);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private AuditLog Build(
        string action, string resourceType, string resourceId,
        object? before, object? after, string? ipAddress, string? userAgent, bool systemActor)
    {
        var http = _httpContextAccessor?.HttpContext;

        // BR-7: job-driven actions use the well-known system actor id, never a null/empty actor.
        var actorUserId = systemActor
            ? PayrollAuditAction.SystemActorUserId
            : (_currentUser.UserId == Guid.Empty ? PayrollAuditAction.SystemActorUserId : _currentUser.UserId);

        return new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.IsResolved ? _tenantContext.TenantId : null,
            UserId = actorUserId,
            // Keep EventType in sync with Action so the legacy (EventType-based) reads still see the entry.
            EventType = action,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Before = Serialize(before),
            After = Serialize(after),
            // BR-5: IP/user-agent for sensitive actions — explicit value wins, else ambient request context.
            IpAddress = ipAddress ?? http?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = userAgent ?? UserAgentOf(http),
            TraceId = http?.TraceIdentifier,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static string? UserAgentOf(HttpContext? http)
    {
        if (http is null) return null;
        var ua = http.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(ua) ? null : Truncate(ua, 500);
    }

    private string? Serialize(object? value)
    {
        if (value is null) return null;
        if (value is string s) return s; // allow pre-serialized JSON to pass through.
        try
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        catch (Exception ex)
        {
            // Audit must never break the business operation — fall back to a best-effort marker.
            _logger.LogWarning(ex, "Failed to serialize audit before/after payload for type {Type}.", value.GetType().Name);
            return null;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
