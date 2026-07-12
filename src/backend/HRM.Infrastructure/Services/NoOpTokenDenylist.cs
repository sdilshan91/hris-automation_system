using HRM.Application.Common.Interfaces;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="ITokenDenylist"/> used when Redis is not configured (the shipped dev default). Records no
/// cutoff and never reports a token as revoked, so the <c>OnTokenValidated</c> denylist check no-ops gracefully
/// without a Redis backend. Paired with <see cref="NoOpSessionRevoker"/> in DI.
/// </summary>
public sealed class NoOpTokenDenylist : ITokenDenylist
{
    public Task RevokeAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> IsRevokedAsync(
        Guid userId, Guid tenantId, long tokenIssuedAtUnix, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
