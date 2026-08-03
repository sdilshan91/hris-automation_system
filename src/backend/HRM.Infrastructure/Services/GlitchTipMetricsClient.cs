using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Reads error metrics from the self-hosted GlitchTip instance's Sentry-compatible API (US-ADM-002 FR-6,
/// TC-ADM-002-14/-15/-16). See <see cref="IGlitchTipMetricsClient"/> for the validated endpoint contract.
///
/// <para><b>Two distinct credentials, do not confuse them.</b> <c>GlitchTip:Dsn</c> is the INGEST credential
/// embedded in the app (write-only, semi-public — it ships in the browser bundle). Reading metrics back needs
/// <c>GlitchTip:ApiToken</c>, a separate auth token created in the GlitchTip UI. Both are secrets supplied via
/// user-secrets/env, never the committed appsettings (Critical Rule #6).</para>
///
/// <para><b>Fail-soft everywhere.</b> Unconfigured, unreachable, non-2xx, or an unexpected payload all return
/// empty rather than throwing — a monitoring read must never break the dashboard it feeds, and GlitchTip is an
/// optional separately-deployed component. Callers render empty as "not available", never as zero errors.</para>
/// </summary>
public sealed class GlitchTipMetricsClient : IGlitchTipMetricsClient
{
    public const string BaseUrlKey = "GlitchTip:ApiBaseUrl";
    public const string TokenKey = "GlitchTip:ApiToken";
    public const string OrgKey = "GlitchTip:Organization";

    private readonly HttpClient _http;
    private readonly ILogger<GlitchTipMetricsClient> _logger;
    private readonly string? _baseUrl;
    private readonly string? _token;
    private readonly string? _org;

    public GlitchTipMetricsClient(
        HttpClient http, IConfiguration configuration, ILogger<GlitchTipMetricsClient> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = configuration[BaseUrlKey]?.TrimEnd('/');
        _token = configuration[TokenKey];
        _org = configuration[OrgKey];
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_baseUrl)
        && !string.IsNullOrWhiteSpace(_token)
        && !string.IsNullOrWhiteSpace(_org);

    public async Task<IReadOnlyList<ErrorRatePointDto>> GetErrorTrendAsync(
        Guid? tenantId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        // stats_v2 aggregates across the org and does not accept the issue-search `query` grammar, so a
        // per-tenant trend is NOT available from it. Returning platform-wide numbers under a tenant heading
        // would be a silent lie, so a tenant-scoped request yields empty until a tenant-capable source exists.
        if (tenantId is not null)
            return [];

        var url = $"{_baseUrl}/api/0/organizations/{_org}/stats_v2/"
                  + $"?category=error&field=sum(quantity)"
                  + $"&start={Iso(startUtc)}&end={Iso(endUtc)}";

        var doc = await GetJsonAsync(url, cancellationToken);
        if (doc is null)
            return [];

        try
        {
            if (!doc.RootElement.TryGetProperty("intervals", out var intervals)
                || intervals.ValueKind != JsonValueKind.Array)
                return [];

            // Counts live under groups[].series["sum(quantity)"], positionally aligned with intervals.
            var series = FirstSeries(doc.RootElement);

            var points = new List<ErrorRatePointDto>(intervals.GetArrayLength());
            var i = 0;
            foreach (var iv in intervals.EnumerateArray())
            {
                if (DateTime.TryParse(iv.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
                {
                    long count = 0;
                    if (series is { } s && i < s.Count) count = s[i];
                    points.Add(new ErrorRatePointDto(at, count));
                }
                i++;
            }
            return points;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GlitchTip] unexpected stats_v2 payload; reporting trend as unavailable");
            return [];
        }
    }

    public async Task<IReadOnlyList<TopErrorDto>> GetTopErrorsAsync(
        Guid? tenantId, int limit = 10, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/api/0/organizations/{_org}/issues/?limit={limit}";
        if (tenantId is { } tid)
            url += $"&query={Uri.EscapeDataString($"tenant_id:{tid}")}";

        var doc = await GetJsonAsync(url, cancellationToken);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        try
        {
            var list = new List<TopErrorDto>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "(untitled)" : "(untitled)";
                var level = e.TryGetProperty("level", out var l) ? l.GetString() ?? "error" : "error";
                long count = 0;
                if (e.TryGetProperty("count", out var c))
                    count = c.ValueKind == JsonValueKind.String
                        ? long.TryParse(c.GetString(), out var pc) ? pc : 0
                        : c.TryGetInt64(out var ic) ? ic : 0;

                DateTime? lastSeen = null;
                if (e.TryGetProperty("lastSeen", out var ls)
                    && DateTime.TryParse(ls.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lsv))
                    lastSeen = lsv;

                list.Add(new TopErrorDto(title, count, level, lastSeen));
            }
            return list.OrderByDescending(x => x.Count).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GlitchTip] unexpected issues payload; reporting top-errors as unavailable");
            return [];
        }
    }

    /// <summary>Positionally-aligned count series from the first group, or null when the shape is unfamiliar.</summary>
    private static List<long>? FirstSeries(JsonElement root)
    {
        if (!root.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var g in groups.EnumerateArray())
        {
            if (!g.TryGetProperty("series", out var series) || series.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var field in series.EnumerateObject())
            {
                if (field.Value.ValueKind != JsonValueKind.Array) continue;
                var vals = new List<long>();
                foreach (var v in field.Value.EnumerateArray())
                    vals.Add(v.TryGetInt64(out var n) ? n : 0);
                return vals;
            }
        }
        return null;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using var res = await _http.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("[GlitchTip] {Url} returned {Status}; reporting unavailable",
                    url, (int)res.StatusCode);
                return null;
            }

            var body = await res.Content.ReadAsStringAsync(cancellationToken);
            return JsonDocument.Parse(body);
        }
        catch (Exception ex)
        {
            // Includes timeouts and DNS/connection failures — GlitchTip being down must not break monitoring.
            _logger.LogWarning(ex, "[GlitchTip] request failed; reporting unavailable");
            return null;
        }
    }

    private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
