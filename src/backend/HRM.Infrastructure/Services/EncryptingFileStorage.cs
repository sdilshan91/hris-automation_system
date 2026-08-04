using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// ISSUE-359: encrypts every stored file at rest, and transparently reads both encrypted and legacy plaintext.
///
/// <para><b>A decorator, not a change to <see cref="LocalFileStorage"/>.</b> Encryption is orthogonal to where
/// bytes live, so the filesystem seam stays a pure filesystem seam and a future S3/Blob implementation inherits
/// encryption for free rather than having to re-implement it. All 18 consuming services and 32 call sites are
/// untouched — they never knew the bytes were plaintext and do not need to know they are now sealed.</para>
///
/// <para><b>Per-tenant purpose strings over a platform ring.</b> <c>CreateProtector($"file:{tenantId}")</c>
/// derives a cryptographically separated key per tenant from the single Postgres-persisted Data-Protection
/// ring, so one tenant's key material cannot open another's files — while there is still only one ring to back
/// up, rotate and never prune.</para>
///
/// <para><b>Buffered, not streaming</b> (decision 2026-08-05). Every existing read path already buffers the
/// whole file into memory, and the largest writes are produced as <c>byte[]</c> before upload — so buffering
/// here pins no memory that was not already pinned, while chunked-AEAD framing would be complexity no caller
/// exercises. The envelope's version byte reserves a streaming v2 without rewriting stored files.</para>
///
/// <para><b>The kill-switch is deliberately write-only.</b> <c>FileEncryption:Enabled=false</c> stops NEW
/// files being encrypted; it never disables decryption, because files already sealed must stay readable. A
/// switch that turned reads off too would turn a precaution into an outage.</para>
/// </summary>
public sealed class EncryptingFileStorage : IFileStorage
{
    private readonly IFileStorage _inner;
    private readonly IDataProtectionProvider _protectionProvider;
    private readonly ILogger<EncryptingFileStorage> _logger;
    private readonly bool _encryptNewWrites;

    public EncryptingFileStorage(
        IFileStorage inner,
        IDataProtectionProvider protectionProvider,
        ILogger<EncryptingFileStorage> logger,
        bool encryptNewWrites = true)
    {
        _inner = inner;
        _protectionProvider = protectionProvider;
        _logger = logger;
        _encryptNewWrites = encryptNewWrites;
    }

    /// <summary>The per-tenant purpose string. Same tenant ⇒ same derived key; different tenant ⇒ different key.</summary>
    internal static string PurposeFor(Guid tenantId) => $"file:{tenantId}";

    public async Task<string> UploadAsync(
        Guid tenantId, string relativePath, Stream content, string contentType,
        CancellationToken cancellationToken = default)
    {
        if (!_encryptNewWrites)
        {
            // Kill-switch: behave exactly as before. Reads still handle both forms, so flipping this back and
            // forth never strands a file.
            return await _inner.UploadAsync(tenantId, relativePath, content, contentType, cancellationToken);
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var protector = _protectionProvider.CreateProtector(PurposeFor(tenantId));
        var sealedBytes = FileEnvelope.Wrap(protector.Protect(buffer.ToArray()));

        await using var sealedStream = new MemoryStream(sealedBytes, writable: false);
        return await _inner.UploadAsync(tenantId, relativePath, sealedStream, contentType, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(
        Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
    {
        var stored = await _inner.OpenReadAsync(tenantId, relativePath, cancellationToken);
        if (stored is null)
            return null;

        using (stored)
        {
            using var buffer = new MemoryStream();
            await stored.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            // TOLERATE LEGACY: a file written before encryption has no envelope and is returned as-is. This is
            // what makes the rollout safe without a migration — but it is also why the remaining-plaintext
            // count matters: without a visible number, "tolerated" quietly becomes "forever".
            if (!FileEnvelope.IsEncrypted(bytes))
                return new MemoryStream(bytes, writable: false);

            var version = FileEnvelope.VersionOf(bytes);
            if (version != FileEnvelope.Version1)
            {
                // A newer envelope than this build understands. Failing loudly beats handing back a header as
                // if it were a document.
                _logger.LogError(
                    "Stored file {Path} for tenant {TenantId} uses envelope version {Version}, which this build "
                    + "cannot read.", relativePath, tenantId, version);
                throw new InvalidOperationException(
                    $"Stored file uses an unsupported encryption envelope version ({version}).");
            }

            var protector = _protectionProvider.CreateProtector(PurposeFor(tenantId));
            var plaintext = protector.Unprotect(FileEnvelope.Unwrap(bytes));
            return new MemoryStream(plaintext, writable: false);
        }
    }

    /// <summary>
    /// Pass-through — and the one seam a future storage backend MUST NOT inherit blindly.
    ///
    /// <para>Only <see cref="OpenReadAsync"/> decrypts. Today that is safe: <c>LocalFileStorage</c> returns a
    /// placeholder path and every real download in this app streams through <c>OpenReadAsync</c>, so nothing
    /// serves stored bytes directly. But a genuine pre-signed URL (S3/Blob) hands the client a link straight to
    /// the object store, bypassing this class entirely — the user would download raw ciphertext and the failure
    /// would look like a corrupt file, not a missing decrypt. Any such backend must either decrypt at the edge
    /// or stop issuing direct URLs for encrypted objects.</para>
    /// </summary>
    public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) =>
        _inner.GetSignedUrl(tenantId, relativePath, expiresIn);

    public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(tenantId, relativePath, cancellationToken);
}
