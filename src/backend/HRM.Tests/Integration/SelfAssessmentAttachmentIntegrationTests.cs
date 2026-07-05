// ============================================================================
// ISSUE-105 (US-PRF-002 FR-5) — self-assessment evidence-file attachment
// upload / list / download integration tests.
//
// HARNESS: clones ApplicantSubmissionIntegrationTests — the EF Core InMemory
// provider driven through the REAL composed pipeline (MediatR handlers +
// ITenantContext-backed global query filters). The service explicitly stamps
// TenantId on every entity it creates (SelfAssessment / SelfAssessmentItem /
// SelfAssessmentAttachment), so tenant isolation is exercised by the global read
// filter without needing the TenantInterceptor. Two collaborators are controllable:
//   • WriteSpyFileStorage — an in-memory IFileStorage that COUNTS UploadAsync calls
//     and keeps the stored bytes (so download round-trips). The write count is the
//     one permitted "mock-call-count" — and only to prove the validate→SCAN→STORE
//     ORDER (see Upload_InfectedRejected).
//   • Allow / Infected IVirusScanner doubles — swap the scan verdict per test.
//
// PROVIDER NOTE (same as the applicant harness): the verify gate runs `dotnet test`
// with NO PostgreSQL bound (Docker unavailable in the agent sandbox), so these use
// InMemory but go through the real handler→service→DbContext path, which is what
// proves the ownership + tenant scoping. The xmin/unique-index PG specifics are not
// under test here.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class SelfAssessmentAttachmentIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    // Owner employee (Tenant A) — linked to _userOwner.
    private readonly Guid _userOwner = Guid.NewGuid();
    private readonly Guid _empOwner = Guid.NewGuid();

    // A different employee in the SAME tenant (Tenant A) — linked to _userOther.
    private readonly Guid _userOther = Guid.NewGuid();
    private readonly Guid _empOther = Guid.NewGuid();

    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _goalId = Guid.NewGuid();

    public SelfAssessmentAttachmentIntegrationTests()
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

    // Write-spy IFileStorage: counts stores and retains bytes so downloads round-trip. The write
    // COUNT is load-bearing for the ordering assertion in Upload_InfectedRejected.
    private sealed class WriteSpyFileStorage : IFileStorage
    {
        public int UploadCount { get; private set; }
        private readonly Dictionary<string, byte[]> _store = new();

        private static string Key(Guid tenantId, string relativePath) => $"{tenantId}::{relativePath}";

        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            UploadCount++;
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
        services.AddScoped<ISelfAssessmentAttachmentService, HRM.Infrastructure.Services.SelfAssessmentAttachmentService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(UploadSelfAssessmentAttachmentCommand).Assembly));

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
        var now = DateTime.UtcNow;

        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A", Status = TenantStatus.Active });

        AddEmployee(db, _empOwner, _tenantA, _userOwner, "EMP-OWN", "owner@a.com");
        AddEmployee(db, _empOther, _tenantA, _userOther, "EMP-OTH", "other@a.com");

        // Self-assessment window OPEN (Active cycle, now inside [-1d, +1d]).
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantA, Name = "FY26", Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-10), GoalSettingEnd = now.AddDays(-2),
            SelfAssessmentStart = now.AddDays(-1), SelfAssessmentEnd = now.AddDays(1),
            ManagerReviewStart = now.AddDays(5), ManagerReviewEnd = now.AddDays(10),
            RatingScaleMax = 5, SelfWeightPercent = 30,
        });

        // Goal assigned to the OWNER employee for the cycle.
        db.Goals.Add(new Goal
        {
            Id = _goalId, TenantId = _tenantA, CycleId = _cycleId, EmployeeId = _empOwner,
            Title = "Ship the thing", Description = "desc", Category = GoalCategory.Kpi, Weight = 100,
            TargetValue = "100%", MeasurementUnit = "%", DueDate = DateOnly.FromDateTime(now.AddDays(30)),
            Status = GoalStatus.Acknowledged,
        });

        db.SaveChanges();
    }

    private static void AddEmployee(AppDbContext db, Guid id, Guid tenantId, Guid userId, string empNo, string email)
        => db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, UserId = userId,
            EmployeeNo = empNo, FirstName = "First", LastName = "Last", Email = email,
            DateOfJoining = new DateTime(2021, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

    private static UploadSelfAssessmentAttachmentCommand UploadCmd(
        Guid cycleId, Guid goalId, byte[]? bytes = null, string fileName = "evidence.pdf", string contentType = "application/pdf")
    {
        bytes ??= [10, 20, 30, 40, 50];
        return new(cycleId, goalId, new MemoryStream(bytes), fileName, contentType, bytes.Length);
    }

    // ── Happy path: validate → scan → store → persist (AC-1/FR-5/NFR-4) ────

    [Fact]
    public async Task Upload_HappyPath_ScansStoresAndPersists_ISSUE105()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7 };

        var result = await mediator.Send(UploadCmd(_cycleId, _goalId, bytes));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.GoalId.Should().Be(_goalId);
        result.Value.FileName.Should().Be("evidence.pdf");

        // Storage was written exactly once (clean scan ⇒ the store step runs).
        spy.UploadCount.Should().Be(1);

        // Exactly one attachment persisted, tenant-stamped, linked to the goal's own item.
        using var verify = RawDb(_tenantA);
        var persisted = await verify.SelfAssessmentAttachments.AsNoTracking().ToListAsync();
        persisted.Should().ContainSingle();
        persisted[0].TenantId.Should().Be(_tenantA);
        persisted[0].SizeBytes.Should().Be(bytes.Length);
        persisted[0].IsScanned.Should().BeTrue();

        var item = await verify.SelfAssessmentItems.AsNoTracking().SingleAsync();
        item.GoalId.Should().Be(_goalId);
        persisted[0].SelfAssessmentItemId.Should().Be(item.Id);

        // The list read returns exactly that one metadata row (never the bytes).
        var list = await mediator.Send(new ListSelfAssessmentAttachmentsQuery(_cycleId, _goalId));
        list.IsSuccess.Should().BeTrue(list.Error);
        list.Value!.Should().ContainSingle().Which.Id.Should().Be(persisted[0].Id);
    }

    // ── Infected upload rejected: proves scan PRECEDES store (NFR-4) ────────

    [Fact]
    public async Task Upload_InfectedRejected_ISSUE105()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new InfectedVirusScanner(), spy);

        var result = await mediator.Send(UploadCmd(_cycleId, _goalId));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("file_infected");

        // Nothing persisted (SaveChanges is never reached on rejection).
        using var verify = RawDb(_tenantA);
        (await verify.SelfAssessmentAttachments.AsNoTracking().CountAsync()).Should().Be(0);

        // ORDERING ASSERTION: zero storage writes. If the service stored BEFORE scanning, the
        // spy would show UploadCount == 1 even though the upload was rejected — this test would
        // then FAIL. So UploadCount == 0 is what proves validate→SCAN→STORE ordering.
        spy.UploadCount.Should().Be(0);
    }

    // ── Already-submitted assessment is locked (BR-3) ──────────────────────

    [Fact]
    public async Task Upload_AlreadySubmitted_Rejected_ISSUE105()
    {
        // Seed a SUBMITTED self-assessment for the owner + cycle.
        using (var db = RawDb(_tenantA))
        {
            db.SelfAssessments.Add(new SelfAssessment
            {
                Id = Guid.NewGuid(), TenantId = _tenantA, CycleId = _cycleId, EmployeeId = _empOwner,
                Status = SelfAssessmentStatus.Submitted, SubmittedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);

        var result = await mediator.Send(UploadCmd(_cycleId, _goalId));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("already_submitted");
        spy.UploadCount.Should().Be(0);
    }

    // ── Per-goal cap of 5 (FR-5) ───────────────────────────────────────────

    [Fact]
    public async Task Upload_PerGoalCap_Enforced_ISSUE105()
    {
        var spy = new WriteSpyFileStorage();
        var mediator = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);

        // Seed 5 successful attachments on the goal.
        for (var i = 0; i < 5; i++)
        {
            var ok = await mediator.Send(UploadCmd(_cycleId, _goalId, fileName: $"e{i}.pdf"));
            ok.IsSuccess.Should().BeTrue(ok.Error);
        }

        // The 6th is rejected.
        var sixth = await mediator.Send(UploadCmd(_cycleId, _goalId, fileName: "e6.pdf"));
        sixth.IsFailure.Should().BeTrue();
        sixth.StatusCode.Should().Be(409);
        sixth.ErrorCode.Should().Be("attachment_limit_reached");

        // Still exactly 5 persisted; the 6th never reached storage (cap checked before scan/store).
        using var verify = RawDb(_tenantA);
        (await verify.SelfAssessmentAttachments.AsNoTracking().CountAsync()).Should().Be(5);
        spy.UploadCount.Should().Be(5);
    }

    // ── Cross-tenant isolation (NFR-2) ─────────────────────────────────────

    [Fact]
    public async Task CrossTenantIsolation_ISSUE105()
    {
        // Tenant B baseline: a tenant + an employee linked to a Tenant-B user.
        var userB = Guid.NewGuid();
        using (var db = RawDb(_tenantB))
        {
            db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "tenant-b", Name = "Tenant B", Status = TenantStatus.Active });
            AddEmployee(db, Guid.NewGuid(), _tenantB, userB, "EMP-B", "b@b.com");
            await db.SaveChangesAsync();
        }

        // Tenant A owner uploads one attachment.
        var spy = new WriteSpyFileStorage();
        var medA = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);
        var upload = await medA.Send(UploadCmd(_cycleId, _goalId));
        upload.IsSuccess.Should().BeTrue(upload.Error);
        var attachmentId = upload.Value!.Id;

        // Tenant B cannot LIST Tenant A's goal attachments — the cycle/goal/item are all filtered
        // out by the tenant read filter, so the list comes back EMPTY.
        var medB = BuildPipeline(_tenantB, userB, new AllowVirusScanner(), spy);
        var bList = await medB.Send(new ListSelfAssessmentAttachmentsQuery(_cycleId, _goalId));
        bList.IsSuccess.Should().BeTrue(bList.Error);
        bList.Value!.Should().BeEmpty();

        // Tenant B cannot DOWNLOAD Tenant A's attachment — 404 (never disclosed), and storage is
        // never touched (the write-spy count stays at the single Tenant-A upload).
        var bDownload = await medB.Send(new DownloadSelfAssessmentAttachmentQuery(attachmentId));
        bDownload.IsFailure.Should().BeTrue();
        bDownload.StatusCode.Should().Be(404);
        spy.UploadCount.Should().Be(1);
    }

    // ── Download: non-owner (same tenant) rejected; owner gets the bytes ────

    [Fact]
    public async Task Download_NonOwnerRejected_ISSUE105()
    {
        var payload = new byte[] { 9, 8, 7, 6, 5 };
        var spy = new WriteSpyFileStorage();

        // Owner uploads.
        var medOwner = BuildPipeline(_tenantA, _userOwner, new AllowVirusScanner(), spy);
        var upload = await medOwner.Send(UploadCmd(_cycleId, _goalId, payload));
        upload.IsSuccess.Should().BeTrue(upload.Error);
        var attachmentId = upload.Value!.Id;

        // A DIFFERENT employee in the SAME tenant cannot download it — 404 (ownership check),
        // and the file store is not opened for them.
        var medOther = BuildPipeline(_tenantA, _userOther, new AllowVirusScanner(), spy);
        var nonOwner = await medOther.Send(new DownloadSelfAssessmentAttachmentQuery(attachmentId));
        nonOwner.IsFailure.Should().BeTrue();
        nonOwner.StatusCode.Should().Be(404);

        // The owner DOES get the stream, with the exact bytes round-tripped through storage.
        var owner = await medOwner.Send(new DownloadSelfAssessmentAttachmentQuery(attachmentId));
        owner.IsSuccess.Should().BeTrue(owner.Error);
        owner.Value!.Content.Should().Equal(payload);
        owner.Value.FileName.Should().Be("evidence.pdf");
        owner.Value.ContentType.Should().Be("application/pdf");
    }
}
