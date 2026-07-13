// ============================================================================
// US-PAY-005: Employee self-service payslip read — integration tests.
//
// Exercises the full handler -> MyPayslipService -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters + ICurrentUser), focused on the story's crux —
// AUTHORIZATION:
//   - AC-4 (the critical AC): Employee A requesting Employee B's payslip id -> 403 Forbidden (NOT 404), and
//     never another employee's data (BR-5).
//   - NFR-4: a Tenant A employee sees NO Tenant B payslips (cross-tenant isolation via the global filter).
//   - FR-2/BR-1: the list + detail exclude payslips from non-Finalized runs.
//   - AC-2/FR-3: the detail breakdown matches the payroll_slip_detail records (earnings vs deductions split).
//   - AC-1/FR-6: list is most-recent-first + paginated; the year filter scopes correctly.
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the other Payroll integration
// tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker). A fake in-memory IFileStorage stands
// in for blob storage.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class MyPayslipIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly InMemoryFileStorage _storage = new();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    /// <summary>Minimal ICurrentUser — only UserId is consulted by MyPayslipService (self resolution).</summary>
    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; set; }
        public string Email => "user@t.com";
        public Guid TenantId { get; set; }
        public Guid UserTenantId => Guid.NewGuid();
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Dictionary<(Guid, string), byte[]> Files { get; } = new();

        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Files[(tenantId, relativePath)] = ms.ToArray();
            return Task.FromResult($"/{tenantId}/{relativePath}");
        }

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
            => Task.FromResult<Stream?>(Files.TryGetValue((tenantId, relativePath), out var bytes)
                ? new MemoryStream(bytes, writable: false) : null);

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) => $"/{tenantId}/{relativePath}";
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
        {
            Files.Remove((tenantId, relativePath));
            return Task.CompletedTask;
        }
    }

    /// <summary>A DI provider scoped to a tenant + current user (the authenticated employee's user id).</summary>
    private IServiceProvider Provider(Guid tenantId, Guid currentUserId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new MutableTenantContext { TenantId = tenantId });
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { UserId = currentUserId, TenantId = tenantId });
        services.AddSingleton<IFileStorage>(_storage);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IMyPayslipService, MyPayslipService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GetMyPayslipsQuery).Assembly));
        return services.BuildServiceProvider();
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    /// <summary>
    /// Seeds an employee (linked to a user) plus one payslip (+ details) inside a run of the given status.
    /// When <paramref name="withPdf"/>, also marks the slip Generated and stores PDF bytes so the download path
    /// can be exercised. Returns the user id, employee id, and payslip id.
    /// </summary>
    private async Task<(Guid UserId, Guid EmployeeId, Guid PayslipId)> SeedEmployeeWithSlip(
        Guid tenantId, string employeeNo, int month = 5, int year = 2026,
        PayrollRunStatus status = PayrollRunStatus.Finalized, bool withPdf = false,
        string? departmentSnapshot = null, string? designationSnapshot = null)
    {
        using var db = Db(tenantId);

        var deptId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Engineering", Code = $"ENG-{employeeNo}", IsActive = true });
        var jobId = BaseEntity.NewUuidV7();
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = tenantId, TitleName = "Senior Engineer", IsActive = true });

        var userId = Guid.NewGuid();
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, UserId = userId, EmployeeNo = employeeNo, FirstName = "Jane", LastName = "Doe",
            Email = $"{employeeNo}@t.com", DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = deptId, JobTitleId = jobId,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = month, PayYear = year, Status = status,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = 1, ProcessedEmployees = 1,
        });

        var slipId = BaseEntity.NewUuidV7();
        var relPath = $"payroll/{runId:D}/{empId:D}.pdf";
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = slipId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
            GrossEarnings = 70_000m, TotalDeductions = 10_000m, NetSalary = 60_000m,
            WorkingDays = 22m, PaidDays = 22m, LopDays = 0m, PayMonth = month, PayYear = year,
            PdfStatus = withPdf ? PayslipPdfStatus.Generated : null,
            PdfStoragePath = withPdf ? relPath : null,
            PdfGeneratedAt = withPdf ? DateTime.UtcNow : null,
            PdfFileSizeBytes = withPdf ? 1024 : null,
            DepartmentSnapshot = departmentSnapshot,   // ISSUE-165: null → legacy slip (read falls back to live).
            DesignationSnapshot = designationSnapshot,
        });
        db.PayrollSlipDetails.AddRange(
            new PayrollSlipDetail { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(), ComponentName = "Basic", ComponentType = nameof(SalaryComponentType.Earning), Amount = 50_000m },
            new PayrollSlipDetail { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(), ComponentName = "HRA", ComponentType = nameof(SalaryComponentType.Earning), Amount = 20_000m },
            new PayrollSlipDetail { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(), ComponentName = "PF", ComponentType = nameof(SalaryComponentType.Statutory), Amount = 6_000m },
            new PayrollSlipDetail { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(), ComponentName = "Tax", ComponentType = nameof(SalaryComponentType.Deduction), Amount = 4_000m });

        await db.SaveChangesAsync();

        if (withPdf)
            _storage.Files[(tenantId, relPath)] = "%PDF-1.7 fake"u8.ToArray();

        return (userId, empId, slipId);
    }

    // ── AC-4 (critical): Employee A requesting Employee B's payslip id → 403, not 404 ──────────────

    [Fact]
    public async Task Detail_OtherEmployeesPayslip_Returns403_NotFound()
    {
        var (userA, _, _) = await SeedEmployeeWithSlip(_tenantA, "EMP-A");
        var (_, _, slipB) = await SeedEmployeeWithSlip(_tenantA, "EMP-B"); // same tenant, different employee.

        var mediator = Provider(_tenantA, userA).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipDetailQuery(slipB));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("forbidden_payslip");
    }

    [Fact]
    public async Task DownloadPdf_OtherEmployeesPayslip_Returns403()
    {
        var (userA, _, _) = await SeedEmployeeWithSlip(_tenantA, "EMP-A", withPdf: true);
        var (_, _, slipB) = await SeedEmployeeWithSlip(_tenantA, "EMP-B", withPdf: true);

        var mediator = Provider(_tenantA, userA).GetRequiredService<IMediator>();
        var result = await mediator.Send(new DownloadMyPayslipQuery(slipB));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    // ── NFR-4: cross-tenant isolation — Tenant A employee sees no Tenant B payslips ────────────────

    [Fact]
    public async Task List_DoesNotIncludeOtherTenantsPayslips()
    {
        var (userA, _, _) = await SeedEmployeeWithSlip(_tenantA, "EMP-A");
        await SeedEmployeeWithSlip(_tenantB, "EMP-B"); // a payslip in another tenant.

        var mediator = Provider(_tenantA, userA).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipsQuery(null, 1, 12));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Detail_CrossTenantPayslipId_Returns404()
    {
        var (userA, _, _) = await SeedEmployeeWithSlip(_tenantA, "EMP-A");
        var (_, _, slipB) = await SeedEmployeeWithSlip(_tenantB, "EMP-B"); // tenant B's slip.

        // Tenant A employee tries B's slip id — the global filter hides it entirely (404, not even 403).
        var mediator = Provider(_tenantA, userA).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipDetailQuery(slipB));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // ── FR-2/BR-1: only Finalized-run payslips are visible ─────────────────────────────────────────

    [Fact]
    public async Task List_ExcludesNonFinalizedRuns()
    {
        // Same employee/user with three slips across three runs: Finalized, Approved, ReviewPending.
        var (userId, empId, _) = await SeedEmployeeWithSlip(_tenantA, "EMP-A", month: 5, status: PayrollRunStatus.Finalized);
        await SeedExtraSlipForEmployee(_tenantA, empId, month: 4, status: PayrollRunStatus.Approved);
        await SeedExtraSlipForEmployee(_tenantA, empId, month: 3, status: PayrollRunStatus.ReviewPending);

        var mediator = Provider(_tenantA, userId).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipsQuery(null, 1, 12));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.Items[0].PayMonth.Should().Be(5); // only the Finalized one.
    }

    [Fact]
    public async Task Detail_NonFinalizedRun_Returns403()
    {
        var (userId, _, slipId) = await SeedEmployeeWithSlip(_tenantA, "EMP-A", status: PayrollRunStatus.ReviewPending);

        var mediator = Provider(_tenantA, userId).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipDetailQuery(slipId));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("payslip_not_finalized");
    }

    // ── AC-2/FR-3: detail breakdown matches payroll_slip_detail records ─────────────────────────────

    [Fact]
    public async Task Detail_ReturnsBreakdown_MatchingSlipDetails()
    {
        var (userId, _, slipId) = await SeedEmployeeWithSlip(_tenantA, "EMP-A");

        var mediator = Provider(_tenantA, userId).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipDetailQuery(slipId));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.PayslipId.Should().Be(slipId);
        dto.Employee.EmployeeNo.Should().Be("EMP-A");
        dto.Employee.Name.Should().Be("Jane Doe");
        dto.Employee.Department.Should().Be("Engineering");
        dto.Employee.Designation.Should().Be("Senior Engineer");

        // Earning components = Basic + HRA; Statutory + Deduction land on the deductions side.
        dto.Earnings.Select(e => e.ComponentName).Should().BeEquivalentTo(new[] { "Basic", "HRA" });
        dto.Deductions.Select(d => d.ComponentName).Should().BeEquivalentTo(new[] { "PF", "Tax" });
        dto.Earnings.Single(e => e.ComponentName == "Basic").Amount.Should().Be(50_000m);
        dto.Deductions.Single(d => d.ComponentName == "PF").Amount.Should().Be(6_000m);

        // YTD is off by default (no tenant config surface) — every component's ytdAmount is null.
        dto.Earnings.Should().OnlyContain(e => e.YtdAmount == null);
        dto.Deductions.Should().OnlyContain(d => d.YtdAmount == null);

        dto.GrossEarnings.Should().Be(70_000m);
        dto.NetSalary.Should().Be(60_000m);
        dto.PaidDays.Should().Be(22m);
    }

    // ── ISSUE-165: point-in-time department/designation snapshot on the DETAIL read ────────────────

    /// <summary>
    /// The crux of ISSUE-165: a historical payslip must show the dept/designation as they were AT GENERATION,
    /// not the employee's CURRENT ones. We seed a slip carrying the snapshot, then MOVE the employee to a
    /// different department + job title, and assert the detail read still shows the OLD values. This would FAIL
    /// on pre-fix code, which resolved dept/designation from the live Employee row.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-165")]
    public async Task Detail_AfterEmployeeMovesDeptAndTitle_ShowsPointInTimeSnapshot_NotLive()
    {
        var (userId, empId, slipId) = await SeedEmployeeWithSlip(
            _tenantA, "EMP-165", departmentSnapshot: "Engineering", designationSnapshot: "Engineer");

        // Move the employee to a DIFFERENT department + designation AFTER the slip was generated.
        using (var db = Db(_tenantA))
        {
            var newDeptId = BaseEntity.NewUuidV7();
            db.Departments.Add(new Department { Id = newDeptId, TenantId = _tenantA, Name = "Finance", Code = "FIN-165", IsActive = true });
            var newTitleId = BaseEntity.NewUuidV7();
            db.JobTitles.Add(new JobTitle { Id = newTitleId, TenantId = _tenantA, TitleName = "Manager", IsActive = true });

            var emp = await db.Employees.FirstAsync(e => e.Id == empId);
            emp.DepartmentId = newDeptId;
            emp.JobTitleId = newTitleId;
            await db.SaveChangesAsync();
        }

        var mediator = Provider(_tenantA, userId).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipDetailQuery(slipId));

        result.IsSuccess.Should().BeTrue();
        // Still the OLD, generation-time values — NOT the current "Finance"/"Manager".
        result.Value!.Employee.Department.Should().Be("Engineering");
        result.Value.Employee.Designation.Should().Be("Engineer");
    }

    /// <summary>
    /// Legacy fallback (ISSUE-165): a slip generated before the snapshot existed has null snapshot columns. The
    /// detail read must fall back to LIVE resolution — never blank/em-dash — so historical slips keep working.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-165")]
    public async Task Detail_NullSnapshot_FallsBackToLiveDepartmentAndDesignation()
    {
        // No snapshot passed → DepartmentSnapshot/DesignationSnapshot are null (a legacy slip).
        var (userId, _, slipId) = await SeedEmployeeWithSlip(_tenantA, "EMP-LEG");

        var mediator = Provider(_tenantA, userId).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipDetailQuery(slipId));

        result.IsSuccess.Should().BeTrue();
        // Falls back to the live employee's dept/designation (the seed's "Engineering"/"Senior Engineer").
        result.Value!.Employee.Department.Should().Be("Engineering");
        result.Value.Employee.Designation.Should().Be("Senior Engineer");
    }

    // ── AC-1/FR-6: list is most-recent-first, paginated, year-filterable ───────────────────────────

    [Fact]
    public async Task List_IsMostRecentFirst_AndPaginates()
    {
        var (userId, empId, _) = await SeedEmployeeWithSlip(_tenantA, "EMP-A", month: 1, year: 2026);
        await SeedExtraSlipForEmployee(_tenantA, empId, month: 2, year: 2026);
        await SeedExtraSlipForEmployee(_tenantA, empId, month: 12, year: 2025);

        var mediator = Provider(_tenantA, userId).GetRequiredService<IMediator>();

        var page1 = await mediator.Send(new GetMyPayslipsQuery(null, 1, 2));
        page1.Value!.TotalCount.Should().Be(3);
        page1.Value.Items.Should().HaveCount(2);
        // Most recent first: 2026-02, then 2026-01.
        page1.Value.Items[0].PayYear.Should().Be(2026);
        page1.Value.Items[0].PayMonth.Should().Be(2);
        page1.Value.Items[1].PayMonth.Should().Be(1);

        var page2 = await mediator.Send(new GetMyPayslipsQuery(null, 2, 2));
        page2.Value!.Items.Should().ContainSingle();
        page2.Value.Items[0].PayYear.Should().Be(2025);
        page2.Value.Items[0].PayMonth.Should().Be(12);
    }

    [Fact]
    public async Task List_YearFilter_ScopesToYear()
    {
        var (userId, empId, _) = await SeedEmployeeWithSlip(_tenantA, "EMP-A", month: 1, year: 2026);
        await SeedExtraSlipForEmployee(_tenantA, empId, month: 12, year: 2025);

        var mediator = Provider(_tenantA, userId).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipsQuery(2025, 1, 12));

        result.Value!.Items.Should().ContainSingle();
        result.Value.Items[0].PayYear.Should().Be(2025);
    }

    // ── AC-3/FR-4: own PDF download streams the stored file ────────────────────────────────────────

    [Fact]
    public async Task DownloadPdf_OwnPayslip_StreamsPdf()
    {
        var (userId, _, slipId) = await SeedEmployeeWithSlip(_tenantA, "EMP-7", withPdf: true);

        var mediator = Provider(_tenantA, userId).GetRequiredService<IMediator>();
        var result = await mediator.Send(new DownloadMyPayslipQuery(slipId));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("application/pdf");
        result.Value.FileName.Should().Be("EMP-7_5_2026.pdf");
        result.Value.Content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DownloadPdf_NotYetGenerated_Returns404()
    {
        var (userId, _, slipId) = await SeedEmployeeWithSlip(_tenantA, "EMP-A", withPdf: false);

        var mediator = Provider(_tenantA, userId).GetRequiredService<IMediator>();
        var result = await mediator.Send(new DownloadMyPayslipQuery(slipId));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("pdf_not_generated");
    }

    // ── no employee linked to the user → 403 (BR-5, never leaks anyone else's data) ────────────────

    [Fact]
    public async Task List_NoEmployeeLinkedToUser_Returns403()
    {
        await SeedEmployeeWithSlip(_tenantA, "EMP-A"); // an employee exists, but not linked to this user.

        var mediator = Provider(_tenantA, Guid.NewGuid()).GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetMyPayslipsQuery(null, 1, 12));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("no_employee_linked");
    }

    /// <summary>Adds another payslip (in its own run of the given status) for an existing employee.</summary>
    private async Task SeedExtraSlipForEmployee(
        Guid tenantId, Guid empId, int month, int year = 2026, PayrollRunStatus status = PayrollRunStatus.Finalized)
    {
        using var db = Db(tenantId);

        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = month, PayYear = year, Status = status,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = 1, ProcessedEmployees = 1,
        });

        var slipId = BaseEntity.NewUuidV7();
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = slipId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
            GrossEarnings = 70_000m, TotalDeductions = 10_000m, NetSalary = 60_000m,
            WorkingDays = 22m, PaidDays = 22m, LopDays = 0m, PayMonth = month, PayYear = year,
        });
        db.PayrollSlipDetails.Add(new PayrollSlipDetail
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(),
            ComponentName = "Basic", ComponentType = nameof(SalaryComponentType.Earning), Amount = 50_000m,
        });

        await db.SaveChangesAsync();
    }
}
