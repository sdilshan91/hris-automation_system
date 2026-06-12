using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Bulk employee import service (US-CHR-010).
/// Supports CSV and Excel file parsing, per-row validation, partial import,
/// plan-limit enforcement, and async processing via Hangfire for large files.
/// </summary>
public sealed class BulkEmployeeImportService : IBulkEmployeeImportService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<BulkEmployeeImportService> _logger;

    /// <summary>Max file size: 25 MB (BR-7).</summary>
    private const long MaxFileSizeBytes = 25 * 1024 * 1024;

    /// <summary>Sync/async threshold: <= 500 rows sync, > 500 async (FR-7).</summary>
    internal const int AsyncThreshold = 500;

    /// <summary>Batch size for database commits (NFR-4).</summary>
    internal const int BatchSize = 100;

    /// <summary>Template columns matching the Data Requirements table.</summary>
    private static readonly string[] TemplateColumns =
    [
        "first_name", "last_name", "email", "phone", "date_of_birth",
        "gender", "date_of_joining", "department_name", "job_title_name",
        "employment_type", "location_name", "status"
    ];

    private static readonly string[] TemplateDescriptions =
    [
        "Required. Max 100 chars.",
        "Required. Max 100 chars.",
        "Required. Valid email, unique per tenant.",
        "Optional. E.164 format.",
        "Optional. Date (YYYY-MM-DD), must be in the past.",
        "Optional. Male/Female/Non-Binary/Prefer Not To Say.",
        "Required. Date (YYYY-MM-DD).",
        "Required. Must match an existing department name in your organization.",
        "Required. Must match an existing job title name in your organization.",
        "Required. Full-Time/Part-Time/Contract/Intern.",
        "Optional. Must match an existing location name if provided.",
        "Optional. Default: Active. Values: Active/Probation."
    ];

    private static readonly string[] TemplateSampleRow =
    [
        "Jane", "Smith", "jane.smith@example.com", "+94771234567", "1990-05-15",
        "Female", "2026-01-15", "Engineering", "Software Engineer",
        "Full-Time", "Head Office", "Active"
    ];

    private static readonly HashSet<string> AllowedEmploymentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Full-Time", "Part-Time", "Contract", "Intern",
        "FullTime", "PartTime" // also accept enum names without hyphens
    };

    private static readonly HashSet<string> AllowedGenders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Male", "Female", "Non-Binary", "NonBinary", "Prefer Not To Say", "PreferNotToSay"
    };

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active", "Probation"
    };

    public BulkEmployeeImportService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<BulkEmployeeImportService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ── Template Generation (FR-2, AC-1) ────────────────────────────

    public Task<Result<ExportFileResult>> GenerateTemplateAsync(
        ExportFormat format,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(format switch
        {
            ExportFormat.Csv => Result<ExportFileResult>.Success(GenerateCsvTemplate()),
            ExportFormat.Excel => Result<ExportFileResult>.Success(GenerateExcelTemplate()),
            _ => Result<ExportFileResult>.Failure($"Unsupported format: {format}.", 400),
        });
    }

    private static ExportFileResult GenerateCsvTemplate()
    {
        var sb = new StringBuilder();

        // Description row (commented)
        sb.AppendLine("# " + string.Join(",", TemplateDescriptions.Select(CsvEscape)));

        // Header row
        sb.AppendLine(string.Join(",", TemplateColumns.Select(CsvEscape)));

        // Sample data row
        sb.AppendLine(string.Join(",", TemplateSampleRow.Select(CsvEscape)));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return new ExportFileResult(bytes, "text/csv", "employee_import_template.csv");
    }

    private static ExportFileResult GenerateExcelTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Import Template");

        // Description row (row 1)
        for (int i = 0; i < TemplateDescriptions.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = TemplateDescriptions[i];
            cell.Style.Font.Italic = true;
            cell.Style.Font.FontColor = XLColor.Gray;
        }

        // Header row (row 2)
        for (int i = 0; i < TemplateColumns.Length; i++)
        {
            var cell = ws.Cell(2, i + 1);
            cell.Value = TemplateColumns[i];
            cell.Style.Font.Bold = true;
        }

        // Sample data row (row 3)
        for (int i = 0; i < TemplateSampleRow.Length; i++)
        {
            ws.Cell(3, i + 1).Value = TemplateSampleRow[i];
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new ExportFileResult(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "employee_import_template.xlsx");
    }

    // ── Import (FR-1, FR-3, FR-4, FR-5, FR-6, FR-7, FR-9) ─────────

    public async Task<Result<BulkImportResult>> ImportAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        bool importUpToLimit,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BulkImportResult>.Failure("Tenant context is not resolved.", 400);

        // BR-7: max 25 MB
        if (fileSize > MaxFileSizeBytes)
            return Result<BulkImportResult>.Failure(
                $"File size ({fileSize / (1024.0 * 1024):F1} MB) exceeds the maximum allowed size of 25 MB.", 400);

        // Determine format from extension
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        if (ext is not ".csv" and not ".xlsx")
            return Result<BulkImportResult>.Failure(
                "Unsupported file format. Only .csv and .xlsx files are accepted.", 400);

        // Parse rows from file
        List<BulkImportRowDto> rows;
        try
        {
            rows = ext == ".csv"
                ? ParseCsvRows(fileStream)
                : ParseExcelRows(fileStream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse import file {FileName}", fileName);
            return Result<BulkImportResult>.Failure(
                $"Failed to parse file: {ex.Message}", 400);
        }

        if (rows.Count == 0)
            return Result<BulkImportResult>.Failure("The import file contains no data rows.", 400);

        // FR-9 / AC-5: Plan limit pre-validation
        var planCheck = await CheckPlanLimitForImportAsync(rows.Count, importUpToLimit, cancellationToken);
        if (planCheck.IsFailure)
            return Result<BulkImportResult>.Failure(planCheck.Error!, planCheck.StatusCode ?? 400);

        int allowedCount = planCheck.Value!;

        // Trim rows if plan limit restricts count
        if (allowedCount < rows.Count)
        {
            rows = rows.Take(allowedCount).ToList();
        }

        // FR-7: Async for > 500 rows
        if (rows.Count > AsyncThreshold)
        {
            return await QueueAsyncImportAsync(fileStream, fileName, rows.Count, importUpToLimit, cancellationToken);
        }

        // Synchronous processing for <= 500 rows
        var result = await ProcessRowsAsync(rows, fileName, cancellationToken);
        return Result<BulkImportResult>.Success(result);
    }

    // ── Async Job Processing ────────────────────────────────────────

    public async Task ProcessImportJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _dbContext.BulkImportJobs
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            _logger.LogError("BulkImportJob {JobId} not found", jobId);
            return;
        }

        job.Status = BulkImportStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            // Re-parse the file from the stored path
            // For async jobs, the file was saved to disk when queued.
            var filePath = GetImportFilePath(jobId);
            if (!File.Exists(filePath))
            {
                job.Status = BulkImportStatus.Failed;
                job.CompletedAt = DateTime.UtcNow;
                job.ErrorDetails = JsonSerializer.Serialize(new[]
                {
                    new BulkImportRowError { RowNumber = 0, Field = "file", Error = "Import file not found on server." }
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var ext = Path.GetExtension(job.FileName)?.ToLowerInvariant();
            List<BulkImportRowDto> rows;
            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                rows = ext == ".csv" ? ParseCsvRows(fs) : ParseExcelRows(fs);
            }

            // Check plan limit again (may have changed since queuing)
            var planCheck = await CheckPlanLimitForImportAsync(rows.Count, true, cancellationToken);
            int allowedCount = planCheck.IsSuccess ? planCheck.Value! : 0;
            if (allowedCount < rows.Count)
                rows = rows.Take(allowedCount).ToList();

            // Process in batches
            var allErrors = new List<BulkImportRowError>();
            int totalSuccess = 0;

            for (int batchStart = 0; batchStart < rows.Count; batchStart += BatchSize)
            {
                var batch = rows.Skip(batchStart).Take(BatchSize).ToList();
                var batchResult = await ProcessBatchAsync(batch, cancellationToken);
                totalSuccess += batchResult.successCount;
                allErrors.AddRange(batchResult.errors);

                // Update progress
                job.ProcessedRows = Math.Min(batchStart + BatchSize, rows.Count);
                job.SuccessCount = totalSuccess;
                job.FailedCount = allErrors.Select(e => e.RowNumber).Distinct().Count();
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            int failedRowCount = allErrors.Select(e => e.RowNumber).Distinct().Count();
            job.TotalRows = rows.Count;
            job.SuccessCount = totalSuccess;
            job.FailedCount = failedRowCount;
            job.Status = BulkImportStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.ProcessedRows = rows.Count;
            job.ErrorDetails = allErrors.Count > 0 ? JsonSerializer.Serialize(allErrors) : null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Bulk import job {JobId} completed. Total={Total}, Success={Success}, Failed={Failed}, TenantId={TenantId}",
                jobId, rows.Count, totalSuccess, failedRowCount, _tenantContext.TenantId);

            // TODO(notification): Dispatch completion notification to the initiator.
            // The Notification module is not built yet. Log the summary for now.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk import job {JobId} failed with exception", jobId);
            job.Status = BulkImportStatus.Failed;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorDetails = JsonSerializer.Serialize(new[]
            {
                new BulkImportRowError { RowNumber = 0, Field = "system", Error = ex.Message }
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // Clean up temp file
            var filePath = GetImportFilePath(jobId);
            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up import file {Path}", filePath); }
            }
        }
    }

    // ── Job Status (AC-4) ───────────────────────────────────────────

    public async Task<Result<BulkImportJobStatus>> GetJobStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BulkImportJobStatus>.Failure("Tenant context is not resolved.", 400);

        var job = await _dbContext.BulkImportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
            return Result<BulkImportJobStatus>.Failure("Import job not found.", 404);

        List<BulkImportRowError>? errors = null;
        if (job.ErrorDetails is not null)
        {
            errors = JsonSerializer.Deserialize<List<BulkImportRowError>>(job.ErrorDetails);
        }

        return Result<BulkImportJobStatus>.Success(new BulkImportJobStatus
        {
            JobId = job.Id,
            Status = job.Status.ToString(),
            FileName = job.FileName,
            TotalRows = job.TotalRows,
            ProcessedRows = job.ProcessedRows,
            SuccessCount = job.SuccessCount,
            FailedCount = job.FailedCount,
            Errors = errors,
            InitiatedBy = job.InitiatedBy,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
        });
    }

    // ── Error Report (FR-8) ─────────────────────────────────────────

    public async Task<Result<ExportFileResult>> GetErrorReportAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ExportFileResult>.Failure("Tenant context is not resolved.", 400);

        var job = await _dbContext.BulkImportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
            return Result<ExportFileResult>.Failure("Import job not found.", 404);

        if (job.ErrorDetails is null)
            return Result<ExportFileResult>.Failure("No errors to report.", 400);

        var errors = JsonSerializer.Deserialize<List<BulkImportRowError>>(job.ErrorDetails)
            ?? new List<BulkImportRowError>();

        var sb = new StringBuilder();
        sb.AppendLine("row_number,field,error");
        foreach (var err in errors)
        {
            sb.AppendLine($"{err.RowNumber},{CsvEscape(err.Field)},{CsvEscape(err.Error)}");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        return Result<ExportFileResult>.Success(new ExportFileResult(
            bytes,
            "text/csv",
            $"import_errors_{timestamp}.csv"));
    }

    // ── File Parsing ────────────────────────────────────────────────

    internal static List<BulkImportRowDto> ParseCsvRows(Stream stream)
    {
        var rows = new List<BulkImportRowDto>();

        // Reset if seekable
        if (stream.CanSeek)
            stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        // Skip comment lines (starting with #)
        string? headerLine = null;
        while ((headerLine = reader.ReadLine()) != null)
        {
            if (!headerLine.TrimStart().StartsWith('#'))
                break;
        }

        if (string.IsNullOrWhiteSpace(headerLine))
            return rows;

        // Parse using CsvHelper for robust RFC 4180 parsing
        var fullCsv = headerLine + "\n" + reader.ReadToEnd();
        using var stringReader = new StringReader(fullCsv);
        using var csvReader = new CsvReader(stringReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim,
        });

        csvReader.Read();
        csvReader.ReadHeader();

        int rowNum = 1; // 1-based, after header
        while (csvReader.Read())
        {
            rowNum++;
            rows.Add(new BulkImportRowDto
            {
                RowNumber = rowNum,
                FirstName = GetField(csvReader, "first_name"),
                LastName = GetField(csvReader, "last_name"),
                Email = GetField(csvReader, "email"),
                Phone = GetField(csvReader, "phone"),
                DateOfBirth = GetField(csvReader, "date_of_birth"),
                Gender = GetField(csvReader, "gender"),
                DateOfJoining = GetField(csvReader, "date_of_joining"),
                DepartmentName = GetField(csvReader, "department_name"),
                JobTitleName = GetField(csvReader, "job_title_name"),
                EmploymentType = GetField(csvReader, "employment_type"),
                LocationName = GetField(csvReader, "location_name"),
                Status = GetField(csvReader, "status"),
            });
        }

        return rows;
    }

    private static string? GetField(CsvReader reader, string name)
    {
        try
        {
            var value = reader.GetField(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }

    internal static List<BulkImportRowDto> ParseExcelRows(Stream stream)
    {
        var rows = new List<BulkImportRowDto>();

        if (stream.CanSeek)
            stream.Position = 0;

        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        // Find header row (skip description rows — look for row containing "first_name")
        int headerRow = 1;
        for (int r = 1; r <= Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1); r++)
        {
            var cellValue = ws.Cell(r, 1).GetString().Trim().ToLowerInvariant();
            if (cellValue == "first_name")
            {
                headerRow = r;
                break;
            }
        }

        // Build column index mapping from headers
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int c = 1; c <= lastCol; c++)
        {
            var header = ws.Cell(headerRow, c).GetString().Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(header))
                colMap[header] = c;
        }

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            // Skip completely empty rows
            bool hasData = false;
            for (int c = 1; c <= lastCol; c++)
            {
                if (!ws.Cell(r, c).IsEmpty())
                {
                    hasData = true;
                    break;
                }
            }
            if (!hasData) continue;

            rows.Add(new BulkImportRowDto
            {
                RowNumber = r,
                FirstName = GetExcelCell(ws, r, colMap, "first_name"),
                LastName = GetExcelCell(ws, r, colMap, "last_name"),
                Email = GetExcelCell(ws, r, colMap, "email"),
                Phone = GetExcelCell(ws, r, colMap, "phone"),
                DateOfBirth = GetExcelCell(ws, r, colMap, "date_of_birth"),
                Gender = GetExcelCell(ws, r, colMap, "gender"),
                DateOfJoining = GetExcelCell(ws, r, colMap, "date_of_joining"),
                DepartmentName = GetExcelCell(ws, r, colMap, "department_name"),
                JobTitleName = GetExcelCell(ws, r, colMap, "job_title_name"),
                EmploymentType = GetExcelCell(ws, r, colMap, "employment_type"),
                LocationName = GetExcelCell(ws, r, colMap, "location_name"),
                Status = GetExcelCell(ws, r, colMap, "status"),
            });
        }

        return rows;
    }

    private static string? GetExcelCell(IXLWorksheet ws, int row, Dictionary<string, int> colMap, string column)
    {
        if (!colMap.TryGetValue(column, out var col))
            return null;

        var cell = ws.Cell(row, col);
        if (cell.IsEmpty())
            return null;

        // Handle date cells that Excel stores as numeric
        if ((column == "date_of_birth" || column == "date_of_joining") && cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var value = cell.GetString().Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // ── Row Validation ──────────────────────────────────────────────

    internal async Task<(List<ValidatedRow> validRows, List<BulkImportRowError> errors)> ValidateRowsAsync(
        IReadOnlyList<BulkImportRowDto> rows,
        CancellationToken cancellationToken)
    {
        var errors = new List<BulkImportRowError>();
        var validRows = new List<ValidatedRow>();

        // Pre-load reference data for the tenant (single DB round trip each)
        var departments = await _dbContext.Departments
            .Where(d => d.IsActive)
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(cancellationToken);
        var deptByName = departments
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var jobTitles = await _dbContext.JobTitles
            .Where(j => j.IsActive)
            .Select(j => new { j.Id, j.TitleName })
            .ToListAsync(cancellationToken);
        var jtByName = jobTitles
            .GroupBy(j => j.TitleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var locations = await _dbContext.Locations
            .Where(l => l.IsActive)
            .Select(l => new { l.Id, l.Name })
            .ToListAsync(cancellationToken);
        var locByName = locations
            .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        // Existing emails in tenant (for uniqueness check)
        var existingEmails = await _dbContext.Employees
            .Select(e => e.Email.ToLower())
            .ToListAsync(cancellationToken);
        var existingEmailSet = new HashSet<string>(existingEmails, StringComparer.OrdinalIgnoreCase);

        // Track emails within the file for in-file duplicate detection (BR-2)
        var fileEmailsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var rowErrors = new List<BulkImportRowError>();

            // Required: first_name
            if (string.IsNullOrWhiteSpace(row.FirstName))
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "first_name", Error = "First name is required." });
            else if (row.FirstName.Length > 100)
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "first_name", Error = "First name must be at most 100 characters." });

            // Required: last_name
            if (string.IsNullOrWhiteSpace(row.LastName))
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "last_name", Error = "Last name is required." });
            else if (row.LastName.Length > 100)
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "last_name", Error = "Last name must be at most 100 characters." });

            // Required: email (format + uniqueness)
            if (string.IsNullOrWhiteSpace(row.Email))
            {
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "email", Error = "Email is required." });
            }
            else
            {
                if (!IsValidEmail(row.Email))
                    rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "email", Error = "Invalid email format." });
                else if (existingEmailSet.Contains(row.Email))
                    rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "email", Error = "Email already exists in tenant." });
                else if (!fileEmailsSeen.Add(row.Email))
                    rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "email", Error = "Duplicate email within file." });
            }

            // Optional: phone (basic E.164 check)
            // No strict enforcement — just trim

            // Optional: date_of_birth
            DateTime? dateOfBirth = null;
            if (!string.IsNullOrWhiteSpace(row.DateOfBirth))
            {
                if (!DateTime.TryParse(row.DateOfBirth, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
                    rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "date_of_birth", Error = "Invalid date format. Use YYYY-MM-DD." });
                else if (dob >= DateTime.UtcNow.Date)
                    rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "date_of_birth", Error = "Date of birth must be in the past." });
                else
                    dateOfBirth = DateTime.SpecifyKind(dob, DateTimeKind.Utc);
            }

            // Optional: gender
            Gender? gender = null;
            if (!string.IsNullOrWhiteSpace(row.Gender))
            {
                if (!AllowedGenders.Contains(row.Gender))
                    rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "gender", Error = $"Invalid gender. Allowed: Male, Female, Non-Binary, Prefer Not To Say." });
                else
                    gender = ParseGender(row.Gender);
            }

            // Required: date_of_joining
            DateTime dateOfJoining = default;
            if (string.IsNullOrWhiteSpace(row.DateOfJoining))
            {
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "date_of_joining", Error = "Date of joining is required." });
            }
            else
            {
                if (!DateTime.TryParse(row.DateOfJoining, CultureInfo.InvariantCulture, DateTimeStyles.None, out var doj))
                    rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "date_of_joining", Error = "Invalid date format. Use YYYY-MM-DD." });
                else
                    dateOfJoining = DateTime.SpecifyKind(doj, DateTimeKind.Utc);
            }

            // Required: department_name (must resolve)
            Guid departmentId = Guid.Empty;
            if (string.IsNullOrWhiteSpace(row.DepartmentName))
            {
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "department_name", Error = "Department name is required." });
            }
            else if (!deptByName.TryGetValue(row.DepartmentName, out departmentId))
            {
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "department_name", Error = $"Department '{row.DepartmentName}' not found in your organization." });
            }

            // Required: job_title_name (must resolve)
            Guid jobTitleId = Guid.Empty;
            if (string.IsNullOrWhiteSpace(row.JobTitleName))
            {
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "job_title_name", Error = "Job title name is required." });
            }
            else if (!jtByName.TryGetValue(row.JobTitleName, out jobTitleId))
            {
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "job_title_name", Error = $"Job title '{row.JobTitleName}' not found in your organization." });
            }

            // Required: employment_type
            EmploymentType employmentType = EmploymentType.FullTime;
            if (string.IsNullOrWhiteSpace(row.EmploymentType))
            {
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "employment_type", Error = "Employment type is required." });
            }
            else if (!AllowedEmploymentTypes.Contains(row.EmploymentType))
            {
                rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "employment_type", Error = $"Invalid employment type. Allowed: Full-Time, Part-Time, Contract, Intern." });
            }
            else
            {
                employmentType = ParseEmploymentType(row.EmploymentType);
            }

            // Optional: location_name (must resolve if provided)
            Guid? locationId = null;
            string? locationName = null;
            if (!string.IsNullOrWhiteSpace(row.LocationName))
            {
                if (!locByName.TryGetValue(row.LocationName, out var locId))
                {
                    rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "location_name", Error = $"Location '{row.LocationName}' not found in your organization." });
                }
                else
                {
                    locationId = locId;
                    locationName = row.LocationName;
                }
            }

            // Optional: status (default Active - BR-4)
            EmployeeStatus status = EmployeeStatus.Active;
            if (!string.IsNullOrWhiteSpace(row.Status))
            {
                if (!AllowedStatuses.Contains(row.Status))
                    rowErrors.Add(new BulkImportRowError { RowNumber = row.RowNumber, Field = "status", Error = "Invalid status. Allowed: Active, Probation." });
                else
                    status = row.Status.Equals("Probation", StringComparison.OrdinalIgnoreCase)
                        ? EmployeeStatus.Probation
                        : EmployeeStatus.Active;
            }

            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors);
            }
            else
            {
                validRows.Add(new ValidatedRow
                {
                    RowNumber = row.RowNumber,
                    FirstName = row.FirstName!,
                    LastName = row.LastName!,
                    Email = row.Email!,
                    Phone = row.Phone,
                    DateOfBirth = dateOfBirth,
                    Gender = gender,
                    DateOfJoining = dateOfJoining,
                    DepartmentId = departmentId,
                    JobTitleId = jobTitleId,
                    EmploymentType = employmentType,
                    LocationId = locationId,
                    LocationName = locationName,
                    Status = status,
                });
            }
        }

        return (validRows, errors);
    }

    // ── Row Processing ──────────────────────────────────────────────

    private async Task<BulkImportResult> ProcessRowsAsync(
        IReadOnlyList<BulkImportRowDto> rows,
        string fileName,
        CancellationToken cancellationToken)
    {
        var (validRows, errors) = await ValidateRowsAsync(rows, cancellationToken);

        // Create a tracking job record for audit (FR-10)
        var job = new BulkImportJob
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            FileName = fileName,
            TotalRows = rows.Count,
            Status = BulkImportStatus.Processing,
            StartedAt = DateTime.UtcNow,
            InitiatedBy = _currentUser.IsAuthenticated ? _currentUser.Email : "system",
        };
        _dbContext.BulkImportJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        int totalSuccess = 0;

        // Process valid rows in batches (NFR-4)
        for (int batchStart = 0; batchStart < validRows.Count; batchStart += BatchSize)
        {
            var batch = validRows.Skip(batchStart).Take(BatchSize).ToList();
            var batchResult = await CreateEmployeeBatchAsync(batch, cancellationToken);
            totalSuccess += batchResult.successCount;
            errors.AddRange(batchResult.errors);
        }

        // Count unique failed rows (a row may have multiple field errors)
        int failedRowCount = errors.Select(e => e.RowNumber).Distinct().Count();

        // Update the job record
        job.SuccessCount = totalSuccess;
        job.FailedCount = failedRowCount;
        job.ProcessedRows = rows.Count;
        job.Status = BulkImportStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.ErrorDetails = errors.Count > 0 ? JsonSerializer.Serialize(errors) : null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Bulk import completed (sync). File={FileName}, Total={Total}, Success={Success}, Failed={Failed}, TenantId={TenantId}",
            fileName, rows.Count, totalSuccess, failedRowCount, _tenantContext.TenantId);

        return new BulkImportResult
        {
            Total = rows.Count,
            Success = totalSuccess,
            Failed = failedRowCount,
            Errors = errors,
            JobId = job.Id,
            IsComplete = true,
        };
    }

    internal async Task<(int successCount, List<BulkImportRowError> errors)> ProcessBatchAsync(
        IReadOnlyList<BulkImportRowDto> batchRows,
        CancellationToken cancellationToken)
    {
        var (validRows, errors) = await ValidateRowsAsync(batchRows, cancellationToken);
        var batchResult = await CreateEmployeeBatchAsync(validRows, cancellationToken);
        errors.AddRange(batchResult.errors);
        return (batchResult.successCount, errors);
    }

    private async Task<(int successCount, List<BulkImportRowError> errors)> CreateEmployeeBatchAsync(
        IReadOnlyList<ValidatedRow> validRows,
        CancellationToken cancellationToken)
    {
        var errors = new List<BulkImportRowError>();
        int successCount = 0;

        if (validRows.Count == 0)
            return (0, errors);

        try
        {
            // Generate employee numbers for the batch
            int nextSeq = await GetNextEmployeeSeqAsync(cancellationToken);

            var employees = new List<Employee>();
            foreach (var row in validRows)
            {
                var employeeNo = $"EMP-{nextSeq:D4}";
                nextSeq++;

                employees.Add(new Employee
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    EmployeeNo = employeeNo,
                    FirstName = row.FirstName,
                    LastName = row.LastName,
                    Email = row.Email,
                    Phone = row.Phone,
                    DateOfBirth = row.DateOfBirth,
                    Gender = row.Gender,
                    DateOfJoining = row.DateOfJoining,
                    DepartmentId = row.DepartmentId,
                    JobTitleId = row.JobTitleId,
                    EmploymentType = row.EmploymentType,
                    Status = row.Status,
                    Location = row.LocationName,
                    LocationId = row.LocationId,
                    IsActive = row.Status is EmployeeStatus.Active or EmployeeStatus.Probation,
                    IsDeleted = false,
                });
            }

            _dbContext.Employees.AddRange(employees);
            await _dbContext.SaveChangesAsync(cancellationToken);
            successCount = employees.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch insert failed for {Count} rows", validRows.Count);
            // Per-batch rollback (NFR-4): mark all rows in this batch as failed
            foreach (var row in validRows)
            {
                errors.Add(new BulkImportRowError
                {
                    RowNumber = row.RowNumber,
                    Field = "batch",
                    Error = $"Batch insert failed: {ex.Message}",
                });
            }

            // Detach the failed entities to allow subsequent batches
            foreach (var entry in _dbContext.ChangeTracker.Entries<Employee>()
                .Where(e => e.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }
        }

        return (successCount, errors);
    }

    // ── Async Queueing ──────────────────────────────────────────────

    private async Task<Result<BulkImportResult>> QueueAsyncImportAsync(
        Stream fileStream,
        string fileName,
        int totalRows,
        bool importUpToLimit,
        CancellationToken cancellationToken)
    {
        var job = new BulkImportJob
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            FileName = fileName,
            TotalRows = totalRows,
            Status = BulkImportStatus.Pending,
            InitiatedBy = _currentUser.IsAuthenticated ? _currentUser.Email : "system",
        };

        _dbContext.BulkImportJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Save file to temp storage for the background job to pick up
        var filePath = GetImportFilePath(job.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        if (fileStream.CanSeek)
            fileStream.Position = 0;

        await using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        {
            await fileStream.CopyToAsync(fs, cancellationToken);
        }

        _logger.LogInformation(
            "Bulk import queued for async processing. JobId={JobId}, FileName={FileName}, Rows={Rows}, TenantId={TenantId}",
            job.Id, fileName, totalRows, _tenantContext.TenantId);

        return Result<BulkImportResult>.Success(new BulkImportResult
        {
            Total = totalRows,
            Success = 0,
            Failed = 0,
            Errors = [],
            JobId = job.Id,
            IsComplete = false,
            Warning = $"File contains {totalRows} rows (> {AsyncThreshold}). Import has been queued for background processing.",
        });
    }

    // ── Plan Limit Check (FR-9, AC-5) ───────────────────────────────

    private async Task<Result<int>> CheckPlanLimitForImportAsync(
        int requestedCount,
        bool importUpToLimit,
        CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, cancellationToken);

        if (tenant?.MaxEmployees is null)
            return Result<int>.Success(requestedCount); // No limit

        var currentCount = await _dbContext.Employees
            .CountAsync(e => e.IsActive, cancellationToken);

        int availableSlots = tenant.MaxEmployees.Value - currentCount;

        if (availableSlots <= 0)
            return Result<int>.Failure(
                $"Your plan's employee limit ({tenant.MaxEmployees.Value}) has been reached. " +
                "Please upgrade your plan or remove inactive employees.", 403);

        if (requestedCount <= availableSlots)
            return Result<int>.Success(requestedCount); // All fit within limit

        // Would exceed limit
        if (!importUpToLimit)
        {
            return Result<int>.Failure(
                $"This import would exceed your plan's employee limit ({tenant.MaxEmployees.Value}). " +
                $"Currently {currentCount} employees, attempting to import {requestedCount}. " +
                $"Only {availableSlots} slots available. " +
                "Set 'importUpToLimit' to true to import only up to the limit, or upgrade your plan.",
                409);
        }

        // Import up to available slots
        return Result<int>.Success(availableSlots);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Gets the next employee sequence number for the tenant.
    /// Reuses the same logic as EmployeeService.GenerateEmployeeNoAsync.
    /// </summary>
    private async Task<int> GetNextEmployeeSeqAsync(CancellationToken cancellationToken)
    {
        var maxNo = await _dbContext.Employees
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == _tenantContext.TenantId)
            .OrderByDescending(e => e.EmployeeNo)
            .Select(e => e.EmployeeNo)
            .FirstOrDefaultAsync(cancellationToken);

        if (maxNo is not null && maxNo.StartsWith("EMP-") && int.TryParse(maxNo[4..], out var currentSeq))
            return currentSeq + 1;

        return 1;
    }

    private static string GetImportFilePath(Guid jobId)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hrm-bulk-imports");
        return Path.Combine(tempDir, $"{jobId}.import");
    }

    private static bool IsValidEmail(string email)
    {
        // Basic email validation
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var atIdx = email.IndexOf('@');
        if (atIdx <= 0 || atIdx >= email.Length - 1)
            return false;

        var dotIdx = email.LastIndexOf('.');
        return dotIdx > atIdx + 1 && dotIdx < email.Length - 1;
    }

    private static Gender ParseGender(string value) => value.ToLowerInvariant() switch
    {
        "male" => Gender.Male,
        "female" => Gender.Female,
        "non-binary" or "nonbinary" => Gender.NonBinary,
        _ => Gender.PreferNotToSay,
    };

    private static EmploymentType ParseEmploymentType(string value) => value.ToLowerInvariant() switch
    {
        "part-time" or "parttime" => EmploymentType.PartTime,
        "contract" => EmploymentType.Contract,
        "intern" => EmploymentType.Intern,
        _ => EmploymentType.FullTime,
    };

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    /// <summary>
    /// Intermediate validated row with resolved IDs.
    /// </summary>
    internal sealed class ValidatedRow
    {
        public int RowNumber { get; init; }
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public DateTime? DateOfBirth { get; init; }
        public Gender? Gender { get; init; }
        public DateTime DateOfJoining { get; init; }
        public Guid DepartmentId { get; init; }
        public Guid JobTitleId { get; init; }
        public EmploymentType EmploymentType { get; init; }
        public Guid? LocationId { get; init; }
        public string? LocationName { get; init; }
        public EmployeeStatus Status { get; init; }
    }
}
