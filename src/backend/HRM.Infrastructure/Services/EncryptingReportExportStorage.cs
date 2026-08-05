using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
using Microsoft.AspNetCore.DataProtection;

namespace HRM.Infrastructure.Services;

/// <summary>
/// ISSUE-359 (report-export half): seals generated report exports at rest.
///
/// <para><b>Why this needs its own decorator.</b> Report exports do not go through
/// <see cref="IFileStorage"/> — they have a separate write-only seam and live under
/// <c>{temp}/hrm-report-exports/…</c>, outside <c>FileStorage:BasePath</c>. So the
/// <see cref="EncryptingFileStorage"/> decorator does not reach them, and ISSUE-359 explicitly names
/// report exports alongside payslips and employee documents: these files hold whole-workforce salary
/// (payroll registers, bank advice) and HR PII. Encrypting uploads while leaving a full salary register
/// in plaintext next door would close the finding on paper only.</para>
///
/// <para><b>A distinct purpose string</b> (<c>report-export:{tenantId}</c>) rather than reusing the uploads
/// purpose: two independent storage surfaces should not share a derived key, so a key compromised for one
/// cannot open the other.</para>
///
/// <para><b>No read path exists to break.</b> <see cref="IReportExportStorage"/> is write-only — the locator
/// is persisted on the export entity and nothing in the application streams the file back. That is what makes
/// encrypting here safe with no migration and no compatibility shim. It is ALSO the trap for whoever
/// implements the TODO(blob-storage) download: <b>the file on disk is now ciphertext</b>, so a signed URL
/// pointing straight at it would hand the user an unreadable blob. Any real download path must unwrap the
/// envelope and Unprotect with this same purpose first.</para>
/// </summary>
public sealed class EncryptingReportExportStorage : IReportExportStorage
{
    private readonly IReportExportStorage _inner;
    private readonly IDataProtectionProvider _protectionProvider;
    private readonly bool _encryptNewWrites;

    public EncryptingReportExportStorage(
        IReportExportStorage inner,
        IDataProtectionProvider protectionProvider,
        bool encryptNewWrites = true)
    {
        _inner = inner;
        _protectionProvider = protectionProvider;
        _encryptNewWrites = encryptNewWrites;
    }

    /// <summary>Per-tenant purpose, deliberately distinct from the uploads seam's <c>file:{tenantId}</c>.</summary>
    internal static string PurposeFor(Guid tenantId) => $"report-export:{tenantId}";

    public Task<string> SaveAsync(
        Guid tenantId,
        Guid reportId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        if (!_encryptNewWrites)
            return _inner.SaveAsync(tenantId, reportId, fileName, contentType, content, cancellationToken);

        var protector = _protectionProvider.CreateProtector(PurposeFor(tenantId));
        var sealedBytes = FileEnvelope.Wrap(protector.Protect(content));

        return _inner.SaveAsync(tenantId, reportId, fileName, contentType, sealedBytes, cancellationToken);
    }
}
