// ============================================================================
// US-NTF-006 (delivery Phase 1) — SendEmailJob (Hangfire worker) retry/terminal +
// idempotency, plus NotificationDelivery tenant-scoping.
// The job wraps the generic IEmailSender seam and drives a NotificationDelivery row
// to a terminal state: success → Sent+SentAt; throw → Attempts++/LastError + RETHROW
// (so Hangfire retries) and terminal Failed once attempts are exhausted; an already
// Sent row is a no-op (idempotent). It loads the row by id + EXPLICIT tenant id.
//
// Provider: EF Core InMemory AppDbContext through a real IServiceScopeFactory (the job
// opens its own scope), IEmailSender faked with NSubstitute.
//
// Maps:  #9  success → Sent + SentAt
//        #10 failure → Attempts++/LastError + rethrow; terminal attempt → Failed
//        #11 already-Sent row → no-op (does not resend)
//        #12 row for tenant A not visible under tenant B (query filter) + job loads by tenant
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
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class SendEmailJobTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();

    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>(); // unresolved in the job scope (as in production)
        services.AddScoped(sp =>
        {
            var tc = sp.GetRequiredService<ITenantContext>();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
            return new AppDbContext(options, tc);
        });
        services.AddSingleton(_sender);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private SendEmailJob CreateJob() => new(BuildScopeFactory());

    private static EmailMessage Message(Guid tenantId) =>
        new(tenantId, "jane@acme.test", "Subject", "<p>body</p>", "body");

    private Guid SeedDelivery(NotificationDeliveryStatus status, int attempts = 0, Guid? tenantId = null)
    {
        var id = BaseEntity.NewUuidV7();
        var tid = tenantId ?? _tenantId;
        using var db = TestDbContextFactory.Create(tid, _dbName);
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            Id = id,
            TenantId = tid,
            Channel = NotificationDeliveryChannel.Email,
            Status = status,
            Attempts = attempts,
            NotificationType = "leave.approved",
            EventKey = "leave_approved",
            RecipientUserId = Guid.NewGuid(),
            RecipientEmail = "jane@acme.test",
            Subject = "Subject",
        });
        db.SaveChanges();
        return id;
    }

    private NotificationDelivery ReadDelivery(Guid id)
    {
        using var db = TestDbContextFactory.Create(_tenantId, _dbName);
        return db.NotificationDeliveries.IgnoreQueryFilters().Single(d => d.Id == id);
    }

    private void SenderThrows(string message) =>
        _sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException(message)));

    // ── #9 Success → row flips to Sent + SentAt stamped ─────────────────────────────────────
    [Fact]
    public async Task Run_WhenSenderSucceeds_MarksRowSent_AndStampsSentAt()
    {
        var id = SeedDelivery(NotificationDeliveryStatus.Queued);

        await CreateJob().RunAsync(_tenantId, id, Message(_tenantId));

        var row = ReadDelivery(id);
        row.Status.Should().Be(NotificationDeliveryStatus.Sent);
        row.SentAt.Should().NotBeNull();
        row.LastError.Should().BeNull();
        await _sender.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ── #10a Failure (non-terminal) → Attempts++/LastError + RETHROW, still Queued ──────────
    [Fact]
    public async Task Run_WhenSenderThrows_IncrementsAttempts_RecordsError_AndRethrows()
    {
        var id = SeedDelivery(NotificationDeliveryStatus.Queued, attempts: 0);
        SenderThrows("smtp down");

        var act = async () => await CreateJob().RunAsync(_tenantId, id, Message(_tenantId));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("smtp down");

        var row = ReadDelivery(id);
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("smtp down");
        row.Status.Should().Be(NotificationDeliveryStatus.Queued); // not terminal yet (1 < MaxAttempts)
        row.SentAt.Should().BeNull();
    }

    // ── #10b Terminal attempt → row Failed (still rethrows so Hangfire records the failure) ──
    [Fact]
    public async Task Run_WhenSenderThrowsOnFinalAttempt_MarksRowFailed_AndRethrows()
    {
        // Seed one below the cap so this attempt is the terminal one.
        var id = SeedDelivery(NotificationDeliveryStatus.Queued, attempts: SendEmailJob.MaxAttempts - 1);
        SenderThrows("still down");

        var act = async () => await CreateJob().RunAsync(_tenantId, id, Message(_tenantId));

        await act.Should().ThrowAsync<InvalidOperationException>();

        var row = ReadDelivery(id);
        row.Attempts.Should().Be(SendEmailJob.MaxAttempts);
        row.Status.Should().Be(NotificationDeliveryStatus.Failed);
    }

    // ── #11 Idempotent: an already-Sent row is a no-op (no resend, no mutation) ──────────────
    [Fact]
    public async Task Run_WhenRowAlreadySent_DoesNotResend_AndLeavesRowUnchanged()
    {
        var id = SeedDelivery(NotificationDeliveryStatus.Sent, attempts: 1);

        await CreateJob().RunAsync(_tenantId, id, Message(_tenantId));

        await _sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        var row = ReadDelivery(id);
        row.Status.Should().Be(NotificationDeliveryStatus.Sent);
        row.Attempts.Should().Be(1); // untouched
    }

    // ── #12 Tenant scoping: global query filter hides tenant A's row from tenant B ───────────
    [Fact]
    public void NotificationDelivery_IsNotVisibleUnderAnotherTenantsContext()
    {
        var id = SeedDelivery(NotificationDeliveryStatus.Queued); // tenant A (_tenantId)

        // Tenant B context → row filtered out.
        using (var tenantB = TestDbContextFactory.Create(Guid.NewGuid(), _dbName))
            tenantB.NotificationDeliveries.Any(d => d.Id == id).Should().BeFalse();

        // Tenant A context → row visible.
        using var tenantA = TestDbContextFactory.Create(_tenantId, _dbName);
        tenantA.NotificationDeliveries.Any(d => d.Id == id).Should().BeTrue();
    }

    // ── #12 Job loads by id + EXPLICIT tenant id: wrong tenant → not found, no send ──────────
    [Fact]
    public async Task Run_WhenTenantIdDoesNotMatchRow_SkipsSend_AndLeavesRowUnchanged()
    {
        var id = SeedDelivery(NotificationDeliveryStatus.Queued); // owned by _tenantId
        var otherTenant = Guid.NewGuid();

        await CreateJob().RunAsync(otherTenant, id, Message(otherTenant));

        await _sender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        var row = ReadDelivery(id);
        row.Status.Should().Be(NotificationDeliveryStatus.Queued); // never touched
        row.Attempts.Should().Be(0);
    }
}
