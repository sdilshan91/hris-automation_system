// ============================================================================
// BUG-244 #6 (US-PRF-010 BR-1) — the completed-cycle picker for the recommendation
// workspace: GetCompletedCyclesForRecommendationsQuery.
//
// HARNESS: the REAL GetCompletedCyclesForRecommendationsQueryHandler over the REAL
// AppraisalCycleService over a real AppDbContext (InMemory) with the
// ITenantContext-driven global query filter. Nothing about the status filter is
// stubbed — the handler delegates to AppraisalCycleService.ListAsync(Completed),
// which issues an actual `WHERE Status == Completed` against the seeded rows. So a
// test that seeds mixed-status cycles and observes ONLY the Completed one(s) coming
// back is exercising the genuine filtering, not a mock's canned answer.
//
// The HTTP authorization gate ([RequirePermission("Performance.Publish.All",
// "Performance.Review.Team")] on RecommendationController.GetCompletedCycles) is
// covered separately by the HttpApi collection; here we assert the query BEHAVIOUR.
//
// PROVIDER NOTE: InMemory — same rationale as the other Performance query tests (the
// verify gate runs `dotnet test` with no PostgreSQL / Docker).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class RecommendationCompletedCyclesTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();

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

    private AppDbContext Db(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new MutableTenantContext { TenantId = tenantId });
    }

    // The real handler over the real AppraisalCycleService (whose ListAsync does the status filtering).
    private GetCompletedCyclesForRecommendationsQueryHandler Handler(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsAuthenticated.Returns(true);

        var notifications = Substitute.For<IPerformanceNotificationService>();

        var cycleService = new AppraisalCycleService(
            db, ctx, currentUser, notifications, NullLogger<AppraisalCycleService>.Instance);

        return new GetCompletedCyclesForRecommendationsQueryHandler(cycleService);
    }

    private void SeedCycle(Guid id, string name, AppraisalCycleStatus status, DateTime start, DateTime end)
    {
        using var db = Db(_tenantA);
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = id, TenantId = _tenantA, Name = name, Status = status,
            StartDate = start, EndDate = end, RatingScaleMax = 5,
        });
        db.SaveChanges();
    }

    // ── Case 5: mixed statuses → only Completed returned, projected to the DTO ─

    [Fact]
    public async Task GetCompletedCycles_ReturnsOnlyCompleted_ProjectedToDto_BUG244_6()
    {
        using (var db = Db(_tenantA))
        {
            db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A", Status = TenantStatus.Active });
            db.SaveChanges();
        }

        var completedId = Guid.NewGuid();
        var completedEnd = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        // Three cycles, three statuses. Only the Completed one is eligible for the recommendation picker.
        SeedCycle(completedId, "FY2025 (completed)", AppraisalCycleStatus.Completed,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), completedEnd);
        SeedCycle(Guid.NewGuid(), "FY2026 (active)", AppraisalCycleStatus.Active,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        SeedCycle(Guid.NewGuid(), "FY2027 (draft)", AppraisalCycleStatus.Draft,
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        var result = await Handler(_tenantA).Handle(new GetCompletedCyclesForRecommendationsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);

        // REAL exclusion assertion: the Active + Draft cycles are absent; exactly the one Completed cycle is
        // returned, projected to CompletedCycleOptionDto with cycleId / cycleName populated.
        result.Value!.Should().ContainSingle();
        var option = result.Value!.Single();
        option.CycleId.Should().Be(completedId);
        option.CycleName.Should().Be("FY2025 (completed)");
        // ISSUE-254: this cycle was seeded directly at Completed WITHOUT going through TransitionStatusAsync, so
        // RatingsPublishedOn is null (a "legacy" completed cycle) ⇒ the projection falls back to the end date.
        option.RatingsPublishedOn.Should().Be(completedEnd);

        // Belt-and-braces: none of the non-Completed names leak through.
        result.Value!.Select(o => o.CycleName).Should().NotContain("FY2026 (active)");
        result.Value!.Select(o => o.CycleName).Should().NotContain("FY2027 (draft)");
    }

    // ── ISSUE-254: a real RatingsPublishedOn is surfaced (not the end-date fallback) ─

    [Fact]
    public async Task GetCompletedCycles_UsesRealRatingsPublishedOn_WhenStamped_ISSUE254()
    {
        using (var db = Db(_tenantA))
        {
            db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A", Status = TenantStatus.Active });
            db.SaveChanges();
        }

        var completedId = Guid.NewGuid();
        var completedEnd = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        // The real publish timestamp is distinct from the end date, so a fallback would be visibly wrong.
        var publishedOn = new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc);

        using (var db = Db(_tenantA))
        {
            db.AppraisalCycles.Add(new AppraisalCycle
            {
                Id = completedId, TenantId = _tenantA, Name = "FY2025 (completed, published)",
                Status = AppraisalCycleStatus.Completed,
                StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), EndDate = completedEnd,
                RatingScaleMax = 5, RatingsPublishedOn = publishedOn,
            });
            db.SaveChanges();
        }

        var result = await Handler(_tenantA).Handle(new GetCompletedCyclesForRecommendationsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        var option = result.Value!.Single();
        option.RatingsPublishedOn.Should().Be(publishedOn);        // the real stamped timestamp
        option.RatingsPublishedOn.Should().NotBe(completedEnd);    // NOT the end-date fallback
    }

    // ── No completed cycles → empty (not an error) ───────────────────────────

    [Fact]
    public async Task GetCompletedCycles_NoCompletedCycles_ReturnsEmpty_BUG244_6()
    {
        using (var db = Db(_tenantA))
        {
            db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A", Status = TenantStatus.Active });
            db.SaveChanges();
        }

        SeedCycle(Guid.NewGuid(), "FY2026 (active)", AppraisalCycleStatus.Active,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        var result = await Handler(_tenantA).Handle(new GetCompletedCyclesForRecommendationsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Should().BeEmpty();
    }
}
