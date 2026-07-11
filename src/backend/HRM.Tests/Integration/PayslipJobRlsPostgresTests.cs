// ============================================================================
// ISSUE-269 — RLS-on regression proof for the per-unit GUC-transaction split of the two payslip jobs.
//
// THE CHANGE: GeneratePayslipsJob / SendPayslipEmailsJob no longer run their whole body (bulk read → heavy
// render/upload / SMTP send → write) inside ONE ITenantJobRunner.RunForTenantAsync transaction. They now split
// into READ → WORK(no tx) → WRITE(per short chunk / per send), with the JOB driving a SEPARATE runner call
// around the READ and around each WRITE, and the heavy WORK outside any runner call.
//
// WHY THIS TEST: the unit tests (PayslipJobPhaseTests) reproduce the READ→WORK→WRITE loop BY HAND on InMemory —
// nothing drives the REAL job classes through the REAL runner, and InMemory does not implement RLS. So the
// job-level orchestration (the ~30 lines that ARE ISSUE-269) and the claim that each per-unit WRITE still
// carries the app.current_tenant GUC were unproven. This test closes that: it constructs the REAL jobs with a
// real IServiceScopeFactory whose scope resolves the REAL ITenantJobRunner + renderer/distribution-runner, and
// runs them under RLS-on (hrm_app NOBYPASSRLS + ENABLE/FORCE RLS). Each per-chunk / per-send WRITE opens its
// own runner transaction; if that transaction did NOT set the GUC the write would fail-closed (SQLSTATE 42501,
// like ISSUE-268). Landing the rows persisted + tenant-scoped is therefore the honest proof.
//
// Harness mirrors NotificationRlsPostgresTests / RlsIsolationPostgresTests: two production roles (hrm_app
// NOBYPASSRLS, hrm_owner BYPASSRLS), migrations + grants applied as the superuser, RLS ENABLE+FORCE on every
// tenant_id table (relies on the dormant tenant_isolation policies from the migrations). AppDbContext is
// hardwired to the hrm_app connection (as RlsIsolationPostgresTests test #9 does) so RLS is actually enforced.
//
// Assertions are on COMMITTED STATE only (rows persisted + tenant-scoped) — NO tx-duration / pg_stat_activity
// timing assertions (those would be flaky and are explicitly out of scope).
// ============================================================================

using System.Collections.Concurrent;
using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Payroll;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-PAY-269-RLS")]
[Trait("Category", "RlsIsolation")]
public sealed class PayslipJobRlsPostgresTests : IAsyncLifetime
{
    private const string AppRole = "hrm_app";
    private const string AppPassword = "app_pw_pay_269";
    private const string OwnerRole = "hrm_owner";
    private const string OwnerPassword = "owner_pw_pay_269";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private string _appConnString = null!;   // connects as hrm_app   (RLS ENFORCED)
    private string _ownerConnString = null!; // connects as hrm_owner (BYPASSRLS)

    private readonly InMemoryFileStorage _storage = new();
    private readonly RecordingEmailSender _sender = new();

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
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null)
            => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private sealed class InMemoryFileStorage : IFileStorage
    {
        public ConcurrentDictionary<(Guid, string), byte[]> Files { get; } = new();
        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken ct = default)
        { using var m = new MemoryStream(); content.CopyTo(m); Files[(tenantId, relativePath)] = m.ToArray(); return Task.FromResult($"/{tenantId}/{relativePath}"); }
        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
            => Task.FromResult<Stream?>(Files.TryGetValue((tenantId, relativePath), out var b) ? new MemoryStream(b, false) : null);
        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) => "/x";
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken ct = default) { Files.TryRemove((tenantId, relativePath), out _); return Task.CompletedTask; }
    }

    private sealed class RecordingEmailSender : IPayslipEmailSender
    {
        public ConcurrentBag<PayslipEmailMessage> Sent { get; } = new();
        public Task SendAsync(PayslipEmailMessage message, CancellationToken cancellationToken = default)
        { Sent.Add(message); return Task.CompletedTask; }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var superCs = _postgres.GetConnectionString();
        _appConnString = WithRole(superCs, AppRole, AppPassword);
        _ownerConnString = WithRole(superCs, OwnerRole, OwnerPassword);

        // (1) Provision the two production roles as the superuser (mirrors roles.sql).
        await ExecAsync(superCs,
            $"""
             DO $r$
             BEGIN
                 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                     CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}';
                 END IF;
                 IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                     CREATE ROLE {OwnerRole} LOGIN PASSWORD '{OwnerPassword}' BYPASSRLS;
                 END IF;
             END
             $r$;
             ALTER ROLE {AppRole} NOBYPASSRLS;
             """);

        // (2) Apply migrations (as the superuser) so the schema + DORMANT tenant_isolation policies exist.
        await using (var migrate = Db(superCs, new MutableTenantContext()))
        {
            await migrate.Database.MigrateAsync();
        }

        // (3) Grants for the app + owner roles.
        await ExecAsync(superCs,
            $"""
             GRANT USAGE ON SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}, {OwnerRole};
             GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}, {OwnerRole};
             """);

        // (4) Simulate the increment-3 reconciler: ENABLE + FORCE RLS on every tenant_id table (excl. users/tenants).
        await ExecAsync(superCs,
            """
            DO $e$
            DECLARE r record;
            BEGIN
                FOR r IN
                    SELECT c.table_name
                    FROM information_schema.columns c
                    JOIN information_schema.tables t
                      ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                    WHERE c.table_schema = 'public'
                      AND c.column_name  = 'tenant_id'
                      AND t.table_type   = 'BASE TABLE'
                      AND c.table_name NOT IN ('users', 'tenants')
                LOOP
                    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', r.table_name);
                    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', r.table_name);
                END LOOP;
            END
            $e$;
            """);

        // Sanity: the tables the jobs WRITE really are policied (else the RLS proof is vacuous).
        (await IsPoliciedAsync("payroll_slip")).Should().BeTrue("payroll_slips must carry the tenant_isolation policy");
        (await IsPoliciedAsync("payslip_email_log")).Should().BeTrue("payslip_email_logs must carry the tenant_isolation policy");

        // (5) Seed the two tenants as the superuser (bypasses RLS + WITH CHECK).
        await using var db = Db(superCs, new MutableTenantContext());
        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "tenant-b", Name = "Tenant B" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // Job C — the REAL SendPayslipEmailsJob, driven through the REAL ITenantJobRunner under RLS-on, lands a Sent
    // PayslipEmailLog row PER employee. Each row is written by its OWN per-send runner transaction; without the
    // GUC that transaction sets, the INSERT would fail-closed (42501). The rows are tenant-scoped: readable under
    // tenant A's GUC, invisible under tenant B's.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SendPayslipEmailsJob_UnderRlsOn_PersistsSentLogs_PerSend_TenantScoped()
    {
        var runId = await SeedFinalizedRunWithPdfs(_tenantA, ("EMP-1", "e1@t.com"), ("EMP-2", "e2@t.com"), ("EMP-3", "e3@t.com"));

        var job = new SendPayslipEmailsJob(BuildRlsScopeFactory());
        await job.RunAsync(_tenantA, $"tenant-{_tenantA}", runId, targetEmployeeIds: null);

        // Every send was recorded (WORK phase ran) …
        _sender.Sent.Select(m => m.RecipientEmail).Should().BeEquivalentTo(new[] { "e1@t.com", "e2@t.com", "e3@t.com" });

        // … and each per-send WRITE committed a Sent row under RLS-on (a 42501 would have thrown out of the job).
        (await RawCountAsync(_appConnString, _tenantA, "payslip_email_log",
                $"payroll_run_id = '{runId}' AND status = 'Sent'"))
            .Should().Be(3, "each per-send WRITE transaction carried the app.current_tenant GUC and satisfied WITH CHECK");

        // Tenant-scoped: tenant B's GUC cannot see tenant A's log rows.
        (await RawCountAsync(_appConnString, _tenantB, "payslip_email_log", $"payroll_run_id = '{runId}'"))
            .Should().Be(0, "tenant B's GUC must not read tenant A's payslip email logs");

        // Confirmed via the privileged (BYPASSRLS) owner too.
        (await OwnerCountAsync("payslip_email_log", $"payroll_run_id = '{runId}' AND tenant_id = '{_tenantA}'")).Should().Be(3);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Job B — the REAL GeneratePayslipsJob, driven through the REAL ITenantJobRunner under RLS-on, marks every
    // slip Generated with its stored path. The updates are committed by per-chunk runner transactions; without
    // the GUC the UPDATE would match 0 rows (RLS USING hides them) and the slips would stay ungenerated.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GeneratePayslipsJob_UnderRlsOn_MarksSlipsGenerated_PerChunk_TenantScoped()
    {
        var runId = await SeedRenderableRun(_tenantA, "EMP-A", "EMP-B");

        var job = new GeneratePayslipsJob(BuildRlsScopeFactory());
        await job.RunAsync(_tenantA, $"tenant-{_tenantA}", runId);

        // Both slips end Generated with a stored path + size — proving the per-chunk WRITE carried the GUC.
        (await RawCountAsync(_appConnString, _tenantA, "payroll_slip",
                $"payroll_run_id = '{runId}' AND pdf_status = 'Generated' AND pdf_storage_path IS NOT NULL AND pdf_file_size_bytes > 0"))
            .Should().Be(2, "each per-chunk WRITE transaction carried the app.current_tenant GUC and updated its rows");

        // Tenant-scoped: tenant B's GUC cannot see tenant A's slips.
        (await RawCountAsync(_appConnString, _tenantB, "payroll_slip", $"payroll_run_id = '{runId}'"))
            .Should().Be(0, "tenant B's GUC must not read tenant A's payroll slips");
    }

    // ── real-service DI graph (RLS-on, hrm_app) ────────────────────────────────

    // Scopes resolve the SAME-scope AppDbContext (hrm_app) + production TenantContext + TenantJobRunner +
    // renderer/distribution-runner — exactly the graph the jobs resolve from their own DI scope. Rls:Enabled=true
    // ⇒ each runner call sets the app.current_tenant GUC in a retry-safe transaction.
    private IServiceScopeFactory BuildRlsScopeFactory()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rls:Enabled"] = "true" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IFileStorage>(_storage);
        services.AddSingleton<IPayslipEmailSender>(_sender);
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddScoped<IPayslipBatchRenderer, PayslipBatchRenderer>();
        services.AddScoped<IPayslipDistributionRunner, PayslipDistributionRunner>();
        // AppDbContext hardwired to hrm_app (RLS enforced); shares the scope's ITenantContext for the query filter.
        services.AddScoped(sp => Db(_appConnString, sp.GetRequiredService<ITenantContext>()));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    // ── seeding (as the BYPASSRLS owner) ───────────────────────────────────────

    private async Task<Guid> SeedFinalizedRunWithPdfs(Guid tenantId, params (string No, string Email)[] employees)
    {
        await using var db = Db(_ownerConnString, new MutableTenantContext { TenantId = tenantId });
        var deptId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Engineering", Code = "ENG", IsActive = true });
        var jobId = BaseEntity.NewUuidV7();
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = tenantId, TitleName = "Engineer", IsActive = true });

        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = 5, PayYear = 2026, Status = PayrollRunStatus.Finalized,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = employees.Length, ProcessedEmployees = employees.Length,
        });
        foreach (var (no, email) in employees)
        {
            var empId = BaseEntity.NewUuidV7();
            db.Employees.Add(new Employee
            {
                Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = "Jane", LastName = no,
                Email = email, DepartmentId = deptId, JobTitleId = jobId,
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });
            var path = PayslipStoragePath.ForSlip(runId, empId);
            db.PayrollSlips.Add(new PayrollSlip
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
                GrossEarnings = 70_000m, TotalDeductions = 10_000m, NetSalary = 60_000m,
                WorkingDays = 22m, PaidDays = 22m, PayMonth = 5, PayYear = 2026,
                PdfStatus = PayslipPdfStatus.Generated, PdfStoragePath = path,
            });
            _storage.Files[(tenantId, path)] = "%PDF-1.4 fake"u8.ToArray();
        }
        await db.SaveChangesAsync();
        return runId;
    }

    private async Task<Guid> SeedRenderableRun(Guid tenantId, params string[] employeeNos)
    {
        await using var db = Db(_ownerConnString, new MutableTenantContext { TenantId = tenantId });
        var deptId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Engineering", Code = "ENG", IsActive = true });
        var jobId = BaseEntity.NewUuidV7();
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = tenantId, TitleName = "Engineer", IsActive = true });

        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = 5, PayYear = 2026, Status = PayrollRunStatus.Finalized,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = employeeNos.Length, ProcessedEmployees = employeeNos.Length,
        });
        foreach (var no in employeeNos)
        {
            var empId = BaseEntity.NewUuidV7();
            db.Employees.Add(new Employee
            {
                Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = "Jane", LastName = no,
                Email = $"{no}@t.com", DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = deptId, JobTitleId = jobId,
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });
            var slipId = BaseEntity.NewUuidV7();
            db.PayrollSlips.Add(new PayrollSlip
            {
                Id = slipId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
                GrossEarnings = 70_000m, TotalDeductions = 10_000m, NetSalary = 60_000m,
                WorkingDays = 22m, PaidDays = 22m, LopDays = 0m, PayMonth = 5, PayYear = 2026,
            });
            db.PayrollSlipDetails.Add(new PayrollSlipDetail
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(),
                ComponentName = "Basic", ComponentType = nameof(SalaryComponentType.Earning), Amount = 50_000m,
            });
        }
        await db.SaveChangesAsync();
        return runId;
    }

    // ── harness helpers (mirror NotificationRlsPostgresTests) ──────────────────

    private static string WithRole(string baseConnString, string user, string password) =>
        new NpgsqlConnectionStringBuilder(baseConnString) { Username = user, Password = password }.ToString();

    private static async Task ExecAsync(string connString, string sql)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private AppDbContext Db(string connString, ITenantContext tc) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options, tc);

    private async Task<bool> IsPoliciedAsync(string table)
    {
        await using var conn = new NpgsqlConnection(_ownerConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM pg_policies WHERE schemaname = 'public' AND policyname = 'tenant_isolation' AND tablename = @t",
            conn);
        cmd.Parameters.AddWithValue("t", table);
        return (long)(await cmd.ExecuteScalarAsync())! > 0;
    }

    // Raw count on hrm_app inside a GUC-set transaction (so RLS is in force), with an extra predicate.
    private static async Task<long> RawCountAsync(string connString, Guid guc, string table, string predicate)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, true)", conn, tx))
        {
            set.Parameters.AddWithValue("t", guc.ToString());
            await set.ExecuteNonQueryAsync();
        }
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {predicate}", conn, tx);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        await tx.CommitAsync();
        return count;
    }

    // Count via the privileged (BYPASSRLS) owner role, unaffected by RLS.
    private async Task<long> OwnerCountAsync(string table, string predicate)
    {
        await using var conn = new NpgsqlConnection(_ownerConnString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {predicate}", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
