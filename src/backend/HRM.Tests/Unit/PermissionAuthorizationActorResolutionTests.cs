// ============================================================================
// ISSUE-054 — PermissionAuthorizationHandler resolves the denied actor from the
// authenticated principal (sub → NameIdentifier → email), logging "anonymous"
// ONLY when the request is genuinely unauthenticated and "unknown" only when
// authenticated-but-claimless.
//
// The actor resolution is inline in HandleRequirementAsync and its OBSERVABLE
// output is the NFR-4 authorization-denied warning log. No helper was extracted,
// so we assert on that log via a capturing ILogger (the handler's only externally
// visible effect on the deny path besides NOT calling Succeed). This is a real
// seam — the rendered log line is exactly what an operator/SIEM sees.
//
// Why it fails pre-fix: the handler previously logged a fixed "unknown" (or the
// raw NameIdentifier only), so an authenticated principal carrying its id under
// "sub" rendered User=unknown — the assertion for User={sub} fails pre-fix.
// ============================================================================

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using HRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace HRM.Tests.Unit;

public sealed class PermissionAuthorizationActorResolutionTests
{
    private const string RequiredPermission = "Payroll.View";

    // Binds @TC-AUTHZ-ACTOR-054.
    [Fact]
    public async Task AuthzDenied_ResolvesActor_ISSUE054()
    {
        var subId = Guid.NewGuid().ToString();
        // Authenticated principal that LACKS the required permission, carrying its id under "sub".
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subId),
                new Claim("tenant_id", Guid.NewGuid().ToString()),
                new Claim("permissions", "Payroll.Edit"), // some other permission, not the required one
            },
            authenticationType: "TestJwt")); // non-null authtype ⇒ IsAuthenticated == true

        var logger = new CapturingLogger<PermissionAuthorizationHandler>();
        var context = await HandleAsync(principal, logger);

        context.HasSucceeded.Should().BeFalse("the principal lacks the required permission");
        logger.Messages.Should().ContainSingle()
            .Which.Should().Contain($"User={subId}",
                "the denied actor must be resolved from the sub claim, not defaulted to 'unknown' (ISSUE-054)");
    }

    // Binds @TC-AUTHZ-ACTOR-054 (anonymous only when genuinely unauthenticated).
    [Fact]
    public async Task AuthzDenied_Anonymous_WhenUnauthenticated_ISSUE054()
    {
        // No authenticationType ⇒ IsAuthenticated == false ⇒ genuinely anonymous.
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var logger = new CapturingLogger<PermissionAuthorizationHandler>();
        var context = await HandleAsync(principal, logger);

        context.HasSucceeded.Should().BeFalse();
        logger.Messages.Should().ContainSingle()
            .Which.Should().Contain("User=anonymous",
                "an unauthenticated request must resolve to 'anonymous', not 'unknown' (ISSUE-054)");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static async Task<AuthorizationHandlerContext> HandleAsync(
        ClaimsPrincipal principal, ILogger<PermissionAuthorizationHandler> logger)
    {
        var handler = new PermissionAuthorizationHandler(logger);
        var requirement = new PermissionRequirement(RequiredPermission);
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, resource: null);
        await handler.HandleAsync(context);
        return context;
    }

    /// <summary>Records the fully-rendered log messages so we can assert on the denied-actor rendering.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
