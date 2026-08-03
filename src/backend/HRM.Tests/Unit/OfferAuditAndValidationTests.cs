// ============================================================================
// US-REC-007 offer-cluster remediation — BUG-067 + ISSUE-124.
//
//   • BUG-067: GenerateOfferValidator must reject an expiry date that precedes the start date. The
//     auto-expire job fires at expiry+1d and flips the offer to Expired, so expiry < start produces an
//     offer that lapses before the candidate could ever begin (TC-REC-007-09 step 4).
//
//   • ISSUE-124: every offer lifecycle mutation must write an audit_logs row. Before this, an offer's
//     salary and status changed with NO trail except the Serilog file, which the in-app audit-search
//     surface cannot read. The rows are added to the change set (not saved separately), so an offer can
//     never change status without its audit row.
//
// The validator arms are pure FluentValidation (no DB). The audit arms drive the real OfferService over
// the EF InMemory provider, mirroring OfferServiceTests' harness.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Validators;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Recruitment;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OfferAuditAndValidationTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _vacancyId = Guid.NewGuid();
    private readonly Guid _applicantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public OfferAuditAndValidationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Permissions.Returns(new List<string>());

        Seed();
    }

    // ── BUG-067: expiry must not precede the start date ────────────────────

    private static readonly DateOnly Start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    private static GenerateOfferCommand Command(DateOnly? expiry) => new(
        ApplicantId: Guid.NewGuid(),
        OfferedPosition: "Backend Engineer",
        DepartmentId: null,
        ReportingManagerEmployeeId: null,
        SalaryAmount: 120000m,
        Currency: "USD",
        SalaryFrequency: SalaryFrequency.Annual,
        BenefitsSummary: null,
        StartDate: Start,
        ExpiryDate: expiry,
        ProbationMonths: 3,
        CustomClauses: null,
        SalaryStructureId: null);

    /// <summary>
    /// The exact BUG-067 repro: both dates in the future, expiry BEFORE start. Previously 201 Created.
    /// </summary>
    [Fact]
    public void Validate_ExpiryBeforeStartDate_IsRejected()
    {
        var result = new GenerateOfferValidator().Validate(Command(Start.AddDays(-15)));

        result.IsValid.Should().BeFalse("an offer cannot expire before the job it offers can begin");
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(GenerateOfferCommand.ExpiryDate)
            && e.ErrorMessage.Contains("earlier than the start date"));
    }

    /// <summary>
    /// Boundary: expiry ON the start date is the first ACCEPTED value (the rule is >=, not >). This is the arm
    /// that fails if the comparison is ever tightened to a strict inequality.
    /// </summary>
    [Fact]
    public void Validate_ExpiryExactlyOnStartDate_IsAccepted()
    {
        new GenerateOfferValidator().Validate(Command(Start)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ExpiryAfterStartDate_IsAccepted()
    {
        new GenerateOfferValidator().Validate(Command(Start.AddDays(5))).IsValid.Should().BeTrue();
    }

    /// <summary>
    /// BR-6: a null expiry is defaulted by the service to generation + 7 days, so the cross-field rule must not
    /// fire on it. Without this arm a `.Must` that dereferenced a null expiry would throw rather than pass.
    /// </summary>
    [Fact]
    public void Validate_NullExpiry_IsUnaffectedByTheStartDateRule()
    {
        new GenerateOfferValidator().Validate(Command(null)).IsValid.Should().BeTrue();
    }

    // ── ISSUE-124: every lifecycle mutation writes an audit row ────────────

    [Fact]
    public async Task Generate_WritesGeneratedAuditRow_CapturingSalary()
    {
        await using var db = CreateDb();
        var offer = (await Service(db).GenerateAsync(Input())).Value!;

        var row = await SingleAuditAsync(db, OfferAuditAction.Generated);
        row.ResourceType.Should().Be("Offer");
        row.ResourceId.Should().Be(offer.Id.ToString());
        row.UserId.Should().Be(_userId, "an operator-driven change must name its actor");
        row.Before.Should().BeNull("a create has no prior state to diff against");
        // The salary is the whole point of auditing an offer — assert the figure itself, not merely that
        // some JSON was written.
        row.After.Should().Contain("120000");
        row.After.Should().Contain("Draft");
    }

    [Fact]
    public async Task Send_WritesSentAuditRow_WithTheStatusTransition()
    {
        await using var db = CreateDb();
        var svc = Service(db);
        var offer = (await svc.GenerateAsync(Input())).Value!;

        (await svc.SendAsync(offer.Id)).IsSuccess.Should().BeTrue();

        var row = await SingleAuditAsync(db, OfferAuditAction.Sent);
        row.Before.Should().Contain("Draft");
        row.After.Should().Contain("Sent");
    }

    [Theory]
    [InlineData(true, "Accepted")]
    [InlineData(false, "Declined")]
    public async Task Respond_WritesRespondedAuditRow_ForBothOutcomes(bool accepted, string expected)
    {
        await using var db = CreateDb();
        var svc = Service(db);
        var offer = (await svc.GenerateAsync(Input())).Value!;
        await svc.SendAsync(offer.Id);

        (await svc.RespondAsync(offer.Id, new RespondToOfferInput { Accepted = accepted }))
            .IsSuccess.Should().BeTrue();

        var row = await SingleAuditAsync(db, OfferAuditAction.Responded);
        row.Before.Should().Contain("Sent");
        row.After.Should().Contain(expected);
    }

    [Fact]
    public async Task Withdraw_WritesWithdrawnAuditRow()
    {
        await using var db = CreateDb();
        var svc = Service(db);
        var offer = (await svc.GenerateAsync(Input())).Value!;

        (await svc.WithdrawAsync(offer.Id)).IsSuccess.Should().BeTrue();

        var row = await SingleAuditAsync(db, OfferAuditAction.Withdrawn);
        row.Before.Should().Contain("Draft");
        row.After.Should().Contain("Withdrawn");
    }

    /// <summary>
    /// The audit row must commit ATOMICALLY with the status change: a rejected transition leaves neither. Here
    /// the respond is refused (the offer is still Draft, not Sent), so no Responded row may exist — otherwise
    /// the trail would record a transition that never happened.
    /// </summary>
    [Fact]
    public async Task RefusedTransition_WritesNoAuditRow()
    {
        await using var db = CreateDb();
        var svc = Service(db);
        var offer = (await svc.GenerateAsync(Input())).Value!;

        var refused = await svc.RespondAsync(offer.Id, new RespondToOfferInput { Accepted = true });
        refused.IsFailure.Should().BeTrue();

        (await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action == OfferAuditAction.Responded))
            .Should().Be(0);
    }

    // ── harness ────────────────────────────────────────────────────────────

    private async Task<AuditLog> SingleAuditAsync(AppDbContext db, string action)
    {
        var rows = await db.AuditLogs.AsNoTracking().Where(a => a.Action == action).ToListAsync();
        rows.Should().HaveCount(1, $"exactly one {action} row is expected");
        rows[0].EventType.Should().Be(action);
        rows[0].TenantId.Should().Be(_tenantId);
        return rows[0];
    }

    private AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, _tenantContext);

    private OfferService Service(AppDbContext db) => new(
        db,
        _tenantContext,
        new NoopFileStorage(),
        Substitute.For<IRecruitmentNotificationService>(),
        Substitute.For<ILogger<OfferService>>(),
        new GanssHtmlSanitizer(),
        expiryScheduler: null,
        expiryReminderScheduler: null,
        currentUser: _currentUser);

    private GenerateOfferInput Input() => new()
    {
        ApplicantId = _applicantId,
        OfferedPosition = "Senior Backend Engineer",
        SalaryAmount = 120000m,
        Currency = "usd",
        SalaryFrequency = SalaryFrequency.Annual,
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40)),
        ProbationMonths = 3,
    };

    private void Seed()
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
            Stage = ApplicantStage.Offer,
            Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow,
            IsDeleted = false,
        });
        db.SaveChanges();
    }

    private sealed class NoopFileStorage : IFileStorage
    {
        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content,
            string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult(relativePath);

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(new MemoryStream());

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiry = null) => relativePath;
    }
}
