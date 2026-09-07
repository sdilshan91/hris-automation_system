// ============================================================================
// ISSUE-303 (gap 1) — HTTP-layer coverage for FnFPolicyController.
//
// WHAT WAS MISSING. `FnFPolicyController` ([Route("api/v1/payroll/fnf-policy")]) had ZERO references
// anywhere in HRM.Tests: the existing F&F suites (FinalSettlementIntegrationTests,
// FinalSettlementPostgresTests, FnFPolicyServiceTests) all enter at the SERVICE layer. That leaves the
// controller's own contract — routing, [FromBody]/[FromQuery] model binding, the [Authorize] +
// [RequirePermission("Payroll.Configure")] attributes, the ApiResponse envelope, and the
// Result→StatusCode mapping incl. 201 CreatedAtAction — entirely unasserted. A service-layer test stays
// green while the route is renamed, the auth attribute is deleted, or the envelope shape changes.
//
// These tests drive the genuine HTTP → routing → auth → MediatR → FluentValidation → Npgsql path via
// ApiTestFactory (real ASP.NET Core pipeline over a throwaway postgres:17-alpine container).
//
// TENANT CHOICE — DELIBERATE. Every arm runs on a FRESH per-test tenant seeded by
// CreateClientWithPermissionsAsync, not the shared `platform` tenant. Two reasons: (a) `platform` is a
// SYSTEM context, which short-circuits ModuleEntitlementMiddleware and TenantStatusEnforcement — testing
// there would skip gates a real customer request passes through; (b) a fresh tenant starts with ZERO F&F
// policy rows, so the list assertion can be EXACT ("exactly my row") instead of the weaker "contains".
// ============================================================================

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

[Collection("HttpApi")]
[Trait("TC", "TC-PAY-013-08")]
public sealed class FnFPolicyApiTests
{
    private const string ConfigurePermission = "Payroll.Configure";
    private const string BaseRoute = "/api/v1/payroll/fnf-policy";

    private readonly ApiTestFactory _factory;

    public FnFPolicyApiTests(ApiTestFactory factory) => _factory = factory;

    // ── Happy path ───────────────────────────────────────────────────────

    /// <summary>
    /// POST → 201 with the ApiResponse envelope and the persisted policy; the Location header points at the
    /// effective-policy route; GET /effective and GET (list) then read the SAME row back over the wire.
    /// This is the controller contract the service-layer suites never touch.
    /// </summary>
    [Fact]
    public async Task Create_ThenReadBack_ReturnsCreatedPolicyOverHttp()
    {
        var client = await _factory.CreateClientWithPermissionsAsync(ConfigurePermission);
        var effectiveFrom = new DateOnly(2026, 3, 17);

        var create = await client.PostAsJsonAsync(BaseRoute, new
        {
            effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
            includeProRatedFinalPay = true,
            includeStatutory = false,
            includeLeaveEncashment = true,
            finalPeriodOwnedBySettlement = true,
            isActive = true,
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created,
            "the controller maps a successful CreateFnFPolicyCommand to 201 CreatedAtAction. " + await BodyAsync(create));

        // CreatedAtAction(nameof(GetEffective)) — the Location must resolve to the effective-policy route,
        // not the collection route. A wrong nameof() here compiles and only shows up over HTTP.
        create.Headers.Location.Should().NotBeNull();
        create.Headers.Location!.ToString().Should().Contain("/api/v1/payroll/fnf-policy/effective",
            "CreatedAtAction points at GetEffective");

        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var envelope = createdDoc.RootElement;
        envelope.GetProperty("success").GetBoolean().Should().BeTrue();

        var created = envelope.GetProperty("data");
        var createdId = created.GetProperty("id").GetGuid();
        createdId.Should().NotBeEmpty("a persisted policy carries a real id, not the code-default Guid.Empty");
        created.GetProperty("effectiveFrom").GetString().Should().Be("2026-03-17");
        created.GetProperty("includeProRatedFinalPay").GetBoolean().Should().BeTrue();
        created.GetProperty("includeStatutory").GetBoolean().Should().BeFalse(
            "the false toggle must survive JSON binding — a dropped bool would silently default to true");
        created.GetProperty("includeLeaveEncashment").GetBoolean().Should().BeTrue();
        created.GetProperty("finalPeriodOwnedBySettlement").GetBoolean().Should().BeTrue();
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();
        created.GetProperty("isDefault").GetBoolean().Should().BeFalse("this is a configured version, not the code-default");

        // GET /effective?asOf= — [FromQuery] DateOnly? binding, on a date at/after the version's start.
        var effective = await client.GetAsync($"{BaseRoute}/effective?asOf=2026-06-30");
        effective.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(effective));

        using var effectiveDoc = JsonDocument.Parse(await effective.Content.ReadAsStringAsync());
        var resolved = effectiveDoc.RootElement.GetProperty("data");
        resolved.GetProperty("id").GetGuid().Should().Be(createdId,
            "the version in effect on 2026-06-30 is the one created with EffectiveFrom 2026-03-17");
        resolved.GetProperty("isDefault").GetBoolean().Should().BeFalse();
        resolved.GetProperty("includeStatutory").GetBoolean().Should().BeFalse();

        // GET (list) — a fresh tenant holds EXACTLY this one version.
        var list = await client.GetAsync(BaseRoute);
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var ids = listDoc.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid()).ToList();
        ids.Should().BeEquivalentTo(new[] { createdId },
            "a freshly seeded tenant sees its own single policy version and nothing else");
    }

    /// <summary>
    /// GET /effective with NO configured version returns the code-default (isDefault=true, Guid.Empty id) at
    /// 200 — the documented safe fallback, asserted over the wire so the envelope shape is pinned too.
    /// </summary>
    [Fact]
    public async Task GetEffective_WithNoConfiguredPolicy_ReturnsCodeDefaultOverHttp()
    {
        var client = await _factory.CreateClientWithPermissionsAsync(ConfigurePermission);

        var response = await client.GetAsync($"{BaseRoute}/effective");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(response));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("isDefault").GetBoolean().Should().BeTrue(
            "with no persisted version the service surfaces the code-default so effective behaviour is visible");
        data.GetProperty("id").GetGuid().Should().BeEmpty("the code-default is unpersisted");
        data.GetProperty("includeProRatedFinalPay").GetBoolean().Should().BeTrue();
        data.GetProperty("includeStatutory").GetBoolean().Should().BeTrue();
        data.GetProperty("includeLeaveEncashment").GetBoolean().Should().BeTrue();
        data.GetProperty("finalPeriodOwnedBySettlement").GetBoolean().Should().BeTrue();
    }

    // ── Security: authentication + authorization ─────────────────────────

    /// <summary>
    /// [Authorize] — every route on the controller rejects an unauthenticated caller with 401, even with a
    /// valid tenant header. Asserted on all three routes so deleting the attribute cannot pass unnoticed.
    /// </summary>
    [Theory]
    [InlineData("GET", BaseRoute)]
    [InlineData("GET", BaseRoute + "/effective")]
    [InlineData("POST", BaseRoute)]
    public async Task AnyRoute_WithoutBearerToken_IsRejectedWith401(string method, string route)
    {
        var client = _factory.CreateClient();

        var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Headers.Add("X-Tenant-Subdomain", "platform");
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { effectiveFrom = "2026-04-01" });
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"[Authorize] on FnFPolicyController must reject an anonymous {method} {route}. " + await BodyAsync(response));
    }

    /// <summary>
    /// [RequirePermission("Payroll.Configure")] — a genuinely logged-in user WITHOUT the permission gets 403.
    ///
    /// <para><b>The confound this arm rules out.</b> /api/v1/payroll is also gated by
    /// ModuleEntitlementMiddleware, which ALSO answers 403. A bare "is it 403?" assertion would therefore stay
    /// green even if [RequirePermission] were deleted. Two things prevent that: the body must NOT carry the
    /// entitlement gate's <c>module_not_entitled</c> code, and the paired control below proves the SAME route
    /// on an identically-provisioned tenant is reachable once the permission is granted. The only difference
    /// between the two callers is the permission, so the 403 can only be the permission gate.</para>
    /// </summary>
    [Theory]
    [InlineData("GET", BaseRoute)]
    [InlineData("GET", BaseRoute + "/effective")]
    [InlineData("POST", BaseRoute)]
    public async Task AnyRoute_AsAuthenticatedUserWithoutPayrollConfigure_IsRejectedWith403(string method, string route)
    {
        var client = await _factory.CreateClientWithPermissionsAsync(); // genuinely permission-less caller

        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { effectiveFrom = "2026-04-01" });
        }

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"[RequirePermission(\"{ConfigurePermission}\")] gates {method} {route}. Response body: {body}");

        ErrorCodeOf(body).Should().NotBe("module_not_entitled",
            "this 403 must come from the PERMISSION gate, not from ModuleEntitlementMiddleware — otherwise "
            + "the arm would stay green with [RequirePermission] deleted");
    }

    /// <summary>
    /// Paired positive control for the arm above: the identical route, on an identically-provisioned fresh
    /// tenant, is NOT 403 once the caller holds Payroll.Configure. Without this, "403 for the wrong reason"
    /// is indistinguishable from "403 for the right reason".
    /// </summary>
    [Fact]
    public async Task ListRoute_AsAuthenticatedUserWithPayrollConfigure_IsNotForbidden()
    {
        var client = await _factory.CreateClientWithPermissionsAsync(ConfigurePermission);

        var response = await client.GetAsync(BaseRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "granting exactly Payroll.Configure — and changing nothing else — makes the same route reachable, "
            + "which is what proves the 403 above is the permission gate. " + await BodyAsync(response));
    }

    // ── Negative: malformed / invalid request bodies ─────────────────────

    /// <summary>
    /// Syntactically broken JSON never reaches the handler: [ApiController]'s automatic model-state check
    /// answers 400 at the binding layer.
    /// </summary>
    [Fact]
    public async Task Create_WithMalformedJsonBody_IsRejectedWith400()
    {
        var client = await _factory.CreateClientWithPermissionsAsync(ConfigurePermission);

        var content = new StringContent("{ \"effectiveFrom\": ", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(BaseRoute, content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "unparseable JSON fails model binding before the handler runs. " + await BodyAsync(response));
    }

    /// <summary>
    /// A well-formed body carrying an unparseable date for the required DateOnly is likewise a binding 400 —
    /// the field is non-nullable, so a bad value cannot silently become default(DateOnly).
    /// </summary>
    [Fact]
    public async Task Create_WithUnparseableEffectiveFrom_IsRejectedWith400()
    {
        var client = await _factory.CreateClientWithPermissionsAsync(ConfigurePermission);

        var content = new StringContent(
            "{\"effectiveFrom\":\"not-a-date\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(BaseRoute, content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "\"not-a-date\" cannot bind to DateOnly. " + await BodyAsync(response));
    }

    /// <summary>
    /// A structurally VALID body that violates CreateFnFPolicyValidator (EffectiveFrom left at default) must
    /// surface as a 400 carrying the validator's own message through ValidationBehavior →
    /// ExceptionHandlingMiddleware → ApiResponse. Asserting the message, not just the status, is what
    /// distinguishes this from the binding-layer 400s above — they are different code paths that share a code.
    /// </summary>
    [Fact]
    public async Task Create_WithDefaultEffectiveFrom_IsRejectedWith400AndTheValidatorMessage()
    {
        var client = await _factory.CreateClientWithPermissionsAsync(ConfigurePermission);

        var response = await client.PostAsJsonAsync(BaseRoute, new
        {
            effectiveFrom = "0001-01-01", // default(DateOnly) — the one rule CreateFnFPolicyValidator enforces
            includeProRatedFinalPay = true,
            includeStatutory = true,
            includeLeaveEncashment = true,
            finalPeriodOwnedBySettlement = true,
            isActive = true,
        });

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "CreateFnFPolicyValidator rejects a default effective-from date. Response body: " + body);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("message").GetString().Should().Be("Effective-from date is required.",
            "the validator's message reaches the client verbatim through the ApiResponse envelope");
        root.GetProperty("errors").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("Effective-from date is required.");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// The machine-readable failure code from the ApiResponse envelope (serialized as <c>code</c>), or null
    /// when the response is not an ApiResponse failure (e.g. the attribute-driven empty-body 403).
    /// </summary>
    private static string? ErrorCodeOf(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("code", out var code)
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";
}
