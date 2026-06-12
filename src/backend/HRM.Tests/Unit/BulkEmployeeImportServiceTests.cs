// ============================================================================
// US-CHR-010: Bulk Employee Import Unit Tests.
// Tests: happy path (N valid rows -> N employees with tenant_id + employee_no),
// partial failure (valid imported, invalid reported with row numbers/fields),
// duplicate email in-file flagged, non-existent department -> row error,
// plan-limit pre-validation warning, async path queued for >500 rows,
// tenant isolation, template generation columns, file format validation,
// batch processing, and error reporting.
// Uses EF Core InMemory provider for lightweight database testing.
// ============================================================================

using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class BulkEmployeeImportServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<BulkEmployeeImportService> _logger;

    public BulkEmployeeImportServiceTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.Subdomain.Returns("test");
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<BulkEmployeeImportService>>();
    }

    private BulkEmployeeImportService CreateService(ITenantContext? ctx = null)
    {
        var dbContext = TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
        return new BulkEmployeeImportService(dbContext, ctx ?? _tenantContext, _currentUser, _logger);
    }

    private AppDbContext CreateDbContext(ITenantContext? ctx = null)
    {
        return TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);
    }

    private async Task<(Guid DeptId, Guid JtId, Guid? LocId)> SeedReferenceData(
        string deptName = "Engineering", string jtName = "Software Engineer",
        string? locName = null, Guid? tenantId = null)
    {
        var tid = tenantId ?? _tenantId;
        ITenantContext ctx;
        if (tenantId.HasValue && tenantId.Value != _tenantId)
        {
            ctx = Substitute.For<ITenantContext>();
            ctx.TenantId.Returns(tid);
            ctx.IsResolved.Returns(true);
        }
        else
        {
            ctx = _tenantContext;
        }

        using var db = TestDbContextFactory.Create(ctx, _dbName);

        var dept = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            Name = deptName,
            Code = deptName.ToUpperInvariant()[..3],
            IsActive = true,
            IsDeleted = false,
        };
        db.Departments.Add(dept);

        var jt = new JobTitle
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tid,
            TitleName = jtName,
            IsActive = true,
            IsDeleted = false,
        };
        db.JobTitles.Add(jt);

        Guid? locId = null;
        if (locName is not null)
        {
            var loc = new Location
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tid,
                Name = locName,
                TimeZone = "UTC",
                IsActive = true,
                IsDeleted = false,
            };
            db.Locations.Add(loc);
            locId = loc.Id;
        }

        await db.SaveChangesAsync();
        return (dept.Id, jt.Id, locId);
    }

    private async Task SeedTenant(int? maxEmployees = null, Guid? tenantId = null)
    {
        var tid = tenantId ?? _tenantId;
        using var db = CreateDbContext();
        var existing = await db.Tenants.FindAsync(tid);
        if (existing is null)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tid,
                Subdomain = "test",
                Name = "Test Tenant",
                Status = TenantStatus.Active,
                MaxEmployees = maxEmployees,
            });
            await db.SaveChangesAsync();
        }
        else
        {
            existing.MaxEmployees = maxEmployees;
            await db.SaveChangesAsync();
        }
    }

    private Stream CreateCsvStream(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new MemoryStream(bytes);
    }

    private string BuildCsvContent(params string[] dataRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("first_name,last_name,email,phone,date_of_birth,gender,date_of_joining,department_name,job_title_name,employment_type,location_name,status");
        foreach (var row in dataRows)
            sb.AppendLine(row);
        return sb.ToString();
    }

    private Stream CreateExcelStream(params string[][] dataRows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Import");

        var headers = new[] { "first_name", "last_name", "email", "phone", "date_of_birth", "gender", "date_of_joining", "department_name", "job_title_name", "employment_type", "location_name", "status" };
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        for (int r = 0; r < dataRows.Length; r++)
        {
            for (int c = 0; c < dataRows[r].Length && c < headers.Length; c++)
                ws.Cell(r + 2, c + 1).Value = dataRows[r][c];
        }

        var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    // ── Template Generation ─────────────────────────────────────────

    [Fact]
    public async Task GenerateTemplate_Csv_ShouldContainAllColumns()
    {
        var service = CreateService();
        var result = await service.GenerateTemplateAsync(ExportFormat.Csv);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("text/csv");
        result.Value.FileName.Should().Be("employee_import_template.csv");

        var text = Encoding.UTF8.GetString(result.Value.FileBytes);
        text.Should().Contain("first_name");
        text.Should().Contain("last_name");
        text.Should().Contain("email");
        text.Should().Contain("phone");
        text.Should().Contain("date_of_birth");
        text.Should().Contain("gender");
        text.Should().Contain("date_of_joining");
        text.Should().Contain("department_name");
        text.Should().Contain("job_title_name");
        text.Should().Contain("employment_type");
        text.Should().Contain("location_name");
        text.Should().Contain("status");
    }

    [Fact]
    public async Task GenerateTemplate_Excel_ShouldContainAllColumns()
    {
        var service = CreateService();
        var result = await service.GenerateTemplateAsync(ExportFormat.Excel);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        result.Value.FileName.Should().Be("employee_import_template.xlsx");
        result.Value.FileBytes.Should().NotBeEmpty();

        using var stream = new MemoryStream(result.Value.FileBytes);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        // Row 2 should be the header
        ws.Cell(2, 1).GetString().Should().Be("first_name");
        ws.Cell(2, 2).GetString().Should().Be("last_name");
        ws.Cell(2, 3).GetString().Should().Be("email");

        // Row 3 should be sample data
        ws.Cell(3, 1).GetString().Should().Be("Jane");
    }

    // ── Happy Path: Valid Import ────────────────────────────────────

    [Fact]
    public async Task Import_AllValidRows_ShouldCreateAllEmployees()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer", "Head Office");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,+1234567890,1990-05-15,Male,2026-01-15,Engineering,Software Engineer,Full-Time,Head Office,Active",
            "Jane,Smith,jane@test.com,+0987654321,1992-03-20,Female,2026-02-01,Engineering,Software Engineer,Part-Time,Head Office,Probation",
            "Alex,Brown,alex@test.com,,,,2026-03-01,Engineering,Software Engineer,Contract,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Total.Should().Be(3);
        result.Value.Success.Should().Be(3);
        result.Value.Failed.Should().Be(0);
        result.Value.Errors.Should().BeEmpty();
        result.Value.IsComplete.Should().BeTrue();

        // Verify employees were created with correct tenant_id and auto-generated employee_no
        using var db = CreateDbContext();
        var employees = db.Employees.ToList();
        employees.Should().HaveCount(3);
        employees.Should().OnlyContain(e => e.TenantId == _tenantId);
        employees.Should().OnlyContain(e => e.EmployeeNo.StartsWith("EMP-"));

        // Verify employee_no is sequential
        var seqs = employees.Select(e => int.Parse(e.EmployeeNo[4..])).OrderBy(x => x).ToList();
        seqs.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Import_Excel_AllValidRows_ShouldCreateEmployees()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        using var excelStream = CreateExcelStream(
            new[] { "John", "Doe", "john@test.com", "+123", "1990-05-15", "Male", "2026-01-15", "Engineering", "Software Engineer", "Full-Time", "", "Active" },
            new[] { "Jane", "Smith", "jane@test.com", "", "", "", "2026-02-01", "Engineering", "Software Engineer", "Contract", "", "" }
        );

        var service = CreateService();
        var result = await service.ImportAsync(excelStream, "test.xlsx", excelStream.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Total.Should().Be(2);
        result.Value.Success.Should().Be(2);
        result.Value.Failed.Should().Be(0);
    }

    // ── Partial Failure ─────────────────────────────────────────────

    [Fact]
    public async Task Import_PartialFailure_ShouldImportValidAndReportInvalid()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,",
            ",Missing,missing@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,", // missing first_name
            "Valid,User,valid@test.com,,,,2026-02-01,Engineering,Software Engineer,Part-Time,,",
            "Bad,Email,not-an-email,,,,2026-03-01,Engineering,Software Engineer,Full-Time,," // invalid email
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Total.Should().Be(4);
        result.Value.Success.Should().Be(2);
        result.Value.Failed.Should().Be(2);

        // Verify error details contain row numbers and field names
        result.Value.Errors.Should().Contain(e => e.RowNumber == 3 && e.Field == "first_name");
        result.Value.Errors.Should().Contain(e => e.RowNumber == 5 && e.Field == "email");

        // Verify only valid employees were created
        using var db = CreateDbContext();
        var employees = db.Employees.ToList();
        employees.Should().HaveCount(2);
        employees.Select(e => e.Email).Should().BeEquivalentTo("john@test.com", "valid@test.com");
    }

    // ── Duplicate Email In-File ─────────────────────────────────────

    [Fact]
    public async Task Import_DuplicateEmailInFile_ShouldFlagSecondOccurrence()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,duplicate@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,",
            "Jane,Smith,duplicate@test.com,,,,2026-02-01,Engineering,Software Engineer,Full-Time,," // duplicate
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Success.Should().Be(1);
        result.Value.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e =>
            e.Field == "email" && e.Error.Contains("Duplicate email within file"));
    }

    // ── Existing Email in Tenant ────────────────────────────────────

    [Fact]
    public async Task Import_EmailExistsInTenant_ShouldFlagRow()
    {
        await SeedTenant();
        var (deptId, jtId, _) = await SeedReferenceData("Engineering", "Software Engineer");

        // Pre-seed an employee with existing email
        using (var db = CreateDbContext())
        {
            db.Employees.Add(new Employee
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeNo = "EMP-0001",
                FirstName = "Existing",
                LastName = "Employee",
                Email = "existing@test.com",
                DateOfJoining = DateTime.UtcNow,
                DepartmentId = deptId,
                JobTitleId = jtId,
                EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active,
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var csv = BuildCsvContent(
            "New,User,existing@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Success.Should().Be(0);
        result.Value.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e =>
            e.Field == "email" && e.Error.Contains("already exists"));
    }

    // ── Non-existent Department ─────────────────────────────────────

    [Fact]
    public async Task Import_NonExistentDepartment_ShouldFlagRow()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,Marketing,Software Engineer,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Success.Should().Be(0);
        result.Value.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e =>
            e.Field == "department_name" && e.Error.Contains("Marketing"));
    }

    // ── Non-existent Job Title ──────────────────────────────────────

    [Fact]
    public async Task Import_NonExistentJobTitle_ShouldFlagRow()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,Engineering,CEO,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Success.Should().Be(0);
        result.Value.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e =>
            e.Field == "job_title_name" && e.Error.Contains("CEO"));
    }

    // ── Non-existent Location (if provided) ─────────────────────────

    [Fact]
    public async Task Import_NonExistentLocation_ShouldFlagRow()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,NonExistentLocation,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Success.Should().Be(0);
        result.Value.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e =>
            e.Field == "location_name" && e.Error.Contains("NonExistentLocation"));
    }

    // ── Plan Limit Pre-validation (AC-5) ────────────────────────────

    [Fact]
    public async Task Import_ExceedsPlanLimit_WithoutUpToLimitFlag_ShouldReturn409()
    {
        await SeedTenant(maxEmployees: 2);
        var (deptId, jtId, _) = await SeedReferenceData("Engineering", "Software Engineer");

        // Pre-seed 1 existing active employee
        using (var db = CreateDbContext())
        {
            db.Employees.Add(new Employee
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeNo = "EMP-0001",
                FirstName = "Existing",
                LastName = "Emp",
                Email = "existing@t.com",
                DateOfJoining = DateTime.UtcNow,
                DepartmentId = deptId,
                JobTitleId = jtId,
                EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active,
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,",
            "Jane,Smith,jane@test.com,,,,2026-02-01,Engineering,Software Engineer,Full-Time,,",
            "Alex,Brown,alex@test.com,,,,2026-03-01,Engineering,Software Engineer,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("employee limit");
    }

    [Fact]
    public async Task Import_ExceedsPlanLimit_WithUpToLimitFlag_ShouldImportAvailableSlots()
    {
        await SeedTenant(maxEmployees: 3);
        var (deptId, jtId, _) = await SeedReferenceData("Engineering", "Software Engineer");

        // Pre-seed 1 existing active employee
        using (var db = CreateDbContext())
        {
            db.Employees.Add(new Employee
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeNo = "EMP-0001",
                FirstName = "Existing",
                LastName = "Emp",
                Email = "existing@t.com",
                DateOfJoining = DateTime.UtcNow,
                DepartmentId = deptId,
                JobTitleId = jtId,
                EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active,
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        // 5 rows but only 2 slots available (3 max - 1 existing)
        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,",
            "Jane,Smith,jane@test.com,,,,2026-02-01,Engineering,Software Engineer,Full-Time,,",
            "Alex,Brown,alex@test.com,,,,2026-03-01,Engineering,Software Engineer,Full-Time,,",
            "Mike,Jones,mike@test.com,,,,2026-04-01,Engineering,Software Engineer,Full-Time,,",
            "Sara,Wilson,sara@test.com,,,,2026-05-01,Engineering,Software Engineer,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, importUpToLimit: true);

        result.IsSuccess.Should().BeTrue();
        // Should only import 2 (the available slots), not all 5
        result.Value!.Success.Should().Be(2);
        result.Value.Total.Should().Be(2); // Trimmed to 2
    }

    // ── Plan Limit Unlimited ────────────────────────────────────────

    [Fact]
    public async Task Import_UnlimitedPlan_ShouldImportAll()
    {
        await SeedTenant(maxEmployees: null); // unlimited
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,",
            "Jane,Smith,jane@test.com,,,,2026-02-01,Engineering,Software Engineer,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Success.Should().Be(2);
    }

    // ── Async Path (> 500 rows) ─────────────────────────────────────

    [Fact]
    public async Task Import_MoreThan500Rows_ShouldQueueAsyncJob()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        // Build CSV with 501 rows
        var sb = new StringBuilder();
        sb.AppendLine("first_name,last_name,email,phone,date_of_birth,gender,date_of_joining,department_name,job_title_name,employment_type,location_name,status");
        for (int i = 1; i <= 501; i++)
        {
            sb.AppendLine($"Emp{i},Last{i},emp{i}@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,");
        }
        var csv = sb.ToString();

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.JobId.Should().NotBeNull();
        result.Value.IsComplete.Should().BeFalse();
        result.Value.Warning.Should().Contain("queued");

        // Verify the job was created in the database
        using var db = CreateDbContext();
        var job = db.BulkImportJobs.FirstOrDefault(j => j.Id == result.Value.JobId!.Value);
        job.Should().NotBeNull();
        job!.Status.Should().Be(BulkImportStatus.Pending);
        job.TotalRows.Should().Be(501);
    }

    // ── Tenant Isolation ────────────────────────────────────────────

    [Fact]
    public async Task Import_ShouldScopeEmployeesToCurrentTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var ctxA = Substitute.For<ITenantContext>();
        ctxA.TenantId.Returns(tenantA);
        ctxA.IsResolved.Returns(true);
        ctxA.Subdomain.Returns("tenantA");

        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(tenantB);
        ctxB.IsResolved.Returns(true);
        ctxB.Subdomain.Returns("tenantB");

        // Seed tenant A data
        using (var db = TestDbContextFactory.Create(ctxA, _dbName))
        {
            db.Tenants.Add(new Tenant { Id = tenantA, Subdomain = "tenantA", Name = "A", Status = TenantStatus.Active });
            db.Departments.Add(new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantA, Name = "DeptA", Code = "DA", IsActive = true });
            db.JobTitles.Add(new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantA, TitleName = "TitleA", IsActive = true });
            await db.SaveChangesAsync();
        }

        // Seed tenant B data
        using (var db = TestDbContextFactory.Create(ctxB, _dbName))
        {
            db.Tenants.Add(new Tenant { Id = tenantB, Subdomain = "tenantB", Name = "B", Status = TenantStatus.Active });
            db.Departments.Add(new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantB, Name = "DeptB", Code = "DB", IsActive = true });
            db.JobTitles.Add(new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantB, TitleName = "TitleB", IsActive = true });
            await db.SaveChangesAsync();
        }

        // Import into tenant A
        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,DeptA,TitleA,Full-Time,,"
        );

        var dbCtxA = TestDbContextFactory.Create(ctxA, _dbName);
        var serviceA = new BulkEmployeeImportService(dbCtxA, ctxA, _currentUser, _logger);
        var result = await serviceA.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Success.Should().Be(1);

        // Verify the employee is in tenant A
        using var verifyA = TestDbContextFactory.Create(ctxA, _dbName);
        var empsA = verifyA.Employees.ToList();
        empsA.Should().HaveCount(1);
        empsA[0].TenantId.Should().Be(tenantA);

        // Verify tenant B sees no employees
        using var verifyB = TestDbContextFactory.Create(ctxB, _dbName);
        var empsB = verifyB.Employees.ToList();
        empsB.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_TenantNotResolved_ShouldFail()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var service = CreateService(ctx);

        var result = await service.ImportAsync(new MemoryStream(), "test.csv", 0, false);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // ── File Format Validation ──────────────────────────────────────

    [Fact]
    public async Task Import_UnsupportedFileFormat_ShouldReject()
    {
        var service = CreateService();
        var result = await service.ImportAsync(new MemoryStream(new byte[10]), "test.json", 10, false);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Unsupported file format");
    }

    [Fact]
    public async Task Import_ExceedsMaxFileSize_ShouldReject()
    {
        var service = CreateService();
        var result = await service.ImportAsync(new MemoryStream(), "test.csv", 30 * 1024 * 1024, false);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("25 MB");
    }

    [Fact]
    public async Task Import_EmptyFile_ShouldReject()
    {
        var service = CreateService();
        var csv = "first_name,last_name,email,phone,date_of_birth,gender,date_of_joining,department_name,job_title_name,employment_type,location_name,status\n";
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no data rows");
    }

    // ── Default Status ──────────────────────────────────────────────

    [Fact]
    public async Task Import_NoStatusSpecified_ShouldDefaultToActive()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var emp = db.Employees.First();
        emp.Status.Should().Be(EmployeeStatus.Active);
        emp.IsActive.Should().BeTrue();
    }

    // ── Invalid Employment Type ─────────────────────────────────────

    [Fact]
    public async Task Import_InvalidEmploymentType_ShouldFlagRow()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,,,2026-01-15,Engineering,Software Engineer,InvalidType,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e =>
            e.Field == "employment_type" && e.Error.Contains("Invalid employment type"));
    }

    // ── Job Status Query ────────────────────────────────────────────

    [Fact]
    public async Task GetJobStatus_ExistingJob_ShouldReturnStatus()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        // Create a job record manually
        using (var db = CreateDbContext())
        {
            db.BulkImportJobs.Add(new BulkImportJob
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TenantId = _tenantId,
                FileName = "test.csv",
                TotalRows = 10,
                SuccessCount = 8,
                FailedCount = 2,
                ProcessedRows = 10,
                Status = BulkImportStatus.Completed,
                InitiatedBy = "hr@test.com",
                CompletedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        var result = await service.GetJobStatusAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Completed");
        result.Value.TotalRows.Should().Be(10);
        result.Value.SuccessCount.Should().Be(8);
        result.Value.FailedCount.Should().Be(2);
        result.Value.ProgressPercent.Should().Be(100);
    }

    [Fact]
    public async Task GetJobStatus_NonExistentJob_ShouldReturn404()
    {
        var service = CreateService();
        var result = await service.GetJobStatusAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Error Report Download ───────────────────────────────────────

    [Fact]
    public async Task GetErrorReport_WithErrors_ShouldReturnCsv()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var errors = new[]
        {
            new BulkImportRowError { RowNumber = 3, Field = "email", Error = "Invalid email" },
            new BulkImportRowError { RowNumber = 5, Field = "department_name", Error = "Not found" },
        };

        using (var db = CreateDbContext())
        {
            db.BulkImportJobs.Add(new BulkImportJob
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TenantId = _tenantId,
                FileName = "test.csv",
                TotalRows = 10,
                FailedCount = 2,
                Status = BulkImportStatus.Completed,
                ErrorDetails = System.Text.Json.JsonSerializer.Serialize(errors),
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService();
        var result = await service.GetErrorReportAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("text/csv");

        var csvText = Encoding.UTF8.GetString(result.Value.FileBytes);
        csvText.Should().Contain("row_number,field,error");
        csvText.Should().Contain("3,email,Invalid email");
        csvText.Should().Contain("5,department_name,Not found");
    }

    // ── CSV Parsing Edge Cases ──────────────────────────────────────

    [Fact]
    public void ParseCsvRows_WithCommentLines_ShouldSkipThem()
    {
        var csv = "# This is a comment\nfirst_name,last_name,email,phone,date_of_birth,gender,date_of_joining,department_name,job_title_name,employment_type,location_name,status\nJohn,Doe,john@test.com,,,,2026-01-15,Eng,SE,Full-Time,,\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var rows = BulkEmployeeImportService.ParseCsvRows(stream);
        rows.Should().HaveCount(1);
        rows[0].FirstName.Should().Be("John");
    }

    [Fact]
    public void ParseExcelRows_WithDescriptionRow_ShouldSkipIt()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Test");

        // Row 1: description
        ws.Cell(1, 1).Value = "Required field description";

        // Row 2: headers
        ws.Cell(2, 1).Value = "first_name";
        ws.Cell(2, 2).Value = "last_name";
        ws.Cell(2, 3).Value = "email";
        ws.Cell(2, 7).Value = "date_of_joining";
        ws.Cell(2, 8).Value = "department_name";
        ws.Cell(2, 9).Value = "job_title_name";
        ws.Cell(2, 10).Value = "employment_type";

        // Row 3: data
        ws.Cell(3, 1).Value = "Jane";
        ws.Cell(3, 2).Value = "Smith";
        ws.Cell(3, 3).Value = "jane@t.com";
        ws.Cell(3, 7).Value = "2026-01-15";
        ws.Cell(3, 8).Value = "Eng";
        ws.Cell(3, 9).Value = "SE";
        ws.Cell(3, 10).Value = "Full-Time";

        var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        var rows = BulkEmployeeImportService.ParseExcelRows(ms);
        rows.Should().HaveCount(1);
        rows[0].FirstName.Should().Be("Jane");
    }

    // ── Validation edge cases ───────────────────────────────────────

    [Fact]
    public async Task Import_MissingRequiredFields_ShouldFlagAllErrors()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        // Row with ALL required fields missing
        var csv = BuildCsvContent(
            ",,,,,,,,,,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Failed.Should().Be(1);

        // Should have errors for first_name, last_name, email, date_of_joining,
        // department_name, job_title_name, employment_type
        var fields = result.Value.Errors.Select(e => e.Field).Distinct().ToList();
        fields.Should().Contain("first_name");
        fields.Should().Contain("last_name");
        fields.Should().Contain("email");
        fields.Should().Contain("date_of_joining");
        fields.Should().Contain("department_name");
        fields.Should().Contain("job_title_name");
        fields.Should().Contain("employment_type");
    }

    [Fact]
    public async Task Import_InvalidGender_ShouldFlagRow()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,1990-05-15,InvalidGender,2026-01-15,Engineering,Software Engineer,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e => e.Field == "gender");
    }

    [Fact]
    public async Task Import_FutureDateOfBirth_ShouldFlagRow()
    {
        await SeedTenant();
        await SeedReferenceData("Engineering", "Software Engineer");

        var csv = BuildCsvContent(
            "John,Doe,john@test.com,,2099-01-01,,2026-01-15,Engineering,Software Engineer,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e => e.Field == "date_of_birth" && e.Error.Contains("past"));
    }

    // ── Employee number generation ──────────────────────────────────

    [Fact]
    public async Task Import_ShouldContinueEmployeeNumberSequence()
    {
        await SeedTenant();
        var (deptId, jtId, _) = await SeedReferenceData("Engineering", "Software Engineer");

        // Pre-seed employee with EMP-0005
        using (var db = CreateDbContext())
        {
            db.Employees.Add(new Employee
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantId,
                EmployeeNo = "EMP-0005",
                FirstName = "Existing",
                LastName = "Emp",
                Email = "existing@t.com",
                DateOfJoining = DateTime.UtcNow,
                DepartmentId = deptId,
                JobTitleId = jtId,
                EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active,
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var csv = BuildCsvContent(
            "New,User,new@test.com,,,,2026-01-15,Engineering,Software Engineer,Full-Time,,"
        );

        var service = CreateService();
        var result = await service.ImportAsync(CreateCsvStream(csv), "test.csv", csv.Length, false);

        result.IsSuccess.Should().BeTrue();

        using var db2 = CreateDbContext();
        var newEmp = db2.Employees.FirstOrDefault(e => e.Email == "new@test.com");
        newEmp.Should().NotBeNull();
        newEmp!.EmployeeNo.Should().Be("EMP-0006");
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes
    }
}
