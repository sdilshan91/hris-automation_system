using HRM.Application.Common.Interfaces;
using Sentry;
using Sentry.Extensibility;

namespace HRM.Api.Observability;

/// <summary>
/// US-PLT-006 (AC-1 / FR-5 / BR-5): tags every captured GlitchTip/Sentry event with the resolved tenant so
/// issues are filterable per tenant in the GlitchTip console. Registered as a <b>scoped</b>
/// <see cref="ISentryEventProcessor"/> — the Sentry ASP.NET Core integration resolves event processors from the
/// current request's DI scope, so the scoped <see cref="ITenantContext"/> (populated by
/// TenantResolutionMiddleware) is available here.
///
/// <para>Runs on the event pipeline before <c>BeforeSend</c>; the tenant tags it sets survive the
/// <see cref="SentryPiiScrubber"/> scrub (which only touches PII bags/headers, never these tags).</para>
/// </summary>
public sealed class TenantTagSentryEventProcessor : ISentryEventProcessor
{
    private readonly ITenantContext _tenant;

    public TenantTagSentryEventProcessor(ITenantContext tenant) => _tenant = tenant;

    public SentryEvent? Process(SentryEvent @event)
    {
        // Only tag when a real tenant is resolved. Startup / system (admin host) / unresolved requests have no
        // tenant to attribute, so we leave the event untagged rather than stamping Guid.Empty.
        if (_tenant.IsResolved && !_tenant.IsSystemContext && _tenant.TenantId != Guid.Empty)
        {
            @event.SetTag("tenant_id", _tenant.TenantId.ToString());
            if (!string.IsNullOrWhiteSpace(_tenant.Subdomain))
                @event.SetTag("tenant_subdomain", _tenant.Subdomain);
        }

        return @event;
    }
}
