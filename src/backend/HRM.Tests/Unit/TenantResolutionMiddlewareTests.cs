using System.Text;
using System.Text.Json;
using FluentAssertions;
using HRM.Api.Middleware;
using HRM.Application.Common.Interfaces;
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
