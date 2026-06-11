using System.Text.RegularExpressions;

namespace HRM.Application.Common.Helpers;

/// <summary>
/// Lightweight user-agent parser that extracts device, browser, and OS info
/// without any heavy third-party dependency. Covers the most common browsers
/// and operating systems for session display purposes (US-AUTH-009 FR-6/FR-7).
/// </summary>
public static partial class UserAgentParser
{
    public static (string Device, string Browser, string Os) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return ("Unknown", "Unknown", "Unknown");

        var browser = ParseBrowser(userAgent);
        var os = ParseOs(userAgent);
        var device = ParseDevice(userAgent);

        return (device, browser, os);
    }

    private static string ParseBrowser(string ua)
    {
        // Order matters: check more specific patterns first
        if (ua.Contains("Edg/", StringComparison.Ordinal))
            return ExtractVersion(ua, "Edg/", "Edge");
        if (ua.Contains("OPR/", StringComparison.Ordinal) || ua.Contains("Opera", StringComparison.Ordinal))
            return ExtractVersion(ua, "OPR/", "Opera");
        if (ua.Contains("Chrome/", StringComparison.Ordinal) && !ua.Contains("Chromium", StringComparison.Ordinal))
            return ExtractVersion(ua, "Chrome/", "Chrome");
        if (ua.Contains("Firefox/", StringComparison.Ordinal))
            return ExtractVersion(ua, "Firefox/", "Firefox");
        if (ua.Contains("Safari/", StringComparison.Ordinal) && ua.Contains("Version/", StringComparison.Ordinal))
            return ExtractVersion(ua, "Version/", "Safari");
        if (ua.Contains("MSIE", StringComparison.Ordinal) || ua.Contains("Trident/", StringComparison.Ordinal))
            return "Internet Explorer";

        return "Unknown";
    }

    private static string ParseOs(string ua)
    {
        // Check iOS-specific identifiers before Mac OS X since iOS UAs contain "Mac OS X"
        if (ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPad", StringComparison.Ordinal))
            return "iOS";
        if (ua.Contains("Windows NT 10", StringComparison.Ordinal))
            return "Windows 10+";
        if (ua.Contains("Windows NT", StringComparison.Ordinal))
            return "Windows";
        if (ua.Contains("Mac OS X", StringComparison.Ordinal))
            return "macOS";
        if (ua.Contains("Android", StringComparison.Ordinal))
            return "Android";
        if (ua.Contains("CrOS", StringComparison.Ordinal))
            return "Chrome OS";
        if (ua.Contains("Linux", StringComparison.Ordinal))
            return "Linux";

        return "Unknown";
    }

    private static string ParseDevice(string ua)
    {
        // Check tablet-specific identifiers before "Mobile" since iPad UAs contain "Mobile"
        if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
            return "Tablet";
        if (ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
            (ua.Contains("Android", StringComparison.OrdinalIgnoreCase) &&
            !ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase)))
            return "Mobile";

        return "Desktop";
    }

    private static string ExtractVersion(string ua, string token, string browserName)
    {
        var idx = ua.IndexOf(token, StringComparison.Ordinal);
        if (idx < 0) return browserName;

        var start = idx + token.Length;
        var end = start;
        while (end < ua.Length && ua[end] != ' ' && ua[end] != ';')
            end++;

        var version = ua[start..end];
        // Only keep major version number for cleanliness
        var dotIdx = version.IndexOf('.');
        if (dotIdx > 0)
            version = version[..dotIdx];

        return $"{browserName} {version}";
    }
}
