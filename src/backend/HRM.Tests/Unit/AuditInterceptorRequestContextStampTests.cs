// ============================================================================
// ISSUE-006 — AuditInterceptor stamps IpAddress / UserAgent from the current
// request onto newly-added AuditLog rows that didn't set them explicitly.
//
// Many audit-write sites (TenantLifecycleService, EmployeeService, ...) don't
// thread ip/user-agent through. The interceptor now takes an optional
// IHttpContextAccessor and, in StampRequestContext, backfills those fields on any
// Added AuditLog whose values are empty — never overwriting values a writer
// already set, and a safe no-op when there is no HttpContext (background jobs).
//
// Seam: the real AuditInterceptor wired into a real AppDbContext (mirrors
// AuditCaptureInterceptorTests), with a fake IHttpContextAccessor carrying a
// RemoteIpAddress + a User-Agent header. We assert on the PERSISTED AuditLog row.
//
// Why the positive case fails pre-fix: the interceptor had no HttpContextAccessor
// and no StampRequestContext, so a row written without ip/ua stayed null. The
// null-safe case guards the additive contract (no throw / no stamp when there is
// no request in scope).
// ============================================================================

using System.Net;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AuditInterceptorRequestContextStampTests
{
    private const string Ip = "203.0.113.42";
    private const string Ua = "Mozilla/5.0 (AuditStampTest)";
    private readonly Guid _tenantId = Guid.NewGuid();

    // Binds @TC-AUDIT-STAMP-006.
    [Fact]
    public async Task Audit_StampsIpUserAgent_ISSUE006()
    {
        var dbName = Guid.NewGuid().ToString();
        var rowId = Guid.NewGuid();

        using (var db = Db(dbName, HttpAccessorWithRequest()))
        {
            // A writer that does NOT set ip/ua (the common case the fix targets).
            db.AuditLogs.Add(new AuditLog
            {
                Id = rowId,
                TenantId = _tenantId,
                EventType = "test.audited_write",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var read = Db(dbName, httpAccessor: null);
        var row = await read.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.Id == rowId);
        row.IpAddress.Should().Be(Ip, "the interceptor must backfill the request IP (ISSUE-006)");
        row.UserAgent.Should().Be(Ua, "the interceptor must backfill the request User-Agent (ISSUE-006)");
    }

    // Binds @TC-AUDIT-STAMP-006 (null-safe: no request in scope → no throw, no stamp).
    [Fact]
    public async Task Audit_NoHttpContext_IsNullSafe_ISSUE006()
    {
        var dbName = Guid.NewGuid().ToString();
        var rowId = Guid.NewGuid();

        // Accessor present but HttpContext is null — e.g. a Hangfire background job.
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        using (var db = Db(dbName, accessor))
        {
            var write = async () =>
            {
                db.AuditLogs.Add(new AuditLog
                {
                    Id = rowId,
                    TenantId = _tenantId,
                    EventType = "test.background_write",
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            };
            await write.Should().NotThrowAsync("a missing HttpContext must be a safe no-op");
        }

        using var read = Db(dbName, httpAccessor: null);
        var row = await read.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.Id == rowId);
        row.IpAddress.Should().BeNull("no request in scope → nothing to stamp");
        row.UserAgent.Should().BeNull("no request in scope → nothing to stamp");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private ITenantContext TenantCtx()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(_tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private IHttpContextAccessor HttpAccessorWithRequest()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(Ip);
        http.Request.Headers.UserAgent = Ua;
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        return accessor;
    }

    private AppDbContext Db(string dbName, IHttpContextAccessor? httpAccessor)
    {
        var ctx = TenantCtx();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new AuditInterceptor(Substitute.For<ICurrentUser>(), httpAccessor))
            .Options;
        return new AppDbContext(options, ctx);
    }
}
