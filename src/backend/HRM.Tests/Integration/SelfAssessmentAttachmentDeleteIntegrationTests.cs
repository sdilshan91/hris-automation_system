// ============================================================================
// BUG-244 #4 (US-PRF-002 FR-5 / ISSUE-105) — DELETE self-assessment evidence
// attachment integration tests.
//
// HARNESS: mirrors SelfAssessmentAttachmentIntegrationTests exactly — the EF Core
// InMemory provider driven through the REAL composed MediatR pipeline
// (Upload/Delete command handlers + the ITenantContext-backed global query filter).
// The service stamps TenantId on everything it creates, so tenant isolation is
// exercised by the global read filter without needing the TenantInterceptor.
//
// The one controllable collaborator that matters for DELETE is the file storage:
//   • CountingFileStorage — an in-memory IFileStorage that RETAINS bytes (so the
//     upload that seeds each test round-trips) and COUNTS DeleteAsync calls. The
//     delete count is the one permitted "mock-call-count" — it is what proves the
//     service actually asked storage to remove the blob on a successful delete, and
//     that it did NOT touch storage when the delete was rejected (ownership / lock /
//     window). It also drops the key so a post-delete store lookup is observable.
//
// PROVIDER NOTE (same as the upload harness): the verify gate runs `dotnet test`
// with NO PostgreSQL bound, so these use InMemory but go through the real
// handler→service→DbContext path, which is what proves the ownership + window/lock
// enforcement. The hard-delete is observed by re-reading the row from a fresh
// context; the blob-delete is observed via the storage double.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.Commands;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class SelfAssessmentAttachmentDeleteIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();

    // Owner employee (Tenant A) — linked to _userOwner; owns the self-assessment.
    private readonly Guid _userOwner = Guid.NewGuid();
    private readonly Guid _empOwner = Guid.NewGuid();

    // A DIFFERENT employee in the SAME tenant — linked to _userOther. Must never be able to delete
    // the owner's evidence (NFR-2 ownership; 404 not 403 so nothing is disclosed).
    private readonly Guid _userOther = Guid.NewGuid();
    private readonly Guid _empOther = Guid.NewGuid();

    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _goalId = Guid.NewGuid();

    public SelfAssessmentAttachmentDeleteIntegrationTests()
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

    // In-memory IFileStorage: retains bytes (upload seeds round-trip) and COUNTS deletes. DeleteCount is
    // load-bearing — it proves the blob delete ran on success and did NOT run on a rejected delete.
    private sealed class CountingFileStorage : IFileStorage
    {
        public int UploadCount { get; private set; }
        public int DeleteCount { get; private set; }
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
        {
            DeleteCount++;
            _store.Remove(Key(tenantId, relativePath));
            return Task.CompletedTask;
        }
    }

    private sealed class AllowVirusScanner : IVirusScanner
    {
        public Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
            => Task.FromResult(VirusScanResult.Clean());
    }

    // ── Pipeline builder ───────────────────────────────────────────────────

    private IMediator BuildPipeline(Guid tenantId, Guid userId, IFileStorage storage)
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
        services.AddSingleton<IVirusScanner>(new AllowVirusScanner());
        services.AddScoped<ISelfAssessmentAttachmentService, HRM.Infrastructure.Services.SelfAssessmentAttachmentService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DeleteSelfAssessmentAttachmentCommand).Assembly));

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
        Guid cycleId, Guid goalId, string fileName = "evidence.pdf", string contentType = "application/pdf")
    {
        var bytes = HRM.Tests.Unit.Helpers.UploadTestBytes.For(contentType);
        return new(cycleId, goalId, new MemoryStream(bytes), fileName, contentType, bytes.Length);
    }

    /// <summary>
    /// Uploads one attachment as the OWNER (window open, draft) and returns (assessmentId, attachmentId). This
    /// is the shared arrange step: every delete test starts from a real, persisted, owner-owned attachment.
    /// </summary>
    private async Task<(Guid AssessmentId, Guid AttachmentId)> SeedOwnedAttachmentAsync(CountingFileStorage storage)
    {
        var medOwner = BuildPipeline(_tenantA, _userOwner, storage);
        var upload = await medOwner.Send(UploadCmd(_cycleId, _goalId));
        upload.IsSuccess.Should().BeTrue(upload.Error);

        using var db = RawDb(_tenantA);
        var assessmentId = (await db.SelfAssessments.AsNoTracking().SingleAsync()).Id;
        return (assessmentId, upload.Value!.Id);
    }

    // ── Case 1: owner deletes own attachment (window open, draft) → success ──

    [Fact]
    public async Task Delete_OwnerHappyPath_RemovesRowAndBlob_BUG244_4()
    {
        var storage = new CountingFileStorage();
        var (assessmentId, attachmentId) = await SeedOwnedAttachmentAsync(storage);

        var medOwner = BuildPipeline(_tenantA, _userOwner, storage);
        var result = await medOwner.Send(new DeleteSelfAssessmentAttachmentCommand(assessmentId, attachmentId));

        result.IsSuccess.Should().BeTrue(result.Error);

        // The row is hard-deleted — a fresh context sees zero attachments.
        using var verify = RawDb(_tenantA);
        (await verify.SelfAssessmentAttachments.AsNoTracking().CountAsync()).Should().Be(0);

        // The blob delete was invoked exactly once (upload=1, delete=1) — the service asked storage to
        // remove the file, not just the DB row.
        storage.UploadCount.Should().Be(1);
        storage.DeleteCount.Should().Be(1);
    }

    // ── Case 2: a different employee (same tenant) is blocked → 404, row kept ─

    [Fact]
    public async Task Delete_NonOwnerBlocked_404_RowNotDeleted_BUG244_4()
    {
        var storage = new CountingFileStorage();
        var (assessmentId, attachmentId) = await SeedOwnedAttachmentAsync(storage);

        // A DIFFERENT employee in the SAME tenant attempts the delete with the owner's real ids.
        var medOther = BuildPipeline(_tenantA, _userOther, storage);
        var result = await medOther.Send(new DeleteSelfAssessmentAttachmentCommand(assessmentId, attachmentId));

        // 404 (not 403) — ownership resolves to no match, and the attachment's existence is never disclosed.
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("attachment_not_found");

        // REAL assertion that the non-owner was actually blocked: the row is STILL present, and the blob
        // was never deleted (delete count stays at zero).
        using var verify = RawDb(_tenantA);
        var still = await verify.SelfAssessmentAttachments.AsNoTracking().SingleAsync();
        still.Id.Should().Be(attachmentId);
        storage.DeleteCount.Should().Be(0);
    }

    // ── Case 3: assessment already submitted → 409 already_submitted, row kept ─

    [Fact]
    public async Task Delete_AfterSubmission_409_AlreadySubmitted_RowIntact_BUG244_4()
    {
        var storage = new CountingFileStorage();
        var (assessmentId, attachmentId) = await SeedOwnedAttachmentAsync(storage);

        // Lock the assessment (BR-3) AFTER the evidence was attached.
        using (var db = RawDb(_tenantA))
        {
            var assessment = await db.SelfAssessments.SingleAsync(s => s.Id == assessmentId);
            assessment.Status = SelfAssessmentStatus.Submitted;
            assessment.SubmittedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var medOwner = BuildPipeline(_tenantA, _userOwner, storage);
        var result = await medOwner.Send(new DeleteSelfAssessmentAttachmentCommand(assessmentId, attachmentId));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("already_submitted");

        // Row intact, blob untouched.
        using var verify = RawDb(_tenantA);
        (await verify.SelfAssessmentAttachments.AsNoTracking().CountAsync()).Should().Be(1);
        storage.DeleteCount.Should().Be(0);
    }

    // ── Case 4: self-assessment window closed → 409 self_assessment_closed ────

    [Fact]
    public async Task Delete_WindowClosed_409_SelfAssessmentClosed_RowIntact_BUG244_4()
    {
        var storage = new CountingFileStorage();
        var (assessmentId, attachmentId) = await SeedOwnedAttachmentAsync(storage);

        // Close the self-assessment window AFTER the evidence was attached: move the legacy phase columns
        // fully into the past (Status stays Active, so IsSelfAssessmentOpen is false purely on the window).
        using (var db = RawDb(_tenantA))
        {
            var cycle = await db.AppraisalCycles.SingleAsync(c => c.Id == _cycleId);
            cycle.SelfAssessmentStart = DateTime.UtcNow.AddDays(-10);
            cycle.SelfAssessmentEnd = DateTime.UtcNow.AddDays(-5);
            await db.SaveChangesAsync();
        }

        var medOwner = BuildPipeline(_tenantA, _userOwner, storage);
        var result = await medOwner.Send(new DeleteSelfAssessmentAttachmentCommand(assessmentId, attachmentId));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("self_assessment_closed");

        // Row intact, blob untouched — you cannot pull evidence once the window is closed.
        using var verify = RawDb(_tenantA);
        (await verify.SelfAssessmentAttachments.AsNoTracking().CountAsync()).Should().Be(1);
        storage.DeleteCount.Should().Be(0);
    }
}
