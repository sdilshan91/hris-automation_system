// ============================================================================
// ISSUE-036 (US-LV-003 FR-5 / NFR-3) — REAL leave supporting-document upload,
// and the create-leave gate that now counts uploaded ROWS instead of strings.
//
// THE DEFECT UNDER TEST: LeaveRequestService.CreateAsync used to satisfy a
// DocumentsRequired leave type from `request.Attachments` — any non-blank string
// whose suffix was in an extension allow-list. The literal "x.pdf" therefore
// passed a medical-certificate requirement and no bytes were ever uploaded or
// stored anywhere. Every arm below fails without the fix.
//
// HARNESS: clones SelfAssessmentAttachmentIntegrationTests — the EF Core InMemory
// provider driven through the REAL composed pipeline (MediatR handlers + the
// ITenantContext-backed global query filters). Two collaborators are controllable:
//   • WriteSpyFileStorage — an in-memory IFileStorage that COUNTS UploadAsync calls.
//     The count is the one permitted "mock-call-count", and only to prove the
//     validate→SCAN→STORE ORDER (see Upload_InfectedRejected).
//   • Allow / Infected IVirusScanner doubles — swap the scan verdict per test.
//
// PROVIDER NOTE (same as the self-assessment harness): the verify gate runs with
// no PostgreSQL bound, so these use InMemory but go through the real
// handler→service→DbContext path, which is what proves the ownership + tenant
// scoping. There is deliberately no end-to-end multipart test — this repo has none.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class LeaveAttachmentIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();

    // Owner employee (Tenant A) — linked to _userOwner.
    private readonly Guid _userOwner = Guid.NewGuid();
    private readonly Guid _empOwner = Guid.NewGuid();

    // A DIFFERENT employee in the SAME tenant — linked to _userOther.
    private readonly Guid _userOther = Guid.NewGuid();
    private readonly Guid _empOther = Guid.NewGuid();

    private Guid _sickLeaveTypeId;

    public LeaveAttachmentIntegrationTests()
    {
        SeedBaseline();
    }

    // ── Test doubles ──────────────────────────────────────────────────────

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
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

    private sealed class WriteSpyFileStorage : IFileStorage
    {
        public int UploadCount { get; private set; }
        public string? LastRelativePath { get; private set; }
        private readonly Dictionary<string, byte[]> _store = new();

        private static string Key(Guid tenantId, string relativePath) => $"{tenantId}::{relativePath}";

        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            UploadCount++;
            LastRelativePath = relativePath;
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            _store[Key(tenantId, relativePath)] = ms.ToArray();
            return Task.FromResult($"/{tenantId}/{relativePath}");
        }

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue(Key(tenantId, relativePath), out var bytes)
                ? (Stream?)new MemoryStream(bytes)
                : null);

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class AllowVirusScanner : IVirusScanner
    {
        public Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult(VirusScanResult.Clean());
    }

    private sealed class InfectedVirusScanner : IVirusScanner
    {
        public Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult(VirusScanResult.Infected("Eicar-Test-Signature"));
    }

    // ── Pipeline builder ───────────────────────────────────────────────────

    private IMediator BuildPipeline(Guid tenantId, Guid userId, IVirusScanner scanner, IFileStorage storage)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.Email.Returns("user@test.com");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton(storage);
        services.AddSingleton(scanner);
        services.AddScoped<ILeaveAttachmentService, LeaveAttachmentService>();

        // The create-leave path, so the compliance gate runs through the real service.
        services.AddScoped<IHolidayProvider, NoOpHolidayProvider>();
        services.AddScoped<ITenantLeaveYearResolver, TenantLeaveYearResolver>();
        services.AddSingleton(Substitute.For<ILeaveNotificationService>());
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(UploadLeaveAttachmentCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    // ── Seeding ──────────────────────────────────────────────────────────

    private AppDbContext RawDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new MutableTenantContext { TenantId = tenantId });
    }

    private void SeedBaseline()
    {
        using var db = RawDb(_tenantA);

        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A", Status = TenantStatus.Active });

        AddEmployee(db, _empOwner, _tenantA, _userOwner, "EMP-OWN", "owner@a.com");
        AddEmployee(db, _empOther, _tenantA, _userOther, "EMP-OTH", "other@a.com");

        // A DocumentsRequired leave type with a 2-day threshold (the medical-certificate case).
        db.LeaveTypes.Add(new LeaveType
        {
            Id = _sickLeaveTypeId = Guid.NewGuid(),
            TenantId = _tenantA,
            Name = "Sick Leave",
            AnnualEntitlement = 14,
            AccrualFrequency = AccrualFrequency.Upfront,
            DocumentsRequired = true,
            DocumentDayThreshold = 2,
            ProbationEligible = true,
            Gender = LeaveTypeGender.All,
            IsActive = true,
        });

        // Balance for both employees so the compliance gate — not the balance gate — is what decides.
        foreach (var empId in new[] { _empOwner, _empOther })
        {
            db.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantA,
                EntryType = LedgerEntryType.Accrual,
                EmployeeId = empId,
                LeaveTypeId = _sickLeaveTypeId,
                LeaveYear = NextMonday().Year,
                Amount = 14m,
                BalanceAfter = 14m,
                OccurredAt = DateTime.UtcNow,
            });
        }

        db.SaveChanges();
    }

    private static void AddEmployee(AppDbContext db, Guid id, Guid tenantId, Guid userId, string empNo, string email)
        => db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, UserId = userId,
            EmployeeNo = empNo, FirstName = "First", LastName = "Last", Email = email,
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

    /// <summary>A Monday inside the BR-1/BR-2 submission window.</summary>
    private static DateOnly NextMonday()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        while (d.DayOfWeek != DayOfWeek.Monday)
            d = d.AddDays(1);
        return d;
    }

    private static UploadLeaveAttachmentCommand UploadCmd(
        byte[]? bytes = null, string fileName = "cert.pdf", string contentType = "application/pdf", long? sizeBytes = null)
    {
        bytes ??= HRM.Tests.Unit.Helpers.UploadTestBytes.For(contentType);
        return new(new MemoryStream(bytes), fileName, contentType, sizeBytes ?? bytes.Length);
    }

    private CreateLeaveRequestCommand CreateCmd(IReadOnlyList<Guid>? attachmentIds, int days = 5)
    {
        var monday = NextMonday();
        return new(_sickLeaveTypeId, monday, monday.AddDays(days - 1), false, null, "Flu", attachmentIds);
    }

    // ── AC-3 COMPLIANCE ARM: the one that proves "x.pdf" no longer works ───

    // A DocumentsRequired=true / threshold=2 leave type, a 5-day request, and attachment ids that
    // reference NOTHING real. Before the fix, ANY non-blank ".pdf"-suffixed string satisfied this gate;
    // the create succeeded with zero uploaded bytes. Now the ids resolve to no rows and the request is
    // rejected 400.
    [Fact]
    public async Task Create_DocumentsRequired_WithUnrealAttachmentIds_Rejected_ISSUE036()
    {
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), new WriteSpyFileStorage());

        var result = await mediator.Send(CreateCmd([Guid.NewGuid()]));

        result.IsFailure.Should().BeTrue("an unreal attachment id must never satisfy a medical-certificate gate");
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("attachment_not_found");

        using var verify = RawDb(_tenantA);
        (await verify.LeaveRequests.AsNoTracking().CountAsync()).Should().Be(0, "nothing may be persisted");
    }

    // The same 5-day DocumentsRequired request with NO attachment at all still hits the AC-3 message —
    // proving the gate itself is intact and the arm above is not just "any id fails".
    [Fact]
    public async Task Create_DocumentsRequired_WithNoAttachments_Rejected_ISSUE036()
    {
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), new WriteSpyFileStorage());

        var result = await mediator.Send(CreateCmd(null));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Medical certificate is required");
    }

    // ── Happy path: upload, then create — the row is linked and the gate is satisfied ──

    [Fact]
    public async Task UploadThenCreate_LinksAttachmentAndSatisfiesGate_ISSUE036()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);
        var bytes = HRM.Tests.Unit.Helpers.UploadTestBytes.Prefixed("application/pdf", 1, 2, 3, 4);

        var upload = await mediator.Send(UploadCmd(bytes));
        upload.IsSuccess.Should().BeTrue(upload.Error);
        upload.Value!.FileName.Should().Be("cert.pdf");
        upload.Value.SizeBytes.Should().Be(bytes.Length);

        // Bytes really were stored, once, under a tenant-relative path that does NOT repeat the tenant id
        // (IFileStorage prefixes that segment itself).
        spy.UploadCount.Should().Be(1);
        spy.LastRelativePath.Should().StartWith($"leaves/{_empOwner}/");
        spy.LastRelativePath.Should().NotContain(_tenantA.ToString());

        var create = await mediator.Send(CreateCmd([upload.Value.Id]));
        create.IsSuccess.Should().BeTrue(create.Error);

        using var verify = RawDb(_tenantA);
        var row = await verify.LeaveRequestAttachments.AsNoTracking().SingleAsync();
        row.TenantId.Should().Be(_tenantA);
        row.UploadedByEmployeeId.Should().Be(_empOwner);
        row.IsScanned.Should().BeTrue();
        row.LeaveRequestId.Should().Be(create.Value!.Id, "the create path must claim the upload");

        // The read paths (HasAttachments / Attachments) keep working off AttachmentUrls.
        create.Value.Attachments.Should().ContainSingle().Which.Should().Be(row.StorageKey);
        var persistedRequest = await verify.LeaveRequests.AsNoTracking().SingleAsync();
        persistedRequest.AttachmentUrls.Should().ContainSingle().Which.Should().Be(row.StorageKey);
    }

    // ── OWNERSHIP: an attachment uploaded by ANOTHER employee cannot be borrowed ──

    [Fact]
    public async Task Create_WithAnotherEmployeesAttachment_Rejected_ISSUE036()
    {
        var spy = new WriteSpyFileStorage();

        // _empOther uploads a genuine, scanned certificate in the SAME tenant.
        var medOther = BuildPipeline(_tenantA, _userOther, new AllowVirusScanner(), spy);
        var upload = await medOther.Send(UploadCmd());
        upload.IsSuccess.Should().BeTrue(upload.Error);

        // _empOwner tries to satisfy their own medical-certificate gate with it.
        var medOwner = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);
        var create = await medOwner.Send(CreateCmd([upload.Value!.Id]));

        create.IsFailure.Should().BeTrue("an attachment id may only be used by the employee who uploaded it");
        create.StatusCode.Should().Be(400);
        create.ErrorCode.Should().Be("attachment_not_found");

        using var verify = RawDb(_tenantA);
        (await verify.LeaveRequests.AsNoTracking().CountAsync()).Should().Be(0);
        // The other employee's row is untouched — never claimed by the borrower's request.
        (await verify.LeaveRequestAttachments.AsNoTracking().SingleAsync()).LeaveRequestId.Should().BeNull();
    }

    // ── SIZE CAP: 5 MB (NFR-3), not the 10 MB the self-assessment path allows ──

    [Fact]
    public async Task Upload_SixMegabyteFile_Rejected_ISSUE036()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);

        var result = await mediator.Send(UploadCmd(sizeBytes: 6L * 1024 * 1024));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("file_too_large");
        result.Error.Should().Contain("5 MB");

        // Rejected before storage/persistence.
        spy.UploadCount.Should().Be(0);
        using var verify = RawDb(_tenantA);
        (await verify.LeaveRequestAttachments.AsNoTracking().CountAsync()).Should().Be(0);
    }

    // A file just under the cap is accepted — so the arm above is the CAP, not a blanket rejection.
    [Fact]
    public async Task Upload_JustUnderCap_Accepted_ISSUE036()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);
        var bytes = HRM.Tests.Unit.Helpers.UploadTestBytes.Padded("application/pdf", (5 * 1024 * 1024) - 1);

        var result = await mediator.Send(UploadCmd(bytes));

        result.IsSuccess.Should().BeTrue(result.Error);
        spy.UploadCount.Should().Be(1);
    }

    // ── TYPE: PDF/JPG/PNG only — narrower than the self-assessment allow-list ──

    // .docx is accepted by the self-assessment evidence path; leave documents are PDF/JPG/PNG only (§10).
    [Fact]
    public async Task Upload_DisallowedMimeType_Rejected_ISSUE036()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);
        const string docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        var result = await mediator.Send(UploadCmd(
            HRM.Tests.Unit.Helpers.UploadTestBytes.For(docx), fileName: "cert.docx", contentType: docx));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("file_type_not_allowed");
        spy.UploadCount.Should().Be(0);
    }

    // The extension allow-list is retained alongside the MIME check (the old validator checked extensions):
    // a declared-PDF payload named ".exe" is rejected too.
    [Fact]
    public async Task Upload_DisallowedExtension_Rejected_ISSUE036()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);

        var result = await mediator.Send(UploadCmd(fileName: "payload.exe"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("file_type_not_allowed");
        spy.UploadCount.Should().Be(0);
    }

    // A PNG payload declared as application/pdf: the declared type is allowed, but the magic bytes are not
    // its own (BUG-058 sniff). Proves the sniff runs on this path too.
    [Fact]
    public async Task Upload_SpoofedContentType_Rejected_ISSUE036()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);

        var result = await mediator.Send(UploadCmd(
            HRM.Tests.Unit.Helpers.UploadTestBytes.For("image/png"), fileName: "cert.pdf", contentType: "application/pdf"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_file_type");
        spy.UploadCount.Should().Be(0);
    }

    // ── SCAN precedes STORE (NFR-3) ───────────────────────────────────────

    [Fact]
    public async Task Upload_InfectedRejected_ISSUE036()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new InfectedVirusScanner(), spy);

        var result = await mediator.Send(UploadCmd());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("file_infected");

        // ORDERING ASSERTION: zero storage writes. Had the service stored before scanning, the spy would
        // show UploadCount == 1 despite the rejection and this test would fail.
        spy.UploadCount.Should().Be(0);
        using var verify = RawDb(_tenantA);
        (await verify.LeaveRequestAttachments.AsNoTracking().CountAsync()).Should().Be(0);
    }
}
