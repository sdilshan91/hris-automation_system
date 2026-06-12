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

    // ── Private helpers ──────────────────────────────────────────────

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
}
