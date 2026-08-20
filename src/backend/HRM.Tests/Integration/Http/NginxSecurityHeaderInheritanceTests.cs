// ============================================================================
// GAP-033a, second half — the SPA's nginx headers, guarded against nginx's silent inheritance trap.
//
// WHY THIS EXISTS. Adding the six §23.4 headers at the server level of nginx.conf looked complete and
// was not. nginx inherits `add_header` from the enclosing level ONLY IF the current level declares no
// `add_header` of its own. The static-asset location block already declared `add_header Cache-Control`,
// so nginx silently dropped ALL SIX security headers for every .js, .css, .woff2 and image -- while
// still serving them correctly on index.html.
//
// That was measured, not theorised. Running the pre-fix config in nginx:alpine and curling the two
// paths gave: /app.js -> 0 of 6 headers, / -> 6 of 6. A browser spot-check of the page would have shown
// six green headers and hidden the hole entirely, and static assets are where nosniff matters MOST --
// MIME-sniffing a served file is the attack it exists to prevent.
//
// This test is a STATIC parse, deliberately: it needs no docker, no running nginx, and no network, so it
// cannot become the flaky test everyone learns to ignore. It encodes the repo's own systemic finding S-2
// -- hand-maintained lists drift, guarded ones do not. This list just drifted, on its very first edit.
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

public sealed class NginxSecurityHeaderInheritanceTests
{
    private static readonly string[] RequiredHeaders =
    [
        "X-Content-Type-Options",
        "X-Frame-Options",
        "Referrer-Policy",
        "Permissions-Policy",
        "Strict-Transport-Security",
        "Content-Security-Policy-Report-Only",
    ];

    private static string NginxConfPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "frontend")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the test must be able to locate the repo root from the test binary");
        var path = Path.Combine(dir!.FullName, "src", "frontend", "nginx.conf");
        File.Exists(path).Should().BeTrue($"nginx.conf is the config baked into the served image; expected it at {path}");
        return path;
    }

    /// <summary>
    /// The server level must carry all six. This is the baseline the location blocks inherit from.
    /// </summary>
    [Fact]
    public void ServerLevel_Declares_AllSix_SecurityHeaders_GAP033a()
    {
        var conf = File.ReadAllText(NginxConfPath());

        foreach (var header in RequiredHeaders)
        {
            conf.Should().MatchRegex($@"add_header\s+{Regex.Escape(header)}\s",
                $"§23.4 requires {header} on SPA responses");
        }
    }

    /// <summary>
    /// THE ARM THAT MATTERS: every `location` block that declares ANY add_header must re-declare ALL six,
    /// because that declaration is exactly what severs inheritance from the server level.
    ///
    /// A future edit that adds a single `add_header` to a new location block -- an X-Robots-Tag, a CORS
    /// header, another Cache-Control -- silently strips all six from that location. Nothing in review or at
    /// runtime shows it. This turns that edit into a red test instead of a security hole.
    /// </summary>
    [Fact]
    public void EveryLocationBlock_WithItsOwn_AddHeader_Redeclares_AllSix_GAP033a()
    {
        var conf = File.ReadAllText(NginxConfPath());

        foreach (var (header, body) in LocationBlocks(conf))
        {
            if (!Regex.IsMatch(body, @"add_header\s"))
            {
                continue; // declares none -> correctly inherits all six from the server level
            }

            foreach (var required in RequiredHeaders)
            {
                Regex.IsMatch(body, $@"add_header\s+{Regex.Escape(required)}\s").Should().BeTrue(
                    $"`location {header}` declares its own add_header, which severs nginx's inheritance "
                    + $"from the server level -- so it must re-declare {required} explicitly or nginx will "
                    + "silently serve that location with NO security headers at all. Measured: the pre-fix "
                    + "config returned 0 of 6 on /app.js while returning 6 of 6 on /.");
            }
        }
    }

    /// <summary>
    /// `always` is what makes a header survive a 4xx/5xx. Without it nginx omits add_header on error
    /// responses -- the mirror of the API-side arm in <see cref="SecurityHeadersApiTests"/>.
    /// </summary>
    [Fact]
    public void EverySecurityHeader_Uses_Always_SoItSurvives_ErrorResponses_GAP033a()
    {
        var conf = File.ReadAllText(NginxConfPath());

        foreach (Match m in Regex.Matches(conf, @"^\s*add_header\s+(?<name>\S+)\s+(?<rest>.*)$", RegexOptions.Multiline))
        {
            var name = m.Groups["name"].Value;
            if (!RequiredHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            m.Groups["rest"].Value.TrimEnd().Should().EndWith("always;",
                $"without the `always` flag nginx drops {name} on 4xx/5xx responses, which is precisely "
                + "where clickjacking and MIME-sniffing protection is still needed");
        }
    }

    /// <summary>
    /// Brace-matched extraction of each `location` block body. A regex alone cannot do this correctly
    /// because the bodies nest.
    /// </summary>
    private static IEnumerable<(string Header, string Body)> LocationBlocks(string conf)
    {
        foreach (Match m in Regex.Matches(conf, @"location\s+(?<hdr>[^{]+?)\s*\{"))
        {
            var start = m.Index + m.Length;
            var depth = 1;
            var i = start;
            while (i < conf.Length && depth > 0)
            {
                if (conf[i] == '{') depth++;
                else if (conf[i] == '}') depth--;
                i++;
            }

            yield return (m.Groups["hdr"].Value.Trim(), conf[start..(i - 1)]);
        }
    }
}
