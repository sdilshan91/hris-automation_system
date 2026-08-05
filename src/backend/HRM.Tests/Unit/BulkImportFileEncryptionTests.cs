// ============================================================================
// ISSUE-359 / DF-359-bulk-import — queued bulk-import files at rest.
//
// A bulk import over 500 rows is written to {temp}/hrm-bulk-imports/{jobId}.import and left there until a
// background job picks it up. That file is the WHOLE WORKFORCE's PII in one place — names, emails, phone
// numbers, dates of birth — and it was landing as plaintext via a raw FileStream, outside both encrypted
// storage seams (IFileStorage and IReportExportStorage).
//
// The arms assert on the BYTES ON DISK, not on a round-trip: a change that returned the right rows to the
// importer while writing the original spreadsheet through unchanged would satisfy every parse-level test and
// fix nothing. The exposure is precisely what someone with filesystem access can read.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class BulkImportFileEncryptionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IDataProtectionProvider _protection =
        DataProtectionProvider.Create(nameof(BulkImportFileEncryptionTests));

    /// <summary>A distinctive value planted in the spreadsheet — if it is readable on disk, so is the PII.</summary>
    private const string PiiMarker = "nirmala.wijeratne@acme.test";

    public BulkImportFileEncryptionTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.Subdomain.Returns("test");
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAuthenticated.Returns(true);
    }

    private BulkEmployeeImportService CreateService() =>
        new(TestDbContextFactory.Create(_tenantContext, _dbName), _tenantContext, _currentUser,
            NullLogger<BulkEmployeeImportService>.Instance, _protection);

    private async Task SeedTenantAndReferenceData()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "test", Name = "Test Tenant", Status = TenantStatus.Active,
        });
        db.Departments.Add(new Department
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = "Software Engineer", IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Builds a CSV over the 500-row async threshold, so the import is QUEUED — which is the only path that
    /// writes a file to disk. The first row carries the PII marker.
    /// </summary>
    private static string LargeCsvContent(int rows = 520)
    {
        var sb = new StringBuilder();
        sb.AppendLine("first_name,last_name,email,phone,date_of_birth,gender,date_of_joining,department_name,job_title_name,employment_type,location_name,status");
        sb.AppendLine($"Nirmala,Wijeratne,{PiiMarker},,,,2026-01-15,Engineering,Software Engineer,Full-Time,,");
        for (var i = 1; i < rows; i++)
        {
            sb.AppendLine(
                $"Emp{i},Test,emp{i}@acme.test,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,");
        }

        return sb.ToString();
    }

    private static string ImportFilePath(Guid jobId) =>
        Path.Combine(Path.GetTempPath(), "hrm-bulk-imports", $"{jobId}.import");

    private async Task<Guid> QueueLargeImportAsync()
    {
        await SeedTenantAndReferenceData();
        var csv = LargeCsvContent();
        var bytes = Encoding.UTF8.GetBytes(csv);

        using var stream = new MemoryStream(bytes);
        var result = await CreateService().ImportAsync(stream, "workforce.csv", bytes.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsComplete.Should().BeFalse("a >500-row import must be queued, not processed inline");
        return result.Value.JobId!.Value;
    }

    // ── The finding itself ──────────────────────────────────────────────

    [Fact]
    public async Task The_queued_import_file_on_disk_is_NOT_the_plaintext_spreadsheet_ISSUE359()
    {
        var jobId = await QueueLargeImportAsync();
        var path = ImportFilePath(jobId);

        try
        {
            File.Exists(path).Should().BeTrue("the queued file must be on disk for the background job");
            var onDisk = await File.ReadAllBytesAsync(path);

            Encoding.UTF8.GetString(onDisk).Should().NotContain(PiiMarker,
                "an operator with filesystem access must not be able to read employee PII out of a queued import");
            FileEnvelope.IsEncrypted(onDisk).Should().BeTrue("the queued file must carry the sealed envelope");
            FileEnvelope.VersionOf(onDisk).Should().Be(FileEnvelope.Version1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task The_background_job_can_still_process_a_SEALED_file_ISSUE359()
    {
        // Encrypted-but-unprocessable would turn a security fix into a broken import feature. This is the arm
        // that proves the decrypt side is wired, end to end through the real job entry point.
        var jobId = await QueueLargeImportAsync();
        var path = ImportFilePath(jobId);

        try
        {
            await CreateService().ProcessImportJobAsync(jobId);

            using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
            var job = await db.BulkImportJobs.FirstAsync(j => j.Id == jobId);

            job.Status.Should().Be(BulkImportStatus.Completed,
                "the queued import must still complete once its file is encrypted");
            job.SuccessCount.Should().BeGreaterThan(0, "rows must actually be imported from the sealed file");

            (await db.Employees.CountAsync(e => e.TenantId == _tenantId))
                .Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task A_LEGACY_plaintext_file_queued_before_the_deploy_still_processes_ISSUE359()
    {
        // A job queued by the old build and processed by the new one would otherwise fail the moment this
        // shipped — an in-flight import lost to a deploy. Tolerance here drains on its own: these files are
        // deleted when the job finishes, unlike the uploads tree which needed a back-fill sweep.
        var jobId = await QueueLargeImportAsync();
        var path = ImportFilePath(jobId);

        try
        {
            // Overwrite the sealed file with what the OLD code would have written.
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(LargeCsvContent()));

            await CreateService().ProcessImportJobAsync(jobId);

            using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
            var job = await db.BulkImportJobs.FirstAsync(j => j.Id == jobId);

            job.Status.Should().Be(BulkImportStatus.Completed,
                "a plaintext file written before this change must still import, or deploying it loses in-flight jobs");
            job.SuccessCount.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── Key separation ──────────────────────────────────────────────────

    [Fact]
    public async Task Another_tenants_key_cannot_open_a_queued_import_file_ISSUE359()
    {
        var jobId = await QueueLargeImportAsync();
        var path = ImportFilePath(jobId);

        try
        {
            var onDisk = await File.ReadAllBytesAsync(path);
            var otherTenant = _protection.CreateProtector($"bulk-import:{Guid.NewGuid()}");

            var act = () => otherTenant.Unprotect(FileEnvelope.Unwrap(onDisk));

            act.Should().Throw<Exception>("another tenant's derived key must not open this workforce file");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task The_uploads_key_cannot_open_a_queued_import_file_ISSUE359()
    {
        // Three storage surfaces now exist (uploads, report exports, bulk imports). Each must derive its own
        // key, so a key compromised for one cannot open the others.
        var jobId = await QueueLargeImportAsync();
        var path = ImportFilePath(jobId);

        try
        {
            var onDisk = await File.ReadAllBytesAsync(path);
            var uploads = _protection.CreateProtector(EncryptingFileStorage.PurposeFor(_tenantId));

            var act = () => uploads.Unprotect(FileEnvelope.Unwrap(onDisk));

            act.Should().Throw<Exception>(
                "the uploads key must not open a bulk-import file, even for the same tenant");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
