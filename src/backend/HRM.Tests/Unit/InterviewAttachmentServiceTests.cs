// ============================================================================
// US-REC-005 FR-8: interview guide / evaluation-criteria attachments.
//
// The AC sat deferred as CONDITIONAL on "File & Document Management (S26)".
// That rationale had expired — IFileStorage, IVirusScanner and the whole
// upload/scan/store idiom already shipped — so the AC was blocked on nothing but
// the work itself.
//
// These arms deliberately weight the SECURITY pipeline over the happy path: an
// upload endpoint that skips magic-byte sniffing or the virus scan would make
// this the weakest file surface in the product, and that is not visible from a
// green "it uploaded" test.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class InterviewAttachmentServiceTests
{
    private const string Pdf = "application/pdf";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _interviewId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IVirusScanner _virusScanner;
    private readonly IFileStorage _fileStorage;

    public InterviewAttachmentServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_userId);

        _virusScanner = Substitute.For<IVirusScanner>();
        _virusScanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Clean());

        _fileStorage = Substitute.For<IFileStorage>();
        _fileStorage.UploadAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => $"/{ci.Arg<Guid>()}/{ci.ArgAt<string>(1)}");
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private InterviewAttachmentService Service() => new(
        Db(), _tenantContext, _currentUser, _fileStorage, _virusScanner,
        Substitute.For<ILogger<InterviewAttachmentService>>());

    private async Task SeedInterviewAsync(Guid? tenantOverride = null, Guid? interviewOverride = null)
    {
        using var db = Db();
        db.Interviews.Add(new Interview
        {
            Id = interviewOverride ?? _interviewId,
            TenantId = tenantOverride ?? _tenantId,
            ApplicantId = Guid.NewGuid(),
            VacancyId = Guid.NewGuid(),
            RoundNumber = 1,
            ScheduledDate = new DateOnly(2026, 6, 1),
            StartTime = new TimeOnly(10, 0),
            DurationMinutes = 60,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A byte sequence whose magic bytes really are a PDF (%PDF-).</summary>
    private static MemoryStream RealPdf()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 256));
        return new MemoryStream(bytes);
    }

    // ── Happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task Uploading_a_guide_stores_it_against_the_interview_FR8()
    {
        await SeedInterviewAsync();
        await using var content = RealPdf();

        var result = await Service().UploadAsync(
            _interviewId, content, "guide.pdf", Pdf, content.Length,
            InterviewAttachmentKind.Guide, "Round 1 structure");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Kind.Should().Be(InterviewAttachmentKind.Guide);
        result.Value.FileName.Should().Be("guide.pdf");

        using var db = Db();
        var stored = await db.InterviewAttachments.AsNoTracking().SingleAsync();
        stored.InterviewId.Should().Be(_interviewId);
        stored.TenantId.Should().Be(_tenantId);
        stored.UploadedBy.Should().Be(_userId);
        stored.StorageKey.Should().StartWith($"recruitment/interviews/{_interviewId}/");
    }

    // The reason this is a child table: the AC names TWO document kinds, and a single column would force one
    // to overwrite the other the moment a recruiter attaches both.
    [Fact]
    public async Task An_interview_can_hold_BOTH_a_guide_and_evaluation_criteria_FR8()
    {
        await SeedInterviewAsync();

        await using (var a = RealPdf())
            (await Service().UploadAsync(_interviewId, a, "guide.pdf", Pdf, a.Length,
                InterviewAttachmentKind.Guide, null)).IsSuccess.Should().BeTrue();

        await using (var b = RealPdf())
            (await Service().UploadAsync(_interviewId, b, "rubric.pdf", Pdf, b.Length,
                InterviewAttachmentKind.EvaluationCriteria, null)).IsSuccess.Should().BeTrue();

        var list = await Service().ListAsync(_interviewId);

        list.Value!.Should().HaveCount(2, "a single path column would have overwritten the first");
        list.Value.Select(a => a.Kind).Should().BeEquivalentTo(
            [InterviewAttachmentKind.Guide, InterviewAttachmentKind.EvaluationCriteria]);
    }

    // ── Security pipeline ───────────────────────────────────────────────

    [Fact]
    public async Task An_executable_renamed_to_pdf_is_REJECTED_on_its_magic_bytes_FR8()
    {
        // The declared content type is the CLIENT's claim. Without a real signature check, a renamed binary
        // with an allowed MIME string walks straight in — this is the arm that proves we do not trust it.
        await SeedInterviewAsync();
        await using var fake = new MemoryStream(Encoding.ASCII.GetBytes("MZ\x90\x00" + new string('x', 256)));

        var result = await Service().UploadAsync(
            _interviewId, fake, "totally-a-guide.pdf", Pdf, fake.Length,
            InterviewAttachmentKind.Guide, null);

        result.IsFailure.Should().BeTrue("the bytes are a PE binary, not a PDF");
        result.StatusCode.Should().Be(400);

        using var db = Db();
        (await db.InterviewAttachments.CountAsync()).Should().Be(0, "nothing may be persisted");
    }

    [Fact]
    public async Task An_infected_file_is_REJECTED_and_never_stored_FR8()
    {
        await SeedInterviewAsync();
        _virusScanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Infected("EICAR-Test-File"));
        await using var content = RealPdf();

        var result = await Service().UploadAsync(
            _interviewId, content, "guide.pdf", Pdf, content.Length,
            InterviewAttachmentKind.Guide, null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("malware_detected");

        await _fileStorage.DidNotReceive().UploadAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        using var db = Db();
        (await db.InterviewAttachments.CountAsync()).Should().Be(0,
            "scanning after storage would mean the malware was already on disk");
    }

    [Fact]
    public async Task An_unsupported_type_is_REJECTED_FR8()
    {
        await SeedInterviewAsync();
        await using var content = RealPdf();

        var result = await Service().UploadAsync(
            _interviewId, content, "guide.exe", "application/x-msdownload", content.Length,
            InterviewAttachmentKind.Guide, null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_file_type");
    }

    [Fact]
    public async Task A_file_over_the_size_ceiling_is_REJECTED_FR8()
    {
        await SeedInterviewAsync();
        await using var content = RealPdf();

        var result = await Service().UploadAsync(
            _interviewId, content, "huge.pdf", Pdf, InterviewAttachmentService.MaxUploadBytes + 1,
            InterviewAttachmentKind.Guide, null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("file_too_large");
    }

    // ── Tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task An_interview_belonging_to_ANOTHER_tenant_cannot_be_attached_to_FR8()
    {
        var otherTenantInterview = Guid.NewGuid();
        await SeedInterviewAsync(tenantOverride: Guid.NewGuid(), interviewOverride: otherTenantInterview);
        await using var content = RealPdf();

        var result = await Service().UploadAsync(
            otherTenantInterview, content, "guide.pdf", Pdf, content.Length,
            InterviewAttachmentKind.Guide, null);

        result.IsFailure.Should().BeTrue("the global query filter must make it invisible, not merely unmatched");
        result.ErrorCode.Should().Be("interview_not_found");
    }

    // ── Lifecycle ───────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_an_attachment_removes_it_from_the_list_FR8()
    {
        await SeedInterviewAsync();
        await using var content = RealPdf();
        var created = await Service().UploadAsync(
            _interviewId, content, "guide.pdf", Pdf, content.Length, InterviewAttachmentKind.Guide, null);

        (await Service().DeleteAsync(created.Value!.Id)).IsSuccess.Should().BeTrue();

        var list = await Service().ListAsync(_interviewId);
        list.Value!.Should().BeEmpty();

        using var db = Db();
        var row = await db.InterviewAttachments.IgnoreQueryFilters().SingleAsync();
        row.IsDeleted.Should().BeTrue("soft-deleted, so a mis-click stays recoverable");
    }

    [Fact]
    public async Task Downloading_when_the_blob_is_missing_reports_it_rather_than_returning_an_empty_file_FR8()
    {
        // A metadata row that outlived its blob must not hand the recruiter zero bytes that look like a
        // corrupt document.
        await SeedInterviewAsync();
        await using var content = RealPdf();
        var created = await Service().UploadAsync(
            _interviewId, content, "guide.pdf", Pdf, content.Length, InterviewAttachmentKind.Guide, null);

        _fileStorage.OpenReadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Stream?)null);

        var result = await Service().DownloadAsync(created.Value!.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("attachment_content_missing");
    }
}
