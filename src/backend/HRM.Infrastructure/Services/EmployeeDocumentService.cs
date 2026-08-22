using System.Text.Json;
using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Authorization;
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

        // BUG-114 (TC-CHR-205): enforce the tenant-wide storage quota (plan MaxStorageGb) — hard-block an upload
        // that would exceed 100%, and carry an ≥80% soft warning into the response. Runs before the expensive
        // scan/EXIF/upload work so an over-quota tenant fails fast.
        var (quotaBlock, storageWarning) = await EnforceStorageQuotaAsync(fileSize, cancellationToken);
        if (quotaBlock is not null)
            return quotaBlock;

        // BUG-058: sniff the real magic bytes BEFORE the virus scan — the AllowedMimeTypes check above only
        // trusts the client-supplied Content-Type, so a renamed .exe with an allowed MIME string would
        // otherwise be accepted. Reject (400 invalid_file_type) when the bytes don't match. Resets the stream.
        var signature = await FileSignatureValidator.ValidateStreamAsync(contentType, fileStream, cancellationToken);
        if (signature.IsFailure)
            return Result<EmployeeDocumentDto>.Failure(
                "File content does not match its type. Supported: PDF, JPEG, PNG, DOCX, XLSX.",
                400, FileSignatureValidator.ErrorCode);

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

        // FR-5: Strip EXIF/IPTC/XMP metadata (GPS, camera PII) from raster images before storage. Returns a
        // cleaned copy for JPEG/PNG; non-image types (PDF/DOCX/XLSX) pass through unchanged.
        var (uploadStream, exifReplaced) = await ImageMetadataStripper.StripAsync(
            fileStream, contentType, _logger, cancellationToken);

        // FR-3 / AC-2: Build storage path: core-hr/{employeeId}/{yyyy}/{mm}/{filename}
        var now = DateTime.UtcNow;
        var sanitizedFileName = FileNameSanitizer.Sanitize(fileName);
        var relativePath = $"core-hr/{employeeId}/{now:yyyy}/{now:MM}/{sanitizedFileName}";

        // Upload to tenant-isolated storage
        var storageKey = await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, uploadStream, contentType, cancellationToken);

        if (exifReplaced)
            await uploadStream.DisposeAsync();

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
        // ISSUE-024 (FR-7): write a queryable tenant audit row with an after-snapshot, not just an ILogger line.
        AddDocumentAudit("EmployeeDocument.Uploaded", document.Id, employeeId, before: null, after: Snapshot(document),
            $"Document '{document.FileName}' ({category}) uploaded for employee {employeeId}.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Document uploaded. DocumentId={DocumentId}, FileName={FileName}, EmployeeId={EmployeeId}, " +
            "Category={Category}, Size={Size}, TenantId={TenantId}, By={User}",
            document.Id, fileName, employeeId, category, fileSize, _tenantContext.TenantId, _currentUser.Email);

        return Result<EmployeeDocumentDto>.Success(ToDto(document) with { StorageWarning = storageWarning });
    }

    /// <summary>
    /// BUG-114 (TC-CHR-205): enforce the tenant document-storage quota against the plan's <c>MaxStorageGb</c>
    /// (resolved with override &gt; plan precedence, same as the employee-count limit). Returns a non-null
    /// <c>block</c> Result (HTTP 403 <c>storage_quota_exceeded</c>) when this upload would push cumulative usage
    /// past 100% of the limit; otherwise returns a <c>warning</c> string when projected usage is ≥80%. When the
    /// plan has no storage limit (unlimited), both are null.
    /// </summary>
    private async Task<(Result<EmployeeDocumentDto>? block, string? warning)> EnforceStorageQuotaAsync(
        long incomingBytes, CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);
        if (tenant is null)
            return (null, null);

        // BUG-307 / ISSUE-388: one shared lookup that keeps the distinction the old inline query destroyed --
        // "plan_id matches no plan" and "plan says no cap" both used to arrive here as a bare null.
        var effective = await PlanLimitLookup.ResolveAsync(
            _dbContext, tenant, PlanLimitKeys.MaxStorageGb, p => p.MaxStorageGb, DateTime.UtcNow, cancellationToken);
        
        if (effective.IsConfigurationError)
        {
            // FAIL CLOSED. An unresolvable plan_id is a misconfiguration, not an unlimited allowance.
            _logger.LogError(
                "Tenant {TenantId} has plan_id '{PlanId}', which matches no subscription plan; refusing to "
                + "treat the {LimitKey} cap as unlimited (BUG-307).",
                tenant.Id, tenant.PlanId, PlanLimitKeys.MaxStorageGb);
            return (Result<EmployeeDocumentDto>.Failure(
                "Your subscription plan could not be resolved. Please contact your administrator.", 403),
                null);
        }

        if (effective.Value is not { } limitGb)
            return (null, null); // unlimited

        var limitBytes = limitGb * 1024L * 1024L * 1024L;
        // ISSUE-340: cumulative usage across ALL four size-bearing tables (employee documents, HR-report exports,
        // payroll-report exports, payslip PDFs) via the shared TenantStorageUsage helper — not just employee
        // documents — so the quota gate and the AC-4 usage gauge read the same total. The global query filter
        // scopes every table to the current tenant.
        var currentUsage = await TenantStorageUsage.ComputeBytesAsync(_dbContext, cancellationToken);
        var projected = currentUsage + incomingBytes;

        if (projected > limitBytes)
        {
            _logger.LogWarning(
                "Document upload blocked — storage quota exceeded. TenantId={TenantId}, UsedBytes={Used}, " +
                "IncomingBytes={Incoming}, LimitBytes={Limit}.",
                _tenantContext.TenantId, currentUsage, incomingBytes, limitBytes);
            return (Result<EmployeeDocumentDto>.Failure(
                "Storage quota reached for your current plan. Please free space or upgrade your plan.",
                403, "storage_quota_exceeded"), null);
        }

        // 80% soft warning based on projected usage after this upload.
        if (projected * 100 >= limitBytes * 80)
        {
            var percent = (int)(projected * 100 / limitBytes);
            return (null, $"Storage is at {percent}% of your plan limit ({limitGb} GB).");
        }

        return (null, null);
    }

    /// <inheritdoc />
    public async Task<Result<EmployeeDocumentListResult>> ListAsync(
        Guid employeeId,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeDocumentListResult>.Failure("Tenant context is not resolved.", 400);

        // Authorization check (FR-10, BR-1, BR-2, BR-3)
        var authResult = await AuthorizeDocumentAccess(employeeId, cancellationToken);
        if (authResult.IsFailure)
            return Result<EmployeeDocumentListResult>.Failure(authResult.Error!, authResult.StatusCode ?? 403);

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
    /// <inheritdoc />
    public async Task<Result<DocumentContentResult>> DownloadAsync(
        Guid employeeId, Guid documentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DocumentContentResult>.Failure("Tenant context is not resolved.", 400);

        // Tenant-scoped by the global query filter; the employeeId predicate stops a document being read
        // through the wrong employee's route.
        var document = await _dbContext.EmployeeDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId, cancellationToken);

        if (document is null)
            return Result<DocumentContentResult>.Failure("Document not found.", 404);

        await using var stream = await _fileStorage.OpenReadAsync(
            _tenantContext.TenantId, document.StorageKey, cancellationToken);

        if (stream is null)
        {
            // The row exists but the blob does not — a real state (failed upload, manual deletion). Say so
            // rather than returning an empty file, which would look like a successful download of nothing.
            _logger.LogWarning(
                "Document {DocumentId} for employee {EmployeeId} has no stored file at {StorageKey} "
                + "(tenant {TenantId}).",
                document.Id, employeeId, document.StorageKey, _tenantContext.TenantId);
            return Result<DocumentContentResult>.Failure("The stored file is no longer available.", 404);
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        // ISSUE-024 (FR-7): the PII access-audit belongs HERE. This is the call that actually discloses the
        // bytes; the old signed-URL call only promised them, and the URL it promised never worked.
        AddDocumentAudit("EmployeeDocument.Downloaded", document.Id, employeeId, before: null, after: null,
            $"Document '{document.FileName}' downloaded for employee {employeeId}.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<DocumentContentResult>.Success(new DocumentContentResult(
            buffer.ToArray(),
            string.IsNullOrWhiteSpace(document.MimeType) ? "application/octet-stream" : document.MimeType,
            document.FileName));
    }

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

        // ISSUE-024 (FR-7): PII access-audit — a download is a read that must be audited. The document was
        // loaded AsNoTracking (untracked), so adding this new AuditLog + SaveChanges persists ONLY the audit
        // row and does not re-write the document. Written after the signed URL is successfully produced.
        AddDocumentAudit("EmployeeDocument.Downloaded", document.Id, employeeId, before: null, after: null,
            $"Signed download URL issued for document '{document.FileName}' of employee {employeeId}.");
        await _dbContext.SaveChangesAsync(cancellationToken);

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
        var beforeSnapshot = Snapshot(document);
        document.IsDeleted = true;

        AddDocumentAudit("EmployeeDocument.Deleted", document.Id, employeeId, before: beforeSnapshot, after: null,
            $"Document '{document.FileName}' of employee {employeeId} soft-deleted.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Document soft-deleted. DocumentId={DocumentId}, FileName={FileName}, " +
            "EmployeeId={EmployeeId}, TenantId={TenantId}, By={User}",
            documentId, document.FileName, employeeId, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    // ── Private helpers ──────────────────────────────────────────────

    private static readonly JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// ISSUE-024 (FR-7): appends a queryable tenant audit row (with optional before/after JSON snapshots) to
    /// the shared audit_log table. Tenant/actor are stamped from context. Mirrors <c>RoleService.AddRbacAudit</c>;
    /// added to the change tracker and persisted by the caller's SaveChanges.
    /// </summary>
    private void AddDocumentAudit(string action, Guid documentId, Guid employeeId, string? before, string? after, string detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "EmployeeDocument",
            ResourceId = documentId.ToString(),
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Serializes the audit-relevant metadata of a document to a JSON snapshot.</summary>
    private static string Snapshot(EmployeeDocument d) => JsonSerializer.Serialize(new
    {
        d.EmployeeId,
        d.FileName,
        d.StorageKey,
        d.FileSizeBytes,
        d.MimeType,
        Category = d.Category.ToString(),
        d.Description,
        d.ExpiryDate,
        d.IsDeleted,
    }, AuditJsonOptions);

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
    /// Sanitizes file names to prevent path traversal and unsafe characters.
    /// </summary>
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
