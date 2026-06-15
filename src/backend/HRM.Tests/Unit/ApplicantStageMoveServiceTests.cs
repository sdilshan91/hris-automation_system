// ============================================================================
// US-REC-003: Applicant pipeline stage-move — service unit tests.
//
// Covers the stage-move business rules in ApplicantService.MoveStageAsync /
// BulkMoveStageAsync (driven via the real EF Core InMemory provider so the
// stage-history row + audit-log row are actually persisted, like
// LeaveApprovalServiceTests):
//   - BR-3: moving to Rejected without a reason is blocked (reason_required)
//   - BR-4: a backward move without a reason is blocked (reason_required)
//   - Happy path Applied -> Screening: stage updated + history row written (BR-5/AC-2)
//   - Audit-log row written for the transition (AC-2)
//   - No-op move (same stage) rejected (stage_unchanged)
//   - Hired is allowed (BR-6 terminal; convert-to-employee deferred)
//   - Bulk move is all-or-nothing (one bad row rejects the batch)
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ApplicantStageMoveServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    private readonly Guid _vacancyId = Guid.NewGuid();

    public ApplicantStageMoveServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);

        SeedVacancy();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ApplicantService CreateService()
        => new(
            CreateDbContext(),
            _tenantContext,
            _currentUser,
            Substitute.For<IFileStorage>(),
            Substitute.For<IVirusScanner>(),
            Substitute.For<IRecruitmentNotificationService>(),
            Substitute.For<ILogger<ApplicantService>>());

    private void SeedVacancy()
    {
        using var db = CreateDbContext();
        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyId,
            TenantId = _tenantId,
            ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer",
            Status = VacancyStatus.Open,
            EmploymentType = EmploymentType.FullTime,
            Headcount = 1,
            Description = "Build things.",
            IsDeleted = false,
        });
        db.SaveChanges();
    }

    private Guid SeedApplicant(ApplicantStage stage = ApplicantStage.Applied, string email = "ada@example.com")
    {
        using var db = CreateDbContext();
        var id = Guid.NewGuid();
        db.Applicants.Add(new Applicant
        {
            Id = id,
            TenantId = _tenantId,
            VacancyId = _vacancyId,
            ApplicationReferenceNumber = $"APP-2026-{db.Applicants.IgnoreQueryFilters().Count() + 1:D4}",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = email,
            ResumeStorageKey = "recruitment/x/y/z.pdf",
            ResumeFileName = "resume.pdf",
            Stage = stage,
            Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow,
            IsDeleted = false,
        });
        db.SaveChanges();
        return id;
    }

    // ── BR-3: Rejected requires a reason ──────────────────────────────

    [Fact]
    public async Task MoveStage_ToRejected_WithoutReason_IsRejected()
    {
        var id = SeedApplicant(ApplicantStage.Screening);

        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Rejected, reason: null, notes: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("reason_required");

        // Stage unchanged, no history row written.
        await using var db = CreateDbContext();
        (await db.Applicants.FirstAsync(a => a.Id == id)).Stage.Should().Be(ApplicantStage.Screening);
        (await db.ApplicantStageHistories.CountAsync(h => h.ApplicantId == id)).Should().Be(0);
    }

    [Fact]
    public async Task MoveStage_ToRejected_WithReason_Succeeds()
    {
        var id = SeedApplicant(ApplicantStage.Screening);

        var result = await CreateService().MoveStageAsync(
            id, ApplicantStage.Rejected, reason: "Not a fit for the role.", notes: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ToStage.Should().Be(ApplicantStage.Rejected);
    }

    // ── BR-4: backward move requires a reason ─────────────────────────

    [Fact]
    public async Task MoveStage_Backward_WithoutReason_IsRejected()
    {
        var id = SeedApplicant(ApplicantStage.Interview);

        // Interview (2) -> Screening (1) is a backward move.
        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Screening, reason: null, notes: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("reason_required");
    }

    [Fact]
    public async Task MoveStage_Backward_WithReason_Succeeds_AndWritesHistory()
    {
        var id = SeedApplicant(ApplicantStage.Interview);

        var result = await CreateService().MoveStageAsync(
            id, ApplicantStage.Screening, reason: "Re-screening after feedback.", notes: null);

        result.IsSuccess.Should().BeTrue();

        await using var db = CreateDbContext();
        var history = await db.ApplicantStageHistories.SingleAsync(h => h.ApplicantId == id);
        history.FromStage.Should().Be(ApplicantStage.Interview);
        history.ToStage.Should().Be(ApplicantStage.Screening);
        history.Reason.Should().Be("Re-screening after feedback.");
    }

    // ── Happy path forward move + history + audit (AC-2/BR-5) ──────────

    [Fact]
    public async Task MoveStage_AppliedToScreening_UpdatesStage_AndWritesHistoryRow()
    {
        var id = SeedApplicant(ApplicantStage.Applied);

        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Screening, reason: null, notes: "Looks good.");

        result.IsSuccess.Should().BeTrue();
        result.Value!.FromStage.Should().Be(ApplicantStage.Applied);
        result.Value.ToStage.Should().Be(ApplicantStage.Screening);
        result.Value.StageHistoryId.Should().NotBeEmpty();

        await using var db = CreateDbContext();
        (await db.Applicants.FirstAsync(a => a.Id == id)).Stage.Should().Be(ApplicantStage.Screening);

        var history = await db.ApplicantStageHistories.SingleAsync(h => h.ApplicantId == id);
        history.FromStage.Should().Be(ApplicantStage.Applied);
        history.ToStage.Should().Be(ApplicantStage.Screening);
        history.ChangedByUserId.Should().Be(_userId);
        history.Notes.Should().Be("Looks good.");
    }

    [Fact]
    public async Task MoveStage_WritesAuditLogEntry()
    {
        var id = SeedApplicant(ApplicantStage.Applied);

        await CreateService().MoveStageAsync(id, ApplicantStage.Screening, reason: null, notes: null);

        await using var db = CreateDbContext();
        var audit = await db.AuditLogs
            .Where(a => a.EventType == "recruitment.applicant.stage_changed")
            .ToListAsync();
        audit.Should().ContainSingle();
        audit[0].TenantId.Should().Be(_tenantId);
        audit[0].UserId.Should().Be(_userId);
    }

    // ── No-op + Hired terminal (BR-6) ─────────────────────────────────

    [Fact]
    public async Task MoveStage_SameStage_IsRejected()
    {
        var id = SeedApplicant(ApplicantStage.Screening);

        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Screening, reason: null, notes: null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("stage_unchanged");
    }

    [Fact]
    public async Task MoveStage_ToHired_IsAllowed()
    {
        var id = SeedApplicant(ApplicantStage.Offer);

        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Hired, reason: null, notes: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ToStage.Should().Be(ApplicantStage.Hired);
    }

    [Fact]
    public async Task MoveStage_ApplicantNotFound_Returns404()
    {
        var result = await CreateService().MoveStageAsync(Guid.NewGuid(), ApplicantStage.Screening, null, null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Bulk move: all-or-nothing ─────────────────────────────────────

    [Fact]
    public async Task BulkMoveStage_AllValid_MovesEveryone_AndWritesHistory()
    {
        var a = SeedApplicant(ApplicantStage.Applied, "a@example.com");
        var b = SeedApplicant(ApplicantStage.Applied, "b@example.com");

        var result = await CreateService().BulkMoveStageAsync(
            new[] { a, b }, ApplicantStage.Screening, reason: null, notes: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MovedCount.Should().Be(2);

        await using var db = CreateDbContext();
        (await db.Applicants.CountAsync(x => x.Stage == ApplicantStage.Screening)).Should().Be(2);
        (await db.ApplicantStageHistories.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task BulkMoveStage_OneInvalid_RejectsWholeBatch()
    {
        var a = SeedApplicant(ApplicantStage.Applied, "a@example.com");
        var b = SeedApplicant(ApplicantStage.Applied, "b@example.com");

        // Bulk-rejecting without a reason violates BR-3 for both — the whole batch must fail and nothing
        // is persisted (no partial move).
        var result = await CreateService().BulkMoveStageAsync(
            new[] { a, b }, ApplicantStage.Rejected, reason: null, notes: null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("reason_required");

        await using var db = CreateDbContext();
        (await db.Applicants.CountAsync(x => x.Stage != ApplicantStage.Applied)).Should().Be(0);
        (await db.ApplicantStageHistories.CountAsync()).Should().Be(0);
    }
}
