namespace HRM.Domain.Interfaces;

/// <summary>
/// Unit of work pattern for coordinating writes across repositories.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
