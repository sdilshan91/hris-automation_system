using System.Net;
using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// GAP-001 detector: proves that a request which reaches the API with <b>no resolvable tenant</b> cannot
/// obtain tenant data from <c>/api/v1/tenant/*</c>.
///
/// <para><b>Why this exists.</b> Tenant isolation is fail-OPEN by construction at every layer except the
/// service guards:</para>
/// <list type="bullet">
///   <item><see cref="HRM.Api.Middleware.TenantResolutionMiddleware"/> passes an unresolved request
///   through (<c>if (string.IsNullOrEmpty(subdomain)) { await _next(context); return; }</c>) rather than
///   rejecting it.</item>
///   <item>The <see cref="AppDbContext"/> global query filters read
///   <c>!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId</c> — a <b>tautology</b> when
///   unresolved, so every row of every tenant matches.</item>
///   <item><c>TenantAccessGuardMiddleware</c>, <c>TenantStatusEnforcementMiddleware</c>,
///   <c>ModuleEntitlementMiddleware</c> and <c>ScimEntitlementMiddleware</c> all short-circuit with
///   "<c>if (!IsResolved) → next</c>".</item>
///   <item>The one correctly-inverted layer, <c>ConnectionRoutingInterceptor</c> (RLS), is inert in the
///   shipped Development config (<c>PrivilegedConnection</c> blank, <c>Rls:Enabled=false</c>) — and this
///   harness pins it off deliberately, so it cannot mask anything here.</item>
/// </list>
///
/// <para>What actually holds Critical Rule #1 today is a <b>convention</b>: ~494 hand-written
/// <c>if (!_tenantContext.IsResolved) return Failure(400)</c> guards across ~104 service files. One new read
/// path shipped without its guard is a full cross-tenant leak, and nothing would fail.</para>
///
/// <para><b>How this test is built to catch that.</b> It does not hard-code a route list — a fixed list
/// would say nothing about the endpoint someone adds next month, which is the actual threat. Instead it
/// enumerates the live <see cref="EndpointDataSource"/> at runtime, so <b>every</b> parameterless
/// <c>GET /api/v1/tenant/*</c> route (controller or minimal API) is swept the moment it is mapped, with no
/// test edit required.</para>
///
/// <para><b>Why it cannot be vacuously green.</b> Three separate anti-vacuity controls:</para>
/// <list type="number">
///   <item>The caller is authenticated and holds <see cref="PermissionCatalog.AllPermissions"/>, so no
///   route can be "denied" by authentication or <c>[RequirePermission]</c> instead of by the tenant guard.
///   (Authentication and permissions are entirely tenant-independent — permissions are JWT claims, not a
///   DB lookup — so an unresolved tenant does not weaken the persona.)</item>
///   <item>Every route is first proven <b>reachable</b>: the same sweep runs WITH a resolved tenant and
///   only routes that answered 2xx there are asserted on. A route that 400s on a missing query parameter,
///   or 404s for lack of setup, is excluded rather than counted as a bogus "fail-closed" win.</item>
///   <item>Floors on both the discovered-route count and the reachable-route count, so a discovery bug
///   that returns an empty set fails loudly instead of passing trivially.</item>
/// </list>
///
/// <para><b>What is actually asserted.</b> "Every route must return a refusal" was the obvious rule and
/// it is WRONG — it has false positives, which is how a security test gets weakened later. Four routes
/// legitimately answer 200 with no tenant resolved: three serve global catalogs
/// (<c>roles/permissions</c>, <c>job-titles/employment-types</c>, <c>employees/import/template</c>) and
/// <c>auth-settings</c> is scoped by the JWT's <c>ICurrentUser.TenantId</c> rather than by
/// <see cref="ITenantContext"/>, so it returns the caller's OWN row. None of those is a leak.
///
/// <para>So the assertion is the invariant that actually encodes the security property:
/// <b>removing tenant resolution must not change what the caller can see.</b> For every reachable route
/// the unresolved response must either (a) be a refusal, or (b) be byte-identical (modulo the response
/// envelope's <c>timestamp</c>) to what the same caller got WITH its tenant resolved. A leak necessarily
/// violates (b): the tautological filter adds rows that were not visible before. This needs no
/// hand-maintained exemption list — the four benign routes satisfy (b) on their own merits, and a route
/// that stops being benign starts failing without anyone editing this test.</para>
///
/// <para>A marker Department seeded in a separate victim tenant backs this up as an independent net:
/// no response may contain it, ever.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class UnresolvedTenantFailClosedApiTests
{
    private const string TenantHeader = "X-Tenant-Subdomain";
    private const string TenantRoutePrefix = "api/v1/tenant/";

    /// <summary>
    /// Statuses that count as a genuine refusal. Deliberately NOT "anything that is not 200": a 5xx is a
    /// crash, not a control, and would be a defect worth surfacing rather than a pass.
    /// </summary>
    private static readonly HttpStatusCode[] RefusalStatuses =
    {
        HttpStatusCode.BadRequest,       // the ~494 service guards: "Tenant context is not resolved." (400)
        HttpStatusCode.Unauthorized,     // 401
        HttpStatusCode.Forbidden,        // 403
        HttpStatusCode.NotFound,         // 404 — incl. the middleware's workspace-not-found page
    };

    /// <summary>
    /// Floor on discovered routes. At the time of writing the sweep finds well over this; the floor exists
    /// only so a broken/renamed route-discovery path fails loudly instead of sweeping an empty set and
    /// reporting green (the self-certifying-tautology trap).
    /// </summary>
    private const int MinimumDiscoveredRoutes = 15;

    /// <summary>
    /// Floor on routes proven reachable under a RESOLVED tenant. This is the anti-vacuity control that
    /// matters: it proves the fail-closed assertions below ran against endpoints that genuinely serve
    /// tenant data, not against a set of routes that reject everything anyway.
    /// </summary>
    private const int MinimumReachableRoutes = 8;

    private readonly ApiTestFactory _factory;

    public UnresolvedTenantFailClosedApiTests(ApiTestFactory factory) => _factory = factory;

    /// <summary>
    /// Case 1 — <b>no subdomain at all</b>. Production analogue: a request to the apex host
    /// (<c>yourhrm.com/api/v1/tenant/...</c>), which yields an empty subdomain and takes
    /// <c>TenantResolutionMiddleware</c>'s pass-through at the "No subdomain" branch. In this harness the
    /// host is always <c>localhost</c>, so omitting the dev <c>X-Tenant-Subdomain</c> header reproduces
    /// exactly that state: <c>ITenantContext.IsResolved == false</c>.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PLT-GAP001")]
    public async Task NoTenantHeader_EveryReachableTenantGetRoute_FailsClosed()
    {
        await AssertSweepFailsClosedAsync(
            unresolvedSubdomain: null,
            because: "a request with NO resolvable tenant (apex host / absent X-Tenant-Subdomain) reaches the "
                   + "controllers with IsResolved == false, where the EF global filters degenerate to TRUE — "
                   + "only the service-layer guard stands between the caller and every tenant's rows (GAP-001)");
    }

    /// <summary>
    /// Case 2 — <b>a reserved subdomain</b>. Production analogue: <c>www.yourhrm.com/api/v1/tenant/...</c>.
    /// This is a genuinely different code path from Case 1: the subdomain is non-empty, so the empty-subdomain
    /// branch does not fire; resolution instead short-circuits at the reserved-subdomain branch — again
    /// passing the request through unresolved. <c>admin</c> is excluded on purpose: it is intercepted earlier
    /// as the system context, which sets <c>IsResolved = true</c> with <c>TenantId == Guid.Empty</c> and so
    /// is a different (fail-closed-by-accident) state, not this one.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PLT-GAP001")]
    public async Task ReservedSubdomain_EveryReachableTenantGetRoute_FailsClosed()
    {
        await AssertSweepFailsClosedAsync(
            unresolvedSubdomain: "www",
            because: "a reserved subdomain (www.yourhrm.com) skips tenant resolution and reaches the "
                   + "controllers unresolved — the same tautological-filter exposure as the apex host, via a "
                   + "separate branch of TenantResolutionMiddleware (GAP-001)");
    }

    /// <summary>
    /// Case 3 — an <b>unknown</b> subdomain. Unlike cases 1 and 2 this one is already fail-CLOSED inside
    /// TenantResolutionMiddleware (404 workspace-not-found), so the arm is regression protection: it pins the
    /// one unresolved shape that is currently rejected at the door, so a future "be lenient about unknown
    /// workspaces" change cannot quietly convert it into a third pass-through.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PLT-GAP001")]
    public async Task UnknownSubdomain_IsRejectedBeforeReachingAnyTenantRoute()
    {
        var client = await CreateAuthedClientAsync($"ghost{Guid.NewGuid():N}"[..14]);

        var response = await client.GetAsync("/api/v1/tenant/departments");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unknown subdomain must be refused by TenantResolutionMiddleware rather than passed through "
            + "unresolved (which is what the apex-host and reserved-subdomain branches do)");
        AssertCarriesNoSuccessfulDataEnvelope("/api/v1/tenant/departments", body);
    }

    /// <summary>
    /// Case 4 — a <b>malformed</b> subdomain (fails the format validation). Also currently fail-closed;
    /// pinned for the same reason as case 3.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PLT-GAP001")]
    public async Task MalformedSubdomain_IsRejectedBeforeReachingAnyTenantRoute()
    {
        var client = await CreateAuthedClientAsync("-not-valid-");

        var response = await client.GetAsync("/api/v1/tenant/departments");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a subdomain that fails IsValidSubdomain must be refused, not resolved and not passed through");
        AssertCarriesNoSuccessfulDataEnvelope("/api/v1/tenant/departments", body);
    }

    /// <summary>
    /// Guards the detector itself. If route discovery silently stops finding endpoints (a routing change, a
    /// prefix rename, an EndpointDataSource that is not materialised), the sweeps above would iterate an
    /// empty collection and pass while testing nothing. This arm makes that failure mode loud.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PLT-GAP001")]
    public void RouteDiscovery_FindsANonTrivialSetOfTenantGetRoutes()
    {
        _factory.CreateClient(); // force the host (and therefore the endpoint graph) to be built

        var routes = DiscoverParameterlessTenantGetRoutes();

        routes.Should().HaveCountGreaterThanOrEqualTo(MinimumDiscoveredRoutes,
            "the fail-closed sweeps are only meaningful if discovery actually enumerates the "
            + $"'{TenantRoutePrefix}' GET surface; an empty or tiny set means discovery broke, not that the "
            + "API shrank");
    }

    // ── Sweep ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the two-pass sweep: first WITH a resolved tenant to establish which routes are genuinely
    /// reachable and serve data, then again in the given unresolved state, asserting each reachable route
    /// fails closed and leaks nothing.
    /// </summary>
    private async Task AssertSweepFailsClosedAsync(string? unresolvedSubdomain, string because)
    {
        // A caller who can be stopped by nothing except the tenant guard: real login, real token, and the
        // entire permission catalogue so [RequirePermission] never denies on our behalf.
        var personaSubdomain = $"gap001{Guid.NewGuid():N}"[..14];
        var resolvedClient = await _factory.CreateClientWithPermissionsAsync(
            PermissionCatalog.AllPermissions.ToArray());

        // The row a leak would expose: a department belonging to a DIFFERENT tenant than the caller's.
        var marker = await SeedVictimTenantDepartmentAsync(personaSubdomain);

        var routes = DiscoverParameterlessTenantGetRoutes();
        routes.Should().HaveCountGreaterThanOrEqualTo(MinimumDiscoveredRoutes,
            "route discovery must enumerate the tenant GET surface for this sweep to mean anything");

        // Pass 1 — reachability, and the baseline of what this caller is ENTITLED to see. Only routes that
        // answer 2xx for a properly resolved tenant are asserted on.
        var baseline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
        {
            var probe = await resolvedClient.GetAsync(route);
            if ((int)probe.StatusCode is >= 200 and < 300)
            {
                baseline[route] = Normalize(await probe.Content.ReadAsStringAsync());
            }
        }

        baseline.Should().HaveCountGreaterThanOrEqualTo(MinimumReachableRoutes,
            "the fail-closed assertions must run against endpoints proven to serve tenant data under a "
            + "resolved tenant — otherwise a green sweep could just mean every route rejects everything");

        // Pass 2 — the same routes, with no resolvable tenant.
        var unresolvedClient = CloneWithSubdomain(resolvedClient, unresolvedSubdomain);
        var leaked = new List<string>();

        foreach (var (route, entitledBody) in baseline)
        {
            var response = await unresolvedClient.GetAsync(route);
            var body = await response.Content.ReadAsStringAsync();

            // Independent net, applied to every response regardless of status: another tenant's row must
            // never appear.
            AssertCarriesNoVictimRow(route, body, marker);

            if (RefusalStatuses.Contains(response.StatusCode))
            {
                // Fail-closed: the service guard refused. Belt-and-braces — a refusal must not also
                // somehow carry a successful data envelope.
                AssertCarriesNoSuccessfulDataEnvelope(route, body);
                continue;
            }

            if ((int)response.StatusCode is < 200 or >= 300)
            {
                // A 5xx is a crash, not a control. Surface it rather than counting it as "not 200, so fine".
                leaked.Add($"{route} → {(int)response.StatusCode} {response.StatusCode} (unexpected status)");
                continue;
            }

            // Answered 2xx with no tenant resolved. That is only acceptable if dropping tenant resolution
            // revealed NOTHING the caller could not already see — i.e. a global catalogue, or a route
            // scoped by the JWT rather than by ITenantContext.
            if (!string.Equals(Normalize(body), entitledBody, StringComparison.Ordinal))
            {
                leaked.Add(
                    $"{route} → 200 OK, but the body DIFFERS from what this caller sees with its own "
                    + $"tenant resolved. Entitled: {Truncate(entitledBody)} | Unresolved: {Truncate(Normalize(body))}");
            }
        }

        leaked.Should().BeEmpty(
            $"{because}. Route(s) that neither refused nor returned the caller's entitled view: "
            + string.Join(" ;; ", leaked));
    }

    // ── Assertions ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The victim tenant's row must never appear in a response served without a resolved tenant. This is
    /// the concrete, unambiguous leak assertion: under a missing guard the tautological query filter
    /// returns that row verbatim to a caller with no membership in its tenant.
    /// </summary>
    private static void AssertCarriesNoVictimRow(string route, string body, string? marker)
    {
        if (marker is null)
        {
            return;
        }

        body.Should().NotContain(marker,
            $"{route} returned a row belonging to a tenant the caller has no membership in, while no "
            + "tenant was resolved — this is exactly the cross-tenant leak GAP-001 describes");
    }

    /// <summary>
    /// A refusal must be a real refusal: the API's failure envelope, not a success envelope that merely
    /// happens to arrive with a 4xx status.
    /// </summary>
    private static void AssertCarriesNoSuccessfulDataEnvelope(string route, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return; // e.g. the workspace-not-found HTML page
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (root.TryGetProperty("success", out var success)
                && success.ValueKind == JsonValueKind.True
                && root.TryGetProperty("data", out var data)
                && !IsEmptyPayload(data))
            {
                Assert.Fail(
                    $"{route} refused by status code but still returned a SUCCESSFUL ApiResponse carrying "
                    + $"data while no tenant was resolved. Body: {Truncate(body)}");
            }
        }
    }

    private static bool IsEmptyPayload(JsonElement data) => data.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.Array => data.GetArrayLength() == 0,
        // A paged envelope ({ items: [...], totalCount: n }) is empty only when every collection in it is.
        JsonValueKind.Object => data.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.Array)
            && data.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Array)
                .All(p => p.Value.GetArrayLength() == 0),
        _ => false,
    };

    /// <summary>
    /// Strips the one field that legitimately differs between two identical responses — the
    /// <c>ApiResponse.Timestamp</c> stamped at construction — so bodies can be compared for equality.
    /// Non-JSON bodies (e.g. the CSV import template) are compared verbatim.
    /// </summary>
    private static string Normalize(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return body;
            }

            var kept = doc.RootElement.EnumerateObject()
                .Where(p => !p.NameEquals("timestamp"))
                .Select(p => $"{p.Name}={p.Value.GetRawText()}");

            return string.Join("&", kept);
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static string Truncate(string body) => body.Length <= 400 ? body : body[..400] + "…";

    // ── Harness helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enumerates every parameterless <c>GET</c> endpoint under <c>api/v1/tenant/</c> from the live routing
    /// graph. Parameterless because a <c>{id}</c> route filled with a random GUID is guaranteed to return
    /// nothing and would prove nothing; the collection endpoints are the ones that return rows.
    /// </summary>
    private IReadOnlyList<string> DiscoverParameterlessTenantGetRoutes()
    {
        var sources = _factory.Services.GetServices<EndpointDataSource>();

        return sources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is not null)
            .Where(e => e.RoutePattern.RawText!.TrimStart('/')
                .StartsWith(TenantRoutePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(e => e.RoutePattern.Parameters.Count == 0)
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                .Contains("GET", StringComparer.OrdinalIgnoreCase) == true)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Seeds a department in its own tenant — one the authenticated caller has no membership in — and
    /// returns a marker string unique to it. Under a missing <c>!IsResolved</c> guard the tautological
    /// query filter returns this row to the unresolved caller, which is precisely what the sweep detects.
    /// </summary>
    private async Task<string> SeedVictimTenantDepartmentAsync(string callerSubdomain)
    {
        var marker = $"GAP001-VICTIM-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var victimTenantId = Guid.NewGuid();
        var victimSubdomain = $"victim{Guid.NewGuid():N}"[..14];
        victimSubdomain.Should().NotBe(callerSubdomain);

        db.Tenants.Add(new Tenant
        {
            Id = victimTenantId,
            Subdomain = victimSubdomain,
            Name = marker,
            Status = TenantStatus.Active,
            PlanId = "default",
        });

        // TenantId is stamped explicitly: the SaveChanges tenant interceptor does not stamp while no tenant
        // is resolved (same reason ApiTestFactory's persona seeding does it).
        db.Departments.Add(new Department
        {
            Id = Guid.NewGuid(),
            TenantId = victimTenantId,
            Name = marker,
            Code = marker[..12],
            IsActive = true,
        });

        await db.SaveChangesAsync();
        return marker;
    }

    /// <summary>
    /// Builds a client carrying the same bearer token but a different (or absent) tenant header. The token
    /// is deliberately kept: an UNAUTHENTICATED sweep would be refused by <c>[Authorize]</c> at 401 and
    /// would prove nothing about tenant isolation.
    /// </summary>
    private HttpClient CloneWithSubdomain(HttpClient authed, string? subdomain)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = authed.DefaultRequestHeaders.Authorization;

        if (subdomain is not null)
        {
            client.DefaultRequestHeaders.Add(TenantHeader, subdomain);
        }

        return client;
    }

    /// <summary>
    /// An authenticated client pointed at an arbitrary subdomain. Logs in against the caller's real tenant
    /// first (so the token is genuine), then repoints the tenant header.
    /// </summary>
    private async Task<HttpClient> CreateAuthedClientAsync(string subdomain)
    {
        var authed = await _factory.CreateClientWithPermissionsAsync(
            PermissionCatalog.AllPermissions.ToArray());

        return CloneWithSubdomain(authed, subdomain);
    }
}
