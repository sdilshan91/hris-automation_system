using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HRM.Api.Middleware;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class TenantResolutionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_CacheHit_ShouldPopulateTenantContextWithoutDatabaseLookup()
    {
        var tenantId = Guid.NewGuid();
        var cache = new FakeDistributedCache();
        cache.SetJson("t:subdomain:acme", new
        {
            Id = tenantId,
            Subdomain = "acme",
            Status = TenantStatus.Active,
            Plan = "growth",
            EnabledModules = new[] { "Employee", "Leave" },
            LogoUrl = "https://cdn.example/logo.png",
            PrimaryColor = "#123456"
        });
        var (context, _) = CreateHttpContext("acme.yourhrm.com", cache: cache);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        nextCalled.Should().BeTrue();
        tenantContext.TenantId.Should().Be(tenantId);
        tenantContext.Subdomain.Should().Be("acme");
        tenantContext.Status.Should().Be(TenantStatus.Active);
        tenantContext.Plan.Should().Be("growth");
        tenantContext.EnabledModules.Should().BeEquivalentTo("Employee", "Leave");
        tenantContext.LogoUrl.Should().Be("https://cdn.example/logo.png");
        tenantContext.PrimaryColor.Should().Be("#123456");
    }

    [Fact]
    public async Task InvokeAsync_CacheMiss_ShouldFallbackToDatabaseAndPopulateCache()
    {
        var tenantId = Guid.NewGuid();
        var cache = new FakeDistributedCache();
        var (context, scope) = CreateHttpContext("acme.yourhrm.com", cache: cache);
        await SeedTenantAsync(scope.ServiceProvider, new Tenant
        {
            Id = tenantId,
            Subdomain = "acme",
            Name = "Acme",
            Status = TenantStatus.Suspended,
            PlanId = "enterprise",
            EnabledModules = new List<string> { "Employee", "Reports" },
            LogoUrl = "https://cdn.example/acme.png",
            PrimaryColor = "#abcdef"
        });
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        tenantContext.TenantId.Should().Be(tenantId);
        tenantContext.Status.Should().Be(TenantStatus.Suspended);
        tenantContext.Plan.Should().Be("enterprise");
        tenantContext.EnabledModules.Should().BeEquivalentTo("Employee", "Reports");
        tenantContext.LogoUrl.Should().Be("https://cdn.example/acme.png");
        cache.Values.Should().ContainKey("t:subdomain:acme");
    }

    [Fact]
    public async Task InvokeAsync_CacheFailure_ShouldFallbackToDatabase()
    {
        var tenantId = Guid.NewGuid();
        var cache = new ThrowingDistributedCache();
        var (context, scope) = CreateHttpContext("acme.yourhrm.com", cache: cache);
        await SeedTenantAsync(scope.ServiceProvider, new Tenant
        {
            Id = tenantId,
            Subdomain = "acme",
            Name = "Acme",
            Status = TenantStatus.Active
        });
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        tenantContext.TenantId.Should().Be(tenantId);
        tenantContext.Subdomain.Should().Be("acme");
    }

    [Fact]
    public async Task InvokeAsync_UnknownTenant_ShouldReturnStaticNotFoundWithoutCallingNext()
    {
        var (context, _) = CreateHttpContext("unknown.yourhrm.com");
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.ContentType.Should().Be("text/html; charset=utf-8");
        var body = await ReadResponseBodyAsync(context);
        body.Should().Contain("This workspace does not exist.");
    }

    [Fact]
    public async Task InvokeAsync_InvalidUppercaseSubdomain_ShouldReturnNotFound()
    {
        var (context, _) = CreateHttpContext("ACME.yourhrm.com");
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task InvokeAsync_AdminSubdomain_ShouldSetSystemContext()
    {
        var (context, _) = CreateHttpContext("admin.yourhrm.com");
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        tenantContext.IsSystemContext.Should().BeTrue();
        tenantContext.Subdomain.Should().Be("admin");
        tenantContext.Status.Should().Be(TenantStatus.Active);
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentHeaderOnLocalhost_ShouldResolveTenant()
    {
        var tenantId = Guid.NewGuid();
        var (context, scope) = CreateHttpContext("localhost", baseDomain: "localhost", environmentName: Environments.Development);
        context.Request.Headers["X-Tenant-Subdomain"] = new StringValues("acme");
        await SeedTenantAsync(scope.ServiceProvider, new Tenant
        {
            Id = tenantId,
            Subdomain = "acme",
            Name = "Acme",
            Status = TenantStatus.Active
        });
        var middleware = CreateMiddleware(_ => Task.CompletedTask, baseDomain: "localhost", environmentName: Environments.Development);

        await middleware.InvokeAsync(context);

        context.RequestServices.GetRequiredService<ITenantContext>().TenantId.Should().Be(tenantId);
    }

    // ── D3 (ISSUE-358): CustomDomain feature seam ─────────────────────────────
    // A custom-domain host (not under the base domain, not localhost) may serve a tenant only when the tenant's
    // plan includes the CustomDomain flag. The ONLY way a tenant resolves on a custom host today is the dev
    // X-Tenant-Subdomain header, so these run in Development. Fail-open: a tenant with no resolvable plan is never
    // refused. The regression arms below prove subdomain/localhost/dev-header/reserved/admin are untouched.

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task InvokeAsync_CustomDomainHost_WithoutCustomDomainFlag_IsRefused()
    {
        var tenantId = Guid.NewGuid();
        var (context, scope) = CreateHttpContext("hr.acme.com", environmentName: Environments.Development);
        context.Request.Headers["X-Tenant-Subdomain"] = new StringValues("acme");
        await SeedTenantAsync(scope.ServiceProvider, new Tenant
        {
            Id = tenantId, Subdomain = "acme", Name = "Acme", Status = TenantStatus.Active, PlanId = "growth"
        });
        await SeedPlanAsync(scope.ServiceProvider, code: "growth", customDomain: false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            environmentName: Environments.Development);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await ReadResponseCodeAsync(context)).Should().Be("custom_domain_not_entitled");
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task InvokeAsync_CustomDomainHost_WithCustomDomainFlag_Resolves()
    {
        // SAME host/tenant, only the plan flag differs (CustomDomain now granted) ⇒ resolves. Single-variable
        // discriminator against the refusal arm above.
        var tenantId = Guid.NewGuid();
        var (context, scope) = CreateHttpContext("hr.acme.com", environmentName: Environments.Development);
        context.Request.Headers["X-Tenant-Subdomain"] = new StringValues("acme");
        await SeedTenantAsync(scope.ServiceProvider, new Tenant
        {
            Id = tenantId, Subdomain = "acme", Name = "Acme", Status = TenantStatus.Active, PlanId = "growth"
        });
        await SeedPlanAsync(scope.ServiceProvider, code: "growth", customDomain: true);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            environmentName: Environments.Development);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.RequestServices.GetRequiredService<ITenantContext>().TenantId.Should().Be(tenantId);
        context.RequestServices.GetRequiredService<ITenantContext>().FeatureFlags
            .Should().Contain(PlanFeatureFlagKeys.CustomDomain);
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task InvokeAsync_CustomDomainHost_NoPlanRow_FailsOpen_Resolves()
    {
        // Fail-open: the tenant's PlanId matches NO plan row ⇒ flags null ⇒ a config problem must never lock the
        // tenant out. Must resolve, not 403. Inverting fail-open to fail-closed kills this arm.
        var tenantId = Guid.NewGuid();
        var (context, scope) = CreateHttpContext("hr.acme.com", environmentName: Environments.Development);
        context.Request.Headers["X-Tenant-Subdomain"] = new StringValues("acme");
        await SeedTenantAsync(scope.ServiceProvider, new Tenant
        {
            Id = tenantId, Subdomain = "acme", Name = "Acme", Status = TenantStatus.Active, PlanId = "orphan-plan"
        });
        // Intentionally seed NO subscription plan for "orphan-plan".
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; },
            environmentName: Environments.Development);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.RequestServices.GetRequiredService<ITenantContext>().TenantId.Should().Be(tenantId);
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task InvokeAsync_SubdomainHost_IsNotTreatedAsCustomDomain_EvenWithoutFlag()
    {
        // REGRESSION arm (matters most): a normal subdomain under the base domain must resolve exactly as before,
        // even when the plan grants no CustomDomain flag — the seam must NOT touch subdomain resolution.
        var tenantId = Guid.NewGuid();
        var (context, scope) = CreateHttpContext("acme.yourhrm.com");
        await SeedTenantAsync(scope.ServiceProvider, new Tenant
        {
            Id = tenantId, Subdomain = "acme", Name = "Acme", Status = TenantStatus.Active, PlanId = "growth"
        });
        await SeedPlanAsync(scope.ServiceProvider, code: "growth", customDomain: false);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.RequestServices.GetRequiredService<ITenantContext>().TenantId.Should().Be(tenantId);
    }

    [Fact]
    [Trait("TC", "TC-ADM-358")]
    public async Task InvokeAsync_ReservedSubdomain_StillPassesThroughUnresolved()
    {
        // REGRESSION arm: a reserved subdomain (www) must skip tenant resolution and pass through, untouched by
        // the custom-domain seam (host IS under the base domain, so it is never a custom host).
        var (context, _) = CreateHttpContext("www.yourhrm.com");
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.RequestServices.GetRequiredService<ITenantContext>().IsResolved.Should().BeFalse();
    }

    // ── US-PLT-004 (item 2): tenant span tags ─────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenActivityExists_SetsTenantSpanTags()
    {
        var tenantId = Guid.NewGuid();
        var cache = new FakeDistributedCache();
        cache.SetJson("t:subdomain:acme", new
        {
            Id = tenantId,
            Subdomain = "acme",
            Status = TenantStatus.Active,
            Plan = "growth",
            EnabledModules = new[] { "Employee" },
            LogoUrl = (string?)null,
            PrimaryColor = (string?)null
        });
        var (context, _) = CreateHttpContext("acme.yourhrm.com", cache: cache);
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        // An active recording Activity, mirroring what the OTel AspNetCore instrumentation supplies at runtime.
        using var source = new ActivitySource("HRM.Tests.TenantSpan");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("request");
        activity.Should().NotBeNull();

        await middleware.InvokeAsync(context);

        activity!.GetTagItem("tenant.id").Should().Be(tenantId);
        activity.GetTagItem("tenant.subdomain").Should().Be("acme");
    }

    [Fact]
    public async Task InvokeAsync_WhenNoActivity_DoesNotThrow_AndStillResolvesTenant()
    {
        // No ActivityListener is attached in this async flow, so Activity.Current is null — the null-conditional
        // in the middleware IS the guard. It must resolve the tenant normally and never throw.
        Activity.Current.Should().BeNull();

        var tenantId = Guid.NewGuid();
        var cache = new FakeDistributedCache();
        cache.SetJson("t:subdomain:acme", new
        {
            Id = tenantId,
            Subdomain = "acme",
            Status = TenantStatus.Active,
            Plan = "growth",
            EnabledModules = new[] { "Employee" },
            LogoUrl = (string?)null,
            PrimaryColor = (string?)null
        });
        var (context, _) = CreateHttpContext("acme.yourhrm.com", cache: cache);
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        var act = async () => await middleware.InvokeAsync(context);

        await act.Should().NotThrowAsync();
        context.RequestServices.GetRequiredService<ITenantContext>().TenantId.Should().Be(tenantId);
    }

    private static TenantResolutionMiddleware CreateMiddleware(
        RequestDelegate next,
        string baseDomain = "yourhrm.com",
        string environmentName = "Production")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:BaseDomain"] = baseDomain,
                ["Platform:TenantCacheTtlMinutes"] = "5"
            })
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        return new TenantResolutionMiddleware(
            next,
            NullLogger<TenantResolutionMiddleware>.Instance,
            configuration,
            environment);
    }

    private static (DefaultHttpContext Context, IServiceScope Scope) CreateHttpContext(
        string host,
        string baseDomain = "yourhrm.com",
        string environmentName = "Production",
        IDistributedCache? cache = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            options.UseInMemoryDatabase(Guid.NewGuid().ToString());
        });
        services.AddSingleton(cache ?? new FakeDistributedCache());

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        context.Request.Host = new HostString(host);
        context.Response.Body = new MemoryStream();

        _ = baseDomain;
        _ = environmentName;
        return (context, scope);
    }

    private static async Task SeedTenantAsync(IServiceProvider services, Tenant tenant)
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedPlanAsync(IServiceProvider services, string code, bool customDomain)
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        dbContext.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = code,
            Code = code,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            FeatureFlags = new PlanFeatureFlags { CustomDomain = customDomain },
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<string?> ReadResponseCodeAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private sealed class FakeDistributedCache : IDistributedCache
    {
        public Dictionary<string, byte[]> Values { get; } = new();

        public byte[]? Get(string key) => Values.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
            => Task.CompletedTask;

        public void Remove(string key) => Values.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => Values[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void SetJson(string key, object value)
            => Set(key, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)), new DistributedCacheEntryOptions());
    }

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Cache unavailable");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Cache unavailable");
        public void Refresh(string key) => throw new InvalidOperationException("Cache unavailable");
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Cache unavailable");
        public void Remove(string key) => throw new InvalidOperationException("Cache unavailable");
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Cache unavailable");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("Cache unavailable");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new InvalidOperationException("Cache unavailable");
    }
}
