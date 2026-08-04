// ============================================================================
// ISSUE-359: every stored file is encrypted at rest — payslip PDFs (salary),
// employee documents (PII) and offer letters all landed as plaintext on disk.
//
// The arms below deliberately assert on the BYTES THAT REACH STORAGE, not just
// on the round-trip. A decorator that returned the right plaintext to callers
// while writing the original bytes through unchanged would pass every
// round-trip test and fix nothing — the whole finding is about what an operator
// with filesystem access can read.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
using HRM.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EncryptingFileStorageTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly IDataProtectionProvider _protection =
        DataProtectionProvider.Create(nameof(EncryptingFileStorageTests));

    /// <summary>An in-memory IFileStorage that records exactly what bytes were handed to it.</summary>
    private sealed class RecordingStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Written { get; } = [];

        public async Task<string> UploadAsync(
            Guid tenantId, string relativePath, Stream content, string contentType,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Written[Key(tenantId, relativePath)] = buffer.ToArray();
            return $"/{tenantId}/{relativePath}";
        }

        public Task<Stream?> OpenReadAsync(
            Guid tenantId, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(
                Written.TryGetValue(Key(tenantId, relativePath), out var bytes)
                    ? new MemoryStream(bytes, writable: false)
                    : null);

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) =>
            $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
        {
            Written.Remove(Key(tenantId, relativePath));
            return Task.CompletedTask;
        }

        /// <summary>Seeds a LEGACY plaintext file — one written before encryption existed.</summary>
        public void SeedLegacy(Guid tenantId, string relativePath, byte[] plaintext) =>
            Written[Key(tenantId, relativePath)] = plaintext;

        private static string Key(Guid tenantId, string path) => $"{tenantId}/{path}";
    }

    private (RecordingStorage inner, EncryptingFileStorage storage) Build(bool encryptNewWrites = true)
    {
        var inner = new RecordingStorage();
        return (inner, new EncryptingFileStorage(
            inner, _protection, NullLogger<EncryptingFileStorage>.Instance, encryptNewWrites));
    }

    private static readonly byte[] Payslip =
        Encoding.UTF8.GetBytes("%PDF-1.7 BASIC SALARY 250000 NET PAY 187500");

    private static MemoryStream Content() => new(Payslip, writable: false);

    // ── The finding itself ──────────────────────────────────────────────

    [Fact]
    public async Task What_lands_on_disk_is_NOT_the_plaintext_ISSUE359()
    {
        var (inner, storage) = Build();

        await storage.UploadAsync(_tenantA, "payroll/slip.pdf", Content(), "application/pdf");

        var onDisk = inner.Written.Values.Single();
        Encoding.UTF8.GetString(onDisk).Should().NotContain("BASIC SALARY",
            "an operator with filesystem access must not be able to read salary out of the stored file");
        onDisk.Should().NotEqual(Payslip);
        FileEnvelope.IsEncrypted(onDisk).Should().BeTrue("the file must carry the envelope that marks it sealed");
        FileEnvelope.VersionOf(onDisk).Should().Be(FileEnvelope.Version1);
    }

    [Fact]
    public async Task A_stored_file_round_trips_back_to_the_original_bytes_ISSUE359()
    {
        var (_, storage) = Build();

        await storage.UploadAsync(_tenantA, "payroll/slip.pdf", Content(), "application/pdf");

        await using var read = await storage.OpenReadAsync(_tenantA, "payroll/slip.pdf");
        using var buffer = new MemoryStream();
        await read!.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal(Payslip, "callers must be completely unaware the bytes were sealed");
    }

    // ── Tolerate legacy: the whole rollout depends on this ──────────────

    [Fact]
    public async Task A_LEGACY_plaintext_file_still_reads_ISSUE359()
    {
        // Years of files predate encryption. If the read path could not handle them, shipping this would be a
        // mass outage rather than a security improvement.
        var (inner, storage) = Build();
        inner.SeedLegacy(_tenantA, "old/doc.pdf", Payslip);

        await using var read = await storage.OpenReadAsync(_tenantA, "old/doc.pdf");
        using var buffer = new MemoryStream();
        await read!.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal(Payslip);
    }

    [Fact]
    public async Task A_missing_file_is_still_null_ISSUE359()
    {
        var (_, storage) = Build();

        (await storage.OpenReadAsync(_tenantA, "nope.pdf")).Should().BeNull(
            "the not-found contract must survive the decorator");
    }

    // ── Per-tenant key separation ───────────────────────────────────────

    [Fact]
    public async Task One_tenants_file_cannot_be_decrypted_with_ANOTHER_tenants_purpose_ISSUE359()
    {
        // The purpose string is what makes the shared ring safe. If it were ignored, every tenant's files
        // would be openable with the same derived key and the platform ring would be a single blast radius.
        var (inner, storage) = Build();
        await storage.UploadAsync(_tenantA, "payroll/slip.pdf", Content(), "application/pdf");

        // Put A's sealed bytes where B's storage key points, then read through the REAL decorator. Calling
        // Unprotect with B's protector directly would only prove that Data Protection separates purposes —
        // which Microsoft already guarantees. The regression worth catching is on OUR read path: if
        // OpenReadAsync ever derived the protector from a constant, or from the wrong tenant, A's file would
        // open for B. That is the BUG-003 class, and only routing through production catches it.
        var sealedBytes = inner.Written.Values.Single();
        inner.SeedLegacy(_tenantB, "payroll/slip.pdf", sealedBytes);

        var act = async () => await storage.OpenReadAsync(_tenantB, "payroll/slip.pdf");

        await act.Should().ThrowAsync<Exception>(
            "tenant B must not be able to read tenant A's file through the production read path");
    }

    // ── Tamper detection ────────────────────────────────────────────────

    [Fact]
    public async Task A_TAMPERED_ciphertext_is_rejected_rather_than_silently_returned_ISSUE359()
    {
        var (inner, storage) = Build();
        await storage.UploadAsync(_tenantA, "payroll/slip.pdf", Content(), "application/pdf");

        // Flip a byte in the payload — an attacker with disk write access editing a stored document.
        var key = inner.Written.Keys.Single();
        var bytes = inner.Written[key];
        bytes[^1] ^= 0xFF;
        inner.Written[key] = bytes;

        var act = async () => await storage.OpenReadAsync(_tenantA, "payroll/slip.pdf");

        await act.Should().ThrowAsync<Exception>(
            "authenticated encryption must refuse modified ciphertext, not hand back corrupted bytes");
    }

    [Fact]
    public async Task An_envelope_from_a_FUTURE_version_fails_loudly_ISSUE359()
    {
        // Forward-compatibility discipline: a build that met a v2 (streaming) envelope must not treat the
        // header as document content.
        var (inner, storage) = Build();
        inner.SeedLegacy(_tenantA, "future.bin", FileEnvelope.Wrap([1, 2, 3], version: 99));

        var act = async () => await storage.OpenReadAsync(_tenantA, "future.bin");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Kill-switch ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_kill_switch_stops_NEW_encryption_but_never_breaks_reads_ISSUE359()
    {
        // Encrypt one file with the switch ON, then flip it OFF. The already-sealed file must still open —
        // a switch that disabled decryption too would turn a precaution into an outage.
        var inner = new RecordingStorage();
        var on = new EncryptingFileStorage(inner, _protection, NullLogger<EncryptingFileStorage>.Instance, true);
        await on.UploadAsync(_tenantA, "sealed.pdf", Content(), "application/pdf");

        var off = new EncryptingFileStorage(inner, _protection, NullLogger<EncryptingFileStorage>.Instance, false);
        await off.UploadAsync(_tenantA, "plain.pdf", Content(), "application/pdf");

        FileEnvelope.IsEncrypted(inner.Written[$"{_tenantA}/plain.pdf"]).Should().BeFalse(
            "the kill-switch stops new writes being encrypted");

        await using var read = await off.OpenReadAsync(_tenantA, "sealed.pdf");
        using var buffer = new MemoryStream();
        await read!.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(Payslip,
            "and must NEVER stop already-encrypted files from being read");
    }

    // ── Back-fill: the count is what stops "tolerate legacy" becoming permanent ──

    [Fact]
    public async Task The_status_report_counts_legacy_plaintext_separately_ISSUE359()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "hrm-enc-" + Guid.NewGuid().ToString("N"));
        var tenantRoot = Path.Combine(basePath, _tenantA.ToString());
        Directory.CreateDirectory(tenantRoot);

        try
        {
            // Two legacy plaintext files and one already sealed.
            await File.WriteAllBytesAsync(Path.Combine(tenantRoot, "old1.pdf"), Payslip);
            await File.WriteAllBytesAsync(Path.Combine(tenantRoot, "old2.pdf"), Payslip);
            await File.WriteAllBytesAsync(
                Path.Combine(tenantRoot, "sealed.pdf"),
                FileEnvelope.Wrap(_protection.CreateProtector(
                    EncryptingFileStorage.PurposeFor(_tenantA)).Protect(Payslip)));

            var service = BuildMaintenance(basePath, encryptionEnabled: true);

            var status = await service.GetStatusAsync();

            status.Value!.PlaintextFiles.Should().Be(2,
                "an invisible legacy count is how 'we will back-fill later' becomes 'still plaintext years on'");
            status.Value.EncryptedFiles.Should().Be(1);
            status.Value.EncryptionEnabled.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task The_sweep_encrypts_legacy_files_and_is_re_runnable_ISSUE359()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "hrm-enc-" + Guid.NewGuid().ToString("N"));
        var tenantRoot = Path.Combine(basePath, _tenantA.ToString());
        Directory.CreateDirectory(tenantRoot);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(tenantRoot, "old.pdf"), Payslip);

            var service = BuildMaintenance(basePath, encryptionEnabled: true);

            var first = await service.SweepAsync();
            first.Value!.Encrypted.Should().Be(1);

            var afterFirst = await service.GetStatusAsync();
            afterFirst.Value!.PlaintextFiles.Should().Be(0, "the sweep must actually move the number");
            afterFirst.Value.EncryptedFiles.Should().Be(1);

            // The status count only inspects the 6-byte magic header, so a sweep that wrote a valid envelope
            // over CORRUPT ciphertext would still report "encrypted" and this test would pass. This is a
            // data-integrity operation on salary and PII: the file has to still come back out.
            var recovered = await ReadAllThroughStorage(basePath, "old.pdf");
            recovered.Should().Equal(Payslip,
                "a back-filled payslip that no longer decrypts is data loss, not encryption");

            // Re-running must not re-encrypt an already-sealed file (which would double-wrap it).
            var second = await service.SweepAsync();
            second.Value!.Encrypted.Should().Be(0, "already-sealed files are skipped, so a re-run is safe");

            var afterSecond = await service.GetStatusAsync();
            afterSecond.Value!.EncryptedFiles.Should().Be(1);
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task The_sweep_refuses_to_run_while_encryption_is_switched_off_ISSUE359()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "hrm-enc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(basePath, _tenantA.ToString()));

        try
        {
            var service = BuildMaintenance(basePath, encryptionEnabled: false);

            var result = await service.SweepAsync();

            result.IsFailure.Should().BeTrue(
                "sweeping while the kill-switch is off would rewrite every file and leave it plaintext");
            result.ErrorCode.Should().Be("encryption_disabled");
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    /// <summary>
    /// Reads a file back through the decorator over the REAL LocalFileStorage — a live FileStream, not the
    /// in-memory fake. Every other round-trip arm reads from RecordingStorage, whose OpenReadAsync hands back
    /// an always-seekable, never-locked MemoryStream; that cannot surface real file semantics.
    /// </summary>
    private async Task<byte[]> ReadAllThroughStorage(string basePath, string relativePath)
    {
        IFileStorage storage = new EncryptingFileStorage(
            new LocalFileStorage(basePath, NullLogger<LocalFileStorage>.Instance),
            _protection, NullLogger<EncryptingFileStorage>.Instance);

        await using var read = await storage.OpenReadAsync(_tenantA, relativePath);
        using var buffer = new MemoryStream();
        await read!.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task An_encrypted_file_round_trips_over_the_REAL_filesystem_ISSUE359()
    {
        // The fake inner storage returns a materialised MemoryStream. Production reads a live FileStream off
        // disk, and this repo's recurring defect class is exactly "the in-memory double masked what the real
        // backing store does". So the encrypted path gets exercised end-to-end at least once, on real files.
        var basePath = Path.Combine(Path.GetTempPath(), "hrm-enc-" + Guid.NewGuid().ToString("N"));

        try
        {
            IFileStorage storage = new EncryptingFileStorage(
                new LocalFileStorage(basePath, NullLogger<LocalFileStorage>.Instance),
                _protection, NullLogger<EncryptingFileStorage>.Instance);

            // Big enough to cross buffer boundaries — the buffered design is asserted in a comment everywhere
            // else, and a ~40-byte payslip would never notice a chunking bug.
            var large = new byte[512 * 1024];
            Random.Shared.NextBytes(large);
            large[0] = (byte)'%';

            await using (var source = new MemoryStream(large, writable: false))
                await storage.UploadAsync(_tenantA, "payroll/big.pdf", source, "application/pdf");

            var onDisk = await File.ReadAllBytesAsync(
                Path.Combine(basePath, _tenantA.ToString(), "payroll", "big.pdf"));
            FileEnvelope.IsEncrypted(onDisk).Should().BeTrue("the bytes actually on disk must be sealed");
            onDisk.Should().NotEqual(large);

            (await ReadAllThroughStorage(basePath, "payroll/big.pdf")).Should().Equal(large);
        }
        finally
        {
            if (Directory.Exists(basePath))
                Directory.Delete(basePath, recursive: true);
        }
    }

    // ── System context: these endpoints exist to answer a PLATFORM-WIDE question ──

    [Fact]
    public async Task The_plaintext_count_sees_EVERY_tenant_in_system_context_ISSUE359()
    {
        // The admin console reaches this on admin.* where TenantId is Guid.Empty. Scoped to the ambient id it
        // would scan {basePath}/00000000-... , find nothing, and report a reassuring 0 plaintext files for the
        // whole platform — a counter that lies in the safe direction is worse than no counter.
        var basePath = Path.Combine(Path.GetTempPath(), "hrm-enc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(basePath, _tenantA.ToString()));
        Directory.CreateDirectory(Path.Combine(basePath, _tenantB.ToString()));

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(basePath, _tenantA.ToString(), "a1.pdf"), Payslip);
            await File.WriteAllBytesAsync(Path.Combine(basePath, _tenantA.ToString(), "a2.pdf"), Payslip);
            await File.WriteAllBytesAsync(Path.Combine(basePath, _tenantB.ToString(), "b1.pdf"), Payslip);

            var status = await BuildMaintenance(basePath, encryptionEnabled: true, systemContext: true)
                .GetStatusAsync();

            status.Value!.PlaintextFiles.Should().Be(3,
                "the platform-wide report must span every tenant, not the empty-GUID directory");
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task A_swept_file_is_sealed_with_ITS_OWN_tenants_key_ISSUE359()
    {
        // THE arm. Sealing with the ambient system id would encrypt every file under a key derived from
        // Guid.Empty: the sweep reports success, the count goes to zero, and each tenant's documents become
        // permanently unopenable. Encrypted-but-lost still counts as "encrypted" to any header-based check,
        // so only reading the bytes back as the OWNING tenant proves the sweep did the right thing.
        var basePath = Path.Combine(Path.GetTempPath(), "hrm-enc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(basePath, _tenantA.ToString()));
        Directory.CreateDirectory(Path.Combine(basePath, _tenantB.ToString()));

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(basePath, _tenantA.ToString(), "doc.pdf"), Payslip);
            await File.WriteAllBytesAsync(Path.Combine(basePath, _tenantB.ToString(), "doc.pdf"), Payslip);

            var sweep = await BuildMaintenance(basePath, encryptionEnabled: true, systemContext: true)
                .SweepAsync();
            sweep.Value!.Encrypted.Should().Be(2);

            IFileStorage storage = new EncryptingFileStorage(
                new LocalFileStorage(basePath, NullLogger<LocalFileStorage>.Instance),
                _protection, NullLogger<EncryptingFileStorage>.Instance);

            foreach (var tenantId in new[] { _tenantA, _tenantB })
            {
                // Assert on the file AT THE TENANT'S OWN PATH first. Reading back alone is not enough: if the
                // sweep sealed under the ambient (empty) id it would write to a DIFFERENT directory, leave this
                // file plaintext, and the read would still succeed — because the decorator tolerates legacy
                // plaintext. The round-trip would pass while every tenant's file stayed unencrypted.
                var onDisk = await File.ReadAllBytesAsync(
                    Path.Combine(basePath, tenantId.ToString(), "doc.pdf"));
                FileEnvelope.IsEncrypted(onDisk).Should().BeTrue(
                    "tenant {0}'s own file must be sealed in place, not left behind by a sweep that wrote "
                    + "somewhere else", tenantId);

                await using var read = await storage.OpenReadAsync(tenantId, "doc.pdf");
                using var buffer = new MemoryStream();
                await read!.CopyToAsync(buffer);
                buffer.ToArray().Should().Equal(Payslip,
                    "tenant {0} must still be able to open its own back-filled file", tenantId);
            }
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task A_directory_that_is_not_a_tenant_id_is_left_alone_ISSUE359()
    {
        // No key can be derived for it, so sealing it would make it unreadable forever. Skipping beats
        // guessing.
        var basePath = Path.Combine(Path.GetTempPath(), "hrm-enc-" + Guid.NewGuid().ToString("N"));
        var strayDir = Path.Combine(basePath, "not-a-tenant");
        Directory.CreateDirectory(strayDir);

        try
        {
            var stray = Path.Combine(strayDir, "mystery.pdf");
            await File.WriteAllBytesAsync(stray, Payslip);

            var sweep = await BuildMaintenance(basePath, encryptionEnabled: true, systemContext: true)
                .SweepAsync();

            sweep.Value!.Scanned.Should().Be(0);
            (await File.ReadAllBytesAsync(stray)).Should().Equal(Payslip, "the stray file must be untouched");
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    private FileEncryptionMaintenanceService BuildMaintenance(
        string basePath, bool encryptionEnabled, bool systemContext = false)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        // In SYSTEM context the ambient tenant id is Guid.Empty while IsResolved is still true — the exact
        // shape that made an all-tenant scan silently scope itself to a directory that does not exist.
        tenantContext.TenantId.Returns(systemContext ? Guid.Empty : _tenantA);
        tenantContext.IsSystemContext.Returns(systemContext);
        tenantContext.IsResolved.Returns(true);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:BasePath"] = basePath,
                ["FileEncryption:Enabled"] = encryptionEnabled ? "true" : "false",
            })
            .Build();

        IFileStorage storage = new EncryptingFileStorage(
            new LocalFileStorage(basePath, NullLogger<LocalFileStorage>.Instance),
            _protection, NullLogger<EncryptingFileStorage>.Instance, encryptionEnabled);

        return new FileEncryptionMaintenanceService(
            tenantContext, storage, config, NullLogger<FileEncryptionMaintenanceService>.Instance);
    }

    // ── Envelope discipline ─────────────────────────────────────────────

    [Theory]
    [InlineData("%PDF-1.7 hello")]                 // PDF
    [InlineData("PK docx-ish")]        // ZIP/DOCX/XLSX
    [InlineData("PNG\r\n")]                  // PNG
    [InlineData("")]                               // empty
    [InlineData("HRM")]                            // shorter than the header
    public void Real_document_prefixes_are_never_mistaken_for_an_envelope_ISSUE359(string leading)
    {
        FileEnvelope.IsEncrypted(Encoding.UTF8.GetBytes(leading)).Should().BeFalse(
            "a false positive here would send a legitimate document to the decryptor and fail the read");
    }
}
