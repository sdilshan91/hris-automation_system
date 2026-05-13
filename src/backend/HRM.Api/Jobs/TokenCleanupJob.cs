using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

public class TokenCleanupJob
{
    private readonly AppDbContext _dbContext;

    public TokenCleanupJob(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RunAsync()
    {
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
