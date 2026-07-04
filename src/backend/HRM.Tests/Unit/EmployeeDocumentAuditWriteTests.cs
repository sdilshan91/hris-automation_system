// ============================================================================
// ISSUE-024 REGRESSION — Employee-document audit-write cluster.
// Document upload/delete are mutations and download is a PII read-access event;
// each MUST write a queryable `audit_logs` (AuditLog) row
// (EmployeeDocument.Uploaded / .Deleted / .Downloaded), mirroring the
// RoleService/LeaveTypeService audit pattern.
//
// Pre-fix (git show HEAD:...EmployeeDocumentService.cs) writes no AuditLog row
// (only an ILogger line) -> these tests FAIL. Post-fix the rows are present -> PASS.
//
// Download/delete seed the document directly so the ONLY audit_logs row afterward
// is the one the download/delete op writes — this proves the read-access audit is
// written by the read op specifically. Harness mirrors EmployeeDocumentServiceTests.
// AuditLog has no query filter -> TenantId asserted directly.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeDocumentAuditWriteTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IVirusScanner _virusScanner = Substitute.For<IVirusScanner>();
    private readonly ILogger<EmployeeDocumentService> _logger =
        Substitute.For<ILogger<EmployeeDocumentService>>();

    public EmployeeDocumentAuditWriteTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(_actorId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Permissions.Returns(new List<string>
        {
            "Employee.Edit", "EmployeeDocument.View", "EmployeeDocument.Upload", "EmployeeDocument.Delete",
        });
        _currentUser.Roles.Returns(new List<string> { "HR Officer" });

        _fileStorage.UploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<string>(1));
        _fileStorage.GetSignedUrl(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
            .Returns("https://storage.example.com/signed-url");

        _virusScanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Clean());
    }

    private EmployeeDocumentService CreateService() =>
        new(TestDbContextFactory.Create(_tenantContext, _dbName), _tenantContext, _currentUser,
            _fileStorage, _virusScanner, _logger);

    private async Task<Guid> SeedEmployee()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true,
        };
        var jt = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TitleName = "Software Engineer", IsActive = true,
        };
        var emp = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeNo = $"EMP-{Guid.NewGuid().ToString()[..4]}",
            FirstName = "John", LastName = "Doe", Email = "john@test.com",
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = dept.Id, JobTitleId = jt.Id,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        };
        db.Departments.Add(dept);
        db.JobTitles.Add(jt);
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    private async Task<Guid> SeedDocument(Guid employeeId)
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var doc = new EmployeeDocument
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeId = employeeId,
            FileName = "contract.pdf",
            StorageKey = $"core-hr/{employeeId}/2026/06/contract.pdf",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            Category = DocumentCategory.Contract,
            UploadedBy = _actorId,
            IsDeleted = false,
        };
        db.EmployeeDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private async Task<AuditLog?> SingleDocumentAudit(Guid documentId)
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        var rows = await db.AuditLogs.Where(a => a.TenantId == _tenantId).ToListAsync();
        return rows.SingleOrDefault(a =>
            (a.Action ?? a.EventType ?? string.Empty).Contains("Document")
            && a.ResourceId == documentId.ToString());
    }

    // ── ISSUE-024: upload → EmployeeDocument.Uploaded ───────────────────────

    [Fact]
    public async Task UploadDocument_WritesAuditRow_ISSUE024()
    {
        var empId = await SeedEmployee();
        using var stream = new MemoryStream(new byte[1024]);

        var result = await CreateService().UploadAsync(
            empId, stream, "offer_letter.pdf", "application/pdf", 1024,
            new UploadEmployeeDocumentRequest { Category = "Contract", Description = "Offer letter" });
        result.IsSuccess.Should().BeTrue();
        var documentId = result.Value!.Id;

        var audit = await SingleDocumentAudit(documentId);
        audit.Should().NotBeNull("uploading a document must write an EmployeeDocument.Uploaded audit row");
        audit!.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorId);
    }

    // ── ISSUE-024: delete → EmployeeDocument.Deleted ────────────────────────

    [Fact]
    public async Task DeleteDocument_WritesAuditRow_ISSUE024()
    {
        var empId = await SeedEmployee();
        var documentId = await SeedDocument(empId);

        var result = await CreateService().DeleteAsync(empId, documentId);
        result.IsSuccess.Should().BeTrue();

        var audit = await SingleDocumentAudit(documentId);
        audit.Should().NotBeNull("deleting a document must write an EmployeeDocument.Deleted audit row");
        audit!.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorId);
    }

    // ── ISSUE-024: download → EmployeeDocument.Downloaded (PII read-access) ──

    [Fact]
    public async Task DownloadDocument_WritesReadAccessAuditRow_ISSUE024()
    {
        var empId = await SeedEmployee();
        var documentId = await SeedDocument(empId);

        // The document is seeded directly, so any audit_logs row afterward is
        // written by the download (read) op specifically — proving read-access audit.
        var result = await CreateService().GetDownloadUrlAsync(empId, documentId);
        result.IsSuccess.Should().BeTrue();

        var audit = await SingleDocumentAudit(documentId);
        audit.Should().NotBeNull("downloading a document must write an EmployeeDocument.Downloaded read-access audit row");
        audit!.ResourceId.Should().Be(documentId.ToString());
        audit.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorId);
    }
}
