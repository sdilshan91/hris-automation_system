// ============================================================================
// DF-58 — OnboardingOutboxReconcileJob (durability backstop sweep, sibling of DF-4).
//
// OnboardingChecklistService.AssignAsync/CompleteTaskAsync write Pending notification-outbox rows in the
// assignment transaction then best-effort post-commit Enqueue the dispatch job. A lost enqueue leaves those
// Pending rows delivered by nothing (OnboardingOverdueSweepJob only dispatches when it writes NEW overdue rows).
// This sweep unconditionally drains Pending outbox rows across active tenants via the (idempotent) dispatch job.
// Proves: 1. an orphaned Pending row is dispatched; 2. a second run dispatches nothing new (outbox watermark);
// 3. inactive tenants are skipped.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OnboardingOutboxReconcileJobTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly INotificationDispatcher _dispatcher = Substitute.For<INotificationDispatcher>();

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // Rls:Enabled defaults false
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton(_dispatcher);
        services.AddScoped<IOnboardingNotificationDispatchJob, OnboardingNotificationDispatchJob>();
        return services.BuildServiceProvider();
    }

    private Guid SeedTenantWithPending(Guid tenantId, string subdomain, TenantStatus status)
    {
        using var db = TestDbContextFactory.Create(tenantId, _dbName);
        if (!db.Tenants.IgnoreQueryFilters().Any(t => t.Id == tenantId))
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = status, FiscalYearStartMonth = 1,
            });
        var rowId = Guid.NewGuid();
        db.OnboardingNotificationOutbox.Add(new OnboardingNotificationOutbox
        {
            Id = rowId,
            TenantId = tenantId,
            ChecklistInstanceId = Guid.NewGuid(),
            RecipientUserId = Guid.NewGuid(),
            RecipientRole = OnboardingResponsibleRole.Employee,
            NotificationType = "onboarding.checklist.assigned",
            Payload = "{}",
            Status = OnboardingNotificationStatus.Pending,
        });
        db.SaveChanges();
        return rowId;
    }

    private OnboardingNotificationOutbox ReadRow(Guid id)
    {
        using var db = TestDbContextFactory.Create(Guid.NewGuid(), _dbName);
        return db.OnboardingNotificationOutbox.IgnoreQueryFilters().Single(o => o.Id == id);
    }

    [Fact]
    [Trait("TC", "TC-ONB-002-15")]
    public async Task Sweep_drains_a_tenants_orphaned_pending_outbox_row()
    {
        var provider = BuildProvider();
        var tenantId = Guid.NewGuid();
        var rowId = SeedTenantWithPending(tenantId, "acme", TenantStatus.Active);

        await new OnboardingOutboxReconcileJob(provider.GetRequiredService<IServiceScopeFactory>()).RunAsync();

        ReadRow(rowId).Status.Should().Be(OnboardingNotificationStatus.Dispatched);
        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("TC", "TC-ONB-002-15")]
    public async Task Sweep_is_idempotent_second_run_dispatches_nothing_new()
    {
        var provider = BuildProvider();
        var tenantId = Guid.NewGuid();
        var rowId = SeedTenantWithPending(tenantId, "acme", TenantStatus.Active);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        await new OnboardingOutboxReconcileJob(scopeFactory).RunAsync();
        await new OnboardingOutboxReconcileJob(scopeFactory).RunAsync();

        ReadRow(rowId).Status.Should().Be(OnboardingNotificationStatus.Dispatched);
        // The watermark makes the second run a no-op — exactly one delivery total.
        await _dispatcher.Received(1).SendEmailAsync(Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("TC", "TC-ONB-002-15")]
    public async Task Sweep_skips_inactive_tenants()
    {
        var provider = BuildProvider();
        var active = Guid.NewGuid();
        var suspended = Guid.NewGuid();
        var activeRow = SeedTenantWithPending(active, "a", TenantStatus.Active);
        var suspendedRow = SeedTenantWithPending(suspended, "s", TenantStatus.Suspended);

        await new OnboardingOutboxReconcileJob(provider.GetRequiredService<IServiceScopeFactory>()).RunAsync();

        ReadRow(activeRow).Status.Should().Be(OnboardingNotificationStatus.Dispatched);
        ReadRow(suspendedRow).Status.Should().Be(OnboardingNotificationStatus.Pending); // not swept
    }
}
