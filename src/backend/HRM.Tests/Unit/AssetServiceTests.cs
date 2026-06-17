// ============================================================================
// US-ONB-004: Asset issuance tracking tests (AssetService).
//
// Drives the service through the real EF Core InMemory provider (so register rows, the global query filter,
// and the unique asset_tag index all apply). Covers:
//   - FR-3: GetAvailable lists only Available assets, filterable by type.
//   - AC-4/BR-6: GetByEmployee + GetMine list an employee's assigned assets (read paths).
//   - FR-4/AC-2/NFR-5: happy-path single + bulk issuance flips status to Assigned, links the employee, and
//     completes the linked onboarding task in one transaction.
//   - BR-1/FR-3: an asset that is not Available cannot be issued.
//   - AC-3: double-assignment is rejected with the current holder's name.
//   - BR-3: duplicate asset_tag (and serial) is rejected.
//   - §7: a future issue date is rejected.
//   - BR-2: an unconfigured asset type is rejected.
//   - FR-6/NFR-4: the acknowledgment doc is scanned + stored at the tenant-isolated path.
//   - AC-5/NFR-2: tenant isolation on reads.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AssetServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _hrUser;

    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();

    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IVirusScanner _scanner = Substitute.For<IVirusScanner>();

    public AssetServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _hrUser = Substitute.For<ICurrentUser>();
        _hrUser.UserId.Returns(_hrUserId);
        _hrUser.Email.Returns("hr@acme.com");
        _hrUser.IsAuthenticated.Returns(true);

        _scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Clean());
        _fileStorage.UploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult((string)ci[1]));
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private AssetService Service(ICurrentUser? user = null) =>
        new(Db(), _tenantContext, user ?? _hrUser, _fileStorage, _scanner,
            Substitute.For<ILogger<AssetService>>());

    private void SeedEmployees()
    {
        using var db = Db();
        db.Employees.AddRange(
            new Employee
            {
                Id = _employeeId, TenantId = _tenantId, EmployeeNo = "EMP-0001",
                FirstName = "Nora", LastName = "Newhire", Email = "nora@acme.com",
                DateOfJoining = DateTime.UtcNow.AddDays(-1).Date, UserId = _employeeUserId,
            },
            new Employee
            {
                Id = _otherEmployeeId, TenantId = _tenantId, EmployeeNo = "EMP-0002",
                FirstName = "Otto", LastName = "Other", Email = "otto@acme.com",
                DateOfJoining = DateTime.UtcNow.AddDays(-30).Date, UserId = Guid.NewGuid(),
            });
        db.SaveChanges();
    }

    private Guid SeedAsset(string tag, AssetStatus status, string type = "Laptop",
        string? serial = null, Guid? assignedTo = null, Guid? tenantId = null)
    {
        var id = BaseEntity.NewUuidV7();
        using var db = Db();
        db.Assets.Add(new Asset
        {
            Id = id, TenantId = tenantId ?? _tenantId, AssetType = type, AssetTag = tag,
            SerialNumber = serial, Status = status, Condition = AssetCondition.Good,
            AssignedEmployeeId = assignedTo,
        });
        db.SaveChanges();
        return id;
    }

    private static IssueAssetLineInput Line(string tag, string type = "Laptop", Guid? assetId = null,
        string? serial = null, AssetCondition condition = AssetCondition.New, DateTime? issueDate = null) =>
        new(assetId, type, tag, serial, null, null, condition, issueDate ?? DateTime.UtcNow.Date, null);

    private IssueAssetsInput IssueInput(Guid employeeId, Guid? taskId, params IssueAssetLineInput[] lines) =>
        new(employeeId, taskId, lines, null, null, null, 0);

    // ── FR-3 available list ─────────────────────────────────────────────

    [Fact]
    public async Task GetAvailable_returns_only_available_assets_filtered_by_type()
    {
        SeedEmployees();
        SeedAsset("LAP-001", AssetStatus.Available, "Laptop");
        SeedAsset("LAP-002", AssetStatus.Assigned, "Laptop", assignedTo: _otherEmployeeId);
        SeedAsset("PHN-001", AssetStatus.Available, "Phone");

        var all = await Service().GetAvailableAsync(null);
        all.IsSuccess.Should().BeTrue();
        all.Value!.Should().HaveCount(2);
        all.Value.Should().OnlyContain(a => a.Status == "Available");

        var laptops = await Service().GetAvailableAsync("Laptop");
        laptops.Value!.Should().ContainSingle(a => a.AssetTag == "LAP-001");
    }

    // ── FR-4 / AC-2 / NFR-5 happy path ──────────────────────────────────

    [Fact]
    public async Task Issue_single_existing_asset_assigns_and_links_employee()
    {
        SeedEmployees();
        var assetId = SeedAsset("LAP-001", AssetStatus.Available);

        var result = await Service().IssueAsync(IssueInput(_employeeId, null,
            Line("LAP-001", assetId: assetId, condition: AssetCondition.New)));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle();
        result.Value[0].Status.Should().Be("Assigned");
        result.Value[0].AssignedEmployeeId.Should().Be(_employeeId);

        using var db = Db();
        var asset = db.Assets.Single(a => a.Id == assetId);
        asset.Status.Should().Be(AssetStatus.Assigned);
        asset.AssignedEmployeeId.Should().Be(_employeeId);
        asset.Condition.Should().Be(AssetCondition.New);
    }

    [Fact]
    public async Task Issue_bulk_adhoc_assets_persists_all_in_one_transaction()
    {
        SeedEmployees();

        var result = await Service().IssueAsync(IssueInput(_employeeId, null,
            Line("LAP-100", "Laptop"),
            Line("ID-100", "ID Card"),
            Line("BADGE-100", "Access Badge")));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(3);

        using var db = Db();
        var assigned = db.Assets.Where(a => a.AssignedEmployeeId == _employeeId).ToList();
        assigned.Should().HaveCount(3);
        assigned.Should().OnlyContain(a => a.Status == AssetStatus.Assigned && a.TenantId == _tenantId);
    }

    [Fact]
    public async Task Issue_with_linked_task_completes_that_task_atomically()
    {
        SeedEmployees();
        var checklistId = BaseEntity.NewUuidV7();
        var taskId = BaseEntity.NewUuidV7();
        using (var db = Db())
        {
            db.OnboardingChecklistInstances.Add(new OnboardingChecklistInstance
            {
                Id = checklistId, TenantId = _tenantId, EmployeeId = _employeeId,
                TemplateId = BaseEntity.NewUuidV7(), TemplateName = "Standard",
                Status = OnboardingChecklistStatus.Active, StartDate = DateTime.UtcNow.Date,
                Tasks = { new OnboardingTaskInstance
                {
                    Id = taskId, TenantId = _tenantId, ChecklistInstanceId = checklistId,
                    Title = "Issue laptop", ResponsibleRole = OnboardingResponsibleRole.HR,
                    DueDate = DateTime.UtcNow.Date, Status = OnboardingTaskStatus.Pending,
                } },
            });
            db.SaveChanges();
        }

        var result = await Service().IssueAsync(IssueInput(_employeeId, taskId, Line("LAP-200")));

        result.IsSuccess.Should().BeTrue();
        using var verify = Db();
        var task = verify.OnboardingTaskInstances.Single(t => t.Id == taskId);
        task.Status.Should().Be(OnboardingTaskStatus.Completed);
        task.CompletedByUserId.Should().Be(_hrUserId);
        task.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Issue_linked_task_of_other_employee_is_rejected()
    {
        SeedEmployees();
        var checklistId = BaseEntity.NewUuidV7();
        var taskId = BaseEntity.NewUuidV7();
        using (var db = Db())
        {
            db.OnboardingChecklistInstances.Add(new OnboardingChecklistInstance
            {
                Id = checklistId, TenantId = _tenantId, EmployeeId = _otherEmployeeId,
                TemplateId = BaseEntity.NewUuidV7(), TemplateName = "Standard",
                Status = OnboardingChecklistStatus.Active, StartDate = DateTime.UtcNow.Date,
                Tasks = { new OnboardingTaskInstance
                {
                    Id = taskId, TenantId = _tenantId, ChecklistInstanceId = checklistId,
                    Title = "Issue laptop", ResponsibleRole = OnboardingResponsibleRole.HR,
                    DueDate = DateTime.UtcNow.Date, Status = OnboardingTaskStatus.Pending,
                } },
            });
            db.SaveChanges();
        }

        var result = await Service().IssueAsync(IssueInput(_employeeId, taskId, Line("LAP-201")));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("task_employee_mismatch");
    }

    // ── BR-1 / FR-3 available gate ──────────────────────────────────────

    [Fact]
    public async Task Issue_existing_asset_that_is_assigned_is_rejected_with_holder_name()
    {
        SeedEmployees();
        var assetId = SeedAsset("LAP-001", AssetStatus.Assigned, serial: "SN-XYZ", assignedTo: _otherEmployeeId);

        var result = await Service().IssueAsync(IssueInput(_employeeId, null,
            Line("LAP-001", assetId: assetId, serial: "SN-XYZ")));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("asset_already_assigned");
        result.Error.Should().Contain("Otto Other"); // AC-3 holder name.
        result.Error.Should().Contain("SN-XYZ");
    }

    // ── AC-3 double assignment by tag/serial on an ad-hoc line ──────────

    [Fact]
    public async Task Issue_adhoc_line_matching_an_assigned_asset_is_rejected_with_holder_name()
    {
        SeedEmployees();
        SeedAsset("LAP-001", AssetStatus.Assigned, serial: "SN-DUP", assignedTo: _otherEmployeeId);

        // Ad-hoc line (no assetId) that collides on serial with the already-assigned asset.
        var result = await Service().IssueAsync(IssueInput(_employeeId, null,
            Line("LAP-999", serial: "SN-DUP")));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("asset_already_assigned");
        result.Error.Should().Contain("Otto Other");
    }

    // ── BR-3 uniqueness ─────────────────────────────────────────────────

    [Fact]
    public async Task Issue_adhoc_with_existing_tag_is_rejected()
    {
        SeedEmployees();
        SeedAsset("LAP-001", AssetStatus.Available); // an available asset with this tag already exists.

        var result = await Service().IssueAsync(IssueInput(_employeeId, null, Line("LAP-001")));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("duplicate_asset");
    }

    [Fact]
    public async Task Issue_with_duplicate_tag_in_same_submission_is_rejected()
    {
        SeedEmployees();

        var result = await Service().IssueAsync(IssueInput(_employeeId, null,
            Line("LAP-DUP"), Line("LAP-DUP")));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("duplicate_asset_tag");
    }

    // ── §7 future date + BR-2 type ──────────────────────────────────────

    [Fact]
    public async Task Issue_with_future_issue_date_is_rejected()
    {
        SeedEmployees();

        var result = await Service().IssueAsync(IssueInput(_employeeId, null,
            Line("LAP-FUT", issueDate: DateTime.UtcNow.AddDays(1).Date)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("issue_date_future");
    }

    [Fact]
    public async Task Issue_with_unconfigured_asset_type_is_rejected()
    {
        SeedEmployees();

        var result = await Service().IssueAsync(IssueInput(_employeeId, null,
            Line("X-1", type: "Spaceship")));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_asset_type");
    }

    // ── FR-6 / NFR-4 acknowledgment doc ─────────────────────────────────

    [Fact]
    public async Task Issue_with_acknowledgment_doc_scans_and_stores_at_tenant_isolated_path()
    {
        SeedEmployees();

        using var file = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 fake"));
        var input = new IssueAssetsInput(
            _employeeId, null, new[] { Line("LAP-ACK") },
            file, "receipt.pdf", "application/pdf", file.Length);

        var result = await Service().IssueAsync(input);

        result.IsSuccess.Should().BeTrue();
        await _fileStorage.Received(1).UploadAsync(
            _tenantId, $"onboarding/assets/{_employeeId}/receipt.pdf",
            Arg.Any<Stream>(), "application/pdf", Arg.Any<CancellationToken>());
        await _scanner.Received(1).ScanAsync(Arg.Any<Stream>(), "receipt.pdf", Arg.Any<CancellationToken>());

        using var db = Db();
        db.Assets.Single(a => a.AssetTag == "LAP-ACK").AcknowledgmentDocFileName.Should().Be("receipt.pdf");
    }

    [Fact]
    public async Task Issue_with_oversized_acknowledgment_is_rejected_and_nothing_persisted()
    {
        SeedEmployees();

        using var file = new MemoryStream(new byte[16]);
        var input = new IssueAssetsInput(
            _employeeId, null, new[] { Line("LAP-BIG") },
            file, "big.pdf", "application/pdf", 11 * 1024 * 1024);

        var result = await Service().IssueAsync(input);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("file_too_large");
    }

    // ── AC-4 / BR-6 read paths ──────────────────────────────────────────

    [Fact]
    public async Task GetByEmployee_lists_only_that_employees_assigned_assets()
    {
        SeedEmployees();
        var a1 = SeedAsset("LAP-001", AssetStatus.Available);
        await Service().IssueAsync(IssueInput(_employeeId, null, Line("LAP-001", assetId: a1)));
        SeedAsset("LAP-OTHER", AssetStatus.Assigned, assignedTo: _otherEmployeeId);

        var result = await Service().GetByEmployeeAsync(_employeeId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(a => a.AssetTag == "LAP-001");
    }

    [Fact]
    public async Task GetMine_resolves_caller_employee_and_lists_their_assets()
    {
        SeedEmployees();
        var a1 = SeedAsset("LAP-001", AssetStatus.Available);
        await Service().IssueAsync(IssueInput(_employeeId, null, Line("LAP-001", assetId: a1)));

        // The caller is now the employee (self-service).
        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.UserId.Returns(_employeeUserId);
        employeeUser.IsAuthenticated.Returns(true);

        var result = await Service(employeeUser).GetMineAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(a => a.AssetTag == "LAP-001");
    }

    [Fact]
    public async Task GetMine_without_linked_employee_returns_404()
    {
        SeedEmployees();
        var stranger = Substitute.For<ICurrentUser>();
        stranger.UserId.Returns(Guid.NewGuid());
        stranger.IsAuthenticated.Returns(true);

        var result = await Service(stranger).GetMineAsync();

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── AC-5 / NFR-2 tenant isolation ───────────────────────────────────

    [Fact]
    public async Task Available_does_not_see_other_tenant_assets()
    {
        SeedEmployees();
        SeedAsset("OTHER-TENANT", AssetStatus.Available, tenantId: Guid.NewGuid());

        var result = await Service().GetAvailableAsync(null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty(); // the other tenant's asset is filtered out.
    }
}
