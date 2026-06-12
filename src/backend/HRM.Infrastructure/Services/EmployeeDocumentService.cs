using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Employee document management service (US-CHR-008).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class EmployeeDocumentService : IEmployeeDocumentService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _virusScanner;
    private readonly ILogger<EmployeeDocumentService> _logger;

    /// <summary>
    /// Allowed MIME types for document uploads (BR-7).
    /// </summary>
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",       // .xlsx
    };

    /// <summary>
    /// File extensions that are always rejected regardless of MIME type (BR-7).
    /// </summary>
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".sh", ".js", ".cmd", ".ps1", ".vbs", ".msi",
    };

    /// <summary>
    /// Image MIME types eligible for EXIF stripping (FR-5).
    /// </summary>
    private static readonly HashSet<string> ImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
    };

    /// <summary>
    /// Maximum file size: 10 MB (FR-2, AC-3).
    /// </summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Signed URL expiry duration: 5 minutes (FR-6, AC-4).
    /// </summary>
    private static readonly TimeSpan SignedUrlExpiry = TimeSpan.FromMinutes(5);

    public EmployeeDocumentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IVirusScanner virusScanner,
        ILogger<EmployeeDocumentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _virusScanner = virusScanner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<EmployeeDocumentDto>> UploadAsync(
        Guid employeeId,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        UploadEmployeeDocumentRequest metadata,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeDocumentDto>.Failure("Tenant context is not resolved.", 400);

        // BR-7: Reject blocked file extensions
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(extension) && BlockedExtensions.Contains(extension))
            return Result<EmployeeDocumentDto>.Failure(
                $"File type '{extension}' is not allowed. Executable files are always rejected.", 400);

        // FR-2 / AC-3: Validate MIME type
        if (!AllowedMimeTypes.Contains(contentType))
            return Result<EmployeeDocumentDto>.Failure(
                $"File type not allowed. Supported: PDF, JPEG, PNG, DOCX, XLSX.", 400);

        // FR-2 / AC-3: Validate file size
        if (fileSize > MaxFileSizeBytes)
            return Result<EmployeeDocumentDto>.Failure(
                "File exceeds the 10 MB limit.", 400);

        // Parse category
        if (!Enum.TryParse<DocumentCategory>(metadata.Category, ignoreCase: true, out var category))
            return Result<EmployeeDocumentDto>.Failure(
                "Invalid category. Allowed: Contract, ID, Certificate, Other.", 400);

        // Verify employee exists in this tenant
        var employeeExists = await _dbContext.Employees
            .AnyAsync(e => e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result<EmployeeDocumentDto>.Failure("Employee not found.", 404);

        // FR-4: Virus scan before persistence
        var scanResult = await _virusScanner.ScanAsync(fileStream, fileName, cancellationToken);
        if (!scanResult.IsClean)
        {
            _logger.LogWarning(
                "Document upload rejected by virus scanner. FileName={FileName}, Threat={Threat}, EmployeeId={EmployeeId}, TenantId={TenantId}",
                fileName, scanResult.ThreatName, employeeId, _tenantContext.TenantId);

            return Result<EmployeeDocumentDto>.Failure(
                $"File rejected by malware scanner: {scanResult.ThreatName}.", 400);
        }

        // Reset stream position after scan
        if (fileStream.CanSeek)
            fileStream.Position = 0;

        // FR-5: Strip EXIF data from images (reusing the same stub pattern as profile photo)
        if (ImageMimeTypes.Contains(contentType))
        {
            StripExifData(contentType);

            if (fileStream.CanSeek)
                fileStream.Position = 0;
        }

        // FR-3 / AC-2: Build storage path: core-hr/{employeeId}/{yyyy}/{mm}/{filename}
        var now = DateTime.UtcNow;
        var sanitizedFileName = SanitizeFileName(fileName);
        var relativePath = $"core-hr/{employeeId}/{now:yyyy}/{now:MM}/{sanitizedFileName}";

        // Upload to tenant-isolated storage
        var storageKey = await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, fileStream, contentType, cancellationToken);

        // Create metadata row
        var document = new EmployeeDocument
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employeeId,
            FileName = fileName,
            StorageKey = relativePath,
            FileSizeBytes = fileSize,
            MimeType = contentType,
            Category = category,
            Description = metadata.Description,
            ExpiryDate = metadata.ExpiryDate,
            UploadedBy = _currentUser.UserId,
            IsDeleted = false,
        };

        _dbContext.EmployeeDocuments.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Document uploaded. DocumentId={DocumentId}, FileName={FileName}, EmployeeId={EmployeeId}, " +
            "Category={Category}, Size={Size}, TenantId={TenantId}, By={User}",
            document.Id, fileName, employeeId, category, fileSize, _tenantContext.TenantId, _currentUser.Email);

        return Result<EmployeeDocumentDto>.Success(ToDto(document));
    }

    /// <inheritdoc />
    public async Task<Result<EmployeeDocumentListResult>> ListAsync(
        Guid employeeId,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeDocumentListResult>.Failure("Tenant context is not resolved.", 400);

        // Verify employee exists
        var employeeExists = await _dbContext.Employees
            .AnyAsync(e => e.Id == employeeId, cancellationToken);

        if (!employeeExists)
            return Result<EmployeeDocumentListResult>.Failure("Employee not found.", 404);

        var query = _dbContext.EmployeeDocuments
            .Where(d => d.EmployeeId == employeeId)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category) &&
            Enum.TryParse<DocumentCategory>(category, ignoreCase: true, out var cat))
        {
            query = query.Where(d => d.Category == cat);
        }

        var documents = await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Document list accessed. EmployeeId={EmployeeId}, Count={Count}, TenantId={TenantId}, By={User}",
            employeeId, documents.Count, _tenantContext.TenantId, _currentUser.Email);

        return Result<EmployeeDocumentListResult>.Success(new EmployeeDocumentListResult
        {
            Items = documents.Select(ToDto).ToList(),
            TotalCount = documents.Count,
        });
    }

    /// <inheritdoc />
    public async Task<Result<DocumentDownloadResult>> GetDownloadUrlAsync(
        Guid employeeId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DocumentDownloadResult>.Failure("Tenant context is not resolved.", 400);

        // Authorization check (FR-10, BR-1, BR-2, BR-3)
        var authResult = await AuthorizeDocumentAccess(employeeId, cancellationToken);
        if (authResult.IsFailure)
            return Result<DocumentDownloadResult>.Failure(authResult.Error!, authResult.StatusCode ?? 403);

        // Load document (global query filter ensures tenant isolation)
        var document = await _dbContext.EmployeeDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId, cancellationToken);

        if (document is null)
            return Result<DocumentDownloadResult>.Failure("Document not found.", 404);

        // FR-6 / AC-4: Generate signed URL with 5-minute expiry
        var signedUrl = _fileStorage.GetSignedUrl(
            _tenantContext.TenantId, document.StorageKey, SignedUrlExpiry);

        _logger.LogInformation(
            "Document download URL generated. DocumentId={DocumentId}, EmployeeId={EmployeeId}, " +
            "TenantId={TenantId}, By={User}",
            documentId, employeeId, _tenantContext.TenantId, _currentUser.Email);

        return Result<DocumentDownloadResult>.Success(new DocumentDownloadResult
        {
            SignedUrl = signedUrl,
            FileName = document.FileName,
            MimeType = document.MimeType,
            ExpiresAt = DateTime.UtcNow.Add(SignedUrlExpiry),
        });
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(
        Guid employeeId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        // BR-1: Only HR Officers can delete documents
        if (!_currentUser.Permissions.Contains("EmployeeDocument.Delete"))
            return Result.Failure("You do not have permission to delete documents.", 403);

        // Load document with tracking for soft-delete
        var document = await _dbContext.EmployeeDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId, cancellationToken);

        if (document is null)
            return Result.Failure("Document not found.", 404);

        // FR-7 / BR-5: Soft-delete (file remains in storage for retention)
        document.IsDeleted = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Document soft-deleted. DocumentId={DocumentId}, FileName={FileName}, " +
            "EmployeeId={EmployeeId}, TenantId={TenantId}, By={User}",
            documentId, document.FileName, employeeId, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Authorization check for document access (FR-10, BR-1, BR-2, BR-3).
    /// HR Officer/HR Manager/Tenant Admin: can access any employee's documents.
    /// Employee: can access only their own documents.
    /// Manager: blocked (BR-3).
    /// </summary>
    private async Task<Result> AuthorizeDocumentAccess(
        Guid employeeId, CancellationToken cancellationToken)
    {
        var permissions = _currentUser.Permissions;

        // HR-level access: EmployeeDocument.View or Employee.Edit grants access to any employee's docs
        if (permissions.Contains("EmployeeDocument.View") ||
            permissions.Contains("Employee.Edit"))
        {
            return Result.Success();
        }

        // Employee self-access: must match own record (BR-2)
        if (permissions.Contains("EmployeeDocument.ViewOwn") ||
            permissions.Contains("Employee.View.Own"))
        {
            // Look up the employee's linked UserId to verify ownership
            var employee = await _dbContext.Employees
                .AsNoTracking()
                .Where(e => e.Id == employeeId)
                .Select(e => new { e.UserId })
                .FirstOrDefaultAsync(cancellationToken);

            if (employee is null)
                return Result.Failure("Employee not found.", 404);

            if (employee.UserId == _currentUser.UserId)
                return Result.Success();

            // Cross-employee access attempt by Employee role -> 404 (don't leak existence)
            return Result.Failure("Document not found.", 404);
        }

        // BR-3: Managers and others are blocked
        return Result.Failure("You do not have permission to access employee documents.", 403);
    }

    /// <summary>
    /// EXIF stripping stub (FR-5). Same pattern as profile photo upload.
    /// </summary>
    private void StripExifData(string contentType)
    {
        _logger.LogWarning(
            "EXIF stripping SKIPPED (stub). ContentType={ContentType}. " +
            "TODO: Add SixLabors.ImageSharp for real EXIF stripping.",
            contentType);
    }

    /// <summary>
    /// Sanitizes file names to prevent path traversal and unsafe characters.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName); // Strip any path components
        // Replace characters not safe for storage keys
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private static EmployeeDocumentDto ToDto(EmployeeDocument d) => new()
    {
        Id = d.Id,
        EmployeeId = d.EmployeeId,
        FileName = d.FileName,
        FileSizeBytes = d.FileSizeBytes,
        MimeType = d.MimeType,
        Category = d.Category.ToString(),
        Description = d.Description,
        ExpiryDate = d.ExpiryDate,
        UploadedBy = d.UploadedBy,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };
}
