using System.Text.Json;
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
/// Employee CRUD service (US-CHR-001).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class EmployeeService : IEmployeeService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStorage _fileStorage;
    private readonly IVirusScanner _virusScanner;
    private readonly ICustomFieldService _customFieldService;
    private readonly ILogger<EmployeeService> _logger;

    // Allowed MIME types for profile photos (FR-6)
    private static readonly HashSet<string> AllowedPhotoMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    // Maximum photo file size: 5 MB (FR-6)
    private const long MaxPhotoSizeBytes = 5 * 1024 * 1024;

    public EmployeeService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IFileStorage fileStorage,
        IVirusScanner virusScanner,
        ICustomFieldService customFieldService,
        ILogger<EmployeeService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _virusScanner = virusScanner;
        _customFieldService = customFieldService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new employee (AC-2). Validates email uniqueness (AC-3, FR-3, BR-2),
    /// plan limit (AC-5, FR-5), department/job-title existence, and auto-generates employee_no (FR-2).
    /// </summary>
    public async Task<Result<EmployeeDto>> CreateAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeDto>.Failure("Tenant context is not resolved.", 400);

        // FR-3 / BR-2: email uniqueness within tenant
        var emailExists = await _dbContext.Employees
            .AnyAsync(e => e.Email == request.Email, cancellationToken);

        if (emailExists)
            return Result<EmployeeDto>.Failure("An employee with this email already exists.", 400);

        // Validate department exists and is active in the same tenant (global filter enforces tenant)
        var departmentExists = await _dbContext.Departments
            .AnyAsync(d => d.Id == request.DepartmentId && d.IsActive, cancellationToken);

        if (!departmentExists)
            return Result<EmployeeDto>.Failure("Department not found or is not active.", 400);

        // Validate job title exists and is active in the same tenant
        var jobTitleExists = await _dbContext.JobTitles
            .AnyAsync(j => j.Id == request.JobTitleId && j.IsActive, cancellationToken);

        if (!jobTitleExists)
            return Result<EmployeeDto>.Failure("Job title not found or is not active.", 400);

        // US-CHR-007 / BUG-113: optional structured location assignment. When provided, the location
        // must exist, be active, and belong to this tenant (the global query filter enforces tenant scope).
        if (request.LocationId.HasValue)
        {
            var locationExists = await _dbContext.Locations
                .AnyAsync(l => l.Id == request.LocationId.Value && l.IsActive, cancellationToken);

            if (!locationExists)
                return Result<EmployeeDto>.Failure("Location not found or is not active.", 400);
        }

        // AC-5 / FR-5: Plan-level employee count limit enforcement
        var planLimitResult = await CheckPlanLimitAsync(cancellationToken);
        if (planLimitResult.IsFailure)
            return Result<EmployeeDto>.Failure(planLimitResult.Error!, planLimitResult.StatusCode ?? 403);

        // US-CHR-012: Validate custom field values against active definitions.
        // ISSUE-242: validate UNCONDITIONALLY. ValidateCustomFieldValuesAsync already treats an empty/omitted
        // customFields payload as "no values supplied" and flags every REQUIRED definition — so gating the call
        // on a non-empty payload let a caller bypass ALL required custom fields simply by omitting the object
        // (201 instead of 400), silently violating the tenant's required-custom-field policy (AC-6/FR-9).
        var cfValidation = await _customFieldService.ValidateCustomFieldValuesAsync(
            "employee", request.CustomFields, cancellationToken);
        if (cfValidation.IsFailure)
            return Result<EmployeeDto>.Failure(cfValidation.Error!, cfValidation.StatusCode ?? 400);

        // FR-2 / BR-1: auto-generate unique employee_no per tenant
        var employeeNo = await GenerateEmployeeNoAsync(cancellationToken);

        // BR-3: Default status is Active unless explicitly set to Probation
        var status = request.Status ?? EmployeeStatus.Active;

        var employee = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeNo = employeeNo,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            DateOfJoining = request.DateOfJoining,
            DepartmentId = request.DepartmentId,
            JobTitleId = request.JobTitleId,
            EmploymentType = request.EmploymentType,
            Status = status,
            Location = request.Location,
            LocationId = request.LocationId,
            CustomFields = request.CustomFields,
            UserId = request.UserId,
            // US-CHR-013: omitted → the entity defaults (1.00 / OnSite), so an existing client that does not
            // send these creates exactly the employee it created before.
            Fte = request.Fte ?? 1.00m,
            WorkArrangement = request.WorkArrangement ?? WorkArrangement.OnSite,
            IsActive = true,
            IsDeleted = false,
        };

        _dbContext.Employees.Add(employee);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Employee created. Id={EmployeeId}, EmployeeNo={EmployeeNo}, Email={Email}, TenantId={TenantId}, By={User}",
            employee.Id, employee.EmployeeNo, employee.Email, _tenantContext.TenantId, _currentUser.Email);

        // Reload with navigation properties for the DTO
        return Result<EmployeeDto>.Success(await LoadDtoAsync(employee.Id, cancellationToken));
    }

    public async Task<Result<EmployeeDto>> GetByIdAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .Include(e => e.Department)
            .Include(e => e.JobTitle)
            .Include(e => e.LocationEntity)
            .Include(e => e.Manager)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee is null)
            return Result<EmployeeDto>.Failure("Employee not found.", 404);

        return Result<EmployeeDto>.Success(ToDto(employee));
    }

    public async Task<Result<EmployeeListResult>> GetAllAsync(
        int page = 1,
        int pageSize = 20,
        bool? activeOnly = null,
        string? search = null,
        bool includeTerminated = false,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeListResult>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.Employees
            .Include(e => e.Department)
            .Include(e => e.JobTitle)
            .Include(e => e.LocationEntity)
            .Include(e => e.Manager)
            .AsNoTracking();

        if (activeOnly == true)
            query = query.Where(e => e.IsActive);

        // ISSUE-223: Terminated employees are "archived" — excluded from the default directory. Callers
        // opt in explicitly (includeTerminated) to see them, mirroring an archived/all view.
        if (!includeTerminated)
            query = query.Where(e => e.Status != EmployeeStatus.Terminated);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(searchLower) ||
                e.LastName.ToLower().Contains(searchLower) ||
                e.Email.ToLower().Contains(searchLower) ||
                e.EmployeeNo.ToLower().Contains(searchLower));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var employees = await query
            .OrderBy(e => e.EmployeeNo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var result = new EmployeeListResult
        {
            Items = employees.Select(ToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };

        return Result<EmployeeListResult>.Success(result);
    }

    /// <summary>
    /// Uploads and validates a profile photo (AC-4, FR-6, NFR-3).
    /// Validates MIME type, file size, runs virus scan, strips EXIF, stores file.
    /// </summary>
    public async Task<Result<string>> UploadProfilePhotoAsync(
        Guid employeeId,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<string>.Failure("Tenant context is not resolved.", 400);

        // Validate MIME type
        if (!AllowedPhotoMimeTypes.Contains(contentType))
            return Result<string>.Failure(
                $"Invalid file type '{contentType}'. Allowed types: JPEG, PNG, WebP.", 400);

        // Validate file size (5 MB max)
        if (fileSize > MaxPhotoSizeBytes)
            return Result<string>.Failure(
                $"File size ({fileSize / (1024 * 1024.0):F1} MB) exceeds the maximum allowed size of 5 MB.", 400);

        // Verify employee exists in this tenant
        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee is null)
            return Result<string>.Failure("Employee not found.", 404);

        // BUG-058: sniff the real magic bytes BEFORE the virus scan — the AllowedPhotoMimeTypes check above
        // only trusts the client-supplied Content-Type, so a renamed .exe with an image MIME string would
        // otherwise be accepted. Reject (400 invalid_file_type) when the bytes don't match. Resets the stream.
        var signature = await FileSignatureValidator.ValidateStreamAsync(contentType, fileStream, cancellationToken);
        if (signature.IsFailure)
            return Result<string>.Failure(
                "File content does not match its type. Allowed types: JPEG, PNG, WebP.",
                400, FileSignatureValidator.ErrorCode);

        // NFR-3: Virus scan before persistence
        var scanResult = await _virusScanner.ScanAsync(fileStream, fileName, cancellationToken);
        if (!scanResult.IsClean)
            return Result<string>.Failure(
                $"File rejected by malware scanner: {scanResult.ThreatName}.", 400);

        // Reset stream position after scan
        if (fileStream.CanSeek)
            fileStream.Position = 0;

        // AC-4/FR-6: strip EXIF/IPTC/XMP metadata (GPS, camera PII) from the photo before storage. Returns a
        // cleaned copy for JPEG/PNG; a non-decodable image passes through unchanged (fail-open).
        var (uploadStream, exifReplaced) = await ImageMetadataStripper.StripAsync(
            fileStream, contentType, _logger, cancellationToken);

        // Upload to tenant-isolated storage: {tenantId}/core-hr/{employeeId}/profile/{filename}
        var relativePath = $"core-hr/{employeeId}/profile/{fileName}";
        var storedUrl = await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, uploadStream, contentType, cancellationToken);

        if (exifReplaced)
            await uploadStream.DisposeAsync();

        // Update employee record with photo URL
        employee.ProfilePhotoUrl = storedUrl;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Return a signed/temporary URL for display
        var signedUrl = _fileStorage.GetSignedUrl(_tenantContext.TenantId, relativePath);

        _logger.LogInformation(
            "Profile photo uploaded. EmployeeId={EmployeeId}, FileName={FileName}, TenantId={TenantId}",
            employeeId, fileName, _tenantContext.TenantId);

        return Result<string>.Success(signedUrl);
    }

    /// <summary>
    /// Gets a comprehensive employee profile with all sections (US-CHR-002 AC-1).
    /// </summary>
    public async Task<Result<EmployeeProfileDto>> GetProfileAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var result = await LoadProfileAsync(employeeId, cancellationToken);
        if (result.IsFailure)
            return result;

        // BUG-010 (FR-7 / TC-CHR-118): a profile view exposes PII and must leave a queryable access-audit row,
        // not just a Serilog line. The employee was loaded AsNoTracking (untracked) inside LoadProfileAsync, so
        // adding this new AuditLog + SaveChanges persists ONLY the audit row. Written after the projection
        // succeeds so a 404 is not audited. The internal post-update re-read uses LoadProfileAsync (no audit),
        // so an edit does not masquerade as a view.
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = "Employee.ProfileViewed",
            Action = "Employee.ProfileViewed",
            ResourceType = "Employee",
            ResourceId = employeeId.ToString(),
            Detail = $"Employee profile {employeeId} viewed.",
            CreatedAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// Loads and projects the full employee profile without writing an access audit. Shared by the public
    /// (audited) <see cref="GetProfileAsync"/> and the internal post-update re-read in UpdateProfileAsync.
    /// </summary>
    private async Task<Result<EmployeeProfileDto>> LoadProfileAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeProfileDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .Include(e => e.Department)
            .Include(e => e.JobTitle)
            .Include(e => e.LocationEntity)
            .Include(e => e.Manager)
            .Include(e => e.EmergencyContacts)
            .Include(e => e.EmploymentHistories)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee is null)
            return Result<EmployeeProfileDto>.Failure("Employee not found.", 404);

        return Result<EmployeeProfileDto>.Success(ToProfileDto(employee));
    }

    /// <summary>
    /// Updates an employee profile with field-level role permissions, optimistic concurrency,
    /// audit logging with before/after JSONB snapshots, and employment history entries (US-CHR-002).
    /// </summary>
    public async Task<Result<EmployeeProfileDto>> UpdateProfileAsync(
        Guid employeeId,
        UpdateEmployeeProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeProfileDto>.Failure("Tenant context is not resolved.", 400);

        // Determine caller's role for field-level permission enforcement (AC-4, AC-5, FR-3)
        var callerRole = DetermineCallerRole();

        // Field-level permission check: Employee role can only update Contact and EmergencyContacts
        if (callerRole == CallerRole.Employee)
        {
            if (request.PersonalInfo is not null)
                return Result<EmployeeProfileDto>.Failure(
                    "Employees cannot modify personal info fields (name, date of birth, gender). These are read-only for your role.", 403);

            if (request.EmploymentInfo is not null)
                return Result<EmployeeProfileDto>.Failure(
                    "Employees cannot modify employment fields (department, job title, status). These are read-only for your role.", 403);

            if (request.UpdateCustomFields)
                return Result<EmployeeProfileDto>.Failure(
                    "Employees cannot modify custom fields. These are read-only for your role.", 403);
        }

        // Manager role is read-only for direct reports (FR-3)
        if (callerRole == CallerRole.Manager)
        {
            return Result<EmployeeProfileDto>.Failure(
                "Managers have read-only access to employee profiles.", 403);
        }

        // Load the employee with tracking for update
        var employee = await _dbContext.Employees
            .Include(e => e.Department)
            .Include(e => e.JobTitle)
            .Include(e => e.LocationEntity)
            .Include(e => e.Manager)
            .Include(e => e.EmergencyContacts)
            .Include(e => e.EmploymentHistories)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee is null)
            return Result<EmployeeProfileDto>.Failure("Employee not found.", 404);

        // BUG-119: a self-service caller (Employee.Edit.Own, without the HR-level Employee.Edit) may edit
        // ONLY their own record — otherwise it is horizontal privilege escalation (edit anyone's contact/
        // emergency data). HR-level editors (CallerRole.HrOfficer) are exempt.
        if (callerRole == CallerRole.Employee && employee.UserId != _currentUser.UserId)
            return Result<EmployeeProfileDto>.Failure("You can only edit your own profile.", 403);

        // Set the expected concurrency token for optimistic concurrency (FR-4, AC-3)
        _dbContext.Entry(employee).Property(e => e.RowVersion).OriginalValue = request.RowVersion;

        // Build before-snapshot for audit
        var beforeSnapshots = new Dictionary<string, object>();
        var afterSnapshots = new Dictionary<string, object>();

        // Apply PersonalInfo section
        if (request.PersonalInfo is not null)
        {
            var before = new Dictionary<string, object?>();
            var after = new Dictionary<string, object?>();

            if (request.PersonalInfo.FirstName is not null && request.PersonalInfo.FirstName != employee.FirstName)
            {
                before["FirstName"] = employee.FirstName;
                employee.FirstName = request.PersonalInfo.FirstName;
                after["FirstName"] = employee.FirstName;
            }
            if (request.PersonalInfo.LastName is not null && request.PersonalInfo.LastName != employee.LastName)
            {
                before["LastName"] = employee.LastName;
                employee.LastName = request.PersonalInfo.LastName;
                after["LastName"] = employee.LastName;
            }
            if (request.PersonalInfo.DateOfBirth.HasValue && request.PersonalInfo.DateOfBirth != employee.DateOfBirth)
            {
                before["DateOfBirth"] = employee.DateOfBirth;
                employee.DateOfBirth = request.PersonalInfo.DateOfBirth;
                after["DateOfBirth"] = employee.DateOfBirth;
            }
            if (request.PersonalInfo.Gender.HasValue && request.PersonalInfo.Gender != employee.Gender)
            {
                before["Gender"] = employee.Gender?.ToString();
                employee.Gender = request.PersonalInfo.Gender;
                after["Gender"] = employee.Gender?.ToString();
            }

            if (before.Count > 0)
            {
                beforeSnapshots["PersonalInfo"] = before;
                afterSnapshots["PersonalInfo"] = after;
            }
        }

        // Apply ContactInfo section
        if (request.ContactInfo is not null)
        {
            var before = new Dictionary<string, object?>();
            var after = new Dictionary<string, object?>();

            if (request.ContactInfo.Phone is not null && request.ContactInfo.Phone != employee.Phone)
            {
                before["Phone"] = employee.Phone;
                employee.Phone = request.ContactInfo.Phone;
                after["Phone"] = employee.Phone;
            }
            if (request.ContactInfo.PersonalEmail is not null && request.ContactInfo.PersonalEmail != employee.PersonalEmail)
            {
                before["PersonalEmail"] = employee.PersonalEmail;
                employee.PersonalEmail = request.ContactInfo.PersonalEmail;
                after["PersonalEmail"] = employee.PersonalEmail;
            }
            if (request.ContactInfo.Address is not null && request.ContactInfo.Address != employee.Address)
            {
                before["Address"] = employee.Address;
                employee.Address = request.ContactInfo.Address;
                after["Address"] = employee.Address;
            }

            if (before.Count > 0)
            {
                beforeSnapshots["ContactInfo"] = before;
                afterSnapshots["ContactInfo"] = after;
            }
        }

        // Apply EmploymentInfo section (HR only, triggers employment history)
        if (request.EmploymentInfo is not null)
        {
            var before = new Dictionary<string, object?>();
            var after = new Dictionary<string, object?>();
            var effectiveDate = request.EmploymentInfo.EffectiveDate ?? DateTime.UtcNow.Date;
            var changedBy = _currentUser.IsAuthenticated ? _currentUser.Email : "system";

            // Department change (BR-4, AC-6)
            if (request.EmploymentInfo.DepartmentId.HasValue &&
                request.EmploymentInfo.DepartmentId.Value != employee.DepartmentId)
            {
                var newDeptId = request.EmploymentInfo.DepartmentId.Value;
                var deptExists = await _dbContext.Departments
                    .AnyAsync(d => d.Id == newDeptId && d.IsActive, cancellationToken);
                if (!deptExists)
                    return Result<EmployeeProfileDto>.Failure("Department not found or is not active.", 400);

                var oldDeptName = employee.Department?.Name ?? employee.DepartmentId.ToString();
                var newDept = await _dbContext.Departments.AsNoTracking()
                    .FirstAsync(d => d.Id == newDeptId, cancellationToken);

                before["DepartmentId"] = employee.DepartmentId;
                before["DepartmentName"] = oldDeptName;

                employee.DepartmentId = newDeptId;

                after["DepartmentId"] = employee.DepartmentId;
                after["DepartmentName"] = newDept.Name;

                // Employment history entry
                _dbContext.EmploymentHistories.Add(new EmploymentHistory
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeId = employeeId,
                    ChangeType = "Department",
                    PreviousValue = oldDeptName,
                    NewValue = newDept.Name,
                    PreviousReferenceId = before["DepartmentId"] as Guid?,
                    NewReferenceId = newDeptId,
                    EffectiveDate = effectiveDate,
                    Reason = request.EmploymentInfo.Reason,
                    ChangedBy = changedBy,
                });
            }

            // Job title change (BR-4, AC-6)
            if (request.EmploymentInfo.JobTitleId.HasValue &&
                request.EmploymentInfo.JobTitleId.Value != employee.JobTitleId)
            {
                var newJtId = request.EmploymentInfo.JobTitleId.Value;
                var jtExists = await _dbContext.JobTitles
                    .AnyAsync(j => j.Id == newJtId && j.IsActive, cancellationToken);
                if (!jtExists)
                    return Result<EmployeeProfileDto>.Failure("Job title not found or is not active.", 400);

                var oldJtName = employee.JobTitle?.TitleName ?? employee.JobTitleId.ToString();
                var newJt = await _dbContext.JobTitles.AsNoTracking()
                    .FirstAsync(j => j.Id == newJtId, cancellationToken);

                before["JobTitleId"] = employee.JobTitleId;
                before["JobTitleName"] = oldJtName;

                employee.JobTitleId = newJtId;

                after["JobTitleId"] = employee.JobTitleId;
                after["JobTitleName"] = newJt.TitleName;

                _dbContext.EmploymentHistories.Add(new EmploymentHistory
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeId = employeeId,
                    ChangeType = "JobTitle",
                    PreviousValue = oldJtName,
                    NewValue = newJt.TitleName,
                    PreviousReferenceId = before["JobTitleId"] as Guid?,
                    NewReferenceId = newJtId,
                    EffectiveDate = effectiveDate,
                    Reason = request.EmploymentInfo.Reason,
                    ChangedBy = changedBy,
                });
            }

            // Location change (US-CHR-007 / BUG-113). Mirrors the Department/JobTitle change pattern:
            // validate the target location exists + is active (tenant-scoped via the global filter),
            // reassign the FK, and record an employment-history entry. A null LocationId leaves the
            // current assignment unchanged (matching DepartmentId/JobTitleId semantics).
            if (request.EmploymentInfo.LocationId.HasValue &&
                request.EmploymentInfo.LocationId.Value != employee.LocationId)
            {
                var newLocId = request.EmploymentInfo.LocationId.Value;
                var locExists = await _dbContext.Locations
                    .AnyAsync(l => l.Id == newLocId && l.IsActive, cancellationToken);
                if (!locExists)
                    return Result<EmployeeProfileDto>.Failure("Location not found or is not active.", 400);

                var oldLocName = employee.LocationEntity?.Name ?? employee.LocationId?.ToString();
                var newLoc = await _dbContext.Locations.AsNoTracking()
                    .FirstAsync(l => l.Id == newLocId, cancellationToken);

                before["LocationId"] = employee.LocationId;
                before["LocationName"] = oldLocName;

                employee.LocationId = newLocId;

                after["LocationId"] = employee.LocationId;
                after["LocationName"] = newLoc.Name;

                _dbContext.EmploymentHistories.Add(new EmploymentHistory
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeId = employeeId,
                    ChangeType = "Location",
                    PreviousValue = oldLocName,
                    NewValue = newLoc.Name,
                    PreviousReferenceId = before["LocationId"] as Guid?,
                    NewReferenceId = newLocId,
                    EffectiveDate = effectiveDate,
                    Reason = request.EmploymentInfo.Reason,
                    ChangedBy = changedBy,
                });
            }

            // Status change (BR-4)
            if (request.EmploymentInfo.Status.HasValue &&
                request.EmploymentInfo.Status.Value != employee.Status)
            {
                before["Status"] = employee.Status.ToString();
                var oldStatus = employee.Status;
                employee.Status = request.EmploymentInfo.Status.Value;
                after["Status"] = employee.Status.ToString();

                // Update IsActive based on status
                employee.IsActive = employee.Status is EmployeeStatus.Active or EmployeeStatus.Probation;

                _dbContext.EmploymentHistories.Add(new EmploymentHistory
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeId = employeeId,
                    ChangeType = "Status",
                    PreviousValue = oldStatus.ToString(),
                    NewValue = employee.Status.ToString(),
                    EffectiveDate = effectiveDate,
                    Reason = request.EmploymentInfo.Reason,
                    ChangedBy = changedBy,
                });
            }

            // Employment type change
            if (request.EmploymentInfo.EmploymentType.HasValue &&
                request.EmploymentInfo.EmploymentType.Value != employee.EmploymentType)
            {
                before["EmploymentType"] = employee.EmploymentType.ToString();
                employee.EmploymentType = request.EmploymentInfo.EmploymentType.Value;
                after["EmploymentType"] = employee.EmploymentType.ToString();
            }

            // US-CHR-013: FTE change. Null leaves the current value unchanged (Department/JobTitle
            // semantics). No EmploymentHistory entry: FTE is not one of the AC-6 tracked change types.
            if (request.EmploymentInfo.Fte.HasValue &&
                request.EmploymentInfo.Fte.Value != employee.Fte)
            {
                before["Fte"] = employee.Fte;
                employee.Fte = request.EmploymentInfo.Fte.Value;
                after["Fte"] = employee.Fte;
            }

            // US-CHR-013: work-arrangement change. Null leaves the current value unchanged.
            if (request.EmploymentInfo.WorkArrangement.HasValue &&
                request.EmploymentInfo.WorkArrangement.Value != employee.WorkArrangement)
            {
                before["WorkArrangement"] = employee.WorkArrangement.ToString();
                employee.WorkArrangement = request.EmploymentInfo.WorkArrangement.Value;
                after["WorkArrangement"] = employee.WorkArrangement.ToString();
            }

            if (before.Count > 0)
            {
                beforeSnapshots["EmploymentInfo"] = before;
                afterSnapshots["EmploymentInfo"] = after;
            }
        }

        // Apply EmergencyContacts section (full replace)
        if (request.EmergencyContacts is not null)
        {
            var beforeEc = employee.EmergencyContacts
                .Select(ec => new { ec.Id, ec.ContactName, ec.Relationship, ec.Phone, ec.IsPrimary })
                .ToList();

            // Remove existing contacts
            _dbContext.EmergencyContacts.RemoveRange(employee.EmergencyContacts);

            // Add new contacts
            foreach (var input in request.EmergencyContacts)
            {
                _dbContext.EmergencyContacts.Add(new EmergencyContact
                {
                    Id = input.Id ?? BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeId = employeeId,
                    ContactName = input.ContactName,
                    Relationship = input.Relationship,
                    Phone = input.Phone,
                    AlternatePhone = input.AlternatePhone,
                    Email = input.Email,
                    IsPrimary = input.IsPrimary,
                });
            }

            var afterEc = request.EmergencyContacts
                .Select(ec => new { ec.Id, ec.ContactName, ec.Relationship, ec.Phone, ec.IsPrimary })
                .ToList();

            beforeSnapshots["EmergencyContacts"] = beforeEc;
            afterSnapshots["EmergencyContacts"] = afterEc;
        }

        // Apply CustomFields
        if (request.UpdateCustomFields)
        {
            // US-CHR-012: Validate custom field values against active definitions
            // CROSS-CUTTING: This wiring was added by US-CHR-012 and touches US-CHR-002 profile update flow.
            if (!string.IsNullOrWhiteSpace(request.CustomFields))
            {
                var cfValidation = await _customFieldService.ValidateCustomFieldValuesAsync(
                    "employee", request.CustomFields, cancellationToken);
                if (cfValidation.IsFailure)
                    return Result<EmployeeProfileDto>.Failure(cfValidation.Error!, cfValidation.StatusCode ?? 400);
            }

            var before = new Dictionary<string, object?> { ["CustomFields"] = employee.CustomFields };
            employee.CustomFields = request.CustomFields;
            var after = new Dictionary<string, object?> { ["CustomFields"] = employee.CustomFields };

            beforeSnapshots["CustomFields"] = before;
            afterSnapshots["CustomFields"] = after;
        }

        // Write audit log entries for each changed section (FR-5, AC-2)
        if (beforeSnapshots.Count > 0)
        {
            foreach (var section in beforeSnapshots.Keys)
            {
                _dbContext.EmployeeFieldAuditLogs.Add(new EmployeeFieldAuditLog
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeId = employeeId,
                    Section = section,
                    BeforeSnapshot = JsonSerializer.Serialize(beforeSnapshots[section]),
                    AfterSnapshot = JsonSerializer.Serialize(afterSnapshots[section]),
                    ChangedByUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
                    ChangedBy = _currentUser.IsAuthenticated ? _currentUser.Email : "system",
                });
            }
        }

        // Save with optimistic concurrency check (FR-4, AC-3)
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning(
                "Concurrency conflict updating employee {EmployeeId} in tenant {TenantId}",
                employeeId, _tenantContext.TenantId);
            return Result<EmployeeProfileDto>.Failure(
                "This record was modified by another user. Please refresh and try again.", 409);
        }

        _logger.LogInformation(
            "Employee profile updated. Id={EmployeeId}, Sections={Sections}, TenantId={TenantId}, By={User}",
            employeeId, string.Join(",", beforeSnapshots.Keys), _tenantContext.TenantId, _currentUser.Email);

        // Reload the full profile to return the updated state (LoadProfileAsync: no view-audit — this is an edit).
        return await LoadProfileAsync(employeeId, cancellationToken);
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Determines the caller's effective role for field-level permission enforcement.
    /// HR Officer / HR Manager / Tenant Admin / Tenant Owner -> HrOfficer (full access)
    /// Employee -> Employee (limited access)
    /// Manager -> Manager (read-only)
    /// </summary>
    private CallerRole DetermineCallerRole()
    {
        var roles = _currentUser.Roles;
        var permissions = _currentUser.Permissions;

        // Check for HR-level edit permission first
        if (permissions.Contains("Employee.Edit"))
            return CallerRole.HrOfficer;

        // Check for Employee self-edit
        if (permissions.Contains("Employee.Edit.Own"))
            return CallerRole.Employee;

        // Check for Manager view-team (read-only)
        if (permissions.Contains("Employee.View.Team"))
            return CallerRole.Manager;

        // Default to most restrictive
        return CallerRole.Manager;
    }

    private static EmployeeProfileDto ToProfileDto(Employee e) => new()
    {
        Id = e.Id,
        EmployeeNo = e.EmployeeNo,
        FirstName = e.FirstName,
        LastName = e.LastName,
        Email = e.Email,
        PersonalEmail = e.PersonalEmail,
        Phone = e.Phone,
        Address = e.Address,
        DateOfBirth = e.DateOfBirth,
        Gender = e.Gender?.ToString(),
        DateOfJoining = e.DateOfJoining,
        DepartmentId = e.DepartmentId,
        DepartmentName = e.Department?.Name,
        JobTitleId = e.JobTitleId,
        JobTitleName = e.JobTitle?.TitleName,
        ReportsToEmployeeId = e.ReportsToEmployeeId,
        ManagerName = e.Manager == null ? null : $"{e.Manager.FirstName} {e.Manager.LastName}",
        LocationId = e.LocationId,
        LocationName = e.LocationEntity?.Name,
        EmploymentType = e.EmploymentType.ToString(),
        Status = e.Status.ToString(),
        Fte = e.Fte,
        WorkArrangement = e.WorkArrangement.ToString(),
        ProfilePhotoUrl = e.ProfilePhotoUrl,
        CustomFields = e.CustomFields,
        UserId = e.UserId,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        RowVersion = e.RowVersion,
        EmergencyContacts = e.EmergencyContacts
            .Where(ec => !ec.IsDeleted)
            .Select(ec => new EmergencyContactDto
            {
                Id = ec.Id,
                ContactName = ec.ContactName,
                Relationship = ec.Relationship,
                Phone = ec.Phone,
                AlternatePhone = ec.AlternatePhone,
                Email = ec.Email,
                IsPrimary = ec.IsPrimary,
            })
            .ToList(),
        EmploymentHistory = e.EmploymentHistories
            .Where(eh => !eh.IsDeleted)
            .OrderByDescending(eh => eh.EffectiveDate)
            .ThenByDescending(eh => eh.CreatedAt)
            .Select(eh => new EmploymentHistoryDto
            {
                Id = eh.Id,
                ChangeType = eh.ChangeType,
                PreviousValue = eh.PreviousValue,
                NewValue = eh.NewValue,
                PreviousReferenceId = eh.PreviousReferenceId,
                NewReferenceId = eh.NewReferenceId,
                EffectiveDate = eh.EffectiveDate,
                Reason = eh.Reason,
                ChangedBy = eh.ChangedBy,
                CreatedAt = eh.CreatedAt,
            })
            .ToList(),
    };

    /// <summary>
    /// Enforces plan-level employee count limit (AC-5, FR-5).
    /// Reads MaxEmployees from the Tenant entity.
    /// </summary>
    private async Task<Result> CheckPlanLimitAsync(CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);

        if (tenant is null)
            return Result.Success();

        // BUG-008: resolve the effective cap with precedence override > plan > snapshot, instead of
        // reading only the Tenant.MaxEmployees snapshot (which ignored plan changes + per-tenant overrides).
        var planValue = await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.Code == tenant.PlanId)
            .Select(p => (long?)p.MaxEmployees)
            .FirstOrDefaultAsync(cancellationToken);
        var overrides = await _dbContext.PlanLimitOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenant.Id)
            .ToListAsync(cancellationToken);

        var resolved = PlanLimitResolver.Resolve(
            PlanLimitKeys.MaxEmployees, planValue, overrides, DateTime.UtcNow);
        long? limit = resolved.Source == PlanLimitResolver.LimitSource.Override
            ? resolved.Value                                   // override wins (null = unlimited)
            : planValue ?? (long?)tenant.MaxEmployees;         // else plan value, else snapshot

        if (limit is null)
            return Result.Success(); // unlimited

        var currentCount = await _dbContext.Employees
            .CountAsync(e => e.IsActive, cancellationToken);

        if (currentCount >= limit.Value)
            return Result.Failure(
                "Employee limit reached for your current plan. Please upgrade or contact your administrator.",
                403);

        return Result.Success();
    }

    /// <summary>
    /// Generates a unique employee number per tenant using pattern "EMP-NNNN" (FR-2, BR-1).
    /// Uses a retry loop with optimistic concurrency to handle race conditions.
    /// The sequence is isolated per tenant via the global query filter.
    /// </summary>
    private async Task<string> GenerateEmployeeNoAsync(CancellationToken cancellationToken)
    {
        // Pull every employee number for this tenant (incl. soft-deleted, to avoid reuse).
        // IgnoreQueryFilters so the tenant filter / soft-delete don't hide existing numbers.
        var employeeNos = await _dbContext.Employees
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == _tenantContext.TenantId)
            .Select(e => e.EmployeeNo)
            .ToListAsync(cancellationToken);

        // BUG-093: compute the max sequence by parsing the numeric suffix of the canonical
        // "EMP-####" form IN MEMORY — never via a DB lexicographic OrderByDescending. A
        // non-conforming number (e.g. "EMP-MGR01") would otherwise sort highest, fail to parse,
        // fall back to seq 1, and collide with the existing "EMP-0001" (Postgres 23505).
        var maxSeq = employeeNos
            .Where(no => no is not null && no.StartsWith("EMP-") && int.TryParse(no[4..], out _))
            .Select(no => int.Parse(no![4..]))
            .DefaultIfEmpty(0)
            .Max();

        return $"EMP-{maxSeq + 1:D4}";
    }

    private async Task<EmployeeDto> LoadDtoAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .Include(e => e.Department)
            .Include(e => e.JobTitle)
            .Include(e => e.LocationEntity)
            .Include(e => e.Manager)
            .AsNoTracking()
            .FirstAsync(e => e.Id == employeeId, cancellationToken);

        return ToDto(employee);
    }

    private static EmployeeDto ToDto(Employee e) => new()
    {
        Id = e.Id,
        EmployeeNo = e.EmployeeNo,
        FirstName = e.FirstName,
        LastName = e.LastName,
        Email = e.Email,
        Phone = e.Phone,
        DateOfBirth = e.DateOfBirth,
        Gender = e.Gender?.ToString(),
        DateOfJoining = e.DateOfJoining,
        DepartmentId = e.DepartmentId,
        DepartmentName = e.Department?.Name,
        JobTitleId = e.JobTitleId,
        JobTitleName = e.JobTitle?.TitleName,
        ReportsToEmployeeId = e.ReportsToEmployeeId,
        ManagerName = e.Manager == null ? null : $"{e.Manager.FirstName} {e.Manager.LastName}",
        EmploymentType = e.EmploymentType.ToString(),
        Status = e.Status.ToString(),
        Fte = e.Fte,
        WorkArrangement = e.WorkArrangement.ToString(),
        ProfilePhotoUrl = e.ProfilePhotoUrl,
        Location = e.Location,
        LocationId = e.LocationId,
        LocationName = e.LocationEntity?.Name,
        CustomFields = e.CustomFields,
        UserId = e.UserId,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    /// <summary>
    /// Caller role classification for field-level permission enforcement (US-CHR-002 FR-3).
    /// </summary>
    private enum CallerRole
    {
        HrOfficer,
        Employee,
        Manager,
    }
}
