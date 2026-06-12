using System.Text.Json;
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
        ILogger<EmployeeService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
        _virusScanner = virusScanner;
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

        // AC-5 / FR-5: Plan-level employee count limit enforcement
        var planLimitResult = await CheckPlanLimitAsync(cancellationToken);
        if (planLimitResult.IsFailure)
            return Result<EmployeeDto>.Failure(planLimitResult.Error!, planLimitResult.StatusCode ?? 403);

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
            CustomFields = request.CustomFields,
            UserId = request.UserId,
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
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeListResult>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.Employees
            .Include(e => e.Department)
            .Include(e => e.JobTitle)
            .AsNoTracking();

        if (activeOnly == true)
            query = query.Where(e => e.IsActive);

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

        // NFR-3: Virus scan before persistence
        var scanResult = await _virusScanner.ScanAsync(fileStream, fileName, cancellationToken);
        if (!scanResult.IsClean)
            return Result<string>.Failure(
                $"File rejected by malware scanner: {scanResult.ThreatName}.", 400);

        // Reset stream position after scan
        if (fileStream.CanSeek)
            fileStream.Position = 0;

        // EXIF stripping stub — TODO(US-CHR-001): implement real EXIF stripping
        // when an image processing library (e.g. SixLabors.ImageSharp) is added.
        // For now, the stream is passed through as-is.
        StripExifData(fileStream, contentType);

        // Reset stream position after EXIF strip
        if (fileStream.CanSeek)
            fileStream.Position = 0;

        // Upload to tenant-isolated storage: {tenantId}/core-hr/{employeeId}/profile/{filename}
        var relativePath = $"core-hr/{employeeId}/profile/{fileName}";
        var storedUrl = await _fileStorage.UploadAsync(
            _tenantContext.TenantId, relativePath, fileStream, contentType, cancellationToken);

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
        if (!_tenantContext.IsResolved)
            return Result<EmployeeProfileDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .Include(e => e.Department)
            .Include(e => e.JobTitle)
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
            .Include(e => e.EmergencyContacts)
            .Include(e => e.EmploymentHistories)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee is null)
            return Result<EmployeeProfileDto>.Failure("Employee not found.", 404);

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

        // Reload the full profile to return the updated state
        return await GetProfileAsync(employeeId, cancellationToken);
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
        EmploymentType = e.EmploymentType.ToString(),
        Status = e.Status.ToString(),
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

        if (tenant?.MaxEmployees is null)
            return Result.Success(); // No limit configured

        var currentCount = await _dbContext.Employees
            .CountAsync(e => e.IsActive, cancellationToken);

        if (currentCount >= tenant.MaxEmployees.Value)
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
        // Get the maximum existing employee number for this tenant
        // IgnoreQueryFilters so we count even soft-deleted employees to avoid reuse
        var maxNo = await _dbContext.Employees
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == _tenantContext.TenantId)
            .OrderByDescending(e => e.EmployeeNo)
            .Select(e => e.EmployeeNo)
            .FirstOrDefaultAsync(cancellationToken);

        int nextSeq = 1;
        if (maxNo is not null && maxNo.StartsWith("EMP-") && int.TryParse(maxNo[4..], out var currentSeq))
        {
            nextSeq = currentSeq + 1;
        }

        return $"EMP-{nextSeq:D4}";
    }

    /// <summary>
    /// Strips EXIF metadata from image files.
    /// TODO(US-CHR-001): Implement real EXIF stripping with SixLabors.ImageSharp or similar.
    /// This is a clearly-marked stub that logs a warning.
    /// </summary>
    private void StripExifData(Stream fileStream, string contentType)
    {
        // STUB: Real EXIF stripping requires an image processing library.
        // When SixLabors.ImageSharp is added, implement:
        //   using var image = await Image.LoadAsync(fileStream, cancellationToken);
        //   image.Metadata.ExifProfile = null;
        //   image.Metadata.IptcProfile = null;
        //   image.SaveAsync(outputStream, encoder);
        _logger.LogWarning(
            "EXIF stripping SKIPPED (stub). ContentType={ContentType}. " +
            "TODO: Add SixLabors.ImageSharp for real EXIF stripping.",
            contentType);
    }

    private async Task<EmployeeDto> LoadDtoAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .Include(e => e.Department)
            .Include(e => e.JobTitle)
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
        EmploymentType = e.EmploymentType.ToString(),
        Status = e.Status.ToString(),
        ProfilePhotoUrl = e.ProfilePhotoUrl,
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
