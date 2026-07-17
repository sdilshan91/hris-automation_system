// ============================================================================
// US-REC-003: Applicant pipeline + stage management integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (with the MediatR pipeline + global query filters), covering:
//   - Pipeline board groups applicants by stage with per-stage counts + total (AC-1/FR-5)
//   - Filters: search by name/email narrows the board (FR-6/AC-4)
//   - Move Applied -> Screening persists the stage change AND writes a history row
//     visible on the applicant detail timeline (AC-2/BR-5)
//   - Cross-tenant isolation: Tenant B sees zero of Tenant A's pipeline (AC-5)
//
// NOTE ON PROVIDER: mirrors ApplicantSubmissionIntegrationTests — the repo verify
// gate runs `dotnet test` with NO PostgreSQL bound (EF Core InMemory by design;
// Docker is unavailable in the agent sandbox). These tests go through the real
// composed pipeline (MediatR + ITenantContext-driven query filters), which is what
// proves tenant isolation. The PG-specific schema is validated by the `migrations`
// CI job applying the generated migration to real PG.
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
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class ApplicantPipelineIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private Guid _vacancyA;
    private Guid _vacancyB;

    public ApplicantPipelineIntegrationTests()
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

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddSingleton<IFileStorage, InMemoryFileStorage>();
        services.AddSingleton<IVirusScanner, AllowWithLogVirusScanner>();
        services.AddScoped<IRecruitmentNotificationService, LogOnlyRecruitmentNotificationService>();
        services.AddScoped<IApplicantService, ApplicantService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SubmitApplicationCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedData()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _vacancyA = Guid.NewGuid();
        _vacancyB = Guid.NewGuid();

        db.Vacancies.AddRange(
            OpenVacancy(_vacancyA, _tenantA, "Backend Engineer (A)"),
            OpenVacancy(_vacancyB, _tenantB, "Backend Engineer (B)"));

        // Tenant A vacancy: 2 Applied, 1 Screening, 1 Rejected.
        db.Applicants.AddRange(
            Applicant(_tenantA, _vacancyA, "ada@a.com", "Ada", "Lovelace", ApplicantStage.Applied),
            Applicant(_tenantA, _vacancyA, "bob@a.com", "Bob", "Newton", ApplicantStage.Applied),
            Applicant(_tenantA, _vacancyA, "cleo@a.com", "Cleo", "Curie", ApplicantStage.Screening),
            Applicant(_tenantA, _vacancyA, "dan@a.com", "Dan", "Turing", ApplicantStage.Rejected));

        // Tenant B vacancy: 1 Applied.
        db.Applicants.Add(
            Applicant(_tenantB, _vacancyB, "eve@b.com", "Eve", "Hopper", ApplicantStage.Applied));

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

    private static Applicant Applicant(
        Guid tenantId, Guid vacancyId, string email, string first, string last, ApplicantStage stage) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        VacancyId = vacancyId,
        ApplicationReferenceNumber = $"APP-2026-{Math.Abs(email.GetHashCode()) % 10000:D4}",
        FirstName = first,
        LastName = last,
        Email = email,
        ResumeStorageKey = $"recruitment/{vacancyId}/x/z.pdf",
        ResumeFileName = "resume.pdf",
        Stage = stage,
        Source = ApplicationSource.Public,
        AppliedAt = DateTime.UtcNow,
        IsDeleted = false,
    };

    // ── Board grouping + counts (AC-1/FR-5) ───────────────────────────

    [Fact]
    public async Task GetPipeline_GroupsApplicantsByStage_WithCountsAndTotal()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(new GetApplicantPipelineQuery(_vacancyA, new PipelineFilter()));

        result.IsSuccess.Should().BeTrue();
        var board = result.Value!;
        board.Total.Should().Be(4);

        // Every REAL stage column is present (FR-1), even empty ones. The Unknown sentinel (ISSUE-231) is a
        // read-only fallback for a corrupt DB row, not a pipeline stage, so it is deliberately not a column.
        board.Stages.Select(s => s.Stage).Should()
            .Contain(Enum.GetValues<ApplicantStage>().Where(s => s != ApplicantStage.Unknown))
            .And.NotContain(ApplicantStage.Unknown);

        Count(board, ApplicantStage.Applied).Should().Be(2);
        Count(board, ApplicantStage.Screening).Should().Be(1);
        Count(board, ApplicantStage.Rejected).Should().Be(1);
        Count(board, ApplicantStage.Interview).Should().Be(0);

        // Cards carry name/source/applied date.
        var appliedCol = board.Stages.Single(s => s.Stage == ApplicantStage.Applied);
        appliedCol.Applicants.Should().OnlyContain(c => !string.IsNullOrEmpty(c.FullName));
    }

    // ── Search filter (FR-6/AC-4) ─────────────────────────────────────

    [Fact]
    public async Task GetPipeline_SearchByName_NarrowsBoard()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(
            new GetApplicantPipelineQuery(_vacancyA, new PipelineFilter { Search = "ada" }));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Total.Should().Be(1);
        Count(result.Value, ApplicantStage.Applied).Should().Be(1);
    }

    [Fact]
    public async Task GetPipeline_FilterByStage_ReturnsOnlyThatStagePopulated()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(
            new GetApplicantPipelineQuery(_vacancyA, new PipelineFilter { Stage = ApplicantStage.Screening }));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Total.Should().Be(1);
        Count(result.Value, ApplicantStage.Screening).Should().Be(1);
        Count(result.Value, ApplicantStage.Applied).Should().Be(0);
    }

    // ── Move + history timeline (AC-2/BR-5) ───────────────────────────

    [Fact]
    public async Task MoveStage_AppliedToScreening_PersistsAndAppearsOnTimeline()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var applicantId = (await mediator.Send(new GetApplicantPipelineQuery(_vacancyA, new PipelineFilter())))
            .Value!.Stages.Single(s => s.Stage == ApplicantStage.Applied).Applicants
            .First(c => c.Email == "ada@a.com").Id;

        var move = await mediator.Send(new MoveApplicantStageCommand(applicantId, ApplicantStage.Screening, null, "Phone screen passed."));
        move.IsSuccess.Should().BeTrue();
        move.Value!.FromStage.Should().Be(ApplicantStage.Applied);
        move.Value.ToStage.Should().Be(ApplicantStage.Screening);

        // The detail timeline now carries the transition (AC-3/BR-5).
        var detail = await mediator.Send(new GetApplicantDetailQuery(applicantId));
        detail.IsSuccess.Should().BeTrue();
        detail.Value!.Profile.Stage.Should().Be(ApplicantStage.Screening);
        detail.Value.StageHistory.Should().ContainSingle();
        detail.Value.StageHistory[0].FromStage.Should().Be(ApplicantStage.Applied);
        detail.Value.StageHistory[0].ToStage.Should().Be(ApplicantStage.Screening);
        detail.Value.StageHistory[0].Notes.Should().Be("Phone screen passed.");
        detail.Value.ResumeFileName.Should().Be("resume.pdf");
        detail.Value.ResumeDownloadUrl.Should().Contain("/resume");

        // The board reflects the move: Applied 2 -> 1, Screening 1 -> 2.
        var board = (await mediator.Send(new GetApplicantPipelineQuery(_vacancyA, new PipelineFilter()))).Value!;
        Count(board, ApplicantStage.Applied).Should().Be(1);
        Count(board, ApplicantStage.Screening).Should().Be(2);
    }

    // ── Resume download (FR-7/NFR-5) ──────────────────────────────────

    [Fact]
    public async Task GetResume_ReturnsStreamedBytes_WithFilenameAndContentType()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var applicantId = (await mediator.Send(new GetApplicantPipelineQuery(_vacancyA, new PipelineFilter())))
            .Value!.Stages.SelectMany(s => s.Applicants).First().Id;

        var resume = await mediator.Send(new GetApplicantResumeQuery(applicantId));

        resume.IsSuccess.Should().BeTrue();
        resume.Value!.Content.Should().NotBeEmpty();
        resume.Value.FileName.Should().Be("resume.pdf");
        resume.Value.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task GetResume_UnknownApplicant_Returns404()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var resume = await mediator.Send(new GetApplicantResumeQuery(Guid.NewGuid()));

        resume.IsFailure.Should().BeTrue();
        resume.StatusCode.Should().Be(404);
    }

    // ── Cross-tenant isolation (AC-5) ─────────────────────────────────

    [Fact]
    public async Task GetPipeline_TenantB_SeesZeroOfTenantAPipeline()
    {
        var medB = BuildPipeline(_tenantB, _userB);

        // B cannot see A's vacancy board at all (filtered out -> 404).
        var bSeesA = await medB.Send(new GetApplicantPipelineQuery(_vacancyA, new PipelineFilter()));
        bSeesA.IsFailure.Should().BeTrue();
        bSeesA.StatusCode.Should().Be(404);

        // B's own board only has B's single applicant.
        var bOwn = await medB.Send(new GetApplicantPipelineQuery(_vacancyB, new PipelineFilter()));
        bOwn.IsSuccess.Should().BeTrue();
        bOwn.Value!.Total.Should().Be(1);
    }

    [Fact]
    public async Task MoveStage_TenantB_CannotMoveTenantAApplicant()
    {
        var medA = BuildPipeline(_tenantA, _userA);
        var aApplicantId = (await medA.Send(new GetApplicantPipelineQuery(_vacancyA, new PipelineFilter())))
            .Value!.Stages.SelectMany(s => s.Applicants).First().Id;

        // Tenant B tries to move Tenant A's applicant — the row is filtered out, so 404, no change.
        var medB = BuildPipeline(_tenantB, _userB);
        var move = await medB.Send(new MoveApplicantStageCommand(aApplicantId, ApplicantStage.Screening, null, null));

        move.IsFailure.Should().BeTrue();
        move.StatusCode.Should().Be(404);
    }

    // ── US-REC-004: structured rejection, FR-8 vacancy gate, BR-4 warning, BR-2 reactivation ─────────

    [Fact]
    public async Task MoveStage_ToRejected_WithoutStructuredReason_IsRejected()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var id = ApplicantId(mediator, "ada@a.com");

        // Free-text reason only, no structured rejection reason → AC-4/FR-3 blocks it.
        var move = await mediator.Send(new MoveApplicantStageCommand(
            id, ApplicantStage.Rejected, "Some text.", null, null));

        move.IsFailure.Should().BeTrue();
        move.StatusCode.Should().Be(400);
        move.ErrorCode.Should().Be("rejection_reason_required");
    }

    [Fact]
    public async Task MoveStage_ToRejected_WithStructuredReason_PersistsOnTimeline()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var id = ApplicantId(mediator, "ada@a.com");

        var move = await mediator.Send(new MoveApplicantStageCommand(
            id, ApplicantStage.Rejected, "Not qualified.", null, RejectionReason.NotQualified));

        move.IsSuccess.Should().BeTrue();
        move.Value!.RejectionReason.Should().Be(RejectionReason.NotQualified);

        var detail = await mediator.Send(new GetApplicantDetailQuery(id));
        detail.Value!.StageHistory[0].RejectionReason.Should().Be(RejectionReason.NotQualified);
        detail.Value.StageHistory[0].RejectionReasonName.Should().Be("NotQualified");
    }

    [Fact]
    public async Task MoveStage_Forward_WhenVacancyClosed_Returns409()
    {
        SetVacancyStatus(_vacancyA, VacancyStatus.Closed);
        var mediator = BuildPipeline(_tenantA, _userA);
        var id = ApplicantId(mediator, "cleo@a.com"); // Screening

        var move = await mediator.Send(new MoveApplicantStageCommand(
            id, ApplicantStage.Interview, null, null));

        move.IsFailure.Should().BeTrue();
        move.StatusCode.Should().Be(409);
        move.ErrorCode.Should().Be("vacancy_not_active");
    }

    [Fact]
    public async Task MoveStage_ToOffer_WhenHeadcountFilled_SucceedsWithWarning()
    {
        // Headcount = 1; mark one applicant Hired so the vacancy is at capacity.
        SetApplicantStage(_tenantA, "dan@a.com", ApplicantStage.Hired); // was Rejected → now Hired
        var mediator = BuildPipeline(_tenantA, _userA);
        var id = ApplicantId(mediator, "cleo@a.com"); // Screening

        var move = await mediator.Send(new MoveApplicantStageCommand(
            id, ApplicantStage.Offer, null, null));

        move.IsSuccess.Should().BeTrue();
        move.Value!.Warnings.Should().Contain(w => w.Contains("headcount"));
    }

    [Fact]
    public async Task MoveStage_OutOfRejected_WithoutReason_IsBlocked()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var id = ApplicantId(mediator, "dan@a.com"); // Rejected

        var move = await mediator.Send(new MoveApplicantStageCommand(
            id, ApplicantStage.Screening, null, null));

        move.IsFailure.Should().BeTrue();
        move.ErrorCode.Should().Be("reason_required");
    }

    // ── ISSUE-232: converted-applicant link surfaced on the read path ────────────────────────────────

    [Fact]
    public async Task ApplicantDetail_ExposesConvertedLink_WhenConverted_ISSUE232()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var id = ApplicantId(mediator, "ada@a.com");
        var employeeId = Guid.NewGuid();
        var convertedAt = new DateTime(2026, 4, 20, 9, 0, 0, DateTimeKind.Utc);
        MarkConverted(_tenantA, "ada@a.com", employeeId, convertedAt);

        var detail = await mediator.Send(new GetApplicantDetailQuery(id));

        detail.IsSuccess.Should().BeTrue();
        detail.Value!.Profile.IsConverted.Should().BeTrue();
        detail.Value.Profile.ConvertedToEmployeeId.Should().Be(employeeId);
        detail.Value.Profile.ConvertedAt.Should().Be(convertedAt);
    }

    [Fact]
    public async Task ApplicantDetail_HasNoConvertedLink_WhenNotConverted_ISSUE232()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var id = ApplicantId(mediator, "bob@a.com"); // untouched, never converted

        var detail = await mediator.Send(new GetApplicantDetailQuery(id));

        detail.IsSuccess.Should().BeTrue();
        detail.Value!.Profile.IsConverted.Should().BeFalse();
        detail.Value.Profile.ConvertedToEmployeeId.Should().BeNull();
        detail.Value.Profile.ConvertedAt.Should().BeNull();
    }

    private void MarkConverted(Guid tenantId, string email, Guid employeeId, DateTime convertedAt)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        var a = db.Applicants.First(x => x.Email == email);
        a.ConvertedToEmployeeId = employeeId;
        a.ConvertedAt = convertedAt;
        db.SaveChanges();
    }

    private Guid ApplicantId(IMediator mediator, string email)
        => mediator.Send(new GetApplicantPipelineQuery(_vacancyA, new PipelineFilter()))
            .GetAwaiter().GetResult()
            .Value!.Stages.SelectMany(s => s.Applicants).First(c => c.Email == email).Id;

    private void SetVacancyStatus(Guid vacancyId, VacancyStatus status)
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        var v = db.Vacancies.First(x => x.Id == vacancyId);
        v.Status = status;
        db.SaveChanges();
    }

    private void SetApplicantStage(Guid tenantId, string email, ApplicantStage stage)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        var a = db.Applicants.First(x => x.Email == email);
        a.Stage = stage;
        db.SaveChanges();
    }

    private static int Count(ApplicantPipelineBoardDto board, ApplicantStage stage)
        => board.Stages.Single(s => s.Stage == stage).Count;

    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult($"/{tenantId}/{relativePath}");

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]));

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
