using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-REC-005 FR-8: interview guide / evaluation-criteria attachments.
///
/// <para>Follows the <c>EmployeeDocumentService</c> upload pipeline deliberately rather than inventing a
/// lighter one: allow-list the content type, <b>then sniff the real magic bytes</b> (the declared type is
/// client-supplied and a renamed executable would otherwise pass), <b>then</b> virus-scan, and only then
/// store. Skipping any step here would make this the weakest upload path in the product.</para>
/// </summary>
public sealed class InterviewAttachmentService : IInterviewAttachmentService
{
    /// <summary>What a guide or rubric plausibly is. Deliberately narrower than the employee-document set.</summary>
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", // .docx
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",       // .xlsx
    };

    /// <summary>10 MB, matching the employee-document and onboarding-attachment ceilings.</summary>
    internal const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _virusScanner;
    private readonly ILogger<InterviewAttachmentService> _logger;

    public InterviewAttachmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IVirusScanner virusScanner,
        ILogger<InterviewAttachmentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _virusScanner = virusScanner;
        _logger = logger;
    }

    public async Task<Result<InterviewAttachmentDto>> UploadAsync(
        Guid interviewId,
        Stream content,
        string fileName,
        string contentType,
        long fileSize,
        InterviewAttachmentKind kind,
        string? description,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<InterviewAttachmentDto>.Failure("Tenant context is not resolved.", 400);

        if (fileSize <= 0)
            return Result<InterviewAttachmentDto>.Failure("The file is empty.", 400, "empty_file");

        if (fileSize > MaxUploadBytes)
            return Result<InterviewAttachmentDto>.Failure(
                "The file exceeds the 10 MB limit.", 400, "file_too_large");

        if (!AllowedMimeTypes.Contains(contentType))
            return Result<InterviewAttachmentDto>.Failure(
                "Unsupported file type. Supported: PDF, DOCX, XLSX.", 400, "invalid_file_type");

        // Tenant-scoped by the global query filter — a cross-tenant interviewId simply does not resolve.
        var interview = await _dbContext.Interviews
            .FirstOrDefaultAsync(i => i.Id == interviewId, cancellationToken);

        if (interview is null)
            return Result<InterviewAttachmentDto>.Failure("Interview not found.", 404, "interview_not_found");

        // The declared content type is the CLIENT's claim. Verify the actual bytes before trusting it —
        // otherwise a renamed executable with an allowed MIME string walks straight through.
        var signature = await FileSignatureValidator.ValidateStreamAsync(contentType, content, cancellationToken);
        if (signature.IsFailure)
            return Result<InterviewAttachmentDto>.Failure(
                "File content does not match its type. Supported: PDF, DOCX, XLSX.",
                400, FileSignatureValidator.ErrorCode);

        var scan = await _virusScanner.ScanAsync(content, fileName, cancellationToken);
        if (!scan.IsClean)
        {
            _logger.LogWarning(
                "Interview attachment rejected by virus scanner. FileName={FileName}, Threat={Threat}, " +
                "InterviewId={InterviewId}, TenantId={TenantId}",
                fileName, scan.ThreatName, interviewId, _tenantContext.TenantId);

            return Result<InterviewAttachmentDto>.Failure(
                $"File rejected by malware scanner: {scan.ThreatName}.", 400, "malware_detected");
        }

        if (content.CanSeek)
            content.Position = 0;

        // Sanitize the name for the PATH but keep the original for display — a caller-controlled file name
        // must never be able to shape where the file lands.
        var now = DateTime.UtcNow;
        var safeName = FileNameSanitizer.Sanitize(fileName);
        var relativePath = $"recruitment/interviews/{interviewId}/{now:yyyyMMddHHmmssfff}-{safeName}";

        await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, content, contentType, cancellationToken);

        var attachment = new InterviewAttachment
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            InterviewId = interviewId,
            FileName = fileName,
            StorageKey = relativePath,
            FileSizeBytes = fileSize,
            MimeType = contentType,
            Kind = kind,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            UploadedBy = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty,
        };

        _dbContext.InterviewAttachments.Add(attachment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Interview attachment stored. AttachmentId={AttachmentId}, InterviewId={InterviewId}, Kind={Kind}, " +
            "TenantId={TenantId}",
            attachment.Id, interviewId, kind, _tenantContext.TenantId);

        return Result<InterviewAttachmentDto>.Success(ToDto(attachment));
    }

    public async Task<Result<IReadOnlyList<InterviewAttachmentDto>>> ListAsync(
        Guid interviewId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<InterviewAttachmentDto>>.Failure("Tenant context is not resolved.", 400);

        var exists = await _dbContext.Interviews.AnyAsync(i => i.Id == interviewId, cancellationToken);
        if (!exists)
            return Result<IReadOnlyList<InterviewAttachmentDto>>.Failure(
                "Interview not found.", 404, "interview_not_found");

        var attachments = await _dbContext.InterviewAttachments
            .AsNoTracking()
            .Where(a => a.InterviewId == interviewId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<InterviewAttachmentDto>>.Success([.. attachments.Select(ToDto)]);
    }

    public async Task<Result<InterviewAttachmentContentDto>> DownloadAsync(
        Guid attachmentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<InterviewAttachmentContentDto>.Failure("Tenant context is not resolved.", 400);

        var attachment = await _dbContext.InterviewAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);

        if (attachment is null)
            return Result<InterviewAttachmentContentDto>.Failure(
                "Attachment not found.", 404, "attachment_not_found");

        await using var stream = await _fileStorage.OpenReadAsync(
            _tenantContext.TenantId, attachment.StorageKey, cancellationToken);

        if (stream is null)
        {
            // The metadata row outlived its blob. Surface it rather than returning an empty file that looks
            // like a corrupt document to the recruiter.
            _logger.LogWarning(
                "Interview attachment blob missing for {AttachmentId} at {StorageKey}, TenantId={TenantId}",
                attachmentId, attachment.StorageKey, _tenantContext.TenantId);

            return Result<InterviewAttachmentContentDto>.Failure(
                "The stored file could not be found.", 404, "attachment_content_missing");
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return Result<InterviewAttachmentContentDto>.Success(
            new InterviewAttachmentContentDto(buffer.ToArray(), attachment.MimeType, attachment.FileName));
    }

    public async Task<Result> DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var attachment = await _dbContext.InterviewAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);

        if (attachment is null)
            return Result.Failure("Attachment not found.", 404, "attachment_not_found");

        // Soft-delete, matching every other document surface: the row disappears from the query filter while
        // the blob remains for the retention sweep, so a mis-click is recoverable.
        attachment.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static InterviewAttachmentDto ToDto(InterviewAttachment a) => new()
    {
        Id = a.Id,
        InterviewId = a.InterviewId,
        FileName = a.FileName,
        FileSizeBytes = a.FileSizeBytes,
        MimeType = a.MimeType,
        Kind = a.Kind,
        KindName = a.Kind.ToString(),
        Description = a.Description,
        UploadedBy = a.UploadedBy,
        UploadedAt = a.CreatedAt,
    };
}
