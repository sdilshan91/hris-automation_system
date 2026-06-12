using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Queries;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Employee directory search, filter, sort, and export service (US-CHR-003).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// Search is ILIKE-based (case-insensitive partial match).
/// tsvector/full-text-search upgrade path documented in vault but NOT implemented here.
/// </summary>
public sealed class EmployeeDirectoryService : IEmployeeDirectoryService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeDirectoryService> _logger;

    public EmployeeDirectoryService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<EmployeeDirectoryService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<EmployeeDirectoryResult>> GetDirectoryAsync(
        string? search,
        IReadOnlyList<Guid>? departmentIds,
        IReadOnlyList<string>? jobTitles,
        IReadOnlyList<string>? statuses,
        IReadOnlyList<string>? employmentTypes,
        IReadOnlyList<string>? locations,
        DateTime? dateOfJoiningFrom,
        DateTime? dateOfJoiningTo,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        bool showArchived,
        DirectoryFieldVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeDirectoryResult>.Failure("Tenant context is not resolved.", 400);

        var query = BuildFilteredQuery(
            search, departmentIds, jobTitles, statuses, employmentTypes,
            locations, dateOfJoiningFrom, dateOfJoiningTo, showArchived);

        var totalCount = await query.CountAsync(cancellationToken);

        query = ApplySorting(query, sortBy, sortDescending);

        var employees = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = employees.Select(e => ToDirectoryDto(e, visibility)).ToList();

        return Result<EmployeeDirectoryResult>.Success(new EmployeeDirectoryResult
        {
            Data = items,
            Total = totalCount,
            Page = page,
            PageSize = pageSize,
        });
    }

    public async Task<Result<ExportFileResult>> ExportDirectoryAsync(
        ExportFormat format,
        string? search,
        IReadOnlyList<Guid>? departmentIds,
        IReadOnlyList<string>? jobTitles,
        IReadOnlyList<string>? statuses,
        IReadOnlyList<string>? employmentTypes,
        IReadOnlyList<string>? locations,
        DateTime? dateOfJoiningFrom,
        DateTime? dateOfJoiningTo,
        string? sortBy,
        bool sortDescending,
        bool showArchived,
        DirectoryFieldVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ExportFileResult>.Failure("Tenant context is not resolved.", 400);

        var query = BuildFilteredQuery(
            search, departmentIds, jobTitles, statuses, employmentTypes,
            locations, dateOfJoiningFrom, dateOfJoiningTo, showArchived);

        query = ApplySorting(query, sortBy, sortDescending);

        // Load all matching records for export
        // TODO(NFR-5): For >10k rows, offload to Hangfire background job and return a job ID.
        var employees = await query.ToListAsync(cancellationToken);
        var items = employees.Select(e => ToDirectoryDto(e, visibility)).ToList();

        _logger.LogInformation(
            "Employee directory export. Format={Format}, Count={Count}, TenantId={TenantId}",
            format, items.Count, _tenantContext.TenantId);

        var columns = GetExportColumns(visibility);

        return format switch
        {
            ExportFormat.Csv => Result<ExportFileResult>.Success(GenerateCsv(items, columns)),
            ExportFormat.Excel => Result<ExportFileResult>.Success(GenerateExcel(items, columns)),
            _ => Result<ExportFileResult>.Failure($"Unsupported export format: {format}.", 400),
        };
    }

    // ── Query building ──────────────────────────────────────────────

    private IQueryable<Employee> BuildFilteredQuery(
        string? search,
        IReadOnlyList<Guid>? departmentIds,
        IReadOnlyList<string>? jobTitles,
        IReadOnlyList<string>? statuses,
        IReadOnlyList<string>? employmentTypes,
        IReadOnlyList<string>? locations,
        DateTime? dateOfJoiningFrom,
        DateTime? dateOfJoiningTo,
        bool showArchived)
    {
        IQueryable<Employee> query;

        if (showArchived)
        {
            // BR-1: HR Officer opted in to see soft-deleted employees.
            // Must still enforce tenant isolation explicitly since IgnoreQueryFilters
            // strips the tenant filter too.
            query = _dbContext.Employees
                .IgnoreQueryFilters()
                .Where(e => e.TenantId == _tenantContext.TenantId)
                .Include(e => e.Department)
                .Include(e => e.JobTitle)
                .AsNoTracking();
        }
        else
        {
            // Normal path — global filter handles IsDeleted and tenant.
            query = _dbContext.Employees
                .Include(e => e.Department)
                .Include(e => e.JobTitle)
                .AsNoTracking();
        }

        // FR-1 / AC-2: case-insensitive partial match across name, email, employee_no, phone
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(searchLower) ||
                e.LastName.ToLower().Contains(searchLower) ||
                e.Email.ToLower().Contains(searchLower) ||
                e.EmployeeNo.ToLower().Contains(searchLower) ||
                (e.Phone != null && e.Phone.ToLower().Contains(searchLower)));
        }

        // FR-2 / AC-3: Multi-select filters
        if (departmentIds is { Count: > 0 })
        {
            query = query.Where(e => departmentIds.Contains(e.DepartmentId));
        }

        if (jobTitles is { Count: > 0 })
        {
            query = query.Where(e => e.JobTitle != null && jobTitles.Contains(e.JobTitle.TitleName));
        }

        if (statuses is { Count: > 0 })
        {
            query = query.Where(e => statuses.Contains(e.Status.ToString()));
        }

        if (employmentTypes is { Count: > 0 })
        {
            query = query.Where(e => employmentTypes.Contains(e.EmploymentType.ToString()));
        }

        if (locations is { Count: > 0 })
        {
            query = query.Where(e => e.Location != null && locations.Contains(e.Location));
        }

        if (dateOfJoiningFrom.HasValue)
        {
            query = query.Where(e => e.DateOfJoining >= dateOfJoiningFrom.Value);
        }

        if (dateOfJoiningTo.HasValue)
        {
            query = query.Where(e => e.DateOfJoining <= dateOfJoiningTo.Value);
        }

        return query;
    }

    private static IQueryable<Employee> ApplySorting(
        IQueryable<Employee> query, string? sortBy, bool sortDescending)
    {
        // FR-4: Sorting by name, employee_no, date_of_joining, department
        // Default: name ascending (AC-1)
        return (sortBy?.ToLower(), sortDescending) switch
        {
            ("employee_no", false) => query.OrderBy(e => e.EmployeeNo),
            ("employee_no", true) => query.OrderByDescending(e => e.EmployeeNo),
            ("date_of_joining", false) => query.OrderBy(e => e.DateOfJoining),
            ("date_of_joining", true) => query.OrderByDescending(e => e.DateOfJoining),
            ("department", false) => query.OrderBy(e => e.Department != null ? e.Department.Name : ""),
            ("department", true) => query.OrderByDescending(e => e.Department != null ? e.Department.Name : ""),
            (_, true) => query.OrderByDescending(e => e.FirstName).ThenByDescending(e => e.LastName),
            _ => query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName), // default: name asc
        };
    }

    // ── DTO mapping with role-based field stripping ──────────────────

    internal static EmployeeDirectoryItemDto ToDirectoryDto(Employee e, DirectoryFieldVisibility visibility)
    {
        return visibility switch
        {
            DirectoryFieldVisibility.Full => new EmployeeDirectoryItemDto
            {
                Id = e.Id,
                EmployeeNo = e.EmployeeNo,
                FirstName = e.FirstName,
                LastName = e.LastName,
                Email = e.Email,
                Phone = e.Phone,
                DepartmentName = e.Department?.Name,
                JobTitleName = e.JobTitle?.TitleName,
                Status = e.Status.ToString(),
                EmploymentType = e.EmploymentType.ToString(),
                DateOfJoining = e.DateOfJoining,
                ProfilePhotoUrl = e.ProfilePhotoUrl,
                Location = e.Location,
            },
            DirectoryFieldVisibility.Manager => new EmployeeDirectoryItemDto
            {
                Id = e.Id,
                EmployeeNo = e.EmployeeNo,
                FirstName = e.FirstName,
                LastName = e.LastName,
                Email = e.Email,
                Phone = e.Phone,
                DepartmentName = e.Department?.Name,
                JobTitleName = e.JobTitle?.TitleName,
                Status = e.Status.ToString(),
                EmploymentType = e.EmploymentType.ToString(),
                DateOfJoining = e.DateOfJoining,
                ProfilePhotoUrl = e.ProfilePhotoUrl,
                Location = e.Location,
            },
            // Basic: strip email, phone, dateOfJoining per BR-3
            _ => new EmployeeDirectoryItemDto
            {
                Id = e.Id,
                EmployeeNo = e.EmployeeNo,
                FirstName = e.FirstName,
                LastName = e.LastName,
                Email = null,
                Phone = null,
                DepartmentName = e.Department?.Name,
                JobTitleName = e.JobTitle?.TitleName,
                Status = e.Status.ToString(),
                EmploymentType = null,
                DateOfJoining = null,
                ProfilePhotoUrl = e.ProfilePhotoUrl,
                Location = e.Location,
            },
        };
    }

    // ── Export helpers ───────────────────────────────────────────────

    private static List<(string Header, Func<EmployeeDirectoryItemDto, string?> Accessor)> GetExportColumns(
        DirectoryFieldVisibility visibility)
    {
        var columns = new List<(string Header, Func<EmployeeDirectoryItemDto, string?> Accessor)>
        {
            ("Employee No", d => d.EmployeeNo),
            ("First Name", d => d.FirstName),
            ("Last Name", d => d.LastName),
        };

        if (visibility != DirectoryFieldVisibility.Basic)
        {
            columns.Add(("Email", d => d.Email));
            columns.Add(("Phone", d => d.Phone));
        }

        columns.Add(("Department", d => d.DepartmentName));
        columns.Add(("Job Title", d => d.JobTitleName));
        columns.Add(("Status", d => d.Status));

        if (visibility != DirectoryFieldVisibility.Basic)
        {
            columns.Add(("Employment Type", d => d.EmploymentType));
            columns.Add(("Date of Joining", d => d.DateOfJoining?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        columns.Add(("Location", d => d.Location));

        return columns;
    }

    private static ExportFileResult GenerateCsv(
        IReadOnlyList<EmployeeDirectoryItemDto> items,
        List<(string Header, Func<EmployeeDirectoryItemDto, string?> Accessor)> columns)
    {
        var sb = new StringBuilder();

        // Header row
        sb.AppendLine(string.Join(",", columns.Select(c => CsvEscape(c.Header))));

        // Data rows
        foreach (var item in items)
        {
            sb.AppendLine(string.Join(",", columns.Select(c => CsvEscape(c.Accessor(item) ?? ""))));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        return new ExportFileResult(
            bytes,
            "text/csv",
            $"employee_directory_{timestamp}.csv");
    }

    private static ExportFileResult GenerateExcel(
        IReadOnlyList<EmployeeDirectoryItemDto> items,
        List<(string Header, Func<EmployeeDirectoryItemDto, string?> Accessor)> columns)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Employee Directory");

        // Headers
        for (int col = 0; col < columns.Count; col++)
        {
            var cell = worksheet.Cell(1, col + 1);
            cell.Value = columns[col].Header;
            cell.Style.Font.Bold = true;
        }

        // Data
        for (int row = 0; row < items.Count; row++)
        {
            for (int col = 0; col < columns.Count; col++)
            {
                worksheet.Cell(row + 2, col + 1).Value = columns[col].Accessor(items[row]) ?? "";
            }
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        return new ExportFileResult(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"employee_directory_{timestamp}.xlsx");
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
