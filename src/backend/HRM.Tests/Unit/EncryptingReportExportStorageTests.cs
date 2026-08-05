// ============================================================================
// ISSUE-359 (report-export half). The finding names report exports alongside payslips and employee
// documents, and this seam does NOT go through IFileStorage — it writes to {temp}/hrm-report-exports/,
// outside FileStorage:BasePath, so the uploads decorator never touched it. A payroll register or bank-advice
// export is whole-workforce salary in one file; leaving it plaintext next to a now-encrypted uploads tree
// would have closed ISSUE-359 on paper only.
//
// As in the uploads suite, the arms assert on THE BYTES HANDED TO STORAGE — a decorator that returned
// success while passing the original content through would fix nothing and pass any round-trip check.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
using HRM.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;

namespace HRM.Tests.Unit;

public sealed class EncryptingReportExportStorageTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly IDataProtectionProvider _protection =
        DataProtectionProvider.Create(nameof(EncryptingReportExportStorageTests));

    /// <summary>Records exactly what content the inner seam was asked to persist.</summary>
    private sealed class RecordingExportStorage : IReportExportStorage
    {
        public byte[]? Saved { get; private set; }
        public string? FileName { get; private set; }
        public string? ContentType { get; private set; }

        public Task<string> SaveAsync(
            Guid tenantId, Guid reportId, string fileName, string contentType, byte[] content,
            CancellationToken cancellationToken = default)
        {
            Saved = content;
            FileName = fileName;
            ContentType = contentType;
            return Task.FromResult($"/{tenantId}/{reportId}-{fileName}");
        }
    }

    // A payroll register: the whole workforce's pay in one file.
    private static readonly byte[] Register =
        Encoding.UTF8.GetBytes("EMP001,Jayasuriya,BASIC,250000\nEMP002,Fernando,BASIC,310000");

    private (RecordingExportStorage inner, EncryptingReportExportStorage storage) Build(bool encrypt = true)
    {
        var inner = new RecordingExportStorage();
        return (inner, new EncryptingReportExportStorage(inner, _protection, encrypt));
    }

    [Fact]
    public async Task A_stored_report_export_is_NOT_the_plaintext_register_ISSUE359()
    {
        var (inner, storage) = Build();

        await storage.SaveAsync(_tenantA, Guid.NewGuid(), "payroll-register.csv", "text/csv", Register);

        Encoding.UTF8.GetString(inner.Saved!).Should().NotContain("250000",
            "a payroll register on disk must not be readable as salary");
        inner.Saved.Should().NotEqual(Register);
        FileEnvelope.IsEncrypted(inner.Saved).Should().BeTrue();
        FileEnvelope.VersionOf(inner.Saved).Should().Be(FileEnvelope.Version1);
    }

    [Fact]
    public async Task The_sealed_export_decrypts_back_to_the_original_report_ISSUE359()
    {
        // Encrypted-but-unrecoverable is data loss, not security. Nothing reads this seam today, so this arm
        // is what proves the bytes are still a report when the TODO(blob-storage) download is finally built.
        var (inner, storage) = Build();

        await storage.SaveAsync(_tenantA, Guid.NewGuid(), "payroll-register.csv", "text/csv", Register);

        var recovered = _protection
            .CreateProtector(EncryptingReportExportStorage.PurposeFor(_tenantA))
            .Unprotect(FileEnvelope.Unwrap(inner.Saved!));

        recovered.Should().Equal(Register);
    }

    [Fact]
    public async Task One_tenants_export_cannot_be_opened_with_ANOTHER_tenants_purpose_ISSUE359()
    {
        var (inner, storage) = Build();
        await storage.SaveAsync(_tenantA, Guid.NewGuid(), "payroll-register.csv", "text/csv", Register);

        var wrong = _protection.CreateProtector(EncryptingReportExportStorage.PurposeFor(_tenantB));
        var act = () => wrong.Unprotect(FileEnvelope.Unwrap(inner.Saved!));

        act.Should().Throw<Exception>("tenant B must not be able to open tenant A's payroll register");
    }

    [Fact]
    public async Task The_export_purpose_is_SEPARATE_from_the_uploads_purpose_ISSUE359()
    {
        // Two independent storage surfaces must not share a derived key: a key compromised for uploads must
        // not also open payroll registers.
        var (inner, storage) = Build();
        await storage.SaveAsync(_tenantA, Guid.NewGuid(), "payroll-register.csv", "text/csv", Register);

        var uploadsProtector = _protection.CreateProtector(EncryptingFileStorage.PurposeFor(_tenantA));
        var act = () => uploadsProtector.Unprotect(FileEnvelope.Unwrap(inner.Saved!));

        act.Should().Throw<Exception>(
            "the uploads key must not open a report export, even for the same tenant");
    }

    [Fact]
    public async Task The_kill_switch_passes_the_report_through_untouched_ISSUE359()
    {
        var (inner, storage) = Build(encrypt: false);

        await storage.SaveAsync(_tenantA, Guid.NewGuid(), "payroll-register.csv", "text/csv", Register);

        inner.Saved.Should().Equal(Register, "one switch governs both storage seams");
    }

    [Fact]
    public async Task The_file_name_and_content_type_reach_the_inner_seam_unchanged_ISSUE359()
    {
        // The locator layout is a documented contract ({reportId}-{fileName}); encryption must not rewrite it.
        var (inner, storage) = Build();

        var locator = await storage.SaveAsync(
            _tenantA, Guid.NewGuid(), "payroll-register.csv", "text/csv", Register);

        inner.FileName.Should().Be("payroll-register.csv");
        inner.ContentType.Should().Be("text/csv");
        locator.Should().EndWith("payroll-register.csv");
    }
}
