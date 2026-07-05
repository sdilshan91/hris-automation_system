// ============================================================================
// US-PRF-006 / ISSUE-118 (BR-5): AppraisalCycleService.UpdateAsync must reject a
// change to IsAnonymousFeedback once any 360 feedback has been submitted for the
// cycle — flipping it would retroactively de-anonymize (or expose) already-
// submitted feedback. Rejection is 409 "anonymity_locked".
//
// PRE-FIX BEHAVIOUR (HEAD): UpdateAsync set
//   cycle.IsAnonymousFeedback = input.IsAnonymousFeedback;
// unconditionally — the flag could be flipped even after feedback existed.
//
// POST-FIX BEHAVIOUR (under test): the change is allowed when NO feedback exists,
// and rejected with anonymity_locked when a Feedback360 row exists for the cycle.
// Other fields stay editable when the anonymity flag itself is unchanged.
//
// PROVIDER: EF Core InMemory (mirrors AppraisalCycleServiceTests; the verify gate
// runs `dotnet test` with no PostgreSQL / Docker). Scheduler seam absent (null).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class CycleAnonymityLockRegressionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _empA = Guid.NewGuid();
    private readonly Guid _empB = Guid.NewGuid();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _hrUser;
    private readonly IPerformanceNotificationService _notifications;

    public CycleAnonymityLockRegressionTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _hrUser = Substitute.For<ICurrentUser>();
        _hrUser.UserId.Returns(Guid.NewGuid());
        _hrUser.IsAuthenticated.Returns(true);
        _hrUser.Email.Returns("hr@acme.com");

        _notifications = Substitute.For<IPerformanceNotificationService>();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private AppraisalCycleService CreateService() => new(
        CreateDbContext(), _tenantContext, _hrUser, _notifications,
        Substitute.For<ILogger<AppraisalCycleService>>(), scheduler: null);

    private void SeedEmployees()
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Employees.Add(new Employee
        {
            Id = _empA, TenantId = _tenantId, EmployeeNo = "EMP-A", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Status = EmployeeStatus.Active, DepartmentId = Guid.NewGuid(), IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _empB, TenantId = _tenantId, EmployeeNo = "EMP-B", FirstName = "Alan", LastName = "Turing",
            Email = "alan@acme.com", Status = EmployeeStatus.Active, DepartmentId = Guid.NewGuid(), IsDeleted = false,
        });
        db.SaveChanges();
    }

    private static IReadOnlyList<CyclePhaseInput> Phases(DateTime start) =>
    [
        new(CyclePhaseType.GoalSetting, start, start.AddDays(10)),
        new(CyclePhaseType.SelfAssessment, start.AddDays(11), start.AddDays(20)),
        new(CyclePhaseType.ManagerReview, start.AddDays(21), start.AddDays(30)),
    ];

    /// <summary>Creates a Draft, all-employees cycle with IsAnonymousFeedback initially false and returns its id.</summary>
    private async Task<Guid> CreateDraftCycleAsync()
    {
        var start = DateTime.UtcNow.Date.AddDays(1);
        var input = new CreateCycleInput(
            "FY2026 Annual", CycleType.Annual, start, start.AddDays(40), Phases(start),
            new ParticipantScopeInput(ParticipantScopeType.AllEmployees),
            RatingScaleMax: 5, SelfWeightPercent: 30,
            Is360Enabled: true, IsCalibrationEnabled: false, IsAnonymousFeedback: false);

        var created = await CreateService().CreateAsync(input);
        created.IsSuccess.Should().BeTrue(created.Error);
        return created.Value!.Id;
    }

    private UpdateCycleInput UpdateWith(bool isAnonymous, string name = "FY2026 Annual")
    {
        var start = DateTime.UtcNow.Date.AddDays(1);
        return new UpdateCycleInput(
            name, start, start.AddDays(40), Phases(start),
            Is360Enabled: true, IsCalibrationEnabled: false, IsAnonymousFeedback: isAnonymous,
            RatingScaleMax: null, SelfWeightPercent: null);
    }

    private void SeedFeedbackFor(Guid cycleId)
    {
        using var db = CreateDbContext();
        db.Feedback360s.Add(new Feedback360
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CycleId = cycleId,
            RevieweeEmployeeId = _empA,
            ReviewerEmployeeId = _empB,
            Category = ReviewerCategory.Peer,
            IsAnonymous = false,
            SubmittedAt = DateTime.UtcNow,
            IsDeleted = false,
        });
        db.SaveChanges();
    }

    // ── Allowed: flip anonymity when NO feedback exists ──────────────────

    [Fact]
    public async Task UpdateCycle_AnonymityChange_NoFeedback_Succeeds_ISSUE118()
    {
        SeedEmployees();
        var cycleId = await CreateDraftCycleAsync();

        // No Feedback360 rows for this cycle → the flip from false → true is allowed.
        var result = await CreateService().UpdateAsync(cycleId, UpdateWith(isAnonymous: true));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.IsAnonymousFeedback.Should().BeTrue();

        using var db = CreateDbContext();
        (await db.AppraisalCycles.FindAsync(cycleId))!.IsAnonymousFeedback.Should().BeTrue();
    }

    // ── Rejected: flip anonymity AFTER feedback has been submitted ───────

    [Fact]
    public async Task UpdateCycle_AnonymityChangeAfterFeedback_Rejected_ISSUE118()
    {
        SeedEmployees();
        var cycleId = await CreateDraftCycleAsync();
        SeedFeedbackFor(cycleId); // a submitted 360 record now exists for this cycle

        var result = await CreateService().UpdateAsync(cycleId, UpdateWith(isAnonymous: true));

        result.IsFailure.Should().BeTrue("anonymity is locked once feedback exists (BR-5)");
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("anonymity_locked");

        // The persisted flag must remain unchanged (still false).
        using var db = CreateDbContext();
        (await db.AppraisalCycles.FindAsync(cycleId))!.IsAnonymousFeedback.Should().BeFalse();
    }

    // ── Guard: with feedback present, editing OTHER fields (anonymity unchanged) still works ──

    [Fact]
    public async Task UpdateCycle_OtherFieldsWithFeedback_AnonymityUnchanged_Succeeds_ISSUE118()
    {
        SeedEmployees();
        var cycleId = await CreateDraftCycleAsync();
        SeedFeedbackFor(cycleId);

        // isAnonymous stays false (unchanged); only the name changes → the lock must not trip.
        var result = await CreateService().UpdateAsync(cycleId, UpdateWith(isAnonymous: false, name: "FY2026 Annual (renamed)"));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Name.Should().Be("FY2026 Annual (renamed)");
        result.Value!.IsAnonymousFeedback.Should().BeFalse();
    }
}
