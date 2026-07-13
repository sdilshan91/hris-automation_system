// ============================================================================
// US-REC-007: Offer-letter generation/send/response/withdrawal — service unit tests.
//
// Drives OfferService through the real EF Core InMemory provider (so offer rows +
// the applicant Hired transition are actually persisted) with a small capturing
// IFileStorage stub (so the generated PDF is stored + retrievable for download).
// Covers the core business rules:
//   - AC-1/FR-2/FR-3: generate -> Draft + pdfStorageKey set + a real PDF stored
//   - BR-6: a null expiry defaults to generation date + 7 days
//   - AC-2: send -> Sent + sentAt set
//   - BR-7: a withdrawn offer cannot be re-sent
//   - BR-2/FR-9: a second generate supersedes the prior active offer + bumps version
//   - AC-3/BR-3: accept -> offer Accepted + applicant advances to Hired
//   - AC-3: decline -> offer Declined + applicant stays in Offer
//   - status-transition guards (respond/withdraw on the wrong status -> 409)
//
// The Hangfire IOfferExpiryScheduler seam is INTENTIONALLY NOT supplied (null), so
// the service runs its scheduling path as a no-op and tests never require real
// Hangfire storage (mirrors the interview reminder tests).
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OfferServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly CapturingFileStorage _fileStorage = new();

    private readonly Guid _vacancyId = Guid.NewGuid();
    private readonly Guid _applicantId = Guid.NewGuid();

    public OfferServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        SeedApplicant();
    }

    private AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, _tenantContext);

    private OfferService CreateService(AppDbContext db) => new(
        db,
        _tenantContext,
        _fileStorage,
        Substitute.For<IRecruitmentNotificationService>(),
        Substitute.For<ILogger<OfferService>>(),
        new GanssHtmlSanitizer(), // REAL sanitizer (ISSUE-226) — actually strips XSS vectors.
        expiryScheduler: null); // Hangfire seam absent -> no-op scheduling in tests.

    private void SeedApplicant(ApplicantStage stage = ApplicantStage.Offer)
    {
        using var db = CreateDb();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme Corp" });
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
        db.Applicants.Add(new Applicant
        {
            Id = _applicantId,
            TenantId = _tenantId,
            VacancyId = _vacancyId,
            ApplicationReferenceNumber = "APP-2026-0001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@a.com",
            ResumeStorageKey = "recruitment/x/y/z.pdf",
            ResumeFileName = "resume.pdf",
            Stage = stage,
            Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow,
            IsDeleted = false,
        });
        db.SaveChanges();
    }

    private static GenerateOfferInput Input(DateOnly? expiry = null) => new()
    {
        OfferedPosition = "Senior Backend Engineer",
        SalaryAmount = 120000m,
        Currency = "usd",
        SalaryFrequency = SalaryFrequency.Annual,
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        ExpiryDate = expiry,
        ProbationMonths = 3,
        BenefitsSummary = "Health + 401k.",
    };

    // ── AC-1/FR-2/FR-3: generate -> Draft + pdf stored ────────────────

    [Fact]
    public async Task Generate_CreatesDraftOffer_WithStoredPdf()
    {
        using var db = CreateDb();
        var svc = CreateService(db);

        var result = await svc.GenerateAsync(Input() with { ApplicantId = _applicantId });

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Status.Should().Be(OfferStatus.Draft);
        dto.StatusName.Should().Be("Draft");
        dto.Version.Should().Be(1);
        dto.OfferReferenceNumber.Should().StartWith("OFR-");
        dto.Currency.Should().Be("USD");
        dto.SalaryFrequency.Should().Be(SalaryFrequency.Annual);
        dto.SalaryFrequencyName.Should().Be("Annual");

        // The PDF was stored under the tenant-scoped offers path and is a real (non-empty) PDF.
        _fileStorage.Stored.Should().ContainSingle();
        var (key, bytes) = _fileStorage.Stored.Single();
        key.Should().Contain($"recruitment/{_vacancyId}/{_applicantId}/offers/");
        key.Should().EndWith(".pdf");
        bytes.Length.Should().BeGreaterThan(100);

        // Persisted with a storage key.
        var stored = await db.Offers.AsNoTracking().FirstAsync(o => o.Id == dto.Id);
        stored.PdfStorageKey.Should().Be(key);
    }

    // ── ISSUE-226: recruiter free-text fields are HTML-sanitized on write (stored-XSS defense-in-depth) ─

    [Fact]
    public async Task Generate_StripsXssFromRecruiterFreeTextFields_Issue226()
    {
        using var db = CreateDb();
        var svc = CreateService(db);

        var result = await svc.GenerateAsync(Input() with
        {
            ApplicantId = _applicantId,
            OfferedPosition = "Engineer <script>bad</script>",
            BenefitsSummary = "<img src=x onerror=alert(1)> Health + 401k.",
            CustomClauses = "<script>alert(1)</script> Standard terms apply.",
        });

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;

        // XSS vectors stripped: no <script> element, no onerror handler survives.
        dto.OfferedPosition.Should().NotContain("<script>");
        dto.OfferedPosition.Should().Contain("Engineer");

        dto.BenefitsSummary.Should().NotContain("onerror");
        dto.BenefitsSummary.Should().Contain("Health + 401k.");

        dto.CustomClauses.Should().NotContain("<script>");
        dto.CustomClauses.Should().Contain("Standard terms apply.");

        // The sanitized values are what actually got persisted (sanitize-on-write is the source of truth).
        var stored = await db.Offers.AsNoTracking().FirstAsync(o => o.Id == dto.Id);
        stored.CustomClauses.Should().NotContain("<script>");
        stored.CustomClauses.Should().NotContain("onerror");
        stored.BenefitsSummary.Should().NotContain("onerror");
        stored.OfferedPosition.Should().NotContain("<script>");
    }

    [Fact]
    public async Task Generate_StripsJavascriptSchemeAndInlineStyle_Issue226()
    {
        using var db = CreateDb();
        var svc = CreateService(db);

        var result = await svc.GenerateAsync(Input() with
        {
            ApplicantId = _applicantId,
            CustomClauses = "<a href=\"javascript:alert(1)\" style=\"color:red\">click</a> ok.",
        });

        result.IsSuccess.Should().BeTrue();
        var clauses = result.Value!.CustomClauses!;

        // Ganss allow-list restricts URL schemes to http/https/mailto and drops inline style.
        clauses.Should().NotContain("javascript:");
        clauses.Should().NotContain("style=");
        clauses.Should().Contain("ok.");
    }

    // ── BR-6: null expiry defaults to +7 days ─────────────────────────

    [Fact]
    public async Task Generate_NullExpiry_DefaultsToSevenDays()
    {
        using var db = CreateDb();
        var svc = CreateService(db);

        var result = await svc.GenerateAsync(Input() with { ApplicantId = _applicantId, ExpiryDate = null });

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpiryDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7));
    }

    [Fact]
    public async Task Generate_ZeroSalary_IsRejected()
    {
        using var db = CreateDb();
        var svc = CreateService(db);

        var result = await svc.GenerateAsync(Input() with { ApplicantId = _applicantId, SalaryAmount = 0m });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("salary_invalid");
    }

    // ── AC-2: send -> Sent ────────────────────────────────────────────

    [Fact]
    public async Task Send_DraftOffer_TransitionsToSent()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var draft = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;

        var sent = await svc.SendAsync(draft.Id);

        sent.IsSuccess.Should().BeTrue();
        sent.Value!.Status.Should().Be(OfferStatus.Sent);
        sent.Value.SentAt.Should().NotBeNull();
    }

    // ── BR-7: a withdrawn offer cannot be re-sent ─────────────────────

    [Fact]
    public async Task Send_WithdrawnOffer_IsRejected()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var draft = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;
        await svc.WithdrawAsync(draft.Id);

        var send = await svc.SendAsync(draft.Id);

        send.IsFailure.Should().BeTrue();
        send.StatusCode.Should().Be(409);
        send.ErrorCode.Should().Be("offer_withdrawn");
    }

    // ── BR-2/FR-9: a second generate supersedes the prior active offer ─

    [Fact]
    public async Task Generate_Second_SupersedesPriorActive_AndBumpsVersion()
    {
        using var db = CreateDb();
        var svc = CreateService(db);

        var first = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;
        var second = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId, SalaryAmount = 130000m })).Value!;

        second.Version.Should().Be(2);

        // The first offer is now withdrawn (superseded, BR-2). The second is the only active one.
        var firstReloaded = await db.Offers.AsNoTracking().FirstAsync(o => o.Id == first.Id);
        firstReloaded.Status.Should().Be(OfferStatus.Withdrawn);

        var actives = await db.Offers.AsNoTracking()
            .Where(o => o.ApplicantId == _applicantId &&
                        (o.Status == OfferStatus.Draft || o.Status == OfferStatus.Sent))
            .ToListAsync();
        actives.Should().ContainSingle().Which.Id.Should().Be(second.Id);
    }

    // ── AC-3/BR-3: accept -> Accepted + applicant Hired ───────────────

    [Fact]
    public async Task Respond_Accept_AdvancesApplicantToHired()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var draft = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;
        await svc.SendAsync(draft.Id);

        var responded = await svc.RespondAsync(draft.Id, new RespondToOfferInput { Accepted = true });

        responded.IsSuccess.Should().BeTrue();
        responded.Value!.Status.Should().Be(OfferStatus.Accepted);
        responded.Value.Response.Should().Be("Accepted");
        responded.Value.RespondedAt.Should().NotBeNull();

        var applicant = await db.Applicants.AsNoTracking().FirstAsync(a => a.Id == _applicantId);
        applicant.Stage.Should().Be(ApplicantStage.Hired);

        var history = await db.ApplicantStageHistories.AsNoTracking()
            .Where(h => h.ApplicantId == _applicantId && h.ToStage == ApplicantStage.Hired)
            .ToListAsync();
        history.Should().ContainSingle();
    }

    // ── AC-3: decline -> Declined + applicant stays in Offer ──────────

    [Fact]
    public async Task Respond_Decline_LeavesApplicantInOffer()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var draft = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;
        await svc.SendAsync(draft.Id);

        var responded = await svc.RespondAsync(draft.Id, new RespondToOfferInput { Accepted = false });

        responded.IsSuccess.Should().BeTrue();
        responded.Value!.Status.Should().Be(OfferStatus.Declined);

        var applicant = await db.Applicants.AsNoTracking().FirstAsync(a => a.Id == _applicantId);
        applicant.Stage.Should().Be(ApplicantStage.Offer);
    }

    [Fact]
    public async Task Respond_OnDraft_IsRejected()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var draft = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;

        var responded = await svc.RespondAsync(draft.Id, new RespondToOfferInput { Accepted = true });

        responded.IsFailure.Should().BeTrue();
        responded.StatusCode.Should().Be(409);
        responded.ErrorCode.Should().Be("offer_not_sent");
    }

    [Fact]
    public async Task Withdraw_TerminalOffer_IsRejected()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var draft = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;
        await svc.SendAsync(draft.Id);
        await svc.RespondAsync(draft.Id, new RespondToOfferInput { Accepted = true });

        var withdraw = await svc.WithdrawAsync(draft.Id);

        withdraw.IsFailure.Should().BeTrue();
        withdraw.StatusCode.Should().Be(409);
        withdraw.ErrorCode.Should().Be("offer_not_active");
    }

    // ── Download streams the stored PDF (AC-1/FR-4) ───────────────────

    [Fact]
    public async Task GetDocument_ReturnsStoredPdf()
    {
        using var db = CreateDb();
        var svc = CreateService(db);
        var draft = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;

        var doc = await svc.GetDocumentAsync(draft.Id);

        doc.IsSuccess.Should().BeTrue();
        doc.Value!.ContentType.Should().Be("application/pdf");
        doc.Value.FileName.Should().Contain(draft.OfferReferenceNumber);
        doc.Value.Content.Length.Should().BeGreaterThan(100);
    }

    // ══ ISSUE-262 / FR-7/AC-4: the "N days before expiry" reminder job + scheduling ════════════════════

    /// <summary>N days-before-expiry the reminder fires — mirrors OfferService.ExpiryReminderDaysBefore.</summary>
    private const int ReminderDaysBefore = 3;

    private OfferService CreateServiceWithReminder(
        AppDbContext db,
        IOfferExpiryReminderScheduler reminderScheduler,
        IRecruitmentNotificationService? notifications = null) => new(
        db,
        _tenantContext,
        _fileStorage,
        notifications ?? Substitute.For<IRecruitmentNotificationService>(),
        Substitute.For<ILogger<OfferService>>(),
        new GanssHtmlSanitizer(), // REAL sanitizer (ISSUE-226).
        expiryScheduler: null,
        expiryReminderScheduler: reminderScheduler);

    // ── Send schedules the reminder N days before expiry + persists the job id ────────────

    [Fact]
    public async Task Send_SchedulesExpiryReminder_AndStoresJobId()
    {
        using var db = CreateDb();
        var reminderScheduler = Substitute.For<IOfferExpiryReminderScheduler>();
        reminderScheduler.Schedule(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns("rem-job-1");
        var svc = CreateServiceWithReminder(db, reminderScheduler);

        var expiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        var draft = (await svc.GenerateAsync(Input(expiry) with { ApplicantId = _applicantId })).Value!;

        await svc.SendAsync(draft.Id);

        var expectedFire = DateTime.SpecifyKind(
            expiry.AddDays(-ReminderDaysBefore).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        reminderScheduler.Received(1).Schedule(_tenantId, draft.Id, expectedFire);

        var stored = await db.Offers.AsNoTracking().FirstAsync(o => o.Id == draft.Id);
        stored.ExpiryReminderJobId.Should().Be("rem-job-1");
    }

    // ── Withdraw cancels the reminder + nulls the field ───────────────────────────────────

    [Fact]
    public async Task Withdraw_CancelsExpiryReminder_AndNullsJobId()
    {
        using var db = CreateDb();
        var reminderScheduler = Substitute.For<IOfferExpiryReminderScheduler>();
        reminderScheduler.Schedule(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns("rem-job-1");
        var svc = CreateServiceWithReminder(db, reminderScheduler);

        var draft = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;
        await svc.SendAsync(draft.Id);

        await svc.WithdrawAsync(draft.Id);

        reminderScheduler.Received(1).Cancel("rem-job-1");
        var stored = await db.Offers.AsNoTracking().FirstAsync(o => o.Id == draft.Id);
        stored.ExpiryReminderJobId.Should().BeNull();
    }

    // ── Respond cancels the reminder + nulls the field ────────────────────────────────────

    [Fact]
    public async Task Respond_CancelsExpiryReminder_AndNullsJobId()
    {
        using var db = CreateDb();
        var reminderScheduler = Substitute.For<IOfferExpiryReminderScheduler>();
        reminderScheduler.Schedule(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns("rem-job-1");
        var svc = CreateServiceWithReminder(db, reminderScheduler);

        var draft = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;
        await svc.SendAsync(draft.Id);

        await svc.RespondAsync(draft.Id, new RespondToOfferInput { Accepted = true });

        reminderScheduler.Received(1).Cancel("rem-job-1");
        var stored = await db.Offers.AsNoTracking().FirstAsync(o => o.Id == draft.Id);
        stored.ExpiryReminderJobId.Should().BeNull();
    }

    // ── Re-generate (supersede) cancels the prior offer's reminder ────────────────────────

    [Fact]
    public async Task Regenerate_CancelsPriorReminder_AndNullsJobId()
    {
        using var db = CreateDb();
        var reminderScheduler = Substitute.For<IOfferExpiryReminderScheduler>();
        reminderScheduler.Schedule(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns("rem-job-1");
        var svc = CreateServiceWithReminder(db, reminderScheduler);

        var first = (await svc.GenerateAsync(Input() with { ApplicantId = _applicantId })).Value!;
        await svc.SendAsync(first.Id);

        // Superseding the sent offer with a new generate must cancel the prior offer's reminder.
        await svc.GenerateAsync(Input() with { ApplicantId = _applicantId, SalaryAmount = 130000m });

        reminderScheduler.Received(1).Cancel("rem-job-1");
        var firstReloaded = await db.Offers.AsNoTracking().FirstAsync(o => o.Id == first.Id);
        firstReloaded.ExpiryReminderJobId.Should().BeNull();
    }

    // ── The reminder JOB: happy path fires the "offer-expiry-reminder" notification ───────

    [Fact]
    public async Task ReminderJob_ActiveOffer_FiresExpiryReminderNotification()
    {
        var notifications = Substitute.For<IRecruitmentNotificationService>();
        var offerId = SeedOfferForJob(OfferStatus.Sent, reminderJobId: "rem-job-1");
        var provider = BuildJobProvider(notifications);

        var job = new OfferExpiryReminderJob(provider.GetRequiredService<IServiceScopeFactory>());
        await job.RunAsync(_tenantId, offerId);

        await notifications.Received(1).NotifyOfferAsync(
            "offer-expiry-reminder", offerId, _applicantId, _vacancyId, Arg.Any<string>());

        // The reminder job id is cleared once fired (read back via the tenant-resolved test context).
        using var db = CreateDb();
        var offer = await db.Offers.AsNoTracking().FirstAsync(o => o.Id == offerId);
        offer.ExpiryReminderJobId.Should().BeNull();
    }

    // ── The reminder JOB: idempotency — no notification when the offer is no longer active ─

    [Fact]
    public async Task ReminderJob_InactiveOffer_DoesNotNotify()
    {
        var notifications = Substitute.For<IRecruitmentNotificationService>();
        // Already accepted (terminal) — the candidate responded before the reminder fired.
        var offerId = SeedOfferForJob(OfferStatus.Accepted, reminderJobId: "rem-job-1");
        var provider = BuildJobProvider(notifications);

        var job = new OfferExpiryReminderJob(provider.GetRequiredService<IServiceScopeFactory>());
        await job.RunAsync(_tenantId, offerId);

        await notifications.DidNotReceive().NotifyOfferAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private ServiceProvider BuildJobProvider(IRecruitmentNotificationService notifications)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        // RLS increment 2c: the reminder/expiry jobs wrap their body in ITenantJobRunner — register the real
        // runner + an empty IConfiguration (Rls:Enabled defaults false → runs the work directly, no tx/GUC).
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(notifications);
        return services.BuildServiceProvider();
    }

    /// <summary>Seeds an Offer row (in the shared InMemory db) in the given status for the job tests.</summary>
    private Guid SeedOfferForJob(OfferStatus status, string? reminderJobId)
    {
        using var db = CreateDb();
        var offerId = BaseEntity.NewUuidV7();
        db.Offers.Add(new Offer
        {
            Id = offerId,
            TenantId = _tenantId,
            ApplicantId = _applicantId,
            VacancyId = _vacancyId,
            OfferReferenceNumber = "OFR-2026-9001",
            Status = status,
            OfferedPosition = "Senior Backend Engineer",
            SalaryAmount = 120000m,
            Currency = "USD",
            SalaryFrequency = SalaryFrequency.Annual,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(4),
            PdfStorageKey = "recruitment/x/y/offers/z.pdf",
            ExpiryReminderJobId = reminderJobId,
            IsDeleted = false,
        });
        db.SaveChanges();
        return offerId;
    }

    /// <summary>
    /// Capturing IFileStorage stub: keeps uploaded bytes per relative path so the download path can read
    /// them back. Avoids the filesystem while still exercising the store-then-retrieve flow in OfferService.
    /// </summary>
    private sealed class CapturingFileStorage : IFileStorage
    {
        public readonly List<(string Key, byte[] Bytes)> Stored = new();

        public async Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            Stored.Add((relativePath, ms.ToArray()));
            return $"/{tenantId}/{relativePath}";
        }

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
        {
            var match = Stored.LastOrDefault(s => s.Key == relativePath);
            return Task.FromResult<Stream?>(match.Bytes is null ? null : new MemoryStream(match.Bytes));
        }

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
