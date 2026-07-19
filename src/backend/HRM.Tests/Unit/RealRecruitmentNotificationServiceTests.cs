// ============================================================================
// US-NTF-006 Phase 5a — RealRecruitmentNotificationService unit tests (US-REC-002/004/005/006/007).
//
// The Real service RESOLVES recipients from the DB (candidate email / hiring-manager User / recruiter pool) and
// dispatches the recruitment notifications. Two load-bearing behaviours are genuinely observed here:
//   • RECIPIENT ROUTING — the recruiter pool (users whose roles grant Recruitment.Manage) + hiring manager are
//     notified for application_new / scorecard_submitted / offer expiry, but the NON-recruiter user never is; the
//     candidate + interviewers are email-only.
//   • OFFER-PDF vs FALLBACK SPLIT — offer_sent sends the offer letter INLINE via IEmailSender (with the PDF from
//     IFileStorage) and does NOT use the dispatcher for that leg; when the PDF can't be read it FALLS BACK to a
//     plain dispatcher email (no attachment) and still never throws.
// Every method is never-throw.
//
// PROVIDER: EF Core InMemory AppDbContext (recruiter-pool resolution is a real UserTenants⋈UserTenantRoles⋈
// RolePermissions query with IgnoreQueryFilters) + a hand RecordingDispatcher + a hand FakeEmailSender (captures
// the EmailMessage) + a hand FakeFileStorage (returns bytes / null / throws) + a substituted IEmailTemplateService.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealRecruitmentNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _recruiter1 = Guid.NewGuid();
    private readonly Guid _recruiter2 = Guid.NewGuid();
    private readonly Guid _nonRecruiter = Guid.NewGuid();
    private readonly Guid _hmUserId = Guid.NewGuid();

    private readonly Guid _vacancyId = Guid.NewGuid();
    private readonly Guid _hiringManagerEmpId = Guid.NewGuid();
    private readonly Guid _applicantId = Guid.NewGuid();
    private readonly Guid _interviewId = Guid.NewGuid();
    private readonly Guid _offerId = Guid.NewGuid();
    private readonly Guid _scorecardId = Guid.NewGuid();

    // Interviewers are Employees; one has a linked user account (in-app + email), one does not (email-only).
    private readonly Guid _interviewerWithUserEmpId = Guid.NewGuid();
    private readonly Guid _interviewerWithUserId = Guid.NewGuid();
    private readonly Guid _interviewerNoUserEmpId = Guid.NewGuid();

    private const string CandidateEmail = "jordan.rivera@example.com";
    private const string HiringManagerEmail = "hm@acme.test";
    private const string InterviewerLinkedEmail = "iv-linked@acme.test";
    private const string InterviewerNoUserEmail = "iv-nouser@acme.test";
    private static readonly byte[] Pdf = "%PDF-1.4 fake-offer-letter-bytes"u8.ToArray();

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    // DF-42: a fixed magic-link token the offer email embeds; extracted from the payload to assert the /portal link.
    private const string PortalToken = "unit-test-portal-token-abc123";

    private readonly IConfiguration _config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:BaseDomain"] = "yourhrm.com" })
        .Build();

    public RealRecruitmentNotificationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.Subdomain.Returns("acme");
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private RealRecruitmentNotificationService Service(
        INotificationDispatcher dispatcher,
        IEmailSender? emailSender = null,
        IFileStorage? fileStorage = null,
        IEmailTemplateService? templateService = null,
        IApplicantPortalTokenService? portalTokenService = null) =>
        new(
            Db(),
            dispatcher,
            emailSender ?? new FakeEmailSender(),
            fileStorage ?? new FakeFileStorage(),
            templateService ?? ResolvingTemplateService(),
            portalTokenService ?? new FakePortalTokenService(PortalToken),
            _config,
            _tenantContext,
            NullLogger<RealRecruitmentNotificationService>.Instance);

    // ── Fakes ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Records dispatched requests per leg; optionally throws to prove the never-throw contract.</summary>
    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        private readonly bool _throw;
        public List<NotificationRequest> InApp { get; } = new();
        public List<NotificationRequest> Email { get; } = new();
        public RecordingDispatcher(bool throwOnDispatch = false) => _throw = throwOnDispatch;

        public Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("in-app dispatch boom");
            InApp.Add(request);
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("email dispatch boom");
            Email.Add(request);
            return Task.CompletedTask;
        }
    }

    /// <summary>Captures the EmailMessage(s) handed to the generic seam; optionally throws to prove never-throw.</summary>
    private sealed class FakeEmailSender : IEmailSender
    {
        private readonly Exception? _throw;
        public List<EmailMessage> Sent { get; } = new();
        public FakeEmailSender(Exception? throwOnSend = null) => _throw = throwOnSend;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            if (_throw is not null) throw _throw;
            return Task.CompletedTask;
        }
    }

    /// <summary>Returns configured PDF bytes, or null (absent), or throws — to drive the PDF-vs-fallback split.</summary>
    private sealed class FakeFileStorage : IFileStorage
    {
        private readonly byte[]? _bytes;
        private readonly Exception? _throw;
        public FakeFileStorage(byte[]? bytes = null, Exception? throwOnRead = null)
        {
            _bytes = bytes;
            _throw = throwOnRead;
        }

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
        {
            if (_throw is not null) throw _throw;
            return Task.FromResult<Stream?>(_bytes is null ? null : new MemoryStream(_bytes));
        }

        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => throw new NotImplementedException();
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// A portal-token service that issues a fixed token (DF-42), or fails issuance when constructed with a null token
    /// — to drive the "issuance fails ⇒ offer still sent, without the link" arm. Records the emails it was asked for.
    /// </summary>
    private sealed class FakePortalTokenService : IApplicantPortalTokenService
    {
        private readonly string? _token;
        public List<string> IssuedFor { get; } = new();
        public FakePortalTokenService(string? token) => _token = token;

        public Task<Result<IssuedPortalToken>> IssueAsync(string applicantEmail, CancellationToken cancellationToken = default)
        {
            IssuedFor.Add(applicantEmail);
            return Task.FromResult(_token is null
                ? Result<IssuedPortalToken>.Failure("issuance failed", 500)
                : Result<IssuedPortalToken>.Success(new IssuedPortalToken
                {
                    Token = _token, ExpiresAt = DateTime.UtcNow.AddDays(30),
                }));
        }

        public Task<Result<bool>> RequestLinkAsync(string email, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Result<ValidatedPortalToken>> ValidateAsync(string token, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    /// <summary>A template service that always resolves (offer_sent inline path renders successfully).</summary>
    private static IEmailTemplateService ResolvingTemplateService()
    {
        var svc = Substitute.For<IEmailTemplateService>();
        svc.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success(new ResolvedEmailTemplate(
                EventKey: (string)ci[0], Language: "en",
                Subject: "Your offer", BodyHtml: "<p>offer</p>", BodyText: "offer",
                IsCustom: false, Version: 1, LastModified: null)));
        svc.Render(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>())
            .Returns(new RenderedEmail("Your offer", "<p>offer</p>", "offer"));
        return svc;
    }

    /// <summary>A template service that never resolves — to prove the "unresolvable template → dispatcher fallback" leg.</summary>
    private static IEmailTemplateService FailingTemplateService()
    {
        var svc = Substitute.For<IEmailTemplateService>();
        svc.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ResolvedEmailTemplate>("no template", 404));
        return svc;
    }

    // ── Seeding ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Two users holding a Recruitment.Manage role + one user without it (the recruiter pool + a decoy).</summary>
    private async Task SeedRecruiterPoolAsync()
    {
        using var db = Db();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = "Recruiter" });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Recruitment.Manage });

        foreach (var uid in new[] { _recruiter1, _recruiter2 })
        {
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = uid, TenantId = _tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        }

        // A tenant user WITHOUT Recruitment.Manage (a role with no such permission) — must never be notified.
        var plainRoleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = plainRoleId, TenantId = _tenantId, Name = "Viewer" });
        var nonUt = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant { Id = nonUt, UserId = _nonRecruiter, TenantId = _tenantId, Status = UserTenantStatus.Active });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = nonUt, RoleId = plainRoleId });

        await db.SaveChangesAsync();
    }

    /// <summary>Vacancy (+ hiring-manager Employee→User), applicant, interview and offer for the tenant.</summary>
    private async Task SeedRecruitmentDataAsync(bool hiringManagerLinkedToUser = true)
    {
        using var db = Db();
        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyId, TenantId = _tenantId, Title = "Senior Software Engineer",
            HiringManagerId = _hiringManagerEmpId,
        });
        db.Employees.Add(new Employee
        {
            Id = _hiringManagerEmpId, TenantId = _tenantId, FirstName = "Morgan", LastName = "Hale",
            Email = HiringManagerEmail, UserId = hiringManagerLinkedToUser ? _hmUserId : null,
        });
        db.Applicants.Add(new Applicant
        {
            Id = _applicantId, TenantId = _tenantId, FirstName = "Jordan", LastName = "Rivera",
            Email = CandidateEmail, ApplicationReferenceNumber = "APP-2026-000123",
        });
        db.Interviews.Add(new Interview
        {
            Id = _interviewId, TenantId = _tenantId,
            ScheduledDate = new DateOnly(2026, 7, 20), StartTime = new TimeOnly(10, 30),
            InterviewType = InterviewType.Video, VideoLink = "https://meet.example.com/abc",
        });
        db.Offers.Add(new Offer
        {
            Id = _offerId, TenantId = _tenantId, OfferReferenceNumber = "OFR-2026-000045",
            OfferedPosition = "Senior Software Engineer",
            StartDate = new DateOnly(2026, 8, 1), ExpiryDate = new DateOnly(2026, 7, 25),
            PdfStorageKey = "recruitment/offers/offer-045.pdf",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Two interviewer Employees: one linked to a User (in-app + email), one account-less (email-only).</summary>
    private async Task SeedInterviewersAsync()
    {
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = _interviewerWithUserEmpId, TenantId = _tenantId, FirstName = "Sam", LastName = "Lin",
            Email = InterviewerLinkedEmail, UserId = _interviewerWithUserId,
        });
        db.Employees.Add(new Employee
        {
            Id = _interviewerNoUserEmpId, TenantId = _tenantId, FirstName = "Alex", LastName = "Poe",
            Email = InterviewerNoUserEmail, UserId = null,
        });
        await db.SaveChangesAsync();
    }

    // ── #1: application_received → candidate email-only ──

    [Fact]
    public async Task NotifyApplicationReceivedAsync_SendsSingleEmailToCandidate_NoInApp()
    {
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyApplicationReceivedAsync(
            _applicantId, _vacancyId, CandidateEmail, "APP-2026-000123");

        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().RecipientEmail.Should().Be(CandidateEmail);
        dispatcher.Email.Single().EventKey.Should().Be("application_received");
        dispatcher.Email.Single().TenantId.Should().Be(_tenantId);
        dispatcher.InApp.Should().BeEmpty("the external candidate has no User row → email-only");
    }

    // ── #2: application_new → recruiter pool (both legs) + hiring-manager User; NOT the non-recruiter ──

    [Fact]
    public async Task NotifyNewApplicationAsync_DispatchesToRecruiterPoolAndHiringManager_NotTheNonRecruiter()
    {
        await SeedRecruiterPoolAsync();
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyNewApplicationAsync(_applicantId, _vacancyId, _hiringManagerEmpId);

        // Recipient routing genuinely observed: exactly the 2 recruiters + the hiring-manager User, both legs.
        var expected = new Guid?[] { _recruiter1, _recruiter2, _hmUserId };
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(expected);
        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(expected);
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_nonRecruiter);

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "application_new");
        dispatcher.InApp.Should().OnlyContain(r => r.EventKey == "application_new");
    }

    // ── #2b: application_new with hiring manager NOT linked to a User → email-only fallback for the HM ──

    [Fact]
    public async Task NotifyNewApplicationAsync_HiringManagerWithoutUser_FallsBackToHmEmail()
    {
        await SeedRecruiterPoolAsync();
        await SeedRecruitmentDataAsync(hiringManagerLinkedToUser: false);
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyNewApplicationAsync(_applicantId, _vacancyId, _hiringManagerEmpId);

        // In-app only for the 2 recruiter-pool users (the HM has no User row).
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _recruiter1, _recruiter2 });
        // The HM is reached email-only by address.
        dispatcher.Email.Should().Contain(r => r.RecipientEmail == HiringManagerEmail && r.EventKey == "application_new");
    }

    // ── #3: interview → candidate email-only; interviewers resolved by employee id (ISSUE-263) ──
    //   • interviewer WITH a linked user account → in-app (that user id) + email
    //   • interviewer WITHOUT a user account     → email-only (no in-app), graceful fallback

    [Fact]
    public async Task NotifyInterviewAsync_Scheduled_InAppPlusEmailForLinkedInterviewer_EmailOnlyForAccountLess()
    {
        await SeedRecruitmentDataAsync();
        await SeedInterviewersAsync();
        var dispatcher = new RecordingDispatcher();
        var interviewerIds = new[] { _interviewerWithUserEmpId, _interviewerNoUserEmpId };

        await Service(dispatcher).NotifyInterviewAsync(
            "interview-scheduled", _interviewId, _applicantId, _vacancyId, CandidateEmail, interviewerIds);

        // In-app goes ONLY to the interviewer with a linked user account — with THAT user's id.
        dispatcher.InApp.Select(r => r.RecipientUserId)
            .Should().BeEquivalentTo(new Guid?[] { _interviewerWithUserId });
        // The account-less interviewer never gets an in-app row.
        dispatcher.InApp.Should().NotContain(r => r.RecipientEmail == InterviewerNoUserEmail);

        // The linked interviewer also gets an email (routed by user id, via DispatchToUsersAsync).
        dispatcher.Email.Should().Contain(r => r.RecipientUserId == _interviewerWithUserId);
        // The account-less interviewer is reached email-only, by address, with no user id.
        dispatcher.Email.Should().Contain(r =>
            r.RecipientEmail == InterviewerNoUserEmail && r.RecipientUserId == null);
        // The candidate stays email-only (external, no User row).
        dispatcher.Email.Should().Contain(r => r.RecipientEmail == CandidateEmail && r.RecipientUserId == null);
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "interview_scheduled");
    }

    [Fact]
    public async Task NotifyInterviewAsync_Cancelled_MapsEventTypeTo_interview_cancelled()
    {
        await SeedRecruitmentDataAsync();
        await SeedInterviewersAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyInterviewAsync(
            "interview-cancelled", _interviewId, _applicantId, _vacancyId, CandidateEmail,
            new[] { _interviewerNoUserEmpId });

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "interview_cancelled");
        // Account-less interviewer + candidate, both email-only.
        dispatcher.Email.Select(r => r.RecipientEmail)
            .Should().BeEquivalentTo(new[] { CandidateEmail, InterviewerNoUserEmail });
        dispatcher.InApp.Should().BeEmpty("neither the candidate nor the account-less interviewer has a user account");
    }

    [Fact]
    public async Task NotifyInterviewReminderAsync_LinkedInterviewer_GetsInAppPlusEmail()
    {
        await SeedRecruitmentDataAsync();
        await SeedInterviewersAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyInterviewReminderAsync(
            _interviewId, _applicantId, _vacancyId, CandidateEmail,
            new[] { _interviewerWithUserEmpId });

        dispatcher.InApp.Select(r => r.RecipientUserId)
            .Should().BeEquivalentTo(new Guid?[] { _interviewerWithUserId });
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "interview_reminder");
        dispatcher.Email.Should().Contain(r => r.RecipientUserId == _interviewerWithUserId);
        dispatcher.Email.Should().Contain(r => r.RecipientEmail == CandidateEmail && r.RecipientUserId == null);
    }

    // ── #4: scorecard_submitted → recruiter pool ONLY (not the non-recruiter, not the candidate) ──

    [Fact]
    public async Task NotifyScorecardSubmittedAsync_DispatchesToRecruiterPoolOnly()
    {
        await SeedRecruiterPoolAsync();
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyScorecardSubmittedAsync(
            _scorecardId, _interviewId, _applicantId, _vacancyId, _hiringManagerEmpId);

        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _recruiter1, _recruiter2 });
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _recruiter1, _recruiter2 });
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_nonRecruiter);
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "scorecard_submitted");
        // The candidate is NOT a scorecard recipient (email-only address never used here).
        dispatcher.Email.Should().NotContain(r => r.RecipientEmail == CandidateEmail);
    }

    // ── #5: offer_sent happy path → candidate leg goes through IEmailSender with the PDF INLINE, NOT the dispatcher ──

    [Fact]
    public async Task NotifyOfferAsync_OfferSent_WithPdf_SendsInlineViaEmailSender_NotDispatcher()
    {
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();
        var emailSender = new FakeEmailSender();
        var fileStorage = new FakeFileStorage(bytes: Pdf);

        await Service(dispatcher, emailSender, fileStorage)
            .NotifyOfferAsync("offer-sent", _offerId, _applicantId, _vacancyId, CandidateEmail);

        // The candidate leg went through IEmailSender (inline PDF) — NOT the dispatcher.
        emailSender.Sent.Should().ContainSingle();
        var msg = emailSender.Sent.Single();
        msg.RecipientEmail.Should().Be(CandidateEmail);
        msg.TenantId.Should().Be(_tenantId);
        msg.Attachments.Should().NotBeNull();
        msg.Attachments!.Should().ContainSingle();
        var att = msg.Attachments!.Single();
        att.ContentType.Should().Be("application/pdf");
        att.Content.Should().Equal(Pdf); // the exact PDF bytes from IFileStorage.

        // The dispatcher was NOT used for the candidate offer_sent leg (offer-sent does not copy the recruiter pool).
        dispatcher.Email.Should().BeEmpty();
        dispatcher.InApp.Should().BeEmpty();
    }

    // ── #6: offer_sent fallback → PDF unreadable → plain DISPATCHER email (no attachment), never throws ──

    [Fact]
    public async Task NotifyOfferAsync_OfferSent_PdfThrows_FallsBackToDispatcherEmail_NoAttachment()
    {
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();
        var emailSender = new FakeEmailSender();
        var fileStorage = new FakeFileStorage(throwOnRead: new IOException("storage down"));

        var act = () => Service(dispatcher, emailSender, fileStorage)
            .NotifyOfferAsync("offer-sent", _offerId, _applicantId, _vacancyId, CandidateEmail);

        await act.Should().NotThrowAsync();

        // Fell back to the dispatcher (email-only) — the candidate is still notified, with no attachment path.
        emailSender.Sent.Should().BeEmpty();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().RecipientEmail.Should().Be(CandidateEmail);
        dispatcher.Email.Single().EventKey.Should().Be("offer_sent");
    }

    [Fact]
    public async Task NotifyOfferAsync_OfferSent_MissingPdf_FallsBackToDispatcherEmail()
    {
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();
        var emailSender = new FakeEmailSender();
        var fileStorage = new FakeFileStorage(bytes: null); // absent → OpenReadAsync returns null.

        await Service(dispatcher, emailSender, fileStorage)
            .NotifyOfferAsync("offer-sent", _offerId, _applicantId, _vacancyId, CandidateEmail);

        emailSender.Sent.Should().BeEmpty();
        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().EventKey.Should().Be("offer_sent");
    }

    // ── DF-42: offer_sent embeds a candidate magic-link (offer.portalUrl) into the offer email payload. ──

    [Fact]
    public async Task NotifyOfferAsync_OfferSent_IssuesPortalToken_AndEmbedsMagicLinkInPayload()
    {
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();
        var emailSender = new FakeEmailSender();
        var fileStorage = new FakeFileStorage(bytes: null); // no PDF → fall back to the dispatcher email (payload visible).
        var portalTokens = new FakePortalTokenService(PortalToken);

        await Service(dispatcher, emailSender, fileStorage, portalTokenService: portalTokens)
            .NotifyOfferAsync("offer-sent", _offerId, _applicantId, _vacancyId, CandidateEmail);

        // A portal token was minted for the candidate…
        portalTokens.IssuedFor.Should().ContainSingle().Which.Should().Be(CandidateEmail);

        // …and the offer email payload embeds the /portal?token= magic link with THAT token.
        dispatcher.Email.Should().ContainSingle();
        var sent = dispatcher.Email.Single();
        sent.EventKey.Should().Be("offer_sent");
        var portalUrl = JsonDocument.Parse(sent.PayloadJson)
            .RootElement.GetProperty("offer").GetProperty("portalUrl").GetString();
        portalUrl.Should().Be($"https://acme.yourhrm.com/portal?token={PortalToken}");
    }

    [Fact]
    public async Task NotifyOfferAsync_OfferSent_TokenIssuanceFails_StillSendsOffer_WithoutTheLink()
    {
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();
        var emailSender = new FakeEmailSender();
        var fileStorage = new FakeFileStorage(bytes: null); // no PDF → dispatcher fallback so we can inspect the payload.
        var portalTokens = new FakePortalTokenService(token: null); // issuance FAILS.

        var act = () => Service(dispatcher, emailSender, fileStorage, portalTokenService: portalTokens)
            .NotifyOfferAsync("offer-sent", _offerId, _applicantId, _vacancyId, CandidateEmail);

        await act.Should().NotThrowAsync();

        // The offer email is STILL sent (the magic-link leg must never break offer delivery)…
        dispatcher.Email.Should().ContainSingle();
        var sent = dispatcher.Email.Single();
        sent.EventKey.Should().Be("offer_sent");
        sent.RecipientEmail.Should().Be(CandidateEmail);
        // …but it carries no magic link (portalUrl is blank).
        sent.PayloadJson.Should().NotContain("/portal?token=");
        var portalUrl = JsonDocument.Parse(sent.PayloadJson)
            .RootElement.GetProperty("offer").GetProperty("portalUrl").GetString();
        portalUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyOfferAsync_OfferSent_TemplateUnresolvable_FallsBackToDispatcherEmail()
    {
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();
        var emailSender = new FakeEmailSender();
        var fileStorage = new FakeFileStorage(bytes: Pdf); // PDF present…

        await Service(dispatcher, emailSender, fileStorage, FailingTemplateService()) // …but the template won't resolve.
            .NotifyOfferAsync("offer-sent", _offerId, _applicantId, _vacancyId, CandidateEmail);

        emailSender.Sent.Should().BeEmpty("no template ⇒ cannot render the inline email");
        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().EventKey.Should().Be("offer_sent");
    }

    // ── #7: offer eventType mapping — withdrawn (candidate only) / expired (candidate + recruiter pool) ──

    [Fact]
    public async Task NotifyOfferAsync_Withdrawn_MapsTo_offer_withdrawn_CandidateOnly()
    {
        await SeedRecruiterPoolAsync();
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyOfferAsync("offer-withdrawn", _offerId, _applicantId, _vacancyId, CandidateEmail);

        // Candidate email only — the recruiter pool is NOT copied for a withdrawal.
        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().RecipientEmail.Should().Be(CandidateEmail);
        dispatcher.Email.Single().EventKey.Should().Be("offer_withdrawn");
        dispatcher.InApp.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyOfferAsync_Expired_MapsTo_offer_expired_CandidatePlusRecruiterPool()
    {
        await SeedRecruiterPoolAsync();
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyOfferAsync("offer-expired", _offerId, _applicantId, _vacancyId, CandidateEmail);

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "offer_expired");
        // Candidate reached email-only (no user id) AND the recruiter pool reached by user id (in-app + email).
        dispatcher.Email.Should().Contain(r => r.RecipientEmail == CandidateEmail && r.RecipientUserId == null);
        dispatcher.Email.Select(r => r.RecipientUserId).Where(id => id != null)
            .Should().BeEquivalentTo(new Guid?[] { _recruiter1, _recruiter2 });
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _recruiter1, _recruiter2 });
    }

    // ── #8: never-throw — the dispatcher throws / the IEmailSender throws ──

    [Fact]
    public async Task NotifyNewApplicationAsync_DispatcherThrows_DoesNotThrow()
    {
        await SeedRecruiterPoolAsync();
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).NotifyNewApplicationAsync(_applicantId, _vacancyId, _hiringManagerEmpId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyOfferAsync_OfferSent_EmailSenderThrows_DoesNotThrow()
    {
        await SeedRecruitmentDataAsync();
        var dispatcher = new RecordingDispatcher();
        var emailSender = new FakeEmailSender(throwOnSend: new InvalidOperationException("smtp boom"));
        var fileStorage = new FakeFileStorage(bytes: Pdf);

        var act = () => Service(dispatcher, emailSender, fileStorage)
            .NotifyOfferAsync("offer-sent", _offerId, _applicantId, _vacancyId, CandidateEmail);

        await act.Should().NotThrowAsync();
        emailSender.Sent.Should().ContainSingle("the inline send was attempted before it threw");
    }
}
