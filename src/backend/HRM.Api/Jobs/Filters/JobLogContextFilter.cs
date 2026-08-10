using Hangfire.Server;
using Serilog.Context;

namespace HRM.Api.Jobs.Filters;

/// <summary>
/// GAP-024 / §9.4-3 — the Hangfire server filter that makes background-job log lines attributable:
/// <c>job_name</c>, <c>job_id</c>, and <c>tenant_id</c> for per-tenant jobs. Before this, no job log line
/// carried a tenant at all, so a suspected isolation incident could not be traced through the jobs that touch
/// the same data as the requests around it.
///
/// <para><b>Why this filter does NOT populate <see cref="HRM.Application.Common.Interfaces.ITenantContext"/>,
/// despite §9.4-3 describing a filter that does.</b> 42 of the 62 job classes create their OWN DI scope
/// (<c>IServiceScopeFactory.CreateScope()</c>) and resolve <c>AppDbContext</c>/<c>ITenantContext</c> from it.
/// A filter can only reach the scope Hangfire activated the job from, so any tenant it set there would land on
/// a different, scoped <c>TenantContext</c> instance than the one the job body actually reads — the job would
/// still see <c>IsResolved == false</c> while the filter looked like a working control. Tenant context is
/// therefore declared by the job bodies (<c>SetSystemContext()</c> for cross-tenant sweeps,
/// <c>ITenantJobRunner.RunForTenantAsync</c> for per-tenant work) and that is enforced mechanically by
/// <c>BackgroundJobTenantContextTests</c> rather than by a filter that cannot see far enough.</para>
///
/// <para>Log context is different, and that is why this half works as a filter: Serilog's
/// <see cref="LogContext"/> is backed by an <c>AsyncLocal</c>, which flows DOWN into the job body and into every
/// scope it creates. <see cref="OnPerforming"/> is synchronous, so the push it performs is still in effect when
/// Hangfire invokes the job on the same execution context.</para>
/// </summary>
public sealed class JobLogContextFilter : IServerFilter
{
    /// <summary>Key under which the pushed-property scope is stashed between the two callbacks.</summary>
    internal const string ScopeItemKey = "HRM.JobLogContextFilter.Scope";

    public void OnPerforming(PerformingContext filterContext)
    {
        var job = filterContext.BackgroundJob?.Job;

        var arguments = new List<KeyValuePair<string, object?>>();
        if (job is not null)
        {
            // Hangfire keeps Args positional; the names live on the method signature.
            var parameters = job.Method.GetParameters();
            for (var i = 0; i < parameters.Length && i < job.Args.Count; i++)
            {
                arguments.Add(new(parameters[i].Name ?? string.Empty, job.Args[i]));
            }
        }

        var properties = JobLogProperties.For(
            job?.Type.Name,
            job?.Method.Name,
            filterContext.BackgroundJob?.Id,
            arguments);

        // Disposed in OnPerformed, which Hangfire calls whether the job succeeded or threw. Popped in reverse
        // order by the composite, so pushing is safe even when a property is already present.
        var scope = new CompositeDisposable(properties.Count);
        foreach (var property in properties)
        {
            scope.Add(LogContext.PushProperty(property.Key, property.Value));
        }

        filterContext.Items[ScopeItemKey] = scope;
    }

    public void OnPerformed(PerformedContext filterContext)
    {
        if (filterContext.Items.TryGetValue(ScopeItemKey, out var stashed)
            && stashed is IDisposable scope)
        {
            filterContext.Items.Remove(ScopeItemKey);
            scope.Dispose();
        }
    }

    /// <summary>
    /// Disposes pushed properties in reverse push order, which is what <see cref="LogContext"/> requires — the
    /// enricher stack is a stack, and popping out of order corrupts it for every subsequent job on the worker.
    /// </summary>
    private sealed class CompositeDisposable(int capacity) : IDisposable
    {
        private readonly List<IDisposable> _disposables = new(capacity);

        public void Add(IDisposable disposable) => _disposables.Add(disposable);

        public void Dispose()
        {
            for (var i = _disposables.Count - 1; i >= 0; i--)
            {
                _disposables[i].Dispose();
            }

            _disposables.Clear();
        }
    }
}
