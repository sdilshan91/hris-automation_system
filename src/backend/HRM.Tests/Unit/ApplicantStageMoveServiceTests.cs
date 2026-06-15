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
        => CreateService(out _);

    private ApplicantService CreateService(out IRecruitmentNotificationService notifications)
    {
        notifications = Substitute.For<IRecruitmentNotificationService>();
        return new ApplicantService(
            CreateDbContext(),
            _tenantContext,
            _currentUser,
            Substitute.For<IFileStorage>(),
            Substitute.For<IVirusScanner>(),
            notifications,
            Substitute.For<ILogger<ApplicantService>>());
    }

    private void SeedVacancy(
        VacancyStatus status = VacancyStatus.Open, int headcount = 1)
    {
        using var db = CreateDbContext();
        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyId,
            TenantId = _tenantId,
            ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer",
            Status = status,
            EmploymentType = EmploymentType.FullTime,
            Headcount = headcount,
            Description = "Build things.",
            IsDeleted = false,
        });
        db.SaveChanges();
    }

    private void SetVacancyStatus(VacancyStatus status)
    {
        using var db = CreateDbContext();
        var v = db.Vacancies.First(x => x.Id == _vacancyId);
        v.Status = status;
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

        // US-REC-004 AC-4/FR-3: rejection now requires the structured reason in addition to free text.
        var result = await CreateService().MoveStageAsync(
            id, ApplicantStage.Rejected, reason: "Not a fit for the role.", notes: null,
            rejectionReason: RejectionReason.NotQualified);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ToStage.Should().Be(ApplicantStage.Rejected);
        result.Value.RejectionReason.Should().Be(RejectionReason.NotQualified);
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

    // ── US-REC-004 AC-4/FR-3: structured rejection reason required ─────

    [Fact]
    public async Task MoveStage_ToRejected_WithoutStructuredReason_IsRejected()
    {
        var id = SeedApplicant(ApplicantStage.Screening);

        // Free-text reason supplied, but the structured reason is missing → blocked (AC-4/FR-3).
        var result = await CreateService().MoveStageAsync(
            id, ApplicantStage.Rejected, reason: "Some text.", notes: null, rejectionReason: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("rejection_reason_required");

        await using var db = CreateDbContext();
        (await db.Applicants.FirstAsync(a => a.Id == id)).Stage.Should().Be(ApplicantStage.Screening);
    }

    [Fact]
    public async Task MoveStage_ToRejected_WithStructuredReason_PersistsItOnApplicantAndHistory()
    {
        var id = SeedApplicant(ApplicantStage.Screening);

        var result = await CreateService().MoveStageAsync(
            id, ApplicantStage.Rejected, reason: "Position has been filled.", notes: null,
            rejectionReason: RejectionReason.PositionFilled);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RejectionReason.Should().Be(RejectionReason.PositionFilled);

        await using var db = CreateDbContext();
        (await db.Applicants.FirstAsync(a => a.Id == id)).RejectionReason.Should().Be(RejectionReason.PositionFilled);
        var history = await db.ApplicantStageHistories.SingleAsync(h => h.ApplicantId == id);
        history.RejectionReason.Should().Be(RejectionReason.PositionFilled);
    }

    // ── US-REC-004 FR-8: vacancy Closed/Cancelled blocks a forward move ─

    [Fact]
    public async Task MoveStage_Forward_WhenVacancyClosed_IsBlocked()
    {
        var id = SeedApplicant(ApplicantStage.Screening);
        SetVacancyStatus(VacancyStatus.Closed);

        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Interview, reason: null, notes: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("vacancy_not_active");

        await using var db = CreateDbContext();
        (await db.Applicants.FirstAsync(a => a.Id == id)).Stage.Should().Be(ApplicantStage.Screening);
    }

    [Fact]
    public async Task MoveStage_Reject_WhenVacancyCancelled_IsStillAllowed()
    {
        var id = SeedApplicant(ApplicantStage.Screening);
        SetVacancyStatus(VacancyStatus.Cancelled);

        // FR-8: rejection is allowed regardless of vacancy state.
        var result = await CreateService().MoveStageAsync(
            id, ApplicantStage.Rejected, reason: "Withdrawing the role.", notes: null,
            rejectionReason: RejectionReason.PositionFilled);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ToStage.Should().Be(ApplicantStage.Rejected);
    }

    // ── US-REC-004 BR-4: headcount-filled soft warning (move still succeeds) ─

    [Fact]
    public async Task MoveStage_ToOffer_WhenHeadcountFilled_SucceedsWithWarning()
    {
        // Headcount 1, and one applicant already Hired → at capacity.
        SetVacancyStatus(VacancyStatus.Open);
        SeedApplicant(ApplicantStage.Hired, "hired@example.com");
        var id = SeedApplicant(ApplicantStage.Interview, "next@example.com");

        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Offer, reason: null, notes: null);

        // Soft gate: the move STILL succeeds (overridable) but surfaces a warning (BR-4).
        result.IsSuccess.Should().BeTrue();
        result.Value!.ToStage.Should().Be(ApplicantStage.Offer);
        result.Value.Warnings.Should().Contain(w => w.Contains("headcount"));
    }

    [Fact]
    public async Task MoveStage_ToOffer_WhenHeadcountNotFilled_HasNoHeadcountWarning()
    {
        SetVacancyStatus(VacancyStatus.Open);
        var id = SeedApplicant(ApplicantStage.Interview, "solo@example.com");

        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Offer, reason: null, notes: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Warnings.Should().NotContain(w => w.Contains("headcount"));
    }

    // ── US-REC-004 FR-1/BR-1 gate, now REAL via US-REC-006: Offer requires a scorecard ─

    [Fact]
    public async Task MoveStage_ToOfferWithoutScorecard_SurfacesGateWarning()
    {
        // US-REC-006 replaced the REC-004 gate stub with the real Offer gate: advancing to Offer
        // without any interview scorecard surfaces a soft (non-blocking) warning.
        var id = SeedApplicant(ApplicantStage.Interview);

        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Offer, reason: null, notes: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Warnings.Should().Contain(w => w.Contains("scorecard"));
    }

    // ── US-REC-004 BR-2: reactivation out of Rejected requires a reason ─

    [Fact]
    public async Task MoveStage_OutOfRejected_WithoutReason_IsBlocked()
    {
        var id = SeedApplicant(ApplicantStage.Rejected);

        // Reactivating a rejected applicant to an active stage with no reason is blocked (BR-2/BR-4).
        var result = await CreateService().MoveStageAsync(id, ApplicantStage.Screening, reason: null, notes: null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("reason_required");
    }

    [Fact]
    public async Task MoveStage_OutOfRejected_WithReason_Succeeds_AndClearsRejectionReason()
    {
        var id = SeedApplicant(ApplicantStage.Rejected);
        // Stamp a prior structured rejection reason so we can assert it is cleared on reactivation.
        using (var seed = CreateDbContext())
        {
            var a = seed.Applicants.First(x => x.Id == id);
            a.RejectionReason = RejectionReason.NotQualified;
            seed.SaveChanges();
        }

        var result = await CreateService().MoveStageAsync(
            id, ApplicantStage.Screening, reason: "Reconsidering after referral.", notes: null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FromStage.Should().Be(ApplicantStage.Rejected);
        result.Value.ToStage.Should().Be(ApplicantStage.Screening);

        await using var db = CreateDbContext();
        (await db.Applicants.FirstAsync(a => a.Id == id)).RejectionReason.Should().BeNull();
    }

    // ── US-REC-004 FR-6/NFR-5: per-transition notification fired on a successful move ─

    [Fact]
    public async Task MoveStage_OnSuccess_FiresStageChangedNotification()
    {
        var id = SeedApplicant(ApplicantStage.Applied, "notify@example.com");
        var service = CreateService(out var notifications);

        var result = await service.MoveStageAsync(id, ApplicantStage.Screening, reason: null, notes: null);

        result.IsSuccess.Should().BeTrue();
        await notifications.Received(1).NotifyStageChangedAsync(
            id, _vacancyId, "notify@example.com",
            ApplicantStage.Applied.ToString(), ApplicantStage.Screening.ToString(),
            Arg.Any<CancellationToken>());
    }
}
