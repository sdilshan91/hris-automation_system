namespace HRM.Api.Jobs.Filters;

/// <summary>
/// GAP-024 — which structured properties a background job's log lines should carry.
///
/// <para>Extracted as a pure function because the property SET is the deliverable: §9.4-3 asks for background-job
/// log lines to be attributable, and today none carries <c>tenant_id</c> — precisely the field you would reach for
/// first when diagnosing an isolation incident. A request gets these from
/// <c>TenantResolutionMiddleware</c>; a job had no equivalent.</para>
///
/// <para>Kept free of Hangfire types so it is testable without a running Hangfire server. The thin shell that
/// adapts a <c>PerformingContext</c> onto it is <see cref="JobLogContextFilter"/>.</para>
/// </summary>
public static class JobLogProperties
{
    /// <summary>Declaring type + method, e.g. <c>LeaveAccrualJob.RunAsync</c>.</summary>
    public const string JobNameKey = "job_name";

    /// <summary>Hangfire's own job id, so a log line can be tied back to a dashboard entry.</summary>
    public const string JobIdKey = "job_id";

    /// <summary>Matches the request-side property name pushed by <c>TenantResolutionMiddleware</c>.</summary>
    public const string TenantIdKey = "tenant_id";

    /// <summary>The job parameter a per-tenant job carries its scope in.</summary>
    private const string TenantIdParameterName = "tenantId";

    /// <summary>
    /// Builds the properties to push for one job execution.
    /// </summary>
    /// <param name="typeName">Job's declaring type name (unqualified is fine).</param>
    /// <param name="methodName">Job's method name.</param>
    /// <param name="jobId">Hangfire job id, if known.</param>
    /// <param name="arguments">The job's (parameter name, value) pairs, in declaration order.</param>
    /// <returns>
    /// Properties in push order. <c>tenant_id</c> is present only when the job actually declares a tenant
    /// argument holding a real tenant — see the remarks on <see cref="TenantIdOf"/>.
    /// </returns>
    public static IReadOnlyList<KeyValuePair<string, object>> For(
        string? typeName,
        string? methodName,
        string? jobId,
        IReadOnlyList<KeyValuePair<string, object?>> arguments)
    {
        var properties = new List<KeyValuePair<string, object>>(3);

        var jobName = NameOf(typeName, methodName);
        if (jobName is not null)
        {
            properties.Add(new(JobNameKey, jobName));
        }

        if (!string.IsNullOrWhiteSpace(jobId))
        {
            properties.Add(new(JobIdKey, jobId));
        }

        var tenantId = TenantIdOf(arguments);
        if (tenantId is not null)
        {
            properties.Add(new(TenantIdKey, tenantId.Value));
        }

        return properties;
    }

    /// <summary>
    /// <c>Type.Method</c>, or just whichever half is known. Returns null when neither is — a
    /// <c>job_name</c> of "." would be noise in every log line.
    /// </summary>
    private static string? NameOf(string? typeName, string? methodName)
    {
        var type = typeName?.Trim();
        var method = methodName?.Trim();
        var hasType = !string.IsNullOrEmpty(type);
        var hasMethod = !string.IsNullOrEmpty(method);

        return (hasType, hasMethod) switch
        {
            (true, true) => $"{type}.{method}",
            (true, false) => type,
            (false, true) => method,
            _ => null,
        };
    }

    /// <summary>
    /// The tenant this execution is scoped to, from a parameter named <c>tenantId</c>.
    ///
    /// <para><see cref="Guid.Empty"/> is deliberately treated as ABSENT rather than logged. An all-zero
    /// tenant id reads like a real scope to whoever is grepping the log during an incident, which is worse
    /// than the field simply not being there — absent correctly says "this job is not per-tenant".</para>
    ///
    /// <para>A string argument that parses as a Guid is accepted too: Hangfire round-trips arguments through
    /// JSON, and a job whose parameter is typed <c>string</c> is still telling us its tenant.</para>
    /// </summary>
    private static Guid? TenantIdOf(IReadOnlyList<KeyValuePair<string, object?>> arguments)
    {
        foreach (var argument in arguments)
        {
            if (!string.Equals(argument.Key, TenantIdParameterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tenantId = argument.Value switch
            {
                Guid guid => guid,
                string text when Guid.TryParse(text, out var parsed) => parsed,
                _ => (Guid?)null,
            };

            if (tenantId is not null && tenantId != Guid.Empty)
            {
                return tenantId;
            }
        }

        return null;
    }
}
