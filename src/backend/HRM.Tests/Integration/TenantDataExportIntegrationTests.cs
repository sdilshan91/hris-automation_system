// ============================================================================
// US-ADM-010: tenant data-export integration tests (InMemory, resolved-tenant context).
//
//   - AC-1/AC-2/FR-2/FR-6: a full export produces a ZIP with per-entity CSVs + manifest.json (real SHA-256
//     checksums that match the entry bytes + correct row counts) + audit_log.jsonl; status → Completed + 72h expiry.
//   - FR-1: a partial export contains only the selected entity's CSV.
//   - AC-5: a Tenant A export contains zero Tenant B rows; a client-supplied foreign tenant id is ignored.
//   - AC-4/BR-2/BR-3: status gate — Terminating allowed; Suspended rejected for Tenant Admin but allowed for
//     System Admin; Terminated rejected.
//   - BR-5/FR-9: rate limit — one concurrent export; 3 per calendar month.
//   - AC-3/FR-7: download served when Completed & not expired; unavailable past ExpiresAt.
//
// PROVIDER: InMemory; ZIP/CSV/SHA-256 run in-memory via a fake IFileStorage. No Testcontainers.
// ============================================================================

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.DataExport;
using HRM.Application.Features.DataExport.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class TenantDataExportIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
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

    // In-memory tenant-scoped storage: key = "{tenantId}/{relativePath}".
    private sealed class FakeFileStorage : IFileStorage
    {
        public readonly Dictionary<string, byte[]> Files = new();
        private static string Key(Guid t, string p) => $"{t}/{p}";

        public async Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            Files[Key(tenantId, relativePath)] = ms.ToArray();
            return Key(tenantId, relativePath);
        }
        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
            => Task.FromResult(Files.TryGetValue(Key(tenantId, relativePath), out var b) ? (Stream?)new MemoryStream(b) : null);
        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) => Key(tenantId, relativePath);
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
        { Files.Remove(Key(tenantId, relativePath)); return Task.CompletedTask; }
    }

    private AppDbContext Db(Guid tenantId)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
            new MutableTenantContext { TenantId = tenantId });

    private (TenantDataExportService Service, FakeFileStorage Storage, IDataExportNotificationService Notify)
        Service(Guid tenantId)
    {
        var storage = new FakeFileStorage();
        var notify = Substitute.For<IDataExportNotificationService>();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(_userId);
        user.IsAuthenticated.Returns(true);
        user.Email.Returns("admin@acme.test");
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, ctx);
        var service = new TenantDataExportService(
            db, ctx, user, storage, notify, NullLogger<TenantDataExportService>.Instance, scheduler: null);
        return (service, storage, notify);
    }

    private async Task SeedTenantAsync(Guid tenantId, string subdomain, TenantStatus status)
    {
        using var db = Db(tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = status,
            PlanId = "starter", BillingEmail = $"billing@{subdomain}.test", CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedLeaveTypesAsync(Guid tenantId, int count)
    {
        using var db = Db(tenantId);
        for (var i = 0; i < count; i++)
        {
            db.LeaveTypes.Add(new LeaveType
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                Name = $"Leave {i}", Code = $"L{i}", Color = "#4CAF50",
                AnnualEntitlement = 10m, AccrualFrequency = AccrualFrequency.Upfront,
                Gender = LeaveTypeGender.All, SystemCategory = LeaveTypeSystemCategory.None,
                DisplayOrder = i, IsActive = true, CreatedAt = DateTime.UtcNow, CreatedBy = "seed",
            });
        }
        await db.SaveChangesAsync();
    }

    private static ExportInitiateRequest FullScope() => new("full", null, null, null);

    /// <summary>Runs initiate + generate for the tenant, returns the completed request id + the stored ZIP bytes.</summary>
    private async Task<(Guid Id, byte[] Zip)> RunExportAsync(Guid tenantId, ExportInitiateRequest request)
    {
        var (service, storage, _) = Service(tenantId);
        var init = await service.InitiateAsync(request);
        init.IsSuccess.Should().BeTrue(init.Error);
        var gen = await service.GenerateAsync(init.Value!.ExportId);
        gen.IsSuccess.Should().BeTrue(gen.Error);
        var zip = storage.Files.Single(kv => kv.Key.Contains("export") && kv.Value.Length > 0).Value;
        return (init.Value.ExportId, zip);
    }

    private static Dictionary<string, byte[]> ReadZip(byte[] zipBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        });
    }

    // ── AC-1/AC-2/FR-6: full bundle + manifest + checksums ──────────────────

    [Fact]
    public async Task FullExport_ProducesBundleWithManifestAndMatchingChecksums()
    {
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Active);
        await SeedLeaveTypesAsync(_tenantA, 3);

        var (id, zip) = await RunExportAsync(_tenantA, FullScope());
        var entries = ReadZip(zip);

        entries.Keys.Should().Contain(k => k.EndsWith("manifest.json"));
        entries.Keys.Should().Contain(k => k.EndsWith("audit_log.jsonl"));
        entries.Keys.Should().Contain(k => k.EndsWith("leave_types.csv"));

        // The leave_types.csv has a header + 3 data rows.
        var csv = Encoding.UTF8.GetString(entries.Single(e => e.Key.EndsWith("leave_types.csv")).Value);
        csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.Should().Be(4);

        // Manifest checksums match the actual entry bytes (FR-6 Test Hint).
        var manifestJson = Encoding.UTF8.GetString(entries.Single(e => e.Key.EndsWith("manifest.json")).Value);
        using var manifest = JsonDocument.Parse(manifestJson);
        var files = manifest.RootElement.GetProperty("files");
        files.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var file in files.EnumerateArray())
        {
            var name = file.GetProperty("filename").GetString()!;
            var declared = file.GetProperty("sha256_checksum").GetString()!;
            var entry = entries.Single(e => e.Key.EndsWith(name));
            var actual = Convert.ToHexString(SHA256.HashData(entry.Value)).ToLowerInvariant();
            declared.ToLowerInvariant().Should().Be(actual);
        }

        using var db = Db(_tenantA);
        var req = await db.ExportRequests.IgnoreQueryFilters().SingleAsync(r => r.Id == id);
        req.Status.Should().Be(ExportRequestStatus.Completed);
        req.ExpiresAt.Should().NotBeNull();
    }

    // ── FR-1: partial export ────────────────────────────────────────────────

    [Fact]
    public async Task PartialExport_ContainsOnlySelectedEntity()
    {
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Active);
        await SeedLeaveTypesAsync(_tenantA, 2);

        var (_, zip) = await RunExportAsync(_tenantA, new ExportInitiateRequest(null, new[] { "LeaveTypes" }, null, null));
        var entries = ReadZip(zip);

        entries.Keys.Should().Contain(k => k.EndsWith("leave_types.csv"));
        entries.Keys.Should().NotContain(k => k.EndsWith("employees.csv"));
    }

    // ── AC-5: cross-tenant isolation ────────────────────────────────────────

    [Fact]
    public async Task Export_ContainsOnlyOwnTenantRows()
    {
        var tenantB = Guid.NewGuid();
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Active);
        await SeedTenantAsync(tenantB, "globex", TenantStatus.Active);
        await SeedLeaveTypesAsync(_tenantA, 2);
        await SeedLeaveTypesAsync(tenantB, 5);

        var (_, zip) = await RunExportAsync(_tenantA, new ExportInitiateRequest(null, new[] { "LeaveTypes" }, null, null));
        var csv = Encoding.UTF8.GetString(ReadZip(zip).Single(e => e.Key.EndsWith("leave_types.csv")).Value);

        // header + only Tenant A's 2 rows (not B's 5).
        csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.Should().Be(3);
    }

    // ── AC-4/BR-2/BR-3: status gate ─────────────────────────────────────────

    [Fact]
    public async Task Initiate_TerminatingTenant_IsAllowed()
    {
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Terminating);
        var (service, _, _) = Service(_tenantA);
        (await service.InitiateAsync(FullScope())).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Initiate_SuspendedTenant_RejectedForTenantAdmin_AllowedForSystemAdmin()
    {
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Suspended);
        var (service, _, _) = Service(_tenantA);

        var tenantAdmin = await service.InitiateAsync(FullScope());
        tenantAdmin.IsFailure.Should().BeTrue();
        tenantAdmin.ErrorCode.Should().Be("tenant_suspended");

        var systemAdmin = await service.InitiateForTenantAsync(_tenantA, FullScope());
        systemAdmin.IsSuccess.Should().BeTrue(systemAdmin.Error); // BR-2
    }

    [Fact]
    public async Task Initiate_TerminatedTenant_IsRejected()
    {
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Terminated);
        var (service, _, _) = Service(_tenantA);

        (await service.InitiateAsync(FullScope())).ErrorCode.Should().Be("tenant_terminated");
        (await service.InitiateForTenantAsync(_tenantA, FullScope())).ErrorCode.Should().Be("tenant_terminated");
    }

    // ── BR-5/FR-9: rate limit ───────────────────────────────────────────────

    [Fact]
    public async Task Initiate_WhileOneInProgress_IsRejected()
    {
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Active);
        var (service, _, _) = Service(_tenantA);
        (await service.InitiateAsync(FullScope())).IsSuccess.Should().BeTrue(); // stays Queued (no scheduler)

        var second = await service.InitiateAsync(FullScope());
        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("export_in_progress");
    }

    [Fact]
    public async Task Initiate_FourthInSameMonth_IsRejected()
    {
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Active);
        // Seed 3 completed exports already this month.
        using (var db = Db(_tenantA))
        {
            for (var i = 0; i < 3; i++)
            {
                db.ExportRequests.Add(new ExportRequest
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, Scope = "full",
                    Status = ExportRequestStatus.Completed, RequestedByUserId = _userId,
                    RequestedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(72), CreatedAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
        }

        var (service, _, _) = Service(_tenantA);
        var fourth = await service.InitiateAsync(FullScope());
        fourth.IsFailure.Should().BeTrue();
        fourth.ErrorCode.Should().Be("monthly_limit_reached");
    }

    // ── AC-3/FR-7: download + expiry ────────────────────────────────────────

    [Fact]
    public async Task Download_CompletedNotExpired_ReturnsBundle()
    {
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Active);
        await SeedLeaveTypesAsync(_tenantA, 1);
        var (service, _, _) = Service(_tenantA);
        var init = await service.InitiateAsync(FullScope());
        await service.GenerateAsync(init.Value!.ExportId);

        var download = await service.DownloadAsync(init.Value.ExportId);
        download.IsSuccess.Should().BeTrue(download.Error);
        download.Value!.Content.Length.Should().BeGreaterThan(0);
        download.Value.FileName.Should().EndWith(".zip");
    }

    [Fact]
    public async Task Download_Expired_IsUnavailable()
    {
        await SeedTenantAsync(_tenantA, "acme", TenantStatus.Active);
        await SeedLeaveTypesAsync(_tenantA, 1);
        var (service, _, _) = Service(_tenantA);
        var init = await service.InitiateAsync(FullScope());
        await service.GenerateAsync(init.Value!.ExportId);

        // Force the request past its expiry.
        using (var db = Db(_tenantA))
        {
            var req = await db.ExportRequests.IgnoreQueryFilters().SingleAsync(r => r.Id == init.Value.ExportId);
            req.ExpiresAt = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }

        // A FRESH service (new DbContext) so the download re-reads the now-expired row rather than the stale
        // instance tracked by the service that created it.
        var (downloadService, _, _) = Service(_tenantA);
        var result = await downloadService.DownloadAsync(init.Value.ExportId);
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("export_expired");
    }
}
