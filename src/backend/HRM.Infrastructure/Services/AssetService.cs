using HRM.Application.Common.Helpers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ONB-004 lite asset-register service. All queries are tenant-scoped via ITenantContext + the EF global
/// query filter (NFR-2/AC-5); the tenant_id is stamped from the session, never user input (FR-7). Audit
/// columns (FR-8) are stamped by AuditInterceptor; the issuance also writes an explicit before/after audit
/// log line for the status transition. Issuance is atomic (NFR-5): the asset status update + employee
/// linkage + optional task completion happen in ONE SaveChanges.
/// </summary>
public sealed class AssetService : IAssetService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _malwareScanner;
    private readonly ILogger<AssetService> _logger;

    /// <summary>NFR-4: acknowledgment uploads limited to 10 MB.</summary>
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>NFR-4 / §7: allowed acknowledgment MIME types (PDF/JPEG/PNG).</summary>
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
    };

    /// <summary>
    /// BR-2: default per-tenant asset-type set. TODO(tenant master-data): replace with the tenant-configured
    /// list from the Tenant Admin Console master-data section once that module exists (US-ONB-004 §10). Until
    /// then every tenant validates against this seeded set.
    /// </summary>
    private static readonly HashSet<string> DefaultAssetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Laptop", "Phone", "ID Card", "Access Badge", "Vehicle",
    };

    public AssetService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IVirusScanner malwareScanner,
        ILogger<AssetService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _malwareScanner = malwareScanner;
        _logger = logger;
    }

    // ── FR-3: available assets for the issuance dropdown ────────────────

    public async Task<Result<IReadOnlyList<AvailableAssetDto>>> GetAvailableAsync(
        string? assetType, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<AvailableAssetDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.Assets
            .AsNoTracking()
            .Where(a => a.Status == AssetStatus.Available);

        if (!string.IsNullOrWhiteSpace(assetType))
        {
            var type = assetType.Trim();
            query = query.Where(a => a.AssetType == type);
        }

        var assets = await query
            .OrderBy(a => a.AssetType).ThenBy(a => a.AssetTag)
            .Select(a => new AvailableAssetDto
            {
                AssetId = a.Id,
                AssetType = a.AssetType,
                AssetTag = a.AssetTag,
                SerialNumber = a.SerialNumber,
                Brand = a.Brand,
                Model = a.Model,
                Condition = a.Condition.ToString(),
                Status = a.Status.ToString(),
            })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AvailableAssetDto>>.Success(assets);
    }

    // ── AC-4 (HR view): an employee's assigned assets ───────────────────

    public async Task<Result<IReadOnlyList<AssetDto>>> GetByEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<AssetDto>>.Failure("Tenant context is not resolved.", 400);

        var assets = await _dbContext.Assets
            .AsNoTracking()
            .Where(a => a.AssignedEmployeeId == employeeId && a.Status == AssetStatus.Assigned)
            .OrderBy(a => a.AssetType).ThenBy(a => a.AssetTag)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AssetDto>>.Success(assets.Select(ToDto).ToList());
    }

    // ── AC-4/BR-6: the logged-in employee's own assets (read-only) ──────

    public async Task<Result<IReadOnlyList<AssetDto>>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<AssetDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<AssetDto>>.Failure(
                "No employee record is linked to your account.", 404, "employee_not_found");

        return await GetByEmployeeAsync(employee.Id, cancellationToken);
    }

    // ── FR-5: bulk issuance (atomic, NFR-5) ─────────────────────────────

    public async Task<Result<IReadOnlyList<AssetDto>>> IssueAsync(
        IssueAssetsInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<AssetDto>>.Failure("Tenant context is not resolved.", 400);

        if (input.Assets.Count == 0)
            return Result<IReadOnlyList<AssetDto>>.Failure("At least one asset must be supplied.", 400, "no_assets");

        // The employee the assets are issued to must exist in this tenant.
        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == input.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<AssetDto>>.Failure("Employee not found.", 404, "employee_not_found");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Resolve the linked onboarding task up front (AC-2): it must be on THIS employee's checklist.
        OnboardingTaskInstance? linkedTask = null;
        if (input.TaskInstanceId.HasValue)
        {
            linkedTask = await _dbContext.OnboardingTaskInstances
                .Include(t => t.ChecklistInstance)
                .FirstOrDefaultAsync(t => t.Id == input.TaskInstanceId.Value && !t.IsDeleted, cancellationToken);
            if (linkedTask is null || linkedTask.ChecklistInstance is null)
                return Result<IReadOnlyList<AssetDto>>.Failure("Onboarding task not found.", 404, "task_not_found");
            if (linkedTask.ChecklistInstance.EmployeeId != employee.Id)
                return Result<IReadOnlyList<AssetDto>>.Failure(
                    "The linked onboarding task does not belong to this employee.", 409, "task_employee_mismatch");
        }

        // Detect duplicate tags within the SAME submission (the unique index would reject these, but a clear
        // up-front message is friendlier and keeps the transaction clean).
        var dupTag = input.Assets
            .GroupBy(a => a.AssetTag.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (dupTag is not null)
            return Result<IReadOnlyList<AssetDto>>.Failure(
                $"Asset tag '{dupTag.Key}' appears more than once in this submission.", 409, "duplicate_asset_tag");

        var issued = new List<Asset>(input.Assets.Count);

        foreach (var line in input.Assets)
        {
            // §7: issue date cannot be in the future (defence-in-depth; also in the validator).
            if (line.IssueDate > today)
                return Result<IReadOnlyList<AssetDto>>.Failure(
                    "Issue date cannot be in the future.", 400, "issue_date_future");

            // BR-2: asset type must be a configured type for the tenant.
            if (!DefaultAssetTypes.Contains(line.AssetType.Trim()))
                return Result<IReadOnlyList<AssetDto>>.Failure(
                    $"'{line.AssetType}' is not a configured asset type.", 400, "invalid_asset_type");

            var tag = line.AssetTag.Trim();
            var serial = string.IsNullOrWhiteSpace(line.SerialNumber) ? null : line.SerialNumber.Trim();

            Asset asset;
            if (line.AssetId.HasValue)
            {
                // Re-issue an existing register asset.
                asset = await _dbContext.Assets
                    .FirstOrDefaultAsync(a => a.Id == line.AssetId.Value, cancellationToken)
                    ?? throw new InvalidOperationException("asset_lookup_null");

                var gate = await GuardAssignableAsync(asset, cancellationToken);
                if (gate.IsFailure)
                    return Result<IReadOnlyList<AssetDto>>.Failure(gate.Error!, gate.StatusCode ?? 409, gate.ErrorCode);
            }
            else
            {
                // Ad-hoc entry — create a new register row. BR-3 uniqueness checked below.
                asset = new Asset
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId, // FR-7
                    AssetType = line.AssetType.Trim(),
                    AssetTag = tag,
                    SerialNumber = serial,
                    Brand = string.IsNullOrWhiteSpace(line.Brand) ? null : line.Brand.Trim(),
                    Model = string.IsNullOrWhiteSpace(line.Model) ? null : line.Model.Trim(),
                    Status = AssetStatus.Available,
                    IsDeleted = false,
                };
                _dbContext.Assets.Add(asset);
            }

            // BR-3: asset_tag unique per tenant; serial_number unique per tenant when provided. AC-3: if a
            // matching asset is already assigned to someone, surface the current holder by name.
            var conflict = await FindConflictAsync(asset.Id, tag, serial, cancellationToken);
            if (conflict is not null)
            {
                if (conflict.Status == AssetStatus.Assigned && conflict.AssignedEmployeeId.HasValue)
                {
                    var holder = await _dbContext.Employees
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == conflict.AssignedEmployeeId.Value, cancellationToken);
                    var holderName = holder is null ? "another employee" : $"{holder.FirstName} {holder.LastName}".Trim();
                    var ident = serial is not null ? $"Serial: {serial}" : $"Tag: {tag}";
                    return Result<IReadOnlyList<AssetDto>>.Failure(
                        $"This asset ({ident}) is currently assigned to {holderName}. " +
                        "Please return it first or select a different asset.", 409, "asset_already_assigned");
                }

                // Otherwise it's a plain uniqueness clash (BR-3) with an available/returned/disposed asset.
                var clashOn = serial is not null && conflict.SerialNumber == serial ? "serial number" : "asset tag";
                return Result<IReadOnlyList<AssetDto>>.Failure(
                    $"An asset with this {clashOn} already exists in your organization.", 409, "duplicate_asset");
            }

            // FR-4 / AC-2: assign — flip status + link employee + record condition/issue date/notes.
            var beforeStatus = asset.Status;
            asset.Condition = line.Condition;
            asset.Status = AssetStatus.Assigned;
            asset.AssignedEmployeeId = employee.Id;
            // DF-1: issue_date is a real `date` column mapped to DateOnly — assign directly (no Kind games).
            asset.IssueDate = line.IssueDate;
            asset.Notes = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim();

            // FR-8: explicit before/after audit line for the status transition (AuditInterceptor stamps columns).
            _logger.LogInformation(
                "Asset issuance: AssetId={AssetId}, Tag={Tag}, Serial={Serial}, StatusBefore={Before}, " +
                "StatusAfter={After}, Employee={EmployeeId}, TenantId={TenantId}, By={User}",
                asset.Id, asset.AssetTag, serial ?? "(none)", beforeStatus, asset.Status,
                employee.Id, _tenantContext.TenantId, _currentUser.Email);

            issued.Add(asset);
        }

        // FR-6 / NFR-4: validate + scan + store the acknowledgment doc, then link it to every issued asset.
        if (input.AcknowledgmentStream is not null)
        {
            var upload = await ValidateAndStoreAcknowledgmentAsync(employee.Id, input, cancellationToken);
            if (upload.IsFailure)
                return Result<IReadOnlyList<AssetDto>>.Failure(upload.Error!, upload.StatusCode ?? 400, upload.ErrorCode);

            foreach (var asset in issued)
            {
                asset.AcknowledgmentDocKey = upload.Value;
                asset.AcknowledgmentDocFileName = FileNameSanitizer.Sanitize(input.AcknowledgmentFileName!, "acknowledgment");
            }
        }

        // AC-2: mark the linked onboarding task completed (mirrors the US-ONB-003 completion fields) in the
        // SAME transaction (NFR-5). HR is acting here, so the employee-ownership check from CompleteTaskAsync
        // does not apply; we set the same status/timestamp/actor fields for consistency.
        if (linkedTask is not null && linkedTask.Status != OnboardingTaskStatus.Completed)
        {
            linkedTask.Status = OnboardingTaskStatus.Completed;
            linkedTask.CompletedAt = DateTime.UtcNow;
            linkedTask.CompletedByUserId = _currentUser.UserId;
        }

        // NFR-5: status updates + linkage + optional task completion persist in ONE transaction.
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Issued {Count} asset(s) to employee {EmployeeId} (task {TaskId}) in tenant {TenantId} by {User}.",
            issued.Count, employee.Id, linkedTask?.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result<IReadOnlyList<AssetDto>>.Success(issued.Select(ToDto).ToList());
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>BR-1/FR-3/AC-3: an existing register asset must be Available before re-issue; if it is already
    /// Assigned, reject with the current holder's name.</summary>
    private async Task<Result> GuardAssignableAsync(Asset asset, CancellationToken cancellationToken)
    {
        if (asset.Status == AssetStatus.Available)
            return Result.Success();

        if (asset.Status == AssetStatus.Assigned && asset.AssignedEmployeeId.HasValue)
        {
            var holder = await _dbContext.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == asset.AssignedEmployeeId.Value, cancellationToken);
            var holderName = holder is null ? "another employee" : $"{holder.FirstName} {holder.LastName}".Trim();
            var ident = asset.SerialNumber is not null ? $"Serial: {asset.SerialNumber}" : $"Tag: {asset.AssetTag}";
            return Result.Failure(
                $"This asset ({ident}) is currently assigned to {holderName}. " +
                "Please return it first or select a different asset.", 409, "asset_already_assigned");
        }

        return Result.Failure(
            $"This asset is '{asset.Status}' and cannot be issued. Only available assets can be issued.",
            409, "asset_not_available");
    }

    /// <summary>BR-3/AC-3: finds a non-deleted asset (other than <paramref name="selfId"/>) in the tenant that
    /// clashes on asset_tag or (when provided) serial_number. Returns null when no conflict.</summary>
    private async Task<Asset?> FindConflictAsync(
        Guid selfId, string tag, string? serial, CancellationToken cancellationToken)
    {
        return await _dbContext.Assets
            .AsNoTracking()
            .Where(a => a.Id != selfId
                && (a.AssetTag == tag || (serial != null && a.SerialNumber == serial)))
            .OrderByDescending(a => a.Status == AssetStatus.Assigned) // prefer surfacing an assigned holder.
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>FR-6/NFR-4: validates the acknowledgment (MIME + size), scans it, and stores it at the
    /// tenant-isolated path onboarding/assets/{employeeId}/{filename}. Returns the relative storage key.</summary>
    private async Task<Result<string>> ValidateAndStoreAcknowledgmentAsync(
        Guid employeeId, IssueAssetsInput input, CancellationToken cancellationToken)
    {
        var stream = input.AcknowledgmentStream!;

        if (string.IsNullOrWhiteSpace(input.AcknowledgmentContentType) ||
            !AllowedMimeTypes.Contains(input.AcknowledgmentContentType))
            return Result<string>.Failure("File type not allowed. Supported: PDF, JPEG, PNG.", 400, "invalid_file_type");

        var size = input.AcknowledgmentSize > 0 ? input.AcknowledgmentSize : (stream.CanSeek ? stream.Length : 0);
        if (size > MaxFileSizeBytes)
            return Result<string>.Failure("File exceeds the 10 MB limit.", 400, "file_too_large");

        // NFR-4: malware scan before persistence (the IVirusScanner seam — allow-all stub until ClamAV).
        var scan = await _malwareScanner.ScanAsync(
            stream, input.AcknowledgmentFileName ?? "acknowledgment", cancellationToken);
        if (!scan.IsClean)
        {
            _logger.LogWarning(
                "Asset acknowledgment rejected by malware scanner. Employee={EmployeeId}, Threat={Threat}, TenantId={TenantId}",
                employeeId, scan.ThreatName, _tenantContext.TenantId);
            return Result<string>.Failure($"File rejected by malware scanner: {scan.ThreatName}.", 400, "malware_detected");
        }
        if (stream.CanSeek)
            stream.Position = 0;

        // IFileStorage already prefixes the tenant id → physical {tenantId}/onboarding/assets/{employeeId}/{filename}.
        var fileName = FileNameSanitizer.Sanitize(input.AcknowledgmentFileName!, "acknowledgment");
        var relativePath = $"onboarding/assets/{employeeId}/{fileName}";
        await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, stream, input.AcknowledgmentContentType!, cancellationToken);

        return Result<string>.Success(relativePath);
    }

    private AssetDto ToDto(Asset a)
    {
        string? ackUrl = null;
        if (!string.IsNullOrEmpty(a.AcknowledgmentDocKey))
            ackUrl = _fileStorage.GetSignedUrl(_tenantContext.TenantId, a.AcknowledgmentDocKey, TimeSpan.FromMinutes(5));

        return new AssetDto
        {
            AssetId = a.Id,
            AssetType = a.AssetType,
            AssetTag = a.AssetTag,
            SerialNumber = a.SerialNumber,
            Brand = a.Brand,
            Model = a.Model,
            PurchaseDate = a.PurchaseDate,
            Condition = a.Condition.ToString(),
            Status = a.Status.ToString(),
            AssignedEmployeeId = a.AssignedEmployeeId,
            IssueDate = a.IssueDate,
            Notes = a.Notes,
            AcknowledgmentUrl = ackUrl,
        };
    }

}
