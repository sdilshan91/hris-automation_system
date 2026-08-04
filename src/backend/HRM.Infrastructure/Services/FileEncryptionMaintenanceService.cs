using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// ISSUE-359 back-fill: reports and sweeps the still-plaintext part of a tenant's file estate.
///
/// <para><b>Why the count is not optional.</b> Tolerating legacy plaintext on read is what lets encryption ship
/// without a migration — but it is also precisely how "we will back-fill later" becomes "there is still salary
/// sitting in plaintext two years on". US-PLT-005's Scope A lesson was that invisible legacy lives forever, so
/// the remaining count is surfaced deliberately rather than left to be discovered.</para>
///
/// <para>Reads the filesystem directly rather than going through <see cref="IFileStorage"/>, because it needs
/// to see the RAW stored bytes — the encrypting decorator's whole job is to hide whether a file was sealed, and
/// a status report that could not tell the difference would always read "all encrypted".</para>
/// </summary>
public sealed class FileEncryptionMaintenanceService : IFileEncryptionMaintenanceService
{
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorage _fileStorage;
    private readonly string _basePath;
    private readonly bool _encryptionEnabled;
    private readonly ILogger<FileEncryptionMaintenanceService> _logger;

    public FileEncryptionMaintenanceService(
        ITenantContext tenantContext,
        IFileStorage fileStorage,
        IConfiguration configuration,
        ILogger<FileEncryptionMaintenanceService> logger)
    {
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
        _basePath = configuration["FileStorage:BasePath"]
                    ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        _encryptionEnabled = configuration.GetValue("FileEncryption:Enabled", true);
        _logger = logger;
    }

    public async Task<Result<FileEncryptionStatusDto>> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<FileEncryptionStatusDto>.Failure("Tenant context is not resolved.", 400);

        int encrypted = 0, plaintext = 0, unreadable = 0;

        foreach (var (_, root) in ResolveRoots())
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var header = await ReadHeaderAsync(file, cancellationToken);
                if (!FileEnvelope.IsEncrypted(header))
                    plaintext++;
                else if (FileEnvelope.VersionOf(header) == FileEnvelope.Version1)
                    encrypted++;
                else
                    unreadable++;
            }
        }

        return Result<FileEncryptionStatusDto>.Success(new FileEncryptionStatusDto
        {
            EncryptedFiles = encrypted,
            PlaintextFiles = plaintext,
            UnreadableFiles = unreadable,
            EncryptionEnabled = _encryptionEnabled,
        });
    }

    public async Task<Result<FileEncryptionSweepResultDto>> SweepAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<FileEncryptionSweepResultDto>.Failure("Tenant context is not resolved.", 400);

        if (!_encryptionEnabled)
            return Result<FileEncryptionSweepResultDto>.Failure(
                "File encryption is disabled (FileEncryption:Enabled=false); enable it before sweeping.",
                409, "encryption_disabled");

        int scanned = 0, encrypted = 0, failed = 0;

        foreach (var (tenantId, root) in ResolveRoots())
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scanned++;

                try
                {
                    var header = await ReadHeaderAsync(file, cancellationToken);
                    if (FileEnvelope.IsEncrypted(header))
                        continue; // already sealed

                    // Relative path as IFileStorage understands it, so the write goes back through the SAME
                    // encrypting decorator that seals new uploads — one encryption implementation, not two.
                    var relativePath = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    var plaintext = await File.ReadAllBytesAsync(file, cancellationToken);

                    // The OWNING tenant's id, taken from the directory the file lives in — never the ambient
                    // context. In system context the ambient id is Guid.Empty, and sealing with it would derive
                    // the wrong key: the file would be encrypted, unreadable by its tenant, and counted as a
                    // success. Silent data loss dressed up as a green sweep.
                    await using var source = new MemoryStream(plaintext, writable: false);
                    await _fileStorage.UploadAsync(
                        tenantId, relativePath, source, "application/octet-stream", cancellationToken);

                    encrypted++;
                }
                catch (Exception ex)
                {
                    // One unreadable file must not abort the sweep — otherwise a single bad file freezes the
                    // back-fill for the whole estate and the plaintext count never moves.
                    failed++;
                    _logger.LogWarning(ex,
                        "File encryption sweep failed for {File} (tenant {TenantId}); continuing.",
                        file, tenantId);
                }
            }
        }

        _logger.LogInformation(
            "File encryption sweep complete for tenant {TenantId}: scanned={Scanned}, encrypted={Encrypted}, failed={Failed}",
            _tenantContext.TenantId, scanned, encrypted, failed);

        return Result<FileEncryptionSweepResultDto>.Success(new FileEncryptionSweepResultDto
        {
            Scanned = scanned,
            Encrypted = encrypted,
            Failed = failed,
        });
    }

    /// <summary>
    /// The (tenantId, directory) pairs this call covers.
    ///
    /// <para><b>System context walks the whole estate.</b> These endpoints are <c>Tenant.Lifecycle</c>-gated and
    /// reached on the <c>admin.*</c> subdomain, where <see cref="ITenantContext.TenantId"/> is
    /// <see cref="Guid.Empty"/> while <c>IsResolved</c> is still true. Scoping to the ambient id there would
    /// point at a directory that does not exist, and the platform-wide plaintext count — the entire reason this
    /// service exists — would read a reassuring <b>0</b>. That is worse than having no counter at all.</para>
    ///
    /// <para><b>Enumerated from disk, not from the tenants table.</b> The question being answered is "what
    /// plaintext is still on disk", so the disk is the authority. A de-provisioned tenant's directory still
    /// holds real salary and PII; enumerating the tenants table would skip exactly the files nobody is
    /// watching. Directories whose name is not a tenant GUID are skipped and logged rather than swept, since
    /// no key can be derived for them.</para>
    /// </summary>
    private List<(Guid TenantId, string Root)> ResolveRoots()
    {
        if (!Directory.Exists(_basePath))
            return [];

        if (!_tenantContext.IsSystemContext && _tenantContext.TenantId != Guid.Empty)
        {
            var root = Path.Combine(_basePath, _tenantContext.TenantId.ToString());
            return Directory.Exists(root) ? [(_tenantContext.TenantId, root)] : [];
        }

        var roots = new List<(Guid, string)>();
        foreach (var dir in Directory.EnumerateDirectories(_basePath))
        {
            if (Guid.TryParse(Path.GetFileName(dir), out var tenantId) && tenantId != Guid.Empty)
            {
                roots.Add((tenantId, dir));
            }
            else
            {
                _logger.LogWarning(
                    "Skipping {Directory} in the file store: its name is not a tenant id, so no encryption key "
                    + "can be derived for it.", dir);
            }
        }

        return roots;
    }

    /// <summary>Reads only the envelope header — classifying the estate must not load every file into memory.</summary>
    private static async Task<byte[]> ReadHeaderAsync(string path, CancellationToken cancellationToken)
    {
        var header = new byte[FileEnvelope.HeaderLength];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var read = await stream.ReadAsync(header, cancellationToken);
        return read < header.Length ? header[..Math.Max(read, 0)] : header;
    }
}
