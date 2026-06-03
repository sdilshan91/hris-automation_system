using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HRM.Tests.Unit.Helpers;

/// <summary>
/// Creates an InMemory AppDbContext for unit testing.
/// Provides a scoped tenant context so EF global query filters work.
/// </summary>
internal static class TestDbContextFactory
{
    public static AppDbContext Create(Guid tenantId, string? databaseName = null)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        tenantContext.IsResolved.Returns(true);
        tenantContext.IsSystemContext.Returns(false);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantContext);
    }

    public static AppDbContext Create(ITenantContext tenantContext, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantContext);
    }
}
