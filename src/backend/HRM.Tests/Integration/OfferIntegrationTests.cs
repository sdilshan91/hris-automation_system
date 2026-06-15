// ============================================================================
// US-REC-007: Offer-letter generation/send/response/withdrawal integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (with the MediatR pipeline + global query filters), covering:
//   - Generate -> Draft + pdfStorageKey persisted (AC-1)
//   - Send -> Sent (AC-2)
//   - Accept -> applicant Hired + offer Accepted (AC-3/BR-3)
//   - Decline -> applicant stays in Offer (AC-3)
//   - Withdraw -> can't re-send (BR-7)
//   - Cross-tenant isolation: Tenant B cannot see/touch Tenant A's offers (AC-5)
//
// NOTE ON PROVIDER + HANGFIRE: mirrors InterviewSchedulingIntegrationTests —
// EF Core InMemory through the real composed pipeline (no PostgreSQL, no Docker in
// the verify gate). IOfferExpiryScheduler (the Hangfire seam) is INTENTIONALLY NOT
// registered, so the service runs its scheduling path as a no-op and tests never
// require real Hangfire storage. The PG-specific schema is validated by the
// `migrations` CI job applying the generated migration to real PG.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class OfferIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private Guid _vacancyA;
    private Guid _vacancyB;
    private Guid _applicantA;
    private Guid _applicantB;

    public OfferIntegrationTests()
    {
        SeedData();
    }

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

    private IMediator BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns("user@test.com");
        currentUser.IsAuthenticated.Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddSingleton<IFileStorage, InMemoryFileStorage>();
        services.AddScoped<IRecruitmentNotificationService, LogOnlyRecruitmentNotificationService>();
        // IOfferExpiryScheduler deliberately NOT registered (Hangfire no-op in tests).
        services.AddScoped<IOfferService, OfferService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GenerateOfferCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedData()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _vacancyA = Guid.NewGuid();
        _vacancyB = Guid.NewGuid();
        _applicantA = Guid.NewGuid();
        _applicantB = Guid.NewGuid();

        db.Tenants.AddRange(
            new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme Corp" },
            new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });

        db.Vacancies.AddRange(
            OpenVacancy(_vacancyA, _tenantA, "Backend Engineer (A)"),
            OpenVacancy(_vacancyB, _tenantB, "Backend Engineer (B)"));

        db.Applicants.AddRange(
            Applicant(_applicantA, _tenantA, _vacancyA, "ada@a.com", "Ada", "Lovelace"),
            Applicant(_applicantB, _tenantB, _vacancyB, "eve@b.com", "Eve", "Hopper"));

        db.SaveChanges();
    }

    private static Vacancy OpenVacancy(Guid id, Guid tenantId, string title) => new()
    {
        Id = id,
        TenantId = tenantId,
        ReferenceNumber = $"VAC-2026-{id.GetHashCode() & 0xFFF:D4}",
        Title = title,
        Status = VacancyStatus.Open,
        EmploymentType = EmploymentType.FullTime,
        Headcount = 1,
        Description = "Build things.",
        IsDeleted = false,
    };

    private static Applicant Applicant(Guid id, Guid tenantId, Guid vacancyId, string email, string first, string last) => new()
    {
        Id = id,
        TenantId = tenantId,
        VacancyId = vacancyId,
        ApplicationReferenceNumber = $"APP-2026-{Math.Abs(email.GetHashCode()) % 10000:D4}",
        FirstName = first,
        LastName = last,
        Email = email,
        ResumeStorageKey = $"recruitment/{vacancyId}/x/z.pdf",
        ResumeFileName = "resume.pdf",
        Stage = ApplicantStage.Offer,
        Source = ApplicationSource.Public,
        AppliedAt = DateTime.UtcNow,
        IsDeleted = false,
    };

    private static GenerateOfferCommand Generate(Guid applicantId, decimal salary = 120000m) => new(
        applicantId, "Senior Backend Engineer", null, null, salary, "USD",
        SalaryFrequency.Annual, "Health + 401k.",
        DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, 3, null);

    // ── Generate -> Draft + pdf key persisted (AC-1) ──────────────────

    [Fact]
    public async Task Generate_CreatesDraftOffer_WithPdfStorageKey()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Generate(_applicantA));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Status.Should().Be(OfferStatus.Draft);
        dto.Version.Should().Be(1);
        dto.VacancyId.Should().Be(_vacancyA);

        // The stored offer carries a tenant-scoped offers PDF path.
        var read = await mediator.Send(new GetOfferByIdQuery(dto.Id));
        read.IsSuccess.Should().BeTrue();

        var doc = await mediator.Send(new DownloadOfferPdfQuery(dto.Id));
        doc.IsSuccess.Should().BeTrue();
        doc.Value!.Content.Length.Should().BeGreaterThan(0);
    }

    // ── Send -> Sent (AC-2) ───────────────────────────────────────────

    [Fact]
    public async Task Send_DraftOffer_TransitionsToSent()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var draft = (await mediator.Send(Generate(_applicantA))).Value!;

        var sent = await mediator.Send(new SendOfferCommand(draft.Id));

        sent.IsSuccess.Should().BeTrue();
        sent.Value!.Status.Should().Be(OfferStatus.Sent);
        sent.Value.SentAt.Should().NotBeNull();
    }

    // ── Accept -> applicant Hired + offer Accepted (AC-3/BR-3) ────────

    [Fact]
    public async Task Accept_AdvancesApplicantToHired_AndOfferAccepted()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var draft = (await mediator.Send(Generate(_applicantA))).Value!;
        await mediator.Send(new SendOfferCommand(draft.Id));

        var responded = await mediator.Send(new RespondToOfferCommand(draft.Id, Accepted: true));

        responded.IsSuccess.Should().BeTrue();
        responded.Value!.Status.Should().Be(OfferStatus.Accepted);

        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
            new MutableTenantContext { TenantId = _tenantA });
        (await db.Applicants.FirstAsync(a => a.Id == _applicantA)).Stage
            .Should().Be(ApplicantStage.Hired);
    }

    // ── Decline -> applicant stays in Offer (AC-3) ────────────────────

    [Fact]
    public async Task Decline_LeavesApplicantInOffer()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var draft = (await mediator.Send(Generate(_applicantA))).Value!;
        await mediator.Send(new SendOfferCommand(draft.Id));

        var responded = await mediator.Send(new RespondToOfferCommand(draft.Id, Accepted: false));

        responded.IsSuccess.Should().BeTrue();
        responded.Value!.Status.Should().Be(OfferStatus.Declined);

        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
            new MutableTenantContext { TenantId = _tenantA });
        (await db.Applicants.FirstAsync(a => a.Id == _applicantA)).Stage
            .Should().Be(ApplicantStage.Offer);
    }

    // ── Withdraw -> can't re-send (BR-7) ──────────────────────────────

    [Fact]
    public async Task Withdraw_ThenSend_IsRejected()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var draft = (await mediator.Send(Generate(_applicantA))).Value!;

        var withdraw = await mediator.Send(new WithdrawOfferCommand(draft.Id));
        withdraw.IsSuccess.Should().BeTrue();
        withdraw.Value!.Status.Should().Be(OfferStatus.Withdrawn);

        var send = await mediator.Send(new SendOfferCommand(draft.Id));
        send.IsFailure.Should().BeTrue();
        send.StatusCode.Should().Be(409);
        send.ErrorCode.Should().Be("offer_withdrawn");
    }

    // ── Cross-tenant isolation (AC-5) ─────────────────────────────────

    [Fact]
    public async Task Generate_ForCrossTenantApplicant_Returns404()
    {
        var medB = BuildPipeline(_tenantB, _userB);

        // Tenant B tries to generate against Tenant A's applicant — filtered out → 404.
        var result = await medB.Send(Generate(_applicantA));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("applicant_not_found");
    }

    [Fact]
    public async Task GetById_TenantB_CannotSeeTenantAOffer()
    {
        var medA = BuildPipeline(_tenantA, _userA);
        var created = (await medA.Send(Generate(_applicantA))).Value!;

        var medB = BuildPipeline(_tenantB, _userB);
        var read = await medB.Send(new GetOfferByIdQuery(created.Id));

        read.IsFailure.Should().BeTrue();
        read.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ListByApplicant_TenantB_SeesNoneOfTenantAOffers()
    {
        var medA = BuildPipeline(_tenantA, _userA);
        await medA.Send(Generate(_applicantA));

        var medB = BuildPipeline(_tenantB, _userB);
        var list = await medB.Send(new ListOffersForApplicantQuery(_applicantA));

        list.IsSuccess.Should().BeTrue();
        list.Value!.Should().BeEmpty();
    }

    /// <summary>
    /// In-memory IFileStorage stub that keeps uploaded bytes per path so the offer-document download works
    /// through the full pipeline. Avoids the filesystem in tests.
    /// </summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public async Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            _store[$"{tenantId}/{relativePath}"] = ms.ToArray();
            return $"/{tenantId}/{relativePath}";
        }

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(
                _store.TryGetValue($"{tenantId}/{relativePath}", out var bytes) ? new MemoryStream(bytes) : null);

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
