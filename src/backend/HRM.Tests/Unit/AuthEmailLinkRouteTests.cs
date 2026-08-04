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

        paths.Should().NotBeEmpty(
            "parsing app.routes.ts produced no routes — the parse has broken and this test would otherwise " +
            "pass vacuously while proving nothing");

        // Sanity anchors: these have existed for the life of the app. If they are missing, the parser is wrong
        // rather than the routes, and every other assertion in this file is meaningless.
        paths.Should().Contain("auth/login",
            "auth/login is a long-standing route, so its absence means the parser is broken, not the app");

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
