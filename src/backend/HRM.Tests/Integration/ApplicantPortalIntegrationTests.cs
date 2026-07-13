// ============================================================================
// US-REC-008: Candidate-portal (magic link) integration tests.
//
// Exercises the full handler -> service -> token-service -> DbContext path through a
// real DI container (with the MediatR pipeline + global query filters), covering:
//   - Valid token -> correct sanitized applicant dashboard (AC-1)
//   - Expired token rejected (FR-8 / test hint)
//   - Cross-tenant token denied (AC-4: a Tenant A token on Tenant B subdomain)
//   - Accept offer via portal -> offer Accepted + applicant Hired (AC-3/BR-3)
//   - Sanitized DTO excludes rejection reason / scorecard / internal notes (NFR-5/BR-3)
//
// NOTE ON PROVIDER: mirrors OfferIntegrationTests — EF Core InMemory through the real
// composed pipeline (no PostgreSQL, no Docker in the verify gate). IOfferExpiryScheduler
// (the Hangfire seam) is INTENTIONALLY NOT registered. The PG-specific schema is validated
// by the `migrations` CI job applying the generated migration to real PG.
//
// The HMAC signing secret is supplied via in-memory IConfiguration (Recruitment:PortalTokenSecret)
// — never hardcoded into the service.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Recruitment;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class ApplicantPortalIntegrationTests
{
    private const string PortalSecret = "integration-test-portal-secret-abcdefghijklmnop";

    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();

    private Guid _vacancyA;
    private Guid _vacancyB;
    private Guid _departmentA;
    private Guid _applicantA;        // Ada @ tenant A, stage Interview, has interview + sent offer
    private Guid _applicantB;        // Eve @ tenant B
    private Guid _rejectedApplicant; // Rejected applicant at tenant A (sanitization check)
    private Guid _interviewerEmp;
    private Guid _offerA;

    /// <summary>Per-instance in-memory file store (each test instance has its own DB + files — no shared static state).</summary>
    private readonly Dictionary<string, byte[]> _files = new();

    private const string EmailA = "ada@a.com";
    private const string EmailRejected = "rex@a.com";

    public ApplicantPortalIntegrationTests()
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

    private IMediator BuildPipeline(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(_userA);
        currentUser.Email.Returns("user@test.com");
        currentUser.IsAuthenticated.Returns(true);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:PortalTokenSecret"] = PortalSecret,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(config);

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddSingleton<IFileStorage>(new InMemoryFileStorage(_files));
        services.AddScoped<IRecruitmentNotificationService, LogOnlyRecruitmentNotificationService>();
        services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>(); // ISSUE-226 XSS sanitizer (OfferService dep).
        services.AddScoped<IOfferService, OfferService>();
        services.AddScoped<IApplicantPortalTokenService, ApplicantPortalTokenService>();
        services.AddScoped<IApplicantPortalService, ApplicantPortalService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(GetPortalDashboardQuery).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    /// <summary>Mints a signed token directly (so tests can craft expired / cross-tenant tokens too).</summary>
    private static string MintToken(Guid tenantId, string email, DateTime expiresAt)
        => PortalMagicLink.Issue(tenantId, email, expiresAt, PortalSecret);

    /// <summary>Persists the matching token-hash row so ValidateAsync's stored-row check passes.</summary>
    private void PersistTokenRow(Guid tenantId, string email, string token, DateTime expiresAt)
    {
        using var db = NewDb(tenantId);
        db.ApplicantPortalTokens.Add(new ApplicantPortalToken
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            ApplicantEmail = email.ToLowerInvariant(),
            TokenHash = PortalMagicLink.HashToken(token),
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
        });
        db.SaveChanges();
    }

    private AppDbContext NewDb(Guid tenantId) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
        new MutableTenantContext { TenantId = tenantId });

    private void SeedData()
    {
        _vacancyA = Guid.NewGuid();
        _vacancyB = Guid.NewGuid();
        _departmentA = Guid.NewGuid();
        _applicantA = Guid.NewGuid();
        _applicantB = Guid.NewGuid();
        _rejectedApplicant = Guid.NewGuid();
        _interviewerEmp = Guid.NewGuid();
        _offerA = Guid.NewGuid();

        using var db = NewDb(_tenantA);

        db.Tenants.AddRange(
            new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme Corp" },
            new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });

        db.Departments.Add(new Department
        {
            Id = _departmentA, TenantId = _tenantA, Name = "Engineering", IsDeleted = false,
        });

        db.Vacancies.AddRange(
            OpenVacancy(_vacancyA, _tenantA, "Backend Engineer (A)", _departmentA),
            OpenVacancy(_vacancyB, _tenantB, "Backend Engineer (B)", null));

        db.Applicants.AddRange(
            Applicant(_applicantA, _tenantA, _vacancyA, EmailA, "Ada", "Lovelace", ApplicantStage.Interview),
            Applicant(_applicantB, _tenantB, _vacancyB, "eve@b.com", "Eve", "Hopper", ApplicantStage.Applied),
            RejectedApplicant());

        db.Employees.Add(new Employee
        {
            Id = _interviewerEmp,
            TenantId = _tenantA,
            FirstName = "Grace",
            LastName = "Hopper",
            Email = "grace@a.com",
            Status = EmployeeStatus.Active,
            IsDeleted = false,
        });

        // An upcoming, Scheduled interview for Ada (AC-2). Internal-only Notes set to prove sanitization.
        var interviewId = Guid.NewGuid();
        db.Interviews.Add(new Interview
        {
            Id = interviewId,
            TenantId = _tenantA,
            ApplicantId = _applicantA,
            VacancyId = _vacancyA,
            RoundNumber = 1,
            InterviewType = InterviewType.Video,
            ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = new TimeOnly(10, 0),
            DurationMinutes = 60,
            VideoLink = "https://meet.example.com/abc",
            Notes = "INTERNAL: candidate seems nervous - do not show",
            Status = InterviewStatus.Scheduled,
            IsDeleted = false,
        });
        db.InterviewInterviewers.Add(new InterviewInterviewer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantA,
            InterviewId = interviewId,
            EmployeeId = _interviewerEmp,
            IsDeleted = false,
        });

        // A Sent offer for Ada (AC-3) with a stored PDF.
        db.Offers.Add(new Offer
        {
            Id = _offerA,
            TenantId = _tenantA,
            ApplicantId = _applicantA,
            VacancyId = _vacancyA,
            OfferReferenceNumber = "OFR-2026-0001",
            Status = OfferStatus.Sent,
            OfferedPosition = "Senior Backend Engineer",
            SalaryAmount = 150000m,
            Currency = "USD",
            SalaryFrequency = SalaryFrequency.Annual,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45)),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)),
            PdfStorageKey = $"recruitment/{_vacancyA}/{_applicantA}/offers/offer.pdf",
            Version = 1,
            SentAt = DateTime.UtcNow.AddDays(-1),
            IsDeleted = false,
        });

        // Stage-history rows including a sanitized Interview move + a Rejected move with internal data.
        db.ApplicantStageHistories.Add(new ApplicantStageHistory
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantA,
            ApplicantId = _applicantA,
            FromStage = ApplicantStage.Screening,
            ToStage = ApplicantStage.Interview,
            Reason = "INTERNAL: strong CV, fast-track",
            Notes = "INTERNAL: refer to panel notes doc",
            ChangedAt = DateTime.UtcNow.AddDays(-2),
            IsDeleted = false,
        });
        db.ApplicantStageHistories.Add(new ApplicantStageHistory
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantA,
            ApplicantId = _rejectedApplicant,
            FromStage = ApplicantStage.Screening,
            ToStage = ApplicantStage.Rejected,
            Reason = "INTERNAL: failed the take-home",
            RejectionReason = RejectionReason.NotQualified,
            Notes = "INTERNAL: do not re-engage",
            ChangedAt = DateTime.UtcNow.AddDays(-1),
            IsDeleted = false,
        });

        db.SaveChanges();

        // Seed the offer PDF bytes into the in-memory storage used by the pipeline (download path).
        _files[$"{_tenantA}/recruitment/{_vacancyA}/{_applicantA}/offers/offer.pdf"] = [1, 2, 3, 4];
    }

    private static Vacancy OpenVacancy(Guid id, Guid tenantId, string title, Guid? deptId) => new()
    {
        Id = id,
        TenantId = tenantId,
        ReferenceNumber = $"VAC-2026-{id.GetHashCode() & 0xFFF:D4}",
        Title = title,
        DepartmentId = deptId,
        Status = VacancyStatus.Open,
        EmploymentType = EmploymentType.FullTime,
        Headcount = 1,
        Description = "Build things.",
        IsDeleted = false,
    };

    private static Applicant Applicant(Guid id, Guid tenantId, Guid vacancyId, string email, string first, string last, ApplicantStage stage) => new()
    {
        Id = id,
        TenantId = tenantId,
        VacancyId = vacancyId,
        ApplicationReferenceNumber = $"APP-2026-{Math.Abs(email.GetHashCode()) % 10000:D4}",
        FirstName = first,
        LastName = last,
        Email = email,
        ResumeStorageKey = $"recruitment/{vacancyId}/{id}/resume.pdf",
        ResumeFileName = "resume.pdf",
        Stage = stage,
        Source = ApplicationSource.Public,
        AppliedAt = DateTime.UtcNow.AddDays(-5),
        IsDeleted = false,
    };

    private Applicant RejectedApplicant() => new()
    {
        Id = _rejectedApplicant,
        TenantId = _tenantA,
        VacancyId = _vacancyA,
        ApplicationReferenceNumber = "APP-2026-9999",
        FirstName = "Rex",
        LastName = "Reject",
        Email = EmailRejected,
        ResumeStorageKey = $"recruitment/{_vacancyA}/{_rejectedApplicant}/resume.pdf",
        ResumeFileName = "resume.pdf",
        Stage = ApplicantStage.Rejected,
        RejectionReason = RejectionReason.NotQualified,
        Source = ApplicationSource.Public,
        AppliedAt = DateTime.UtcNow.AddDays(-6),
        IsDeleted = false,
    };

    // ── AC-1: valid token -> correct sanitized dashboard ──────────────

    [Fact]
    public async Task ValidToken_ReturnsCorrectApplicantDashboard()
    {
        var expiry = DateTime.UtcNow.AddDays(30);
        var token = MintToken(_tenantA, EmailA, expiry);
        PersistTokenRow(_tenantA, EmailA, token, expiry);

        var mediator = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetPortalDashboardQuery(token));

        result.IsSuccess.Should().BeTrue();
        var dash = result.Value!;
        dash.ApplicantEmail.Should().Be(EmailA);
        dash.ApplicantName.Should().Be("Ada Lovelace");
        dash.Applications.Should().HaveCount(1);

        var app = dash.Applications[0];
        app.VacancyTitle.Should().Be("Backend Engineer (A)");
        app.DepartmentName.Should().Be("Engineering");
        app.CurrentStage.Should().Be(ApplicantStage.Interview);

        // AC-1 step indicator: Applied + Screening completed, Interview current.
        app.Steps.Should().Contain(s => s.Stage == ApplicantStage.Applied && s.IsCompleted);
        app.Steps.Should().Contain(s => s.Stage == ApplicantStage.Screening && s.IsCompleted);
        app.Steps.Should().Contain(s => s.Stage == ApplicantStage.Interview && s.IsCurrent);
        app.Steps.Should().Contain(s => s.Stage == ApplicantStage.Offer && !s.IsCompleted && !s.IsCurrent);

        // AC-2 interview + AC-3 offer.
        app.UpcomingInterview.Should().NotBeNull();
        app.UpcomingInterview!.InterviewerNames.Should().Contain("Grace Hopper");
        app.UpcomingInterview.VideoLink.Should().Be("https://meet.example.com/abc");
        app.ActiveOffer.Should().NotBeNull();
        app.ActiveOffer!.OfferedPosition.Should().Be("Senior Backend Engineer");
        app.ActiveOffer.SalaryAmount.Should().Be(150000m);
        app.ActiveOffer.CanRespond.Should().BeTrue();
        app.ActiveOffer.IsDocumentAvailable.Should().BeTrue();
    }

    // ── FR-8: expired token rejected ──────────────────────────────────

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var expiry = DateTime.UtcNow.AddMinutes(-1);
        var token = MintToken(_tenantA, EmailA, expiry);
        PersistTokenRow(_tenantA, EmailA, token, expiry);

        var mediator = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetPortalDashboardQuery(token));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
        result.ErrorCode.Should().Be("token_expired");
    }

    // ── AC-4: cross-tenant token denied ───────────────────────────────

    [Fact]
    public async Task CrossTenantToken_IsDenied()
    {
        // Token minted for Tenant A, presented against Tenant B's subdomain pipeline.
        var expiry = DateTime.UtcNow.AddDays(30);
        var token = MintToken(_tenantA, EmailA, expiry);
        PersistTokenRow(_tenantA, EmailA, token, expiry);

        var mediatorB = BuildPipeline(_tenantB);
        var result = await mediatorB.Send(new GetPortalDashboardQuery(token));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(401);
        result.ErrorCode.Should().Be("invalid_token");
    }

    // ── AC-3/BR-3: accept offer via portal -> Accepted + Hired ────────

    [Fact]
    public async Task AcceptOfferViaPortal_OfferAccepted_AndApplicantHired()
    {
        var expiry = DateTime.UtcNow.AddDays(30);
        var token = MintToken(_tenantA, EmailA, expiry);
        PersistTokenRow(_tenantA, EmailA, token, expiry);

        var mediator = BuildPipeline(_tenantA);
        var result = await mediator.Send(new RespondToPortalOfferCommand(token, _offerA, Accept: true));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(OfferStatus.Accepted);

        using var db = NewDb(_tenantA);
        (await db.Offers.FirstAsync(o => o.Id == _offerA)).Status.Should().Be(OfferStatus.Accepted);
        (await db.Applicants.FirstAsync(a => a.Id == _applicantA)).Stage.Should().Be(ApplicantStage.Hired);
    }

    [Fact]
    public async Task RespondToOffer_NotTheTokenHoldersOffer_IsForbidden()
    {
        // Rex's token cannot respond to Ada's offer (BR-4 token-scoping).
        var expiry = DateTime.UtcNow.AddDays(30);
        var token = MintToken(_tenantA, EmailRejected, expiry);
        PersistTokenRow(_tenantA, EmailRejected, token, expiry);

        var mediator = BuildPipeline(_tenantA);
        var result = await mediator.Send(new RespondToPortalOfferCommand(token, _offerA, Accept: true));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("forbidden");
    }

    // ── NFR-5/BR-3: sanitized DTO excludes internal data ──────────────

    [Fact]
    public async Task Dashboard_DoesNotExposeRejectionReason_Scorecards_OrInternalNotes()
    {
        var expiry = DateTime.UtcNow.AddDays(30);
        var token = MintToken(_tenantA, EmailRejected, expiry);
        PersistTokenRow(_tenantA, EmailRejected, token, expiry);

        var mediator = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetPortalDashboardQuery(token));

        result.IsSuccess.Should().BeTrue();
        var app = result.Value!.Applications.Single();
        app.CurrentStage.Should().Be(ApplicantStage.Rejected);

        // Serialize the whole dashboard and assert NONE of the seeded internal strings leak (BR-3/NFR-5):
        // internal reasons, the rejection-reason enum name, and internal notes must be absent.
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        json.Should().NotContain("INTERNAL");
        json.Should().NotContain("failed the take-home");
        json.Should().NotContain("do not re-engage");
        json.Should().NotContain("NotQualified");

        // The timeline still shows a sanitized closure label (FR-6).
        app.Timeline.Should().Contain(e => e.Stage == ApplicantStage.Rejected);
        app.Timeline.Should().Contain(e => e.Label == "Application received");
    }

    [Fact]
    public async Task Dashboard_InterviewSection_DoesNotExposeInternalNotes()
    {
        var expiry = DateTime.UtcNow.AddDays(30);
        var token = MintToken(_tenantA, EmailA, expiry);
        PersistTokenRow(_tenantA, EmailA, token, expiry);

        var mediator = BuildPipeline(_tenantA);
        var result = await mediator.Send(new GetPortalDashboardQuery(token));

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        json.Should().NotContain("candidate seems nervous"); // interview.Notes is internal
    }

    // ── BR-5: request-link only issues when an application exists ──────

    [Fact]
    public async Task RequestLink_ReturnsGenericMessage_RegardlessOfExistence()
    {
        var mediator = BuildPipeline(_tenantA);

        var exists = await mediator.Send(new RequestPortalLinkCommand(EmailA));
        var missing = await mediator.Send(new RequestPortalLinkCommand("nobody@a.com"));

        exists.IsSuccess.Should().BeTrue();
        missing.IsSuccess.Should().BeTrue();
        // Anti-enumeration: identical message either way.
        exists.Value!.Message.Should().Be(missing.Value!.Message);

        // A token row WAS persisted for the existing email, but NOT for the missing one.
        using var db = NewDb(_tenantA);
        (await db.ApplicantPortalTokens.AnyAsync(t => t.ApplicantEmail == EmailA)).Should().BeTrue();
        (await db.ApplicantPortalTokens.AnyAsync(t => t.ApplicantEmail == "nobody@a.com")).Should().BeFalse();
    }

    // ── Shared in-memory file store across the test (offer PDF download) ─

    private static readonly Dictionary<string, byte[]> SeededFiles = new();

    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _store;
        public InMemoryFileStorage(Dictionary<string, byte[]> store) => _store = store;

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
