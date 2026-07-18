// ============================================================================
// US-PAY-012 / ISSUE-184 — payroll run-history status filter contract.
// An UNPARSEABLE status must be rejected with 400 (machine code "invalid_status"), NOT silently
// dropped (which returned the full unfiltered list). A valid status filters; an absent/blank status
// returns all. Real EF InMemory-through-service (PayrollAuditService).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-PAY-012-01")]
public sealed class PayrollRunHistoryStatusFilterTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private ITenantContext Tenant()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(_tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private AppDbContext Db() => TestDbContextFactory.Create(Tenant(), _dbName);

    private PayrollAuditService Service()
    {
        var ctx = Tenant();
        return new PayrollAuditService(
            TestDbContextFactory.Create(ctx, _dbName), ctx, Substitute.For<ILogger<PayrollAuditService>>());
    }

    private async Task SeedRun(int month, PayrollRunStatus status)
    {
        var db = Db();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            PayMonth = month,
            PayYear = 2026,
            Status = status,
            InitiatedBy = Guid.NewGuid(),
            InitiatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedTwoRuns()
    {
        await SeedRun(5, PayrollRunStatus.Finalized);
        await SeedRun(6, PayrollRunStatus.ReviewPending);
    }

    // ── ISSUE-184: unparseable status → 400 invalid_status (not the full list) ───────

    [Theory]
    [InlineData("zzz")]
    [InlineData("Draft")]   // a plausible-but-nonexistent status (PayrollRunStatus has no Draft member)
    public async Task UnparseableStatus_IsRejected400(string bad)
    {
        await SeedTwoRuns();

        var result = await Service().GetRunHistoryAsync(new PayrollRunHistoryFilter { Status = bad });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("invalid_status");
        result.Value.Should().BeNull("a rejected filter returns no data, not the unfiltered list");
    }

    // ── A valid status filters to only the matching runs ────────────────────────────

    [Fact]
    public async Task ValidStatus_FiltersToMatchingRuns()
    {
        await SeedTwoRuns();

        var result = await Service().GetRunHistoryAsync(new PayrollRunHistoryFilter { Status = "Finalized" });

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle()
            .Which.Status.Should().Be(nameof(PayrollRunStatus.Finalized));
    }

    [Fact]
    public async Task ValidStatus_IsCaseInsensitive()
    {
        await SeedTwoRuns();

        var result = await Service().GetRunHistoryAsync(new PayrollRunHistoryFilter { Status = "finalized" });

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
    }

    // ── Absent/blank status returns all runs (unchanged) ────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AbsentOrBlankStatus_ReturnsAll(string? status)
    {
        await SeedTwoRuns();

        var result = await Service().GetRunHistoryAsync(new PayrollRunHistoryFilter { Status = status });

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(2);
    }
}
