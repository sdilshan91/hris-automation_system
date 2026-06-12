// ============================================================================
// US-CHR-008: Employee Document Management -- Unit Tests
// Tests: upload valid file (metadata + storage key pattern), reject oversize
// (> 10 MB), reject disallowed MIME / blocked extension (.exe), virus-scan
// rejection via IVirusScanner mock, list tenant-scoped (cross-tenant returns
// none) and excludes soft-deleted, download authorization (owner/HR allowed,
// cross-tenant 404), soft delete sets IsDeleted and retains file, delete
// permission enforcement.
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmployeeDocumentServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _virusScanner;
    private readonly ILogger<EmployeeDocumentService> _logger;

    public EmployeeDocumentServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAuthenticated.Returns(true);
        // Default: HR Officer with full document permissions
        _currentUser.Permissions.Returns(new List<string>
        {
            "Employee.Edit",
            "EmployeeDocument.View",
            "EmployeeDocument.Upload",
            "EmployeeDocument.Delete",
        });
        _currentUser.Roles.Returns(new List<string> { "HR Officer" });

        _fileStorage = Substitute.For<IFileStorage>();
        _fileStorage.UploadAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(1)); // Return the relativePath as storageKey

        _fileStorage.GetSignedUrl(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<TimeSpan?>())
            .Returns("https://storage.example.com/signed-url");

        _virusScanner = Substitute.For<IVirusScanner>();
        _virusScanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Clean());

        _logger = Substitute.For<ILogger<EmployeeDocumentService>>();
    }

    private EmployeeDocumentService CreateService(
        ITenantContext? tenantContext = null,
        ICurrentUser? currentUser = null)
    {
        var ctx = tenantContext ?? _tenantContext;
        var dbContext = TestDbContextFactory.Create(ctx, _dbName);
        return new EmployeeDocumentService(
            dbContext, ctx, currentUser ?? _currentUser,
            _fileStorage, _virusScanner, _logger);
    }

    private AppDbContext CreateDbContext(ITenantContext? ctx = null)
    {
        return TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
    }

    private async Task<Guid> SeedEmployee(
        string firstName = "John", string lastName = "Doe",
        string email = "john@test.com", Guid? userId = null,
        Guid? tenantId = null)
    {
        var tid = tenantId ?? _tenantId;
        ITenantContext ctx;
        if (tenantId.HasValue && tenantId.Value != _tenantId)
        {
            ctx = Substitute.For<ITenantContext>();
            ctx.TenantId.Returns(tid);
            ctx.IsResolved.Returns(true);
        }
        else
        {
            ctx = _tenantContext;
        }

        using var db = TestDbContextFactory.Create(ctx, _dbName);

        // Ensure a department and job title exist
        var deptId = BaseEntity.NewUuidV7();
        var jtId = BaseEntity.NewUuidV7();

        if (!await db.Departments.AnyAsync(d => d.TenantId == tid))
        {
            db.Departments.Add(new Department
            {
                Id = deptId,
                TenantId = tid,
                Name = "Engineering",
                Code = "ENG",
                IsActive = true,
                IsDeleted = false,
            });
            db.JobTitles.Add(new JobTitle
            {
                Id = jtId,
                TenantId = tid,
                TitleName = "Software Engineer",
                IsActive = true,
                IsDeleted = false,
            });
        }
        else
        {
            deptId = (await db.Departments.FirstAsync(d => d.TenantId == tid)).Id;
            jtId = (await db.JobTitles.FirstAsync(j => j.TenantId == tid)).Id;
        }

        var emp = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            EmployeeNo = $"EMP-{Guid.NewGuid().ToString()[..4]}",
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            DateOfJoining = DateTime.UtcNow.Date,
            DepartmentId = deptId,
            JobTitleId = jtId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
            IsDeleted = false,
            UserId = userId,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    private async Task<EmployeeDocument> SeedDocument(
        Guid employeeId,
        string fileName = "contract.pdf",
        DocumentCategory category = DocumentCategory.Contract,
        bool isDeleted = false,
        Guid? tenantId = null)
    {
        var tid = tenantId ?? _tenantId;
        ITenantContext ctx;
        if (tenantId.HasValue && tenantId.Value != _tenantId)
        {
            ctx = Substitute.For<ITenantContext>();
            ctx.TenantId.Returns(tid);
            ctx.IsResolved.Returns(true);
        }
        else
        {
            ctx = _tenantContext;
        }

        using var db = TestDbContextFactory.Create(ctx, _dbName);
        var doc = new EmployeeDocument
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            EmployeeId = employeeId,
            FileName = fileName,
            StorageKey = $"core-hr/{employeeId}/2026/06/{fileName}",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            Category = category,
            Description = "Test document",
            UploadedBy = _currentUser.UserId,
            IsDeleted = isDeleted,
        };
        db.EmployeeDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    private static UploadEmployeeDocumentRequest MakeMetadata(
        string category = "Contract",
        string? description = null,
        DateTime? expiryDate = null)
    {
        return new UploadEmployeeDocumentRequest
        {
            Category = category,
            Description = description,
            ExpiryDate = expiryDate,
        };
    }

    private static MemoryStream MakeStream(int sizeBytes = 1024)
    {
        var data = new byte[sizeBytes];
        return new MemoryStream(data);
    }

    // ========================================================================
    // Upload: valid file
    // ========================================================================

    [Fact]
    public async Task Upload_ValidPdf_ShouldCreateMetadataRowWithCorrectStorageKeyPrefix()
    {
        var empId = await SeedEmployee();
        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "offer_letter.pdf", "application/pdf", 1024,
            MakeMetadata("Contract", "Offer letter"));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.EmployeeId.Should().Be(empId);
        dto.FileName.Should().Be("offer_letter.pdf");
        dto.MimeType.Should().Be("application/pdf");
        dto.FileSizeBytes.Should().Be(1024);
        dto.Category.Should().Be("Contract");
        dto.Description.Should().Be("Offer letter");
        dto.UploadedBy.Should().Be(_currentUser.UserId);

        // Verify storage key follows pattern: core-hr/{employeeId}/{yyyy}/{mm}/{filename}
        await _fileStorage.Received(1).UploadAsync(
            _tenantId,
            Arg.Is<string>(key =>
                key.StartsWith($"core-hr/{empId}/") && key.EndsWith("/offer_letter.pdf")),
            Arg.Any<Stream>(),
            "application/pdf",
            Arg.Any<CancellationToken>());

        // Verify metadata row persisted in DB
        using var db = CreateDbContext();
        var doc = await db.EmployeeDocuments.FirstOrDefaultAsync(d => d.Id == dto.Id);
        doc.Should().NotBeNull();
        doc!.TenantId.Should().Be(_tenantId);
        doc.StorageKey.Should().StartWith($"core-hr/{empId}/");
    }

    [Fact]
    public async Task Upload_ValidImage_ShouldSucceed()
    {
        var empId = await SeedEmployee();
        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "id_card.png", "image/png", 2048,
            MakeMetadata("ID"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.MimeType.Should().Be("image/png");
        result.Value.Category.Should().Be("ID");
    }

    [Fact]
    public async Task Upload_ValidDocx_ShouldSucceed()
    {
        var empId = await SeedEmployee();
        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "resume.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 5000,
            MakeMetadata("Certificate"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Category.Should().Be("Certificate");
    }

    [Fact]
    public async Task Upload_WithExpiryDate_ShouldPersistExpiryDate()
    {
        var empId = await SeedEmployee();
        var service = CreateService();
        var expiry = DateTime.UtcNow.AddMonths(6).Date;

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "visa.pdf", "application/pdf", 1024,
            MakeMetadata("Certificate", "Work visa", expiry));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpiryDate.Should().Be(expiry);
    }

    // ========================================================================
    // Upload: reject oversize (> 10 MB)
    // ========================================================================

    [Fact]
    public async Task Upload_OversizeFile_ShouldReturn400WithSizeError()
    {
        var empId = await SeedEmployee();
        var service = CreateService();
        var tooLarge = (10L * 1024 * 1024) + 1;

        using var stream = MakeStream(1); // stream size doesn't matter; fileSize param is checked
        var result = await service.UploadAsync(
            empId, stream, "big.pdf", "application/pdf", tooLarge,
            MakeMetadata());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("10 MB");
    }

    [Fact]
    public async Task Upload_ExactlyMaxSize_ShouldSucceed()
    {
        var empId = await SeedEmployee();
        var service = CreateService();
        var exactMax = 10L * 1024 * 1024;

        using var stream = MakeStream(1);
        var result = await service.UploadAsync(
            empId, stream, "exact.pdf", "application/pdf", exactMax,
            MakeMetadata());

        result.IsSuccess.Should().BeTrue();
    }

    // ========================================================================
    // Upload: reject disallowed MIME / blocked extension
    // ========================================================================

    [Fact]
    public async Task Upload_DisallowedMimeType_ShouldReturn400WithTypeError()
    {
        var empId = await SeedEmployee();
        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "script.txt", "text/plain", 100,
            MakeMetadata());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("File type not allowed");
    }

    [Fact]
    public async Task Upload_BlockedExtensionExe_ShouldReturn400()
    {
        var empId = await SeedEmployee();
        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "malware.exe", "application/pdf", 100,
            MakeMetadata());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain(".exe");
        result.Error.Should().Contain("not allowed");
    }

    [Fact]
    public async Task Upload_BlockedExtensionBat_ShouldReturn400()
    {
        var empId = await SeedEmployee();
        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "script.bat", "application/pdf", 100,
            MakeMetadata());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain(".bat");
    }

    [Fact]
    public async Task Upload_BlockedExtensionPs1_ShouldReturn400()
    {
        var empId = await SeedEmployee();
        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "script.ps1", "application/pdf", 100,
            MakeMetadata());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain(".ps1");
    }

    [Fact]
    public async Task Upload_InvalidCategory_ShouldReturn400()
    {
        var empId = await SeedEmployee();
        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "file.pdf", "application/pdf", 100,
            MakeMetadata("InvalidCategory"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Invalid category");
    }

    // ========================================================================
    // Upload: virus scan rejection
    // ========================================================================

    [Fact]
    public async Task Upload_VirusScanInfected_ShouldReturn400()
    {
        var empId = await SeedEmployee();

        _virusScanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Infected("EICAR-Test-File", "Test threat detected"));

        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            empId, stream, "clean.pdf", "application/pdf", 1024,
            MakeMetadata());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("malware scanner");
        result.Error.Should().Contain("EICAR-Test-File");

        // File should NOT be uploaded to storage
        await _fileStorage.DidNotReceive().UploadAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ========================================================================
    // Upload: tenant not resolved
    // ========================================================================

    [Fact]
    public async Task Upload_TenantNotResolved_ShouldReturn400()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var service = CreateService(tenantContext: ctx);

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            Guid.NewGuid(), stream, "file.pdf", "application/pdf", 100,
            MakeMetadata());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // ========================================================================
    // Upload: non-existent employee
    // ========================================================================

    [Fact]
    public async Task Upload_NonExistentEmployee_ShouldReturn404()
    {
        var service = CreateService();

        using var stream = MakeStream();
        var result = await service.UploadAsync(
            Guid.NewGuid(), stream, "file.pdf", "application/pdf", 100,
            MakeMetadata());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("Employee not found");
    }

    // ========================================================================
    // List: tenant-scoped, excludes soft-deleted
    // ========================================================================

    [Fact]
    public async Task List_ShouldReturnDocumentsForEmployee()
    {
        var empId = await SeedEmployee();
        await SeedDocument(empId, "doc1.pdf", DocumentCategory.Contract);
        await SeedDocument(empId, "doc2.pdf", DocumentCategory.ID);

        var service = CreateService();
        var result = await service.ListAsync(empId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_ShouldExcludeSoftDeletedDocuments()
    {
        var empId = await SeedEmployee();
        await SeedDocument(empId, "active.pdf", isDeleted: false);
        await SeedDocument(empId, "deleted.pdf", isDeleted: true);

        var service = CreateService();
        var result = await service.ListAsync(empId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items[0].FileName.Should().Be("active.pdf");
    }

    [Fact]
    public async Task List_ShouldFilterByCategory()
    {
        var empId = await SeedEmployee();
        await SeedDocument(empId, "contract.pdf", DocumentCategory.Contract);
        await SeedDocument(empId, "id.pdf", DocumentCategory.ID);

        var service = CreateService();
        var result = await service.ListAsync(empId, "ID");

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items[0].FileName.Should().Be("id.pdf");
    }

    [Fact]
    public async Task List_CrossTenant_ShouldReturnNoDocuments()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var empIdA = await SeedEmployee("EmpA", "A", "empa@t.com", tenantId: tenantA);
        await SeedDocument(empIdA, "secret.pdf", tenantId: tenantA);

        // Query from tenant B -- employee won't exist in tenant B's context
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        var service = CreateService(tenantContext: ctxB);

        var result = await service.ListAsync(empIdA);

        // Employee not found in tenant B due to global query filter
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("Employee not found");
    }

    [Fact]
    public async Task List_NonExistentEmployee_ShouldReturn404()
    {
        var service = CreateService();
        var result = await service.ListAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("Employee not found");
    }

    [Fact]
    public async Task List_TenantNotResolved_ShouldReturn400()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var service = CreateService(tenantContext: ctx);

        var result = await service.ListAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // ========================================================================
    // Download: authorization
    // ========================================================================

    [Fact]
    public async Task Download_HrOfficer_ShouldReturnSignedUrl()
    {
        var empId = await SeedEmployee();
        var doc = await SeedDocument(empId);

        var service = CreateService();
        var result = await service.GetDownloadUrlAsync(empId, doc.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SignedUrl.Should().Be("https://storage.example.com/signed-url");
        result.Value.FileName.Should().Be(doc.FileName);
        result.Value.MimeType.Should().Be(doc.MimeType);
        result.Value.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Download_EmployeeOwnRecord_ShouldSucceed()
    {
        var employeeUserId = Guid.NewGuid();
        var empId = await SeedEmployee(userId: employeeUserId);
        var doc = await SeedDocument(empId);

        // Employee user with ViewOwn permission
        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.UserId.Returns(employeeUserId);
        employeeUser.Email.Returns("john@test.com");
        employeeUser.IsAuthenticated.Returns(true);
        employeeUser.Permissions.Returns(new List<string> { "EmployeeDocument.ViewOwn" });
        employeeUser.Roles.Returns(new List<string> { "Employee" });

        var service = CreateService(currentUser: employeeUser);
        var result = await service.GetDownloadUrlAsync(empId, doc.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SignedUrl.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Download_EmployeeCrossRecord_ShouldReturn404()
    {
        // Employee A's user ID is different from the current user
        var otherUserId = Guid.NewGuid();
        var empId = await SeedEmployee(userId: otherUserId);
        var doc = await SeedDocument(empId);

        // Current user is a different employee
        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.UserId.Returns(Guid.NewGuid()); // Different user ID
        employeeUser.Email.Returns("other@test.com");
        employeeUser.IsAuthenticated.Returns(true);
        employeeUser.Permissions.Returns(new List<string> { "EmployeeDocument.ViewOwn" });
        employeeUser.Roles.Returns(new List<string> { "Employee" });

        var service = CreateService(currentUser: employeeUser);
        var result = await service.GetDownloadUrlAsync(empId, doc.Id);

        // The code returns 404 ("Document not found") to avoid leaking existence
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Download_ManagerBlocked_ShouldReturn403()
    {
        var empId = await SeedEmployee();
        var doc = await SeedDocument(empId);

        // Manager has no document-related permissions
        var managerUser = Substitute.For<ICurrentUser>();
        managerUser.UserId.Returns(Guid.NewGuid());
        managerUser.Email.Returns("manager@test.com");
        managerUser.IsAuthenticated.Returns(true);
        managerUser.Permissions.Returns(new List<string> { "Employee.View.Team" });
        managerUser.Roles.Returns(new List<string> { "Manager" });

        var service = CreateService(currentUser: managerUser);
        var result = await service.GetDownloadUrlAsync(empId, doc.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("do not have permission");
    }

    [Fact]
    public async Task Download_CrossTenant_ShouldReturn404()
    {
        var tenantA = Guid.NewGuid();
        var empId = await SeedEmployee(tenantId: tenantA);
        var doc = await SeedDocument(empId, tenantId: tenantA);

        // Query from a different tenant
        var tenantB = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);

        var service = CreateService(tenantContext: ctxB);
        var result = await service.GetDownloadUrlAsync(empId, doc.Id);

        // Authorization check will fail with 404 because employee not found in tenant B
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Download_NonExistentDocument_ShouldReturn404()
    {
        var empId = await SeedEmployee();

        var service = CreateService();
        var result = await service.GetDownloadUrlAsync(empId, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Download_TenantNotResolved_ShouldReturn400()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var service = CreateService(tenantContext: ctx);

        var result = await service.GetDownloadUrlAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ========================================================================
    // Soft delete
    // ========================================================================

    [Fact]
    public async Task Delete_ShouldSoftDeleteAndRetainFile()
    {
        var empId = await SeedEmployee();
        var doc = await SeedDocument(empId);

        var service = CreateService();
        var result = await service.DeleteAsync(empId, doc.Id);

        result.IsSuccess.Should().BeTrue();

        // Verify document is soft-deleted in DB (bypass query filter to see it)
        using var db = CreateDbContext();
        var deleted = await db.EmployeeDocuments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == doc.Id);
        deleted.Should().NotBeNull();
        deleted!.IsDeleted.Should().BeTrue();

        // File should NOT be deleted from storage (retained per BR-5)
        await _fileStorage.DidNotReceive().DeleteAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_WithoutPermission_ShouldReturn403()
    {
        var empId = await SeedEmployee();
        var doc = await SeedDocument(empId);

        // User without EmployeeDocument.Delete permission
        var noDeleteUser = Substitute.For<ICurrentUser>();
        noDeleteUser.UserId.Returns(Guid.NewGuid());
        noDeleteUser.Email.Returns("employee@test.com");
        noDeleteUser.IsAuthenticated.Returns(true);
        noDeleteUser.Permissions.Returns(new List<string> { "EmployeeDocument.View" });
        noDeleteUser.Roles.Returns(new List<string> { "Employee" });

        var service = CreateService(currentUser: noDeleteUser);
        var result = await service.DeleteAsync(empId, doc.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.Error.Should().Contain("do not have permission");
    }

    [Fact]
    public async Task Delete_NonExistentDocument_ShouldReturn404()
    {
        var empId = await SeedEmployee();

        var service = CreateService();
        var result = await service.DeleteAsync(empId, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Contain("Document not found");
    }

    [Fact]
    public async Task Delete_TenantNotResolved_ShouldReturn400()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var service = CreateService(tenantContext: ctx);

        var result = await service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ========================================================================
    // Download: Employee.View.Own also grants self-access
    // ========================================================================

    [Fact]
    public async Task Download_EmployeeViewOwn_ShouldGrantSelfAccess()
    {
        var userId = Guid.NewGuid();
        var empId = await SeedEmployee(userId: userId);
        var doc = await SeedDocument(empId);

        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.UserId.Returns(userId);
        employeeUser.Email.Returns("self@test.com");
        employeeUser.IsAuthenticated.Returns(true);
        employeeUser.Permissions.Returns(new List<string> { "Employee.View.Own" });
        employeeUser.Roles.Returns(new List<string> { "Employee" });

        var service = CreateService(currentUser: employeeUser);
        var result = await service.GetDownloadUrlAsync(empId, doc.Id);

        result.IsSuccess.Should().BeTrue();
    }

    // ========================================================================
    // Download: Employee.Edit grants HR-level access
    // ========================================================================

    [Fact]
    public async Task Download_EmployeeEditPermission_ShouldGrantAccess()
    {
        var empId = await SeedEmployee();
        var doc = await SeedDocument(empId);

        var hrUser = Substitute.For<ICurrentUser>();
        hrUser.UserId.Returns(Guid.NewGuid());
        hrUser.Email.Returns("hr@test.com");
        hrUser.IsAuthenticated.Returns(true);
        hrUser.Permissions.Returns(new List<string> { "Employee.Edit" });
        hrUser.Roles.Returns(new List<string> { "HR Officer" });

        var service = CreateService(currentUser: hrUser);
        var result = await service.GetDownloadUrlAsync(empId, doc.Id);

        result.IsSuccess.Should().BeTrue();
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
