// ============================================================================
// US-NTF-006 Phase 8 (tail) — BulkEmployeeImportService completion-notification unit tests (US-CHR-010).
//
// On async-import completion the service emails the initiator a `bulk_import_completed` summary via
// INotificationDispatcher. Load-bearing behaviours genuinely observed here:
//   • RECIPIENT — the email goes to BulkImportJob.InitiatedBy (a raw email string) via RecipientEmail (email-only:
//     the in-app leg is never used for a raw-address recipient).
//   • SKIP for non-users — an unauthenticated / system-driven import (InitiatedBy = "system", empty, or null) is
//     NOT emailed (there is no real person to notify).
//   • PAYLOAD-COMPLETENESS — the import.total / import.success / import.failed counts the template renders are
//     actually populated from the job.
//   • EVENT-KEY — the exact catalog event key `bulk_import_completed` is dispatched.
//   • NEVER-THROW — a dispatcher failure must not break the committed import.
//
// The dispatch method is exercised directly (the full ProcessImportJobAsync path needs an on-disk temp file); it
// resolves the recipient + builds the payload from the completed job — the same object the async path passes.
// PROVIDER: EF Core InMemory AppDbContext + a hand RecordingDispatcher (mirrors RealCoreHrNotificationServiceTests).
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class BulkEmployeeImportNotificationTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public BulkEmployeeImportNotificationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    private BulkEmployeeImportService Service(INotificationDispatcher dispatcher)
        => new(TestDbContextFactory.Create(_tenantContext, _dbName), _tenantContext, _currentUser,
            NullLogger<BulkEmployeeImportService>.Instance, dispatcher);

    private BulkImportJob CompletedJob(string? initiatedBy) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantId,
        FileName = "employees.csv",
        TotalRows = 10,
        SuccessCount = 8,
        FailedCount = 2,
        ProcessedRows = 10,
        Status = BulkImportStatus.Completed,
        InitiatedBy = initiatedBy,
        CompletedAt = DateTime.UtcNow,
    };

    // ── Fake dispatcher ──────────────────────────────────────────────────────────────────────────────
    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        private readonly bool _throw;
        public List<NotificationRequest> InApp { get; } = new();
        public List<NotificationRequest> Emails { get; } = new();

        public RecordingDispatcher(bool @throw = false) => _throw = @throw;

        public Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("in-app boom");
            InApp.Add(request);
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("email boom");
            Emails.Add(request);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Completion_DispatchesEmailToInitiator_WithPopulatedCounts_AndNoInApp()
    {
        var dispatcher = new RecordingDispatcher();
        var job = CompletedJob("initiator@test.com");

        await Service(dispatcher).DispatchImportCompletedAsync(job, CancellationToken.None);

        dispatcher.InApp.Should().BeEmpty("the initiator is a raw email address — email-only, no in-app leg");
        dispatcher.Emails.Should().ContainSingle();

        var req = dispatcher.Emails[0];
        req.EventKey.Should().Be("bulk_import_completed");
        req.TenantId.Should().Be(_tenantId);
        req.RecipientEmail.Should().Be("initiator@test.com");
        req.RecipientUserId.Should().BeNull();

        using var doc = JsonDocument.Parse(req.PayloadJson);
        var import = doc.RootElement.GetProperty("import");
        import.GetProperty("total").GetInt32().Should().Be(10);
        import.GetProperty("success").GetInt32().Should().Be(8);
        import.GetProperty("failed").GetInt32().Should().Be(2);
        import.GetProperty("jobId").GetString().Should().Be(job.Id.ToString());
    }

    [Theory]
    [InlineData("system")]
    [InlineData("System")]   // case-insensitive
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Completion_SkipsDispatch_WhenInitiatorIsSystemOrUnknown(string? initiatedBy)
    {
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).DispatchImportCompletedAsync(CompletedJob(initiatedBy), CancellationToken.None);

        dispatcher.Emails.Should().BeEmpty();
        dispatcher.InApp.Should().BeEmpty();
    }

    [Fact]
    public async Task Completion_NeverThrows_WhenDispatcherFails()
    {
        var dispatcher = new RecordingDispatcher(@throw: true);

        var act = async () =>
            await Service(dispatcher).DispatchImportCompletedAsync(CompletedJob("initiator@test.com"), CancellationToken.None);

        await act.Should().NotThrowAsync("a delivery failure must not fail the committed import");
    }
}
