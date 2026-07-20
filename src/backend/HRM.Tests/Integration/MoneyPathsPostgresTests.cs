// ============================================================================
// DF-3: real-Postgres (Testcontainers) coverage for two money-aggregation paths
// that were previously proven ONLY on the EF InMemory provider — the repo's
// standing "InMemory-masks-Postgres" bug class.
//
//   (a) BUG-114 / US-CHR-008 — the storage-quota check: a numeric SUM over
//       employee_documents.file_size_bytes JOINed against the plan's
//       max_storage_gb limit (EmployeeDocumentService.EnforceStorageQuotaAsync,
//       reached via the public UploadAsync). InMemory can mask both a numeric
//       SUM-precision issue and a plan-join that only "works" because InMemory
//       client-evaluates it.
//
//   (b) BUG-118 / US-LV-002 — the leave-ledger running balance: the latest
//       running balance is the BalanceAfter of the entry with the greatest
//       OccurredAt (OrderByDescending(OccurredAt).ThenByDescending(CreatedAt)),
//       driven through the public ComputeEffectiveEntitlementAsync →
//       GetLedgerBalanceAsync. InMemory can return insertion order and hide a
//       dropped/wrong OrderBy; on real Postgres the ORDER BY must translate and
//       be honoured. The seed inserts entries so that insertion order, CreatedAt
//       order, and OccurredAt order all DISAGREE, so the OccurredAt sort is
//       load-bearing (a dropped ORDER BY or a sort on the wrong column fails).
//
// WHY POSTGRES (not InMemory): numeric SUM translation, the plan Code join, and
// the ORDER BY on a running-balance ledger are exactly the SQL-translation
// behaviours InMemory does not exercise. An InMemory version of this file would
// be test theatre.
//
// ⚠ FK LESSON (last PG batch): Postgres enforces the FKs InMemory ignores. Every
// Employee gets a real Tenant + Department + JobTitle; every tenant_id row FKs a
// seeded Tenant (unique Subdomain). Distinct GUIDs per test.
//
// Harness copied from AttendanceSettingsCrudPostgresTests. UseSnakeCaseNaming-
// Convention() is NOT optional — omitting it makes MigrateAsync throw
// PendingModelChangesWarning.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Security;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class MoneyPathsPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
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
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db(Guid tenantId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options,
            tc);
    }

    private static EmployeeDocumentService DocumentService(AppDbContext db, Guid tenantId)
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");
        return new EmployeeDocumentService(
            db, new FixedTenantContext { TenantId = tenantId }, cu,
            Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(),
            NullLogger<EmployeeDocumentService>.Instance);
    }

    private static LeaveEntitlementService EntitlementService(AppDbContext db, Guid tenantId, int startMonth = 1)
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");
        var leaveYear = Substitute.For<ITenantLeaveYearResolver>();
        leaveYear.GetStartMonthAsync(Arg.Any<CancellationToken>()).Returns(startMonth);
        return new LeaveEntitlementService(
            db, new FixedTenantContext { TenantId = tenantId }, cu,
            NullLogger<LeaveEntitlementService>.Instance, leaveYear);
    }

    // ── seeding ────────────────────────────────────────────────────────

    /// <summary>Seeds the tenant + a plan (Code == Tenant.PlanId) with a known max_storage_gb.</summary>
    private static void SeedTenantWithPlan(AppDbContext db, Guid tenantId, string planCode, int? maxStorageGb)
    {
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = $"t{tenantId:N}"[..20], Name = "Acme Corp",
            Status = TenantStatus.Active, PlanId = planCode,
        });
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Code = planCode, Name = "Pro", MaxStorageGb = maxStorageGb,
        });
    }

    /// <summary>Seeds a real Employee with its Department + JobTitle FK rows and returns its id.</summary>
    private static Guid SeedEmployee(AppDbContext db, Guid tenantId, string no)
    {
        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = $"D{no}", Code = no, IsActive = true };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = $"T{no}", IsActive = true };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        var id = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "W",
            Email = $"{no}@acme.test", DepartmentId = dept.Id, JobTitleId = title.Id,
            DateOfJoining = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        return id;
    }

    private static void SeedDocument(AppDbContext db, Guid tenantId, Guid employeeId, long fileSizeBytes)
        => db.EmployeeDocuments.Add(new EmployeeDocument
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            FileName = "seed.pdf", StorageKey = $"core-hr/{employeeId}/seed-{Guid.NewGuid():N}.pdf",
            FileSizeBytes = fileSizeBytes, MimeType = "application/pdf",
            Category = DocumentCategory.Other, UploadedBy = Guid.NewGuid(), IsDeleted = false,
        });

    private static UploadEmployeeDocumentRequest Meta() => new() { Category = "Other" };

    private static Stream NonPdfBytes() => new MemoryStream([0x01, 0x02, 0x03, 0x04]);

    // ══════════════════════════════════════════════════════════════════
    //  (a) BUG-114 — storage-quota SUM + plan-join on real Postgres
    // ══════════════════════════════════════════════════════════════════

    // ══ Arm a1 — SUM(file_size_bytes) + plan join blocks an over-quota upload ══

    /// <summary>
    /// The quota check SUMs the tenant's document bytes and joins the plan's max_storage_gb; when the SUM
    /// plus the incoming file exceeds 100% of the limit the upload is hard-blocked (403 storage_quota_exceeded).
    /// This proves the numeric SUM and the plan Code join both translate + evaluate correctly on Npgsql: the
    /// block decision is a function of BOTH the aggregated usage and the joined limit.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-205")]
    public async Task StorageQuota_SumOverPlanLimit_BlocksTheUpload_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        Guid employeeId;

        await using (var seed = Db(tenantId))
        {
            SeedTenantWithPlan(seed, tenantId, "pro", maxStorageGb: 1); // 1 GB = 1_073_741_824 bytes
            employeeId = SeedEmployee(seed, tenantId, "E1");
            // Two documents summing to 1_070_000_000 — leaves ~3.5 MB head-room under the 1 GB cap.
            SeedDocument(seed, tenantId, employeeId, 700_000_000L);
            SeedDocument(seed, tenantId, employeeId, 370_000_000L);
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            // 6 MB incoming (≤ the 10 MB per-file cap) pushes projected usage past the 1 GB limit.
            var result = await DocumentService(db, tenantId).UploadAsync(
                employeeId, NonPdfBytes(), "x.pdf", "application/pdf", 6_000_000L, Meta(), default);

            result.IsFailure.Should().BeTrue("the SUM (1_070_000_000) + 6 MB exceeds the 1 GB plan limit");
            result.StatusCode.Should().Be(403);
            result.ErrorCode.Should().Be("storage_quota_exceeded");
        }
    }

    // ══ Arm a2 — the SUM is exact to the byte (boundary) ══

    /// <summary>
    /// Pins the numeric SUM precisely on Postgres. With seeded usage U and limit L, an incoming file of
    /// exactly (L − U) makes projected == L, which is NOT over quota — the upload passes the quota gate and
    /// is only then rejected by the file-signature gate (400 invalid_file_type, our dummy bytes aren't a PDF).
    /// One more byte (L − U + 1) makes projected == L + 1 and IS blocked (403). The transition happening at
    /// exactly one byte proves the SUM computed U to the byte (a client-eval or precision drift would move it).
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-205")]
    public async Task StorageQuota_SumBoundary_IsExactToTheByte_OnPostgres()
    {
        const long limitBytes = 1L * 1024 * 1024 * 1024;   // 1 GB, from MaxStorageGb = 1
        const long usage = 1_070_000_000L;                 // 700_000_000 + 370_000_000
        const long gap = limitBytes - usage;               // exact remaining head-room

        var tenantId = Guid.NewGuid();
        Guid employeeId;

        await using (var seed = Db(tenantId))
        {
            SeedTenantWithPlan(seed, tenantId, "pro", maxStorageGb: 1);
            employeeId = SeedEmployee(seed, tenantId, "E1");
            SeedDocument(seed, tenantId, employeeId, 700_000_000L);
            SeedDocument(seed, tenantId, employeeId, 370_000_000L);
            await seed.SaveChangesAsync();
        }

        // projected == limit → NOT blocked → falls through to the signature gate (400 invalid_file_type).
        await using (var db = Db(tenantId))
        {
            var atLimit = await DocumentService(db, tenantId).UploadAsync(
                employeeId, NonPdfBytes(), "x.pdf", "application/pdf", gap, Meta(), default);

            atLimit.IsFailure.Should().BeTrue();
            atLimit.StatusCode.Should().Be(400);
            atLimit.ErrorCode.Should().Be(FileSignatureValidator.ErrorCode,
                "projected usage exactly equals the limit — the quota gate must PASS, not block");
        }

        // projected == limit + 1 → blocked (403). One byte over the exact SUM flips the outcome.
        await using (var db = Db(tenantId))
        {
            var overLimit = await DocumentService(db, tenantId).UploadAsync(
                employeeId, NonPdfBytes(), "x.pdf", "application/pdf", gap + 1, Meta(), default);

            overLimit.IsFailure.Should().BeTrue();
            overLimit.StatusCode.Should().Be(403);
            overLimit.ErrorCode.Should().Be("storage_quota_exceeded",
                "one byte past the exact SUM must cross the limit — proves SUM(file_size_bytes) is byte-exact");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  (b) BUG-118 — leave-ledger running balance ordering on real Postgres
    // ══════════════════════════════════════════════════════════════════

    // ══ Arm b1 — the balance is the latest-by-OccurredAt entry, not insertion order ══

    /// <summary>
    /// GetLedgerBalanceAsync returns the BalanceAfter of the entry with the greatest OccurredAt
    /// (OrderByDescending(OccurredAt).ThenByDescending(CreatedAt)), surfaced through the public
    /// ComputeEffectiveEntitlementAsync.CurrentBalance. The three entries are inserted so that insertion order
    /// DISAGREES with OccurredAt order — the greatest-OccurredAt entry (Mar-03, the winner) is NOT the first
    /// inserted:
    ///
    ///   insert #1: OccurredAt = Mar-02, BalanceAfter = 15   (first inserted)
    ///   insert #2: OccurredAt = Mar-03, BalanceAfter = 17   (greatest OccurredAt → winner)
    ///   insert #3: OccurredAt = Mar-01, BalanceAfter = 20   (last inserted)
    ///
    /// Expected = 17. A dropped ORDER BY returns rows in insertion/heap order, so FirstOrDefault would grab
    /// the first-inserted row (15) — a wrong number. This makes the OccurredAt sort load-bearing, and only
    /// real Postgres proves the ORDER BY translates and is honoured (InMemory can return insertion order and
    /// mask the missing sort). CreatedAt is stamped by the AuditInterceptor at SaveChanges (it overrides any
    /// value set here), so this arm relies on OccurredAt — the primary sort key — not on the CreatedAt
    /// tiebreak; the three OccurredAt values are distinct so the tiebreak never engages.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-002-03")]
    public async Task LedgerBalance_IsLatestByOccurredAt_NotInsertionOrder_OnPostgres()
    {
        const int leaveYear = 2026;
        var tenantId = Guid.NewGuid();
        Guid employeeId, leaveTypeId;

        await using (var seed = Db(tenantId))
        {
            SeedTenantWithPlan(seed, tenantId, "pro", maxStorageGb: null);
            employeeId = SeedEmployee(seed, tenantId, "L1");
            leaveTypeId = BaseEntity.NewUuidV7();
            seed.LeaveTypes.Add(new LeaveType
            {
                Id = leaveTypeId, TenantId = tenantId, Name = "Annual Leave",
                AnnualEntitlement = 20m, DisplayOrder = 1,
            });
            await seed.SaveChangesAsync();

            // Insertion order deliberately disagrees with OccurredAt order: the winner (Mar-03) is not first.
            AddLedger(seed, tenantId, employeeId, leaveTypeId, leaveYear,
                occurredAt: new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc), balanceAfter: 15m);
            AddLedger(seed, tenantId, employeeId, leaveTypeId, leaveYear,
                occurredAt: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc), balanceAfter: 17m); // greatest OccurredAt
            AddLedger(seed, tenantId, employeeId, leaveTypeId, leaveYear,
                occurredAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), balanceAfter: 20m);
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var result = await EntitlementService(db, tenantId)
                .ComputeEffectiveEntitlementAsync(employeeId, leaveTypeId, leaveYear, default);

            result.IsSuccess.Should().BeTrue(result.Error);
            result.Value!.CurrentBalance.Should().Be(
                17m,
                "the running balance is the BalanceAfter of the entry with the greatest OccurredAt (Mar-03) — "
                + "NOT the first-inserted row (15)");
        }
    }

    /// <summary>
    /// Adds a LeaveLedger row with an explicit UTC-kinded OccurredAt (timestamptz). CreatedAt is intentionally
    /// left to the AuditInterceptor, which stamps it at SaveChanges.
    /// </summary>
    private static void AddLedger(
        AppDbContext db, Guid tenantId, Guid employeeId, Guid leaveTypeId, int leaveYear,
        DateTime occurredAt, decimal balanceAfter)
        => db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            EntryType = LedgerEntryType.Adjusted, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
            LeaveYear = leaveYear, Amount = balanceAfter, BalanceAfter = balanceAfter,
            OccurredAt = occurredAt,
        });
}
