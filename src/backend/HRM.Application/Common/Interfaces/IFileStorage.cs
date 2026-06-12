namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Abstraction for tenant-isolated file storage (US-CHR-001 FR-6).
/// Implementations: local filesystem (dev), Azure Blob / S3 / MinIO (production).
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Uploads a file to tenant-isolated storage.
    /// </summary>
    /// <param name="tenantId">Tenant identifier for path isolation.</param>
    /// <param name="relativePath">Path within the tenant scope (e.g. "core-hr/{employeeId}/profile/{filename}").</param>
    /// <param name="content">File content stream.</param>
    /// <param name="contentType">MIME type of the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored file URL or path.</returns>
    Task<string> UploadAsync(
        Guid tenantId,
        string relativePath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a signed/temporary URL for accessing a stored file.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="relativePath">Path within the tenant scope.</param>
    /// <param name="expiresIn">URL validity duration.</param>
    /// <returns>Temporary access URL.</returns>
    string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null);

    /// <summary>
    /// Deletes a file from storage.
    /// </summary>
    Task DeleteAsync(
        Guid tenantId,
        string relativePath,
        CancellationToken cancellationToken = default);
}
