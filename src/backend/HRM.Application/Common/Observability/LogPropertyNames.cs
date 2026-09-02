namespace HRM.Application.Common.Observability;

/// <summary>
/// Structured log property names that say WHICH SCOPE a log line belongs to.
///
/// <para>They live here, in the layer both HRM.Api and HRM.Infrastructure reference, because the same property
/// is pushed from three unrelated places in two different assemblies: <c>TenantResolutionMiddleware</c> (request
/// path, HRM.Api), <c>JobLogProperties</c> via <c>JobLogContextFilter</c> (a job's own <c>tenantId</c> argument,
/// HRM.Api) and <c>TenantJobRunner</c> (one sweep-job iteration, HRM.Infrastructure — which cannot reference
/// HRM.Api). A drifted literal in any one of them would make an incident query silently miss a whole class of
/// lines while still returning plausible results, which is worse than returning none.</para>
/// </summary>
public static class LogPropertyNames
{
    /// <summary>
    /// The tenant whose data the line concerns. <see cref="System.Guid.Empty"/> is never emitted under this
    /// key — an all-zero tenant id reads like a real scope to whoever is grepping during an incident, so
    /// absence is the honest signal. See <c>JobLogProperties.TenantIdOf</c> and <c>TenantJobRunner</c>.
    /// </summary>
    public const string TenantId = "tenant_id";
}
