using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HRM.Infrastructure.Behaviors;

/// <summary>
/// US-PLT-002 — defense-in-depth tenant isolation via PostgreSQL Row-Level Security.
///
/// Opens a per-request transaction and sets the <c>app.current_tenant</c> GUC using
/// <c>set_config(name, value, is_local =&gt; true)</c> (the parameterised, injection-safe
/// equivalent of <c>SET LOCAL</c>). RLS policies (added in the Phase-4 switch-on
/// migration) read this GUC to scope every statement to the resolved tenant. Because
/// the value is set with <c>is_local =&gt; true</c> it is transaction-scoped and reset on
/// commit/rollback, so it cannot leak across pooled connections.
///
/// INERT BY DEFAULT. It only does anything when ALL of these hold:
///   - <c>Rls:Enabled</c> is true (the Phase-4 switch-on flag — false until RLS policies exist);
///   - a tenant is resolved AND it is not the system/admin context
///     (system/admin + background paths run on the privileged BYPASSRLS connection);
///   - the provider is relational — it no-ops on the EF InMemory provider used by tests,
///     which supports neither transactions nor raw SQL;
///   - there is no ambient transaction already in flight.
///
/// Registered after ValidationBehavior/LoggingBehavior in Program.cs so it is the
/// innermost behavior — its transaction spans the handler's database work. Note the
/// integration-test host registers MediatR handlers but NOT the pipeline behaviors,
/// so this never participates in the InMemory test suite regardless of the guards.
/// </summary>
public sealed class TenantTransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly bool _rlsEnabled;

    public TenantTransactionBehavior(
        AppDbContext db,
        ITenantContext tenant,
        IConfiguration configuration)
    {
        _db = db;
        _tenant = tenant;
        _rlsEnabled = configuration.GetValue("Rls:Enabled", false);
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_rlsEnabled
            || !_tenant.IsResolved
            || _tenant.IsSystemContext
            || !_db.Database.IsRelational()
            || _db.Database.CurrentTransaction is not null)
        {
            return await next(cancellationToken);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // set_config(..., is_local => true) == SET LOCAL, but accepts a bind parameter
        // (plain SET LOCAL does not), so the tenant id is passed safely as a parameter.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant', {_tenant.TenantId.ToString()}, true)",
            cancellationToken);

        var response = await next(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
