using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Self-assessment evidence-file attachments (US-PRF-002 FR-5 / ISSUE-105). Kept SEPARATE from
/// <see cref="SelfAssessmentService"/> so the ratings service constructor is untouched. Every read/write is
/// scoped to the CALLING employee's own self-assessment (resolved from ICurrentUser) AND their tenant (EF
/// global query filter), so an employee can never touch another employee's evidence (NFR-2). Attachments hang
/// off a <see cref="SelfAssessmentItem"/> (one goal); the ≤5-files-per-goal limit + the ≤10 MB size and
/// content-type caps are enforced here. The upload ordering is validate → SCAN (<see cref="IVirusScanner"/>)
/// → STORE (<see cref="IFileStorage"/>, tenant-scoped path) → PERSIST (NFR-4), mirroring
/// <c>ApplicantService</c> / <c>AssetService</c>.
/// </summary>
public sealed class SelfAssessmentAttachmentService : ISelfAssessmentAttachmentService
{
    /// <summary>FR-5: evidence uploads limited to 10 MB each.</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>FR-5: at most 5 evidence files per goal.</summary>
    private const int MaxFilesPerGoal = 5;

    /// <summary>
    /// Allowed evidence MIME types — documents + images (screenshots are common evidence). Same KIND of
    /// allow-list validation the applicant-resume path uses, widened for the evidence use case.
    /// </summary>
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _virusScanner;
    private readonly ILogger<SelfAssessmentAttachmentService> _logger;

    public SelfAssessmentAttachmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IVirusScanner virusScanner,
        ILogger<SelfAssessmentAttachmentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _virusScanner = virusScanner;
        _logger = logger;
    }

    public async Task<Result<SelfAssessmentAttachmentDto>> UploadAsync(
        UploadSelfAssessmentAttachmentInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SelfAssessmentAttachmentDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<SelfAssessmentAttachmentDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        // Defensive file validation (mirrors ApplicantService — the FluentValidation/controller gate is the
        // primary check, but the service enforces the same rules for any entry point). FR-5.
        if (input.SizeBytes <= 0 || string.IsNullOrWhiteSpace(input.FileName))
            return Result<SelfAssessmentAttachmentDto>.Failure("A file is required.", 400, "file_required");
        if (input.SizeBytes > MaxFileSizeBytes)
            return Result<SelfAssessmentAttachmentDto>.Failure("File exceeds the 10 MB limit.", 400, "file_too_large");
        if (string.IsNullOrWhiteSpace(input.ContentType) || !AllowedMimeTypes.Contains(input.ContentType))
            return Result<SelfAssessmentAttachmentDto>.Failure(
                "File type not allowed. Supported: PDF, JPEG, PNG, DOC, DOCX, XLS, XLSX.", 400, "file_type_not_allowed");

        var cycle = await _dbContext.AppraisalCycles
            .FirstOrDefaultAsync(c => c.Id == input.CycleId, cancellationToken);
        if (cycle is null)
            return Result<SelfAssessmentAttachmentDto>.Failure("Appraisal cycle not found.", 404, "cycle_not_found");

        // BR-1/AC-4: evidence can only be attached while the self-assessment window is open (same gate as
        // editing ratings).
        if (!cycle.IsSelfAssessmentOpen(DateTime.UtcNow))
            return Result<SelfAssessmentAttachmentDto>.Failure(
                "The self-assessment period for this cycle has ended.", 409, "self_assessment_closed");

        // The goal must be assigned to THIS employee for THIS cycle (tenant-scoped).
        var goalExists = await _dbContext.Goals
            .AnyAsync(g => g.Id == input.GoalId && g.EmployeeId == employee.Id && g.CycleId == input.CycleId,
                cancellationToken);
        if (!goalExists)
            return Result<SelfAssessmentAttachmentDto>.Failure(
                "That goal is not assigned to you for this cycle.", 404, "unknown_goal");

        // Load (tracking) the caller's own self-assessment + items. Create a draft + item on demand so the
        // employee can attach evidence before/while filling ratings (mirrors save-draft's lazy creation).
        var assessment = await _dbContext.SelfAssessments
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.EmployeeId == employee.Id && s.CycleId == input.CycleId, cancellationToken);

        // BR-3: a submitted (locked) self-assessment cannot accept new evidence.
        if (assessment is not null && assessment.Status == SelfAssessmentStatus.Submitted)
            return Result<SelfAssessmentAttachmentDto>.Failure(
                "This self-assessment has already been submitted and is locked.", 409, "already_submitted");

        if (assessment is null)
        {
            assessment = new SelfAssessment
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                CycleId = input.CycleId,
                EmployeeId = employee.Id,
                Status = SelfAssessmentStatus.Draft,
                IsDeleted = false,
            };
            _dbContext.SelfAssessments.Add(assessment);
        }

        var item = assessment.Items.FirstOrDefault(i => i.GoalId == input.GoalId);
        if (item is null)
        {
            item = new SelfAssessmentItem
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                SelfAssessmentId = assessment.Id,
                GoalId = input.GoalId,
                IsDeleted = false,
            };
            assessment.Items.Add(item);
            _dbContext.SelfAssessmentItems.Add(item);
        }

        // FR-5: at most 5 files per goal (count only non-deleted rows).
        var existingCount = await _dbContext.SelfAssessmentAttachments
            .CountAsync(a => a.SelfAssessmentItemId == item.Id, cancellationToken);
        if (existingCount >= MaxFilesPerGoal)
            return Result<SelfAssessmentAttachmentDto>.Failure(
                $"A maximum of {MaxFilesPerGoal} evidence files per goal is allowed.", 409, "attachment_limit_reached");

        // NFR-4: virus-scan BEFORE storing / persisting. A non-clean result rejects the upload (400).
        var scan = await _virusScanner.ScanAsync(input.Content, input.FileName, cancellationToken);
        if (!scan.IsClean)
        {
            _logger.LogWarning(
                "Self-assessment evidence rejected by virus scanner. FileName={FileName}, Threat={Threat}, " +
                "EmployeeId={EmployeeId}, GoalId={GoalId}, TenantId={TenantId}",
                input.FileName, scan.ThreatName, employee.Id, input.GoalId, _tenantContext.TenantId);
            return Result<SelfAssessmentAttachmentDto>.Failure(
                $"File rejected by malware scanner: {scan.ThreatName}.", 400, "file_infected");
        }

        if (input.Content.CanSeek)
            input.Content.Position = 0;

        // Store under the tenant-isolated path (IFileStorage prefixes {tenantId}); UUID-rename to avoid
        // collisions / path traversal. The original name is kept for display.
        var storedFileName = BuildStoredFileName(input.FileName);
        var relativePath = $"performance/self-assessments/{assessment.Id}/{input.GoalId}/{storedFileName}";
        await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, input.Content, input.ContentType!, cancellationToken);

        var attachment = new SelfAssessmentAttachment
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            SelfAssessmentItemId = item.Id,
            FileName = input.FileName,
            ContentType = input.ContentType!,
            SizeBytes = input.SizeBytes,
            StorageKey = relativePath,
            IsScanned = true, // NFR-4: scanned clean above before persistence.
            IsDeleted = false,
        };
        _dbContext.SelfAssessmentAttachments.Add(attachment);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Self-assessment evidence uploaded. AttachmentId={AttachmentId}, SelfAssessmentId={SelfAssessmentId}, " +
            "GoalId={GoalId}, EmployeeId={EmployeeId}, Size={Size}, TenantId={TenantId}",
            attachment.Id, assessment.Id, input.GoalId, employee.Id, attachment.SizeBytes, _tenantContext.TenantId);

        return Result<SelfAssessmentAttachmentDto>.Success(ToDto(attachment, input.GoalId));
    }

    public async Task<Result<IReadOnlyList<SelfAssessmentAttachmentDto>>> ListAsync(
        Guid cycleId, Guid goalId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<SelfAssessmentAttachmentDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<SelfAssessmentAttachmentDto>>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        // Resolve the caller's OWN item for (cycle, goal) — scoped by employee + tenant (global filter),
        // matched through the SelfAssessment navigation.
        var itemId = await _dbContext.SelfAssessmentItems
            .AsNoTracking()
            .Where(i => i.GoalId == goalId
                && i.SelfAssessment!.EmployeeId == employee.Id
                && i.SelfAssessment.CycleId == cycleId)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (itemId is null)
            return Result<IReadOnlyList<SelfAssessmentAttachmentDto>>.Success([]);

        var attachments = await _dbContext.SelfAssessmentAttachments
            .AsNoTracking()
            .Where(a => a.SelfAssessmentItemId == itemId.Value)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SelfAssessmentAttachmentDto>>.Success(
            attachments.Select(a => ToDto(a, goalId)).ToList());
    }

    public async Task<Result<SelfAssessmentAttachmentDownloadDto>> DownloadAsync(
        Guid attachmentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SelfAssessmentAttachmentDownloadDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<SelfAssessmentAttachmentDownloadDto>.Failure(
                "The current user is not linked to an employee record.", 403, "no_employee_record");

        // Tenant-scoped by the global filter; ownership-checked by joining the attachment → item → assessment
        // and requiring the assessment's employee to be the caller. 404 (not 403) so we never disclose that
        // another employee's attachment exists (NFR-2).
        var attachment = await _dbContext.SelfAssessmentAttachments
            .AsNoTracking()
            .Where(a => a.Id == attachmentId
                && a.SelfAssessmentItem!.SelfAssessment!.EmployeeId == employee.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (attachment is null)
            return Result<SelfAssessmentAttachmentDownloadDto>.Failure(
                "Attachment not found.", 404, "attachment_not_found");

        await using var stream = await _fileStorage.OpenReadAsync(
            _tenantContext.TenantId, attachment.StorageKey, cancellationToken);
        if (stream is null)
            return Result<SelfAssessmentAttachmentDownloadDto>.Failure(
                "The evidence file could not be found in storage.", 404, "attachment_not_found");

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return Result<SelfAssessmentAttachmentDownloadDto>.Success(new SelfAssessmentAttachmentDownloadDto
        {
            Content = buffer.ToArray(),
            FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? "attachment" : attachment.FileName,
            ContentType = string.IsNullOrWhiteSpace(attachment.ContentType)
                ? "application/octet-stream" : attachment.ContentType,
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    private static SelfAssessmentAttachmentDto ToDto(SelfAssessmentAttachment a, Guid goalId) => new()
    {
        Id = a.Id,
        GoalId = goalId,
        FileName = a.FileName,
        ContentType = a.ContentType,
        SizeBytes = a.SizeBytes,
        UploadedAt = a.CreatedAt,
    };

    /// <summary>
    /// Produces a collision-free, path-traversal-safe stored file name: a fresh UUID plus the sanitized
    /// original extension. Mirrors <c>ApplicantService.BuildStoredFileName</c>.
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
