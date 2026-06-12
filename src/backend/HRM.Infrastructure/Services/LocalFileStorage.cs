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
        var fullPath = Path.Combine(_basePath, tenantId.ToString(), relativePath);
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
        var fullPath = Path.Combine(_basePath, tenantId.ToString(), relativePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation(
                "File deleted from local storage. TenantId={TenantId}, Path={Path}",
                tenantId, relativePath);
        }

        return Task.CompletedTask;
    }
}
