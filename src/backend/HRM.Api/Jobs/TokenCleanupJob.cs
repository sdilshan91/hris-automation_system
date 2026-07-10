using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

public class TokenCleanupJob
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public TokenCleanupJob(AppDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task RunAsync()
    {
        // RLS increment 2c: cross-tenant purge (IgnoreQueryFilters spans every tenant). Establish the system
        // context so the ambient routes to the privileged (BYPASSRLS) connection under RLS and caches under sys:.
        _tenantContext.SetSystemContext();

        var cutoff = DateTime.UtcNow.AddDays(-30);

        var expiredTokens = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.ExpiresAt < cutoff || (rt.RevokedAt != null && rt.RevokedAt < cutoff))
            .ToListAsync();

        if (expiredTokens.Count > 0)
        {
            _dbContext.RefreshTokens.RemoveRange(expiredTokens);
            await _dbContext.SaveChangesAsync();
            Log.Information("Cleaned up {Count} expired refresh tokens", expiredTokens.Count);
        }
    }
}
