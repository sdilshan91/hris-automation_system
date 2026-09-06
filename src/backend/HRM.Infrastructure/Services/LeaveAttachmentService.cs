using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Real leave supporting-document uploads (US-LV-003 FR-5 / NFR-3 / ISSUE-036).
///
/// <para>ISSUE-036: leave attachments used to be plain strings on the create request that were only
/// extension-checked, so <c>"x.pdf"</c> satisfied a medical-certificate requirement and no bytes ever
/// existed. This service is the missing upload path — it produces a persisted, scanned
/// <see cref="LeaveRequestAttachment"/> whose id the create request then references.</para>
///
/// <para>Ports the order-of-operations from <see cref="SelfAssessmentAttachmentService"/>: tenant check →
/// size cap → declared-MIME allow-list → extension allow-list → magic-byte sniff → virus scan → store →
/// persist. Two deliberate differences from that service: the cap is <b>5 MB</b> (NFR-3 for leave, not the
/// 10 MB the evidence path uses) and the allow-list is <b>PDF/JPEG/PNG only</b> (§10). The extension check is
/// kept alongside the MIME check because the pre-ISSUE-036 leave validator checked extensions — dropping it
/// would silently change the KIND of validation applied; <see cref="EmployeeDocumentService"/> does both too.</para>
/// </summary>
public sealed class LeaveAttachmentService : ILeaveAttachmentService
{
    /// <summary>NFR-3: leave attachments are limited to 5 MB each (NOT the 10 MB of the evidence path).</summary>
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    /// <summary>§10: leave supporting documents are PDF/JPG/PNG only — deliberately narrower than the evidence list.</summary>
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
    };

    /// <summary>The same three types by extension — retained from the pre-ISSUE-036 validator (§10).</summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png",
    };

    private const string TypeRejectMessage = "File type not allowed. Supported: PDF, JPG, PNG.";

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _virusScanner;
    private readonly ILogger<LeaveAttachmentService> _logger;

    public LeaveAttachmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IVirusScanner virusScanner,
        ILogger<LeaveAttachmentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _virusScanner = virusScanner;
        _logger = logger;
    }

    public async Task<Result<LeaveAttachmentDto>> UploadAsync(
        UploadLeaveAttachmentInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveAttachmentDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<LeaveAttachmentDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        if (input.SizeBytes <= 0 || string.IsNullOrWhiteSpace(input.FileName))
            return Result<LeaveAttachmentDto>.Failure("A file is required.", 400, "file_required");

        // NFR-3: 5 MB cap.
        if (input.SizeBytes > MaxFileSizeBytes)
            return Result<LeaveAttachmentDto>.Failure("File exceeds the 5 MB limit.", 400, "file_too_large");

        // §10: declared content type must be one of the three allowed leave document types.
        if (string.IsNullOrWhiteSpace(input.ContentType) || !AllowedMimeTypes.Contains(input.ContentType))
            return Result<LeaveAttachmentDto>.Failure(TypeRejectMessage, 400, "file_type_not_allowed");

        // §10: and the file name's extension, preserving the kind of check the old string validator did.
        var extension = Path.GetExtension(Path.GetFileName(input.FileName));
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            return Result<LeaveAttachmentDto>.Failure(TypeRejectMessage, 400, "file_type_not_allowed");

        // BUG-058: sniff the REAL magic bytes before the scan — the checks above only see client-supplied
        // strings, so a renamed executable declaring application/pdf would otherwise pass. Resets the stream.
        var signature = await FileSignatureValidator.ValidateStreamAsync(
            input.ContentType, input.Content, cancellationToken);
        if (signature.IsFailure)
            return Result<LeaveAttachmentDto>.Failure(
                TypeRejectMessage, 400, FileSignatureValidator.ErrorCode);

        // NFR-3: virus-scan BEFORE storing / persisting.
        var scan = await _virusScanner.ScanAsync(input.Content, input.FileName, cancellationToken);
        if (!scan.IsClean)
        {
            _logger.LogWarning(
                "Leave attachment rejected by virus scanner. FileName={FileName}, Threat={Threat}, " +
                "EmployeeId={EmployeeId}, TenantId={TenantId}",
                input.FileName, scan.ThreatName, employee.Id, _tenantContext.TenantId);
            return Result<LeaveAttachmentDto>.Failure(
                $"File rejected by malware scanner: {scan.ThreatName}.", 400, "file_infected");
        }

        if (input.Content.CanSeek)
            input.Content.Position = 0;

        // IFileStorage prefixes the {tenantId} segment itself, so the relative path must NOT repeat it.
        // UUID-rename to avoid collisions / path traversal; the original name is kept for display.
        var storedFileName = BuildStoredFileName(input.FileName);
        var relativePath = $"leaves/{employee.Id}/{storedFileName}";
        await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, input.Content, input.ContentType!, cancellationToken);

        var attachment = new LeaveRequestAttachment
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            LeaveRequestId = null, // linked when the leave request is created (ISSUE-036).
            FileName = input.FileName,
            ContentType = input.ContentType!,
            SizeBytes = input.SizeBytes,
            StorageKey = relativePath,
            IsScanned = true,
            UploadedByEmployeeId = employee.Id,
            IsDeleted = false,
        };
        _dbContext.LeaveRequestAttachments.Add(attachment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Leave attachment uploaded. AttachmentId={AttachmentId}, EmployeeId={EmployeeId}, " +
            "Size={Size}, TenantId={TenantId}",
            attachment.Id, employee.Id, attachment.SizeBytes, _tenantContext.TenantId);

        return Result<LeaveAttachmentDto>.Success(new LeaveAttachmentDto
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            UploadedAt = attachment.CreatedAt,
        });
    }

    /// <summary>
    /// Collision-free, path-traversal-safe stored file name: a fresh UUID plus the sanitized original
    /// extension. Mirrors <c>SelfAssessmentAttachmentService.BuildStoredFileName</c>.
    /// </summary>
    private static string BuildStoredFileName(string originalFileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(originalFileName));
        var safeExt = string.Empty;
        if (!string.IsNullOrEmpty(extension))
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                extension = extension.Replace(c, '_');
            safeExt = "." + new string(extension.TrimStart('.').Where(char.IsLetterOrDigit).ToArray());
            if (safeExt == ".") safeExt = string.Empty;
        }

        return $"{Guid.NewGuid():N}{safeExt}";
    }
}
