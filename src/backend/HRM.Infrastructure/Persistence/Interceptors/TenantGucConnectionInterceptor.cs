using System.Data.Common;
using HRM.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HRM.Infrastructure.Persistence.Interceptors;

/// <summary>
/// US-PLT-002 (ISSUE-277) — sets the RLS tenant GUC `app.current_tenant` at SESSION scope on every connection open,
/// replacing the former per-request transaction (which broke under EnableRetryOnFailure and nested with handlers that
/// open their own transaction). Runs AFTER ConnectionRoutingInterceptor has routed a resolved-non-system tenant to
/// hrm_app. Because Npgsql's default reset-on-close clears session state on pool return, the GUC is set on EVERY open
/// (never cached) and reset to '' for unresolved/system opens so no stale tenant can leak if reset-on-close is ever
/// disabled. INERT when Rls:Enabled=false.
/// </summary>
public sealed class TenantGucConnectionInterceptor : DbConnectionInterceptor
{
    private readonly bool _rlsEnabled;
    public TenantGucConnectionInterceptor(IConfiguration configuration)
        => _rlsEnabled = configuration.GetValue("Rls:Enabled", false);

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (!_rlsEnabled || connection is not NpgsqlConnection npgsql) return;
        using var cmd = BuildSetConfig(npgsql);
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (!_rlsEnabled || connection is not NpgsqlConnection npgsql) return;
        await using var cmd = BuildSetConfig(npgsql);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // Resolved-non-system tenant → set the GUC to its id (RLS-enforced hrm_app connections).
    // Unresolved/system → reset to '' (defensive; these route to hrm_owner/BYPASSRLS so the value is moot, but a
    // blank GUC is fail-closed for any hrm_app reuse if reset-on-close were ever disabled).
    private static NpgsqlCommand BuildSetConfig(NpgsqlConnection connection)
    {
        var value = AmbientTenant.Current is { IsResolved: true, IsSystemContext: false } t
            ? t.TenantId.ToString()
            : string.Empty;
        var cmd = new NpgsqlCommand("SELECT set_config('app.current_tenant', @tid, false)", connection);
        cmd.Parameters.AddWithValue("tid", value);
        return cmd;
    }
}
