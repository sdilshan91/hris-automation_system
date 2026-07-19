using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Local filesystem implementation of IFileStorage for development.
/// Stores files under a configurable base path (default: ./uploads).
/// For production, replace with Azure Blob / S3 / MinIO implementation.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(string basePath, ILogger<LocalFileStorage> logger)
    {
        _basePath = basePath;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        Guid tenantId,
        string relativePath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveTenantPath(tenantId, relativePath);
        var directory = Path.GetDirectoryName(fullPath)!;

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogInformation(
            "File uploaded to local storage. TenantId={TenantId}, Path={Path}, ContentType={ContentType}",
            tenantId, relativePath, contentType);

        return $"/{tenantId}/{relativePath}";
    }

    public Task<Stream?> OpenReadAsync(
        Guid tenantId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveTenantPath(tenantId, relativePath);

        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
    {
        // Local dev: return a simple path (no real signing).
        // In production, this would generate a pre-signed URL with expiration.
        return $"/files/{tenantId}/{relativePath}";
    }

    public Task DeleteAsync(
        Guid tenantId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveTenantPath(tenantId, relativePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation(
                "File deleted from local storage. TenantId={TenantId}, Path={Path}",
                tenantId, relativePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the physical path for a tenant-scoped asset and asserts (defense-in-depth) that the canonicalized
    /// result stays inside the tenant's own base directory. Not exploitable today (relativePath is always
    /// server-derived), but a stray <c>..</c> segment must never let one tenant's write/read/delete escape its
    /// directory or reach a sibling tenant. The trailing-separator check prevents <c>/base/tenantX</c> from
    /// being treated as a prefix of <c>/base/tenantXY</c>.
    /// </summary>
    private string ResolveTenantPath(Guid tenantId, string relativePath)
    {
        var tenantBase = Path.GetFullPath(Path.Combine(_basePath, tenantId.ToString()));
        var fullPath = Path.GetFullPath(Path.Combine(tenantBase, relativePath));

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var boundary = tenantBase.EndsWith(Path.DirectorySeparatorChar)
            ? tenantBase
            : tenantBase + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(boundary, comparison))
        {
            _logger.LogWarning(
                "Rejected path outside tenant storage boundary. TenantId={TenantId}, RelativePath={RelativePath}",
                tenantId, relativePath);
            throw new InvalidOperationException("Resolved path escapes the tenant storage directory.");
        }

        return fullPath;
    }
}
