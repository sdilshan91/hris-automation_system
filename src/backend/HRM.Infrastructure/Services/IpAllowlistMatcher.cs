using System.Net;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Matches a client IP against a per-tenant allowlist (US-ATT-004 FR-4 / ISSUE-066). Each allowlist entry is
/// either an exact IP address (e.g. "198.51.100.7") OR a CIDR range (e.g. "10.0.0.0/24"); a clock-in is allowed
/// when the client IP equals any exact entry or falls within any CIDR entry. A malformed entry is skipped
/// (never throws) so one bad row cannot break enforcement for the whole tenant.
/// </summary>
public static class IpAllowlistMatcher
{
    /// <summary>
    /// True when <paramref name="clientIp"/> matches any allowlist entry — exact IP equality or containment in a
    /// CIDR range. Returns false when the client IP is missing/unparseable or when no entry matches (an empty
    /// allowlist therefore denies, preserving the pre-CIDR behaviour).
    /// </summary>
    public static bool IsAllowed(string? clientIp, IEnumerable<string> allowlist)
    {
        if (string.IsNullOrWhiteSpace(clientIp) || !IPAddress.TryParse(clientIp.Trim(), out var ip))
            return false;

        foreach (var entry in allowlist)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            var trimmed = entry.Trim();

            if (trimmed.Contains('/'))
            {
                // CIDR entry: skip silently if malformed; IPNetwork.Contains handles address-family mismatch.
                if (IPNetwork.TryParse(trimmed, out var network) && network.Contains(ip))
                    return true;
            }
            else if (IPAddress.TryParse(trimmed, out var exact) && exact.Equals(ip))
            {
                return true;
            }
        }

        return false;
    }
}
