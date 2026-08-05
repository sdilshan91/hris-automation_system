// ============================================================================
// BUG-294 / BUG-295 — the seam test that stops "the email links somewhere that
// does not exist" from ever shipping again.
//
// Both bugs had the same shape: the backend built a URL, the Angular router had
// no route for it, and the SPA wildcard quietly redirected the recipient to the
// login page with their token discarded. Nothing failed. Every backend test
// passed (the token logic was correct), every frontend test passed (the
// component worked when handed the right params), and the *seam between them*
// was untested — because no test on either side could see both halves.
//
// This test deliberately reaches across the stack: it parses the real
// app.routes.ts and asserts that the paths the real link builders emit are
// actually routable. A one-sided assertion (backend pins the literal it emits,
// frontend pins the literal it routes) would NOT have caught the original bug —
// each side was self-consistent; only their disagreement was fatal.
// ============================================================================

using System.Text.RegularExpressions;
using FluentAssertions;

namespace HRM.Tests.Unit;

public sealed class AuthEmailLinkRouteTests
{
    /// <summary>
    /// Every path a transactional auth email can send a recipient to. Add a row here when a new emailed link
    /// is introduced — that is the point: a new link with no route fails immediately rather than in production.
    /// </summary>
    public static TheoryData<string, string> EmailedAuthLinkPaths => new()
    {
        // BUG-295: password reset. Emitted by AuthService.DispatchPasswordResetEmailAsync.
        { "auth/reset-password", "the self-service password-reset email" },
        // BUG-294: invitation redemption. Emitted by RealUserManagementNotificationService.SendInvitationAsync.
        { "auth/accept-invite", "the tenant user-invitation email" },
    };

    [Theory]
    [MemberData(nameof(EmailedAuthLinkPaths))]
    public void Every_emailed_auth_link_resolves_to_a_real_Angular_route(string path, string description)
    {
        var routes = ParseAngularRoutePaths();

        routes.Should().Contain(
            path,
            "{0} sends recipients to /{1}, so a route must exist for it — otherwise the SPA wildcard " +
            "redirects them to the login page and silently discards their one-time token (BUG-294/BUG-295)",
            description, path);
    }

    /// <summary>
    /// The link builders are string interpolation over configuration, so rather than invoking the services
    /// (which would need a dispatcher, config and tenant context) this asserts on their source. Crude, but it
    /// is the actual coupling: if someone edits the emitted path, this fails and points at the route table.
    /// </summary>
    [Fact]
    public void The_link_builders_emit_exactly_the_paths_this_test_verifies()
    {
        var backend = RepoPath("src", "backend");

        var authService = File.ReadAllText(
            Path.Combine(backend, "HRM.Infrastructure", "Services", "AuthService.cs"));
        var inviteNotifications = File.ReadAllText(
            Path.Combine(backend, "HRM.Infrastructure", "Services", "RealUserManagementNotificationService.cs"));

        // Anchored on the leading slash so a regression to the root-level path (the original bug) fails here
        // rather than passing on a substring match — the trap that let RealUserManagementNotificationServiceTests
        // stay green while the link was broken.
        authService.Should().Contain(
            "/auth/reset-password?token=",
            "the reset email must link at the nested auth route, not the root");
        authService.Should().NotContain(
            "}/reset-password?token=",
            "a root-level reset path matches no Angular route (BUG-295)");

        inviteNotifications.Should().Contain(
            "/auth/accept-invite?token=",
            "the invitation email must link at the nested auth route, not the root");
        inviteNotifications.Should().NotContain(
            ")}/accept-invite?token=",
            "a root-level accept path matches no Angular route (BUG-294)");
    }

    /// <summary>
    /// The one that cannot go stale: DISCOVERS every emailed app link in the backend and asserts each resolves.
    ///
    /// <para>The two tests above enumerate links by hand — and that is precisely how BUG-295 survived its own
    /// fix. The hand-list named two links; the codebase emitted four. `RealTenantWelcomeEmailService` and
    /// `ApplicantConversionService` both pointed at a root-level <c>/forgot-password</c> that matches no route
    /// (the real one is nested under <c>auth</c>), so the tenant-owner welcome email — the documented ONLY route
    /// to a first password — led to the wildcard. The guard reported green the whole time.</para>
    ///
    /// <para>A list you have to remember to update is not a guard. This sweeps the source instead, so a NEW
    /// emitter with a bad path fails here the day it is written, without anyone remembering this file exists.</para>
    /// </summary>
    [Fact]
    public void EVERY_emailed_app_link_found_anywhere_in_the_backend_resolves_to_a_real_route()
    {
        var routes = ParseAngularRoutePaths();
        var discovered = DiscoverEmittedAppPaths();

        // Vacuity guard: if the sweep finds nothing, the regex has drifted and this test would "pass" forever.
        discovered.Should().HaveCountGreaterThanOrEqualTo(3,
            "the sweep found almost no emitted links — the discovery regex has broken, and this test would "
            + "otherwise pass while proving nothing");

        // Collect ALL dead links before asserting. Failing on the first one turns a sweep into a one-at-a-time
        // grind — you fix a link, re-run, discover the next — and hides the size of the problem.
        var dead = discovered
            .Where(d => !IsRoutable(d.Path, routes))
            .Select(d => $"{d.Path}  (emitted by {d.Source})")
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        dead.Should().BeEmpty(
            "every emitted link must resolve to a real Angular route; these fall through to the ** wildcard "
            + "and land the user on a blank/not-found page:\n  " + string.Join("\n  ", dead));
    }

    /// <summary>
    /// Finds every <c>https://…</c> literal assigned to a url-ish key or variable in production backend code,
    /// and reduces it to the Angular path it points at. API paths are excluded — they are server routes, not
    /// client routes.
    /// </summary>
    private static List<(string Path, string Source)> DiscoverEmittedAppPaths()
    {
        var backend = RepoPath("src", "backend");
        // Absolute (emailed) links AND relative in-app links (dashboard widget click-through). Both are paths
        // the Angular router must resolve; a dead relative link is a clickable card that goes nowhere.
        var urlLiteral = new Regex(
            @"(?:\[""url""\]|\b\w*[Uu]rl)\s*=\s*\$?""((?:https://|/)[^""]+)""", RegexOptions.Compiled);

        var found = new List<(string, string)>();

        foreach (var project in new[] { "HRM.Infrastructure", "HRM.Domain", "HRM.Api" })
        {
            var root = Path.Combine(backend, project);
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                foreach (Match m in urlLiteral.Matches(File.ReadAllText(file)))
                {
                    var path = AppPathOf(m.Groups[1].Value);
                    if (path is not null)
                        found.Add((path, Path.GetFileName(file)));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Matches a concrete path against the route table, treating <c>:param</c> segments as wildcards — a real
    /// emitted link carries real ids (<c>admin/users/8f2c…/sessions</c>), so a plain set-contains would reject
    /// every parameterised route and force this guard to be narrowed until it proved nothing.
    /// </summary>
    private static bool IsRoutable(string path, HashSet<string> routes)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var route in routes)
        {
            var routeSegments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (routeSegments.Length != segments.Length)
                continue;

            var matches = true;
            for (var i = 0; i < segments.Length; i++)
            {
                if (routeSegments[i].StartsWith(':') || routeSegments[i] == "*")
                    continue;

                if (!string.Equals(routeSegments[i], segments[i], StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    /// <summary>Reduces a URL literal to its client-side path, or null when it is not an app link.</summary>
    private static string? AppPathOf(string url)
    {
        string path;

        if (url.StartsWith("https://", StringComparison.Ordinal))
        {
            var afterScheme = url["https://".Length..];
            var slash = afterScheme.IndexOf('/');
            if (slash < 0)
                return null;

            path = afterScheme[(slash + 1)..];
        }
        else
        {
            path = url; // already a relative in-app path
        }

        // Stop at a query string or an interpolation hole — the routable part is what precedes it.
        var cut = path.IndexOfAny(['?', '{', '#']);
        if (cut >= 0)
            path = path[..cut];

        path = path.Trim('/');

        // Server routes and bare hosts are not Angular routes.
        if (path.Length == 0 || path.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            return null;

        return path;
    }

    // ── Angular route-table parsing ─────────────────────────────────────

    /// <summary>
    /// Builds the set of fully-qualified route paths from app.routes.ts by walking <c>path: '...'</c> literals
    /// with brace-depth awareness, so a child route is qualified by its parent's path.
    ///
    /// <para>This is a source parse, not an evaluation, so it is only as good as the file's shape — which is
    /// why it <b>throws rather than returns empty</b> when the file is missing or yields nothing. A seam test
    /// that silently passes because it found no routes would be worse than no test at all.</para>
    /// </summary>
    private static HashSet<string> ParseAngularRoutePaths()
    {
        var routesFile = RepoPath("src", "frontend", "src", "app", "app.routes.ts");

        File.Exists(routesFile).Should().BeTrue(
            "the Angular route table must be readable at {0} for this seam test to mean anything", routesFile);

        var source = File.ReadAllText(routesFile);

        var paths = new HashSet<string>(StringComparer.Ordinal);
        // (depth, path) of each enclosing route whose object is still open.
        var stack = new Stack<(int Depth, string Path)>();

        var depth = 0;
        var matcher = new Regex(@"path:\s*'([^']*)'", RegexOptions.Compiled);
        var index = 0;

        while (index < source.Length)
        {
            var c = source[index];

            if (c == '{' || c == '[')
            {
                depth++;
                index++;
                continue;
            }

            if (c == '}' || c == ']')
            {
                depth--;
                while (stack.Count > 0 && stack.Peek().Depth > depth)
                    stack.Pop();
                index++;
                continue;
            }

            var match = matcher.Match(source, index);
            if (!match.Success)
                break;

            // Anything between here and the match may change depth — replay it.
            for (var i = index; i < match.Index; i++)
            {
                if (source[i] is '{' or '[') depth++;
                else if (source[i] is '}' or ']')
                {
                    depth--;
                    while (stack.Count > 0 && stack.Peek().Depth > depth)
                        stack.Pop();
                }
            }

            while (stack.Count > 0 && stack.Peek().Depth >= depth)
                stack.Pop();

            var segment = match.Groups[1].Value;
            var prefix = stack.Count > 0 ? stack.Peek().Path : string.Empty;
            var full = string.IsNullOrEmpty(segment)
                ? prefix
                : string.IsNullOrEmpty(prefix) ? segment : $"{prefix}/{segment}";

            if (!string.IsNullOrEmpty(full))
                paths.Add(full);

            stack.Push((depth, full));
            index = match.Index + match.Length;
        }

        // Follow lazy-loaded child route files. app.routes.ts mounts 34 feature route arrays via
        // `loadChildren: () => import('./features/x/y.routes')`, and their children are the routes most
        // emitted links actually point at (`leave/approvals` lives in leave-management.routes.ts, not here).
        // Without this the sweep reports a pile of phantom dead links and gets switched off.
        foreach (var (mountPath, childFile) in ParseLazyMounts(source))
        {
            foreach (var childPath in ParseRoutePathsIn(childFile))
            {
                paths.Add(string.IsNullOrEmpty(childPath) ? mountPath : $"{mountPath}/{childPath}");
            }
        }

        paths.Should().NotBeEmpty(
            "parsing app.routes.ts produced no routes — the parse has broken and this test would otherwise " +
            "pass vacuously while proving nothing");

        // Sanity anchors: these have existed for the life of the app. If they are missing, the parser is wrong
        // rather than the routes, and every other assertion in this file is meaningless.
        paths.Should().Contain("auth/login",
            "auth/login is a long-standing route, so its absence means the parser is broken, not the app");

        return paths;
    }

    /// <summary>
    /// Extracts (mountPath, childRoutesFile) for every <c>loadChildren</c> in app.routes.ts, by pairing each
    /// dynamic import with the nearest preceding <c>path:</c>.
    /// </summary>
    private static List<(string MountPath, string ChildFile)> ParseLazyMounts(string appRoutesSource)
    {
        var appDir = RepoPath("src", "frontend", "src", "app");
        var mounts = new List<(string, string)>();

        var pathMatcher = new Regex(@"path:\s*'([^']*)'", RegexOptions.Compiled);
        var loadChildren = new Regex(
            @"loadChildren:\s*\(\)\s*=>\s*import\(\s*'([^']+)'", RegexOptions.Compiled);

        foreach (Match lc in loadChildren.Matches(appRoutesSource))
        {
            // The mount path is the last `path:` declared before this loadChildren.
            var preceding = pathMatcher.Matches(appRoutesSource[..lc.Index]);
            if (preceding.Count == 0)
                continue;

            var mount = preceding[^1].Groups[1].Value;
            if (string.IsNullOrEmpty(mount))
                continue;

            var relative = lc.Groups[1].Value.TrimStart('.', '/');
            var file = Path.Combine(appDir, relative.Replace('/', Path.DirectorySeparatorChar) + ".ts");
            if (File.Exists(file))
                mounts.Add((mount, file));
        }

        return mounts;
    }

    /// <summary>Parses the nested <c>path:</c> tree inside a single routes file, returning relative paths.</summary>
    private static HashSet<string> ParseRoutePathsIn(string routesFile)
    {
        var source = File.ReadAllText(routesFile);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(int Depth, string Path)>();
        var matcher = new Regex(@"path:\s*'([^']*)'", RegexOptions.Compiled);

        var depth = 0;
        var index = 0;

        while (index < source.Length)
        {
            var match = matcher.Match(source, index);
            if (!match.Success)
                break;

            for (var i = index; i < match.Index; i++)
            {
                if (source[i] is '{' or '[') depth++;
                else if (source[i] is '}' or ']')
                {
                    depth--;
                    while (stack.Count > 0 && stack.Peek().Depth > depth) stack.Pop();
                }
            }

            while (stack.Count > 0 && stack.Peek().Depth >= depth) stack.Pop();

            var segment = match.Groups[1].Value;
            var prefix = stack.Count > 0 ? stack.Peek().Path : string.Empty;
            var full = string.IsNullOrEmpty(segment)
                ? prefix
                : string.IsNullOrEmpty(prefix) ? segment : $"{prefix}/{segment}";

            paths.Add(full);
            stack.Push((depth, full));
            index = match.Index + match.Length;
        }

        return paths;
    }

    /// <summary>Walks up from the test assembly to the repository root, then resolves the given segments.</summary>
    private static string RepoPath(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the repository root (identified by CLAUDE.md) must be locatable from the test output directory");

        return Path.Combine(new[] { dir!.FullName }.Concat(segments).ToArray());
    }
}
