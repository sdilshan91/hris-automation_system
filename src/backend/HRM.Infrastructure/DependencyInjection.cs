using EFCoreSecondLevelCacheInterceptor;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Interfaces;
using HRM.Infrastructure.Caching;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Security;
using HRM.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure;

/// <summary>
/// Registers all infrastructure services: DbContext, repositories, JWT, auth, tenant context, and RBAC.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // P3-4: field-at-rest encryptor (AES-256-GCM key ring). Singleton — stateless/thread-safe, so the EF value
        // converters on Pip notes / Recommendation compensation can safely close over the one instance. Fail-fast:
        // its constructor throws if no usable Encryption:ActiveKeyId + base64 32-byte Encryption:Keys key is
        // configured (never silently stores plaintext). Registered BEFORE AddDbContext so it is injectable into
        // AppDbContext's constructor.
        services.AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();

        // Tenant context (scoped per request)
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // P3/RLS increment 2c: the per-tenant job runner — sets the tenant context (and, gated on Rls:Enabled,
        // the app.current_tenant GUC inside a retry-safe transaction) so Hangfire per-tenant jobs stay inside the
        // RLS backstop. Scoped: resolved from the per-tenant scope each job creates, so it shares that scope's
        // AppDbContext + ITenantContext. Behaviour-neutral while Rls:Enabled is false (the committed default).
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();

        // EF Core interceptors
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<TenantInterceptor>();
        // US-NTF-004 / BUG-082: automatic generic INSERT/UPDATE/DELETE capture for all tenant BaseEntity
        // types EXCEPT those marked IAuditExempt (explicit-writer or high-volume — opt-OUT).
        services.AddScoped<AuditCaptureInterceptor>();

        // P3/RLS increment 2b: routes the DB connection per operation (hrm_app runtime vs hrm_owner privileged)
        // via the AsyncLocal ambient tenant. Singleton — it holds only the two static connection strings. With a
        // BLANK PrivilegedConnection (the committed default) it always uses DefaultConnection and never mutates
        // the connection string, so behaviour is identical to today until the increment-3 flip.
        services.AddSingleton<ConnectionRoutingInterceptor>();

        // US-PLT-002 (ISSUE-277): sets the app.current_tenant GUC at SESSION scope on every connection open (a
        // single, tx-less set_config), replacing the retired per-request TenantTransactionBehavior — whose
        // request-wide transaction broke under EnableRetryOnFailure and nested with handlers that open their own
        // transaction. Singleton — it holds only the Rls:Enabled flag. INERT while Rls:Enabled is false.
        services.AddSingleton<TenantGucConnectionInterceptor>();

        // P3 (EF second-level cache): transparent query-result caching for slow-changing REFERENCE tables,
        // tenant-safe via a dynamic per-request cache-key prefix (see Caching/CacheTenantPrefix). Registers the
        // SecondLevelCacheInterceptor (a COMMAND interceptor) which is added to AppDbContext below. Must run
        // BEFORE AddDbContext so the interceptor is resolvable in the options factory.
        services.AddTenantSafeSecondLevelCache(configuration);

        // DbContext with PostgreSQL + snake_case naming
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention();

            // Add interceptors. ORDER MATTERS: tenant stamping + audit-field/UUIDv7 stamping must run BEFORE
            // audit CAPTURE so the captured rows see the stamped TenantId and generated resource Id
            // (US-NTF-004). The second-level cache is a COMMAND interceptor (not a SaveChanges one), so it
            // coexists with the three SaveChanges interceptors without ordering interplay; it auto-invalidates
            // whitelisted tables on SaveChanges. On the InMemory provider it is inert (no relational commands).
            var tenantInterceptor = serviceProvider.GetRequiredService<TenantInterceptor>();
            var auditInterceptor = serviceProvider.GetRequiredService<AuditInterceptor>();
            var auditCaptureInterceptor = serviceProvider.GetRequiredService<AuditCaptureInterceptor>();
            var secondLevelCacheInterceptor = serviceProvider.GetRequiredService<SecondLevelCacheInterceptor>();
            // ConnectionRoutingInterceptor is a CONNECTION interceptor (routes hrm_app vs hrm_owner at open),
            // independent of the SaveChanges/command interceptors above — ordering among them is irrelevant.
            var connectionRoutingInterceptor = serviceProvider.GetRequiredService<ConnectionRoutingInterceptor>();
            // TenantGucConnectionInterceptor is also a CONNECTION interceptor but hooks the POST-open events
            // (ConnectionOpened/Async) to run set_config on the now-open connection — ordering vs the routing
            // interceptor (which hooks pre-open) is irrelevant. Inert while Rls:Enabled is false.
            var tenantGucConnectionInterceptor = serviceProvider.GetRequiredService<TenantGucConnectionInterceptor>();
            options.AddInterceptors(
                tenantInterceptor, auditInterceptor, auditCaptureInterceptor, secondLevelCacheInterceptor,
                connectionRoutingInterceptor, tenantGucConnectionInterceptor);
        });

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());

        // TOTP service (singleton — no per-request state)
        services.AddSingleton<ITotpService, TotpService>();

        // US-AUTH-005 NFR-2: encrypt the TOTP MFA secret at rest via ASP.NET Core Data Protection.
        // ISSUE-247: the key ring is persisted to Postgres (via EF, DataProtectionKeys DbSet) so keys survive
        // redeploys and are shared across instances — otherwise the default ephemeral per-instance ring rotates
        // and encrypted MFA secrets become undecryptable. SetApplicationName("HRM") fixes the discriminator
        // (the default is derived from the content-root path, which differs per container/deploy slot, so
        // without it the persisted keys still wouldn't be shared). Singleton; the protector is thread-safe.
        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>()
            .SetApplicationName("HRM");
        services.AddSingleton<IFieldProtector, MfaSecretProtector>();

        // Note: JwtService is registered in Program.cs alongside JWT authentication config.
        // Auth service
        services.AddScoped<IAuthService, AuthService>();

        // CR-AUTH-001 Increment 2: Microsoft Entra (O365) OIDC SSO.
        services.Configure<EntraSsoOptions>(configuration.GetSection(EntraSsoOptions.SectionName));
        services.AddHttpClient("EntraSso");
        // ConfigurationManager caches the Entra discovery document + JWKS and auto-refreshes; one shared
        // singleton across requests. Built from the configured authority's well-known metadata endpoint.
        services.AddSingleton(sp =>
        {
            var entra = configuration.GetSection(EntraSsoOptions.SectionName).Get<EntraSsoOptions>() ?? new EntraSsoOptions();
            var metadataAddress = $"{entra.Authority.TrimEnd('/')}/.well-known/openid-configuration";
            return new Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                metadataAddress,
                new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever(),
                new Microsoft.IdentityModel.Protocols.HttpDocumentRetriever());
        });
        services.AddScoped<IEntraSsoService, EntraSsoService>();

        // Lockout notification service (US-AUTH-010 FR-8)
        services.AddScoped<ILockoutNotificationService, LockoutNotificationService>();

        // RBAC service
        services.AddScoped<IRoleService, RoleService>();

        // Department service (US-CHR-004)
        services.AddScoped<IDepartmentService, DepartmentService>();

        // Job title service (US-CHR-005)
        services.AddScoped<IJobTitleService, JobTitleService>();

        // Employee service (US-CHR-001)
        services.AddScoped<IEmployeeService, EmployeeService>();

        // Location service (US-CHR-007)
        services.AddScoped<ILocationService, LocationService>();

        // Employee directory service (US-CHR-003)
        services.AddScoped<IEmployeeDirectoryService, EmployeeDirectoryService>();

        // Organization tree service (US-CHR-006)
        services.AddScoped<IOrganizationTreeService, OrganizationTreeService>();

        // Employee document service (US-CHR-008)
        services.AddScoped<IEmployeeDocumentService, EmployeeDocumentService>();

        // Employee status management service (US-CHR-009)
        services.AddScoped<IEmployeeStatusService, EmployeeStatusService>();

        // Bulk employee import service (US-CHR-010)
        services.AddScoped<IBulkEmployeeImportService, BulkEmployeeImportService>();

        // Reporting structure service (US-CHR-011)
        services.AddScoped<IReportingStructureService, ReportingStructureService>();

        // Custom field service (US-CHR-012)
        services.AddScoped<ICustomFieldService, CustomFieldService>();

        // Leave type service (US-LV-001)
        services.AddScoped<ILeaveTypeService, LeaveTypeService>();

        // Leave entitlement service (US-LV-002)
        services.AddScoped<ILeaveEntitlementService, LeaveEntitlementService>();

        // Leave request service (US-LV-003)
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();

        // Leave balance dashboard read/aggregation service (US-LV-006)
        services.AddScoped<ILeaveDashboardService, LeaveDashboardService>();

        // Holiday calendar service (US-LV-007)
        services.AddScoped<IHolidayService, HolidayService>();

        // Leave carry-forward / expiry service (US-LV-008)
        services.AddScoped<ILeaveCarryForwardService, LeaveCarryForwardService>();

        // Compulsory-leave / Loss-of-Pay (LOP) service (US-LV-011)
        services.AddScoped<ILopService, LopService>();

        // Leave reports & analytics read/aggregation service (US-LV-012). IBackgroundJobClient is
        // optional (registered by Hangfire in Program.cs) — large exports route to a background job
        // when it is present; without it the service reports that background routing is required.
        services.AddScoped<ILeaveReportService, LeaveReportService>();

        // Report-export storage seam (US-LV-012 FR-5) — local/log-only until a real blob store exists.
        services.AddScoped<IReportExportStorage, LocalReportExportStorage>();

        // US-NTF-006 Phase 6: real in-app + email delivery for attendance/overtime/regularization notifications.
        // LogOnlyAttendanceNotificationService is kept as a sibling (integration tests may register it); this Real
        // impl supersedes it in the app composition.
        services.AddScoped<IAttendanceNotificationService, RealAttendanceNotificationService>();

        // US-NTF-006 Phase 7: real in-app + email delivery for the Core-HR "deferred notify" sites
        // (probation-ending / manager-reassignment / document-expiry). LogOnlyCoreHrNotificationService is kept as a
        // sibling (tests may register it); this Real impl supersedes it in the app composition.
        services.AddScoped<ICoreHrNotificationService, RealCoreHrNotificationService>();

        // Attendance clock-in service (US-ATT-001)
        services.AddScoped<IAttendanceService, AttendanceService>();

        // Manager approve/reject of attendance regularizations (US-ATT-004)
        services.AddScoped<IRegularizationApprovalService, RegularizationApprovalService>();

        // Shift management and assignment (US-ATT-005)
        services.AddScoped<IShiftService, ShiftService>();

        // Overtime tracking and approval (US-ATT-006). Also drives clock-out auto-detection
        // (AttendanceService depends on it) — register BEFORE/alongside IAttendanceService.
        services.AddScoped<IOvertimeService, OvertimeService>();

        // Monthly attendance summary aggregation + read/export (US-ATT-007). IBackgroundJobClient is
        // optional (registered by Hangfire in Program.cs) — large exports (> 1,000 employees) route to a
        // background job when present; reuses IReportExportStorage for the stored file.
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();

        // Attendance ⇄ Payroll integration — attendance side (US-ATT-009): payroll-data pull, the
        // canonical attendance-period lock lifecycle, and the attendance-side reconciliation. Reuses
        // IAttendanceSummaryService for the core rollup.
        services.AddScoped<IAttendancePayrollService, AttendancePayrollService>();

        // Late-arrival / early-departure policy + reporting (US-ATT-008). Detection itself is inline in
        // AttendanceService (clock-in/out) and RegularizationApprovalService (approval recompute).
        services.AddScoped<ILateEarlyService, LateEarlyService>();

        // HR attendance dashboard + reports (US-ATT-010): KPIs, live board (polled — SignalR deferred),
        // department comparison, custom report + export, 12-month trends (from the monthly summary), and
        // scheduled-report config CRUD. Reuses IAttendanceSummaryService for the monthly rollup.
        services.AddScoped<IAttendanceDashboardService, AttendanceDashboardService>();

        // Holiday provider — DB-backed (US-LV-007 AC-2). Replaced the NoOp seam left by US-LV-003.
        services.AddScoped<IHolidayProvider, HolidayProvider>();

        // Attendance provider — NoOp seam (US-LV-011 FR-2). No attendance module yet (US-ATT-*), so
        // the absenteeism auto-LOP job is wired/idempotent but generates nothing until a real provider
        // lands (mirrors how IHolidayProvider was a NoOp until US-LV-007 swapped in the real impl).
        services.AddScoped<IAttendanceProvider, NoOpAttendanceProvider>();

        // ISSUE-188: real leave notification producer — persists + real-time-pushes via
        // INotificationService (US-NTF-001) for requested/approved/rejected/cancelled/LOP events.
        services.AddScoped<ILeaveNotificationService, LeaveNotificationService>();

        // US-REC-001: Recruitment — vacancy lifecycle + anonymous public careers page.
        services.AddScoped<IVacancyService, VacancyService>();
        services.AddScoped<IPublicCareersService, PublicCareersService>();

        // US-REC-002: Recruitment — applicant submission (public + internal) + recruiter reads.
        services.AddScoped<IApplicantService, ApplicantService>();
        // Recruitment notification seam — US-NTF-006 Phase 5a: real delivery (email + in-app) via the notification
        // dispatcher; the offer_sent candidate leg attaches the offer PDF inline via IEmailSender + IFileStorage.
        // LogOnlyRecruitmentNotificationService is retained (integration tests register it).
        services.AddScoped<IRecruitmentNotificationService, RealRecruitmentNotificationService>();

        // US-REC-009: Recruitment dashboard + analytics (read-only aggregation, no new entities).
        services.AddScoped<IRecruitmentDashboardService, RecruitmentDashboardService>();

        // US-ONB-001: Onboarding checklist template management (create/clone/activate/list).
        services.AddScoped<IOnboardingTemplateService, OnboardingTemplateService>();

        // US-ONB-002: Onboarding checklist assignment (assign/applicable-templates/get/modify). Takes an
        // OPTIONAL IBackgroundJobClient (Hangfire) so it never requires real Hangfire storage in tests/dev;
        // notification dispatch uses the outbox pattern (NFR-3) drained by OnboardingNotificationDispatchJob.
        services.AddScoped<IOnboardingChecklistService, OnboardingChecklistService>();

        // US-ONB-004: lite asset register + issuance tracking (reuses IFileStorage + IVirusScanner seams).
        services.AddScoped<IAssetService, AssetService>();

        // US-ONB-005: offboarding / exit clearance. Reuses IAuthService (refresh-token revocation) + the
        // ISessionRevoker (FR-7 access-token revocation) + IPayrollFnFIntegration (FR-6 F&F trigger seam).
        services.AddScoped<IOffboardingService, OffboardingService>();
        services.AddScoped<IPayrollFnFIntegration, LogOnlyPayrollFnFIntegration>();

        // P3-2: real JWT access-token denylist / session revocation. When Redis is configured, revoking a user's
        // sessions writes a per-(tenant,user) "revoked-before" cutoff to Redis and the OnTokenValidated hook rejects
        // pre-cutoff access tokens (fail-open on any Redis error). Without Redis, keep the no-op pair so the app runs
        // (and the denylist check no-ops gracefully). The IConnectionMultiplexer is registered by the API host's
        // AddSharedRedisMultiplexer (Program.cs) under the same Redis-configured condition.
        var revokerRedisConn = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(revokerRedisConn))
        {
            services.AddScoped<ITokenDenylist, RedisTokenDenylist>();
            services.AddScoped<ISessionRevoker, RedisSessionRevoker>();
        }
        else
        {
            services.AddScoped<ITokenDenylist, NoOpTokenDenylist>();
            services.AddScoped<ISessionRevoker, NoOpSessionRevoker>();
        }

        // US-ONB-006: exit-interview recording + analytics. Reuses the onboarding notification outbox (FR-8)
        // and the offboarding instance/task link (FR-3/AC-2). Anonymized analytics (FR-5).
        services.AddScoped<IExitInterviewService, ExitInterviewService>();

        // US-PAY-001: Payroll — salary component + salary structure configuration.
        services.AddScoped<ISalaryComponentService, SalaryComponentService>();
        services.AddScoped<ISalaryStructureService, SalaryStructureService>();

        // US-PAY-002: Payroll — assign salary structure to employee (CTC breakdown, revision history,
        // bulk assign, future-dated supersession).
        services.AddScoped<ISalaryAssignmentService, SalaryAssignmentService>();

        // US-PAY-003: Payroll — monthly payroll-run engine. The run service (initiate + reads) takes an
        // OPTIONAL IPayrollRunJobScheduler (Hangfire-backed impl registered in Program.cs) so it never
        // requires real Hangfire storage in tests/dev. The processor does the heavy compute (invoked by the
        // Hangfire job, or directly in tests). Notification is a log-only seam until US-NTF.
        services.AddScoped<IPayrollRunService, PayrollRunService>();
        services.AddScoped<IPayrollRunProcessor, PayrollRunProcessor>();
        // ISSUE-154: the shared slip-cleanup used by BOTH the re-run (ProcessAsync) and cancel (CancelAsync)
        // paths — ONE implementation of the slip-removal + adjustment-revert logic.
        services.AddScoped<IPayrollSlipCleaner, PayrollSlipCleaner>();
        services.AddScoped<IPayrollApprovalService, PayrollApprovalService>();  // US-PAY-008
        services.AddScoped<IPayrollNotificationService, RealPayrollNotificationService>();  // US-NTF-006 Phase 4

        // US-PAY-004: Payroll — payslip-PDF generation. The generation service (enqueue + status + downloads)
        // takes an OPTIONAL IPayslipGenerationJobScheduler (Hangfire-backed impl registered in Program.cs) so
        // it never requires real Hangfire storage in tests/dev. The batch renderer does the heavy QuestPDF
        // render + tenant-isolated blob store (invoked by the Hangfire job, or directly in tests). Reuses the
        // existing IFileStorage abstraction for blob storage.
        services.AddScoped<IPayslipGenerationService, PayslipGenerationService>();
        services.AddScoped<IPayslipBatchRenderer, PayslipBatchRenderer>();

        // US-PAY-011: Payroll — bulk payslip-email distribution. The distribution service (enqueue + summary +
        // duplicate-send guard) takes an OPTIONAL IPayslipDistributionJobScheduler (Hangfire-backed impl in
        // Program.cs) so it never requires real Hangfire storage in tests/dev. The runner does the per-employee
        // send loop with Polly retry (NFR-2) + writes a PayslipEmailLog per employee; it reuses the existing
        // IFileStorage abstraction to load each PDF and the log-only IPayslipEmailSender seam to dispatch (real
        // SMTP deferred, TODO US-NTF).
        services.AddScoped<IPayslipDistributionService, PayslipDistributionService>();
        services.AddScoped<IPayslipDistributionRunner, PayslipDistributionRunner>();
        services.AddScoped<IPayslipEmailSender, RealPayslipEmailSender>();  // US-NTF-006 Phase 4

        // US-PAY-006: Payroll — statutory deduction configuration (income-tax slabs, EPF/ETF/professional/custom
        // social-security) + the side-effect-free FR-5 test calculation. The deduction resolver (FR-4) is the
        // single source of truth shared by the test calc AND the payroll-run engine (US-PAY-003) so previewed
        // numbers == run numbers; all math is delegated to the pure HRM.Domain StatutoryCalculator (NFR-5).
        services.AddScoped<IStatutoryRuleService, StatutoryRuleService>();
        services.AddScoped<IStatutoryDeductionResolver, StatutoryDeductionResolver>();

        // US-PAY-007: Payroll — adjustments (bonus/deduction/reimbursement/correction). CRUD + bulk CSV +
        // supporting-document upload live in the service; the run-engine integration is the adjustment resolver
        // (FR-3 pickup + FR-4 mark-Applied), wired ADDITIVELY into PayrollRunProcessor exactly like the
        // statutory resolver — when no adjustments exist it is a no-op and existing US-PAY-003/006 runs are
        // unchanged. Documents reuse the existing IFileStorage abstraction.
        services.AddScoped<IPayrollAdjustmentService, PayrollAdjustmentService>();
        services.AddScoped<IPayrollAdjustmentResolver, PayrollAdjustmentResolver>();

        // US-PAY-010: Payroll — attendance + leave integration. The overtime earning (AC-2) is wired into the
        // run engine (PayrollRunProcessor) via the existing US-ATT-009 attendance pull; leave encashment (AC-3)
        // reuses the US-PAY-007 adjustment mechanism (creates a Bonus earning for the next run); the
        // attendance-finalized gate (AC-4) lives in PayrollRunService.InitiateAsync; the pre-payroll
        // reconciliation report (FR-7) is a read on PayrollRunService. Only the encashment service is new.
        services.AddScoped<ILeaveEncashmentService, LeaveEncashmentService>();

        // US-PAY-005: Payroll — employee self-service payslip read (list / detail / PDF download). Resolves the
        // caller's employee_id from ICurrentUser and scopes every read to it (own employee + own tenant); only
        // Finalized-run slips are visible (BR-1); a cross-employee payslip id is a deliberate 403 (AC-4).
        services.AddScoped<IMyPayslipService, MyPayslipService>();

        // US-PAY-009: Payroll — reports + analytics. Pure read/aggregation over the existing slips/details/
        // adjustments from FINALIZED runs only (BR-1), tenant-scoped via the EF global query filter (AC-5).
        // Exports reuse the leave-module export approach (ClosedXML/CsvHelper) + the US-PAY-004 QuestPDF
        // setup via the pure PayrollReportRenderer; no new export infra is introduced.
        services.AddScoped<IPayrollReportService, PayrollReportService>();

        // US-RPT-001: Reports — pre-built HR reports (Headcount, Turnover, Demographics, Joiners & Leavers,
        // Department Distribution, Employment Type Breakdown). Pure read/aggregation over the existing
        // employee / department / location / employment_history tables (NO new entity / migration),
        // tenant-scoped via the EF global query filter (AC-5). IDistributedCache is the SAME optional Redis
        // seam used elsewhere (TenantSettings / NotificationPreferences): present → cached results
        // (FR-5, 10-min TTL, FR-8 refresh bypass), absent/in-memory → straight compute. Export (US-RPT-004)
        // and materialized views (FR-6) are deliberately NOT built here.
        services.AddScoped<IHrReportService, HrReportService>();

        // US-RPT-004: Reports — export the generic HR/leave/attendance reports to CSV/Excel/PDF. Initiation
        // regenerates the report (IHrReportService) to learn the row count, then routes small reports (< 1000
        // rows, FR-5) to an INLINE render+store+complete and large ones (>= 1000 rows) to a Hangfire job
        // (HrReportExportJob) via the OPTIONAL IHrReportExportJobScheduler (bound in HRM.Api; absent in
        // tests → the test/job calls GenerateAsync directly). Rendering is the pure HrReportRenderer (mirrors the
        // US-PAY-009 PayrollReportRenderer); storage reuses the existing IReportExportStorage seam; the FR-8
        // async-complete notify uses the US-NTF-001 INotificationService seam (optional). FR-10 limits each user
        // to 3 in-progress exports; every export is audited (FR-9). The BR-3 7-day retention purge is
        // HrReportExportCleanupService, driven by the daily HrReportExportCleanupJob. SEPARATE from payroll
        // (US-PAY-009) + US-ADM-010 tenant export. DEFERRED: chart-as-PNG in the PDF (no server-side chart
        // renderer) + signed URLs/15-min expiry (FR-7 — local storage stub; the authenticated tenant+owner
        // /download endpoint + 7-day retention satisfy AC-5).
        services.AddScoped<IHrReportExportService, HrReportExportService>();
        services.AddScoped<IHrReportExportCleanupService, HrReportExportCleanupService>();

        // ISSUE-178 PR2: Payroll — async large-report export (CSV/Excel/PDF). The parallel of the US-RPT-004
        // HrReportExport pattern for the US-PAY-009/US-RPT-003 payroll reports. Initiation regenerates the report
        // (IPayrollReportService) to learn the row count, then routes small reports (< 1000 rows) to an INLINE
        // render+store+complete and large ones (>= 1000 rows) to a Hangfire job (PayrollReportExportJob) via the
        // OPTIONAL IPayrollReportExportJobScheduler (bound in HRM.Api; absent in tests → the test/job calls
        // GenerateAsync directly). Rendering reuses the shipped IPayrollReportService.ExportReportAsync (so
        // BankAdvice stays FULL-account-number unmasked, BR-2); storage reuses IReportExportStorage; the
        // async-complete notify uses INotificationService (optional). Each user is limited to 3 in-progress
        // exports; every export is audited. The 7-day retention purge is PayrollReportExportCleanupService, driven
        // by the daily PayrollReportExportCleanupJob. The existing synchronous GET .../export endpoint is kept for
        // back-compat.
        services.AddScoped<IPayrollReportExportService, PayrollReportExportService>();
        services.AddScoped<IPayrollReportExportCleanupService, PayrollReportExportCleanupService>();

        // US-RPT-005: Reports — role-based KPI dashboard. A THIN COMPOSITION layer over the existing
        // per-module services (IHrReportService, IVacancyService, ILeaveRequestService, ILeaveDashboardService,
        // IAttendanceDashboardService, IOnboardingChecklistService, IHolidayService, IAppraisalCycleService,
        // IMyPayslipService) + direct tenant-scoped Employee/Onboarding/AttendanceSummary reads for the
        // team-size / birthdays / recent-joiners / counts. It RECOMPUTES nothing and adds NO new entity /
        // migration. The role (hr / manager / employee) is derived SERVER-SIDE from ICurrentUser (BR-1 / FR-8).
        // IDistributedCache is the SAME optional Redis seam used elsewhere: present → per-widget cache
        // (FR-4, ~3-min TTL, refresh=true bypass), absent/in-memory → straight compute. The optional
        // collaborators are resolved by the container when registered; constructor-optional so tests can wire a
        // subset. DEFERRED: BR-5 module-enablement gating (no per-tenant enablement flag wired) — all modules
        // assumed on; the general US-NTF pending-actions/forms queue (not persisted) — onboarding tasks only.
        services.AddScoped<IDashboardService, DashboardService>();

        // US-PAY-012: Payroll — history + structured audit trail. The audit logger writes structured entries
        // into the shared audit_log table (extended additively); the history/audit-trail/export reads live in
        // PayrollAuditService. Audit export reuses the US-PAY-009 PayrollReportRenderer + IReportExportStorage.
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IPayrollAuditService, PayrollAuditService>();

        // US-REC-005: Recruitment — interview scheduling/rescheduling/cancellation + calendar reads.
        // IInterviewReminderScheduler is OPTIONAL (Hangfire-backed impl registered in Program.cs); without
        // it the service skips reminder scheduling so the flow never requires real Hangfire storage.
        services.AddScoped<IInterviewService, InterviewService>();

        // US-REC-006: Recruitment — structured interview scorecard submission + reads (anti-bias, FR-6).
        services.AddScoped<IScorecardService, ScorecardService>();

        // US-REC-007: Recruitment — offer-letter generation/send/response/withdrawal + reads.
        // IOfferExpiryScheduler is OPTIONAL (Hangfire-backed impl registered in Program.cs); without it the
        // service skips expiry scheduling so the flow never requires real Hangfire storage.
        services.AddScoped<IOfferService, OfferService>();

        // US-REC-008: Recruitment — candidate portal (magic-link token issue/validate + sanitized dashboard,
        // offer respond, resume/offer downloads). The HMAC signing secret is read from configuration
        // (Recruitment:PortalTokenSecret, falling back to Jwt:PrivateKey) — never hardcoded.
        services.AddScoped<IApplicantPortalTokenService, ApplicantPortalTokenService>();
        services.AddScoped<IApplicantPortalService, ApplicantPortalService>();

        // US-REC-010: Recruitment — convert an accepted applicant to a Core HR employee (atomic, reuses
        // IEmployeeService.CreateAsync). Completes the Recruitment module.
        services.AddScoped<IApplicantConversionService, ApplicantConversionService>();

        // US-PRF-001: Performance — manager goal-setting (create/update/delete goals + team dashboard +
        // per-employee goals). Authorization (BR-4), goal-setting-window gate (BR-1/AC-5), 1-10 count (BR-2)
        // and ≤100% weight (FR-3/AC-3) are enforced in the service. Notification is a log-only seam (FR-7,
        // TODO US-NTF). The minimal AppraisalCycle entity is created here to unblock goal-setting; full cycle
        // management is owned by US-PRF-004.
        services.AddScoped<IGoalService, GoalService>();
        // US-NTF-006 Phase 5b (final seam): real in-app + email delivery for performance notifications. LogOnly is
        // kept (integration tests register it); this Real impl supersedes it in the app composition.
        services.AddScoped<IPerformanceNotificationService, RealPerformanceNotificationService>();

        // US-PRF-002: Performance — employee self-assessment (get-my / save-draft / submit). Every read/write
        // is scoped to the calling employee's own record + tenant (NFR-2); the self-assessment-window gate
        // (BR-1/AC-4), all-goals-rated + comment-length submit rules (BR-2/FR-3), weighted-score calc (FR-4)
        // and submitted-lock (BR-3) are enforced in the service. The reminder service (FR-7/AC-5) is driven by
        // the SelfAssessmentReminderJob Hangfire job and dispatches via the shared performance notification seam.
        services.AddScoped<ISelfAssessmentService, SelfAssessmentService>();
        services.AddScoped<ISelfAssessmentReminderService, SelfAssessmentReminderService>();

        // US-PRF-002 (FR-5 / ISSUE-105): self-assessment evidence-file attachments (upload / list / download).
        // Kept SEPARATE from ISelfAssessmentService so the ratings service constructor is untouched. Reuses the
        // existing IFileStorage + IVirusScanner seams; scoped to the caller's own self-assessment + tenant.
        services.AddScoped<ISelfAssessmentAttachmentService, SelfAssessmentAttachmentService>();

        // US-PRF-003: Performance — manager performance review (workspace / save-draft / submit / reopen /
        // team dashboard). The manager-review-window gate (BR-1/AC-5), the direct-report scope (BR-2) and
        // HR-override + reopen (BR-3/AC-5), the all-goals-rated + comment-length submit rules (AC-3/FR-3), the
        // weighted-manager-score and the FINAL combined-score blend (BR-4) are enforced in the service.
        // ComputeFinalScore is the single extension point US-PRF-005 (360 feedback, BR-5) will widen.
        services.AddScoped<IManagerReviewService, ManagerReviewService>();

        // US-PRF-006: Performance — review meeting notes + digital sign-off. The notes-only-after-submit gate
        // (BR-1), HTML sanitization of rich text (FR-1/§10), the manager→employee sign-off order (FR-3), the
        // IMMUTABLE append-only ReviewSignoff log + locked-review enforcement (NFR-3/BR-5), mandatory dispute
        // comments + HR resolve (FR-4/FR-5/BR-4) and the client-IP/timestamp audit (FR-7) are enforced in the
        // service. The auto-close service (BR-3) is driven by the ReviewSignoffAutoCloseJob via the same
        // performance notification seam. PDF export (FR-6) is a data endpoint + a deliberate seam (no PDF lib).
        services.AddScoped<IReviewSignoffService, ReviewSignoffService>();
        services.AddScoped<IReviewSignoffAutoCloseService, ReviewSignoffAutoCloseService>();

        // US-PRF-005: Performance — 360-degree feedback. ReviewerAssignmentService handles reviewer
        // nomination/auto-suggest (Self+Manager auto, Peers same-dept, Direct Reports org-tree) + notify;
        // Feedback360Service handles reviewer submission (BR-3 one-per-reviewer-per-cycle, anonymity captured
        // at submit per BR-5) and aggregation (per-competency/category averages + composite FR-6 + BR-4
        // peer-threshold gate). Anonymity is enforced in the DTO projection (NFR-3/FR-5 — reviewer ids are
        // never written into results when anonymity is on). The BR-6 final-score incorporation lives in
        // ManagerReviewService.ComputeFinalScoreWith360 (delegates to the pure ThreeSixtyScoreCalculator).
        // The reminder service (AC-5/FR-8) is driven by the Feedback360ReminderJob via the notification seam.
        services.AddScoped<IReviewerAssignmentService, ReviewerAssignmentService>();
        services.AddScoped<IFeedback360Service, Feedback360Service>();
        services.AddScoped<IFeedback360ReminderService, Feedback360ReminderService>();

        // US-PRF-008: Performance Improvement Plans (PIP). HR-only create/extend/close/escalate (BR-1), one
        // non-terminal PIP per employee (BR-2), ≥30-day duration (BR-3), the immutable acknowledgement +
        // append-only checkpoint/event history (BR-4/FR-5, reusing the US-PRF-006 ReviewSignoff pattern) and the
        // FR-8 visibility restriction (employee / manager / HR / mentor) are enforced in PipService. PIP data is
        // intentionally NOT surfaced in the general performance dashboard (BR-5). The reminder/not-acknowledged
        // sweep (FR-3/BR-4) is driven by the PipReminderJob via the same log-only performance notification seam.
        // The sensitive Reason/EscalationNotes/FinalOutcomeNotes (NFR-4) are ENCRYPTED AT REST (P3-4) via the
        // AES-256-GCM IFieldEncryptor value converter in PipConfiguration.ApplyEncryption.
        services.AddScoped<IPipService, PipService>();
        services.AddScoped<IPipReminderService, PipReminderService>();

        // US-PRF-009: Performance — goal tracking with progress updates. GoalProgressService enforces the
        // APPEND-ONLY progress-update history (NFR-3/FR-3 — no update/delete path), the active-cycle window gate on
        // posting (BR-1), BR-2 (100% auto-sets Completed, overridable), BR-3 (Blocked notifies manager + HR), the
        // FR-4 weighted overall completion (pure GoalProgressCalculator), the AC-4 manager team summary scoped to
        // direct reports (Review.Team) / all (Review.All), the FR-8 manager/HR comment thread, and BR-5 visibility
        // (employee / manager / HR only). The manager-notify on every update (FR-5) goes through the log-only
        // performance seam; real-time/SignalR delivery is a deferred hook (US-NTF-001 — SignalR is not set up
        // here). The daily stale-goal nudge sweep (AC-5/FR-6/BR-4) is StaleGoalNudgeService, driven by the
        // StaleGoalNudgeJob Hangfire job; the tenant-configurable interval is Tenant.StaleGoalNudgeDays (0 disables).
        services.AddScoped<IGoalProgressService, GoalProgressService>();
        services.AddScoped<IStaleGoalNudgeService, StaleGoalNudgeService>();

        // US-PRF-004: Performance — full appraisal-cycle management (create/edit/clone/transition/cancel +
        // dashboard + active-cycle). The phase-sequencing/in-range rules (FR-2/BR-3) are in the validators;
        // participant scoping (FR-3), BR-4 (no two active same-type cycles per employee), the FR-7 status
        // state machine, BR-5 (scale-lock on Active), BR-6 (cancel-needs-reason + notify) and BR-2
        // (delete-only-Draft-without-reviews) are enforced in the service. The GoalSetting/SelfAssessment/
        // ManagerReview phases are kept in sync with the legacy cycle window columns, so US-PRF-001/002/003
        // windows are now phase-driven. Hangfire scheduling (FR-5) is an OPTIONAL seam (ICyclePhaseScheduler,
        // bound in HRM.Api) — absent in tests, where the service simply skips scheduling.
        services.AddScoped<IAppraisalCycleService, AppraisalCycleService>();

        // US-PRF-007: Performance — dashboard + analytics. Read-only tenant-scoped aggregation over
        // ManagerReview/SelfAssessment/Goal/Feedback360/AppraisalCycle/CycleParticipant + Core HR; NO new
        // entities. HR (Performance.View.All) sees org-wide data + top/bottom performers; a manager
        // (Performance.View.Team) is hard-scoped to their direct reports + a team ranking (BR-1/BR-3/AC-5).
        // Final scores reuse ManagerReview.FinalScore (BR-4). CSV+XLSX export via ClosedXML; PDF + the
        // materialized-view/Redis refresh (NFR-3) are documented extension points, not built here.
        services.AddScoped<IPerformanceDashboardService, PerformanceDashboardService>();

        // US-PRF-010: Performance — performance-based recommendations (promotion/bonus/increment/training). HR
        // (Performance.Publish.All) drives the full workspace/auto-generate/save/submit/budget/rule config; a
        // manager (Performance.Review.Team) is hard-scoped to their direct reports (AC-5/NFR-5). Auto-generation
        // applies tenant-configurable rating-threshold RULES (FR-2/BR-3 — config, not hard-coded); manual override
        // requires a justification (FR-3); promotion needs grade+effective-date (BR-5); submit is gated by BR-1
        // (final ratings published = cycle Completed) + BR-2 (calibration complete if enabled); the approval
        // workflow (FR-4) reuses the leave/attendance approver-chain + per-step status approach; budget tracking
        // is a SOFT warning (FR-8/BR-4, never a hard block). The immutable approval audit lives in append-only
        // RecommendationEvent rows (FR-7). On final approval the BR-6 downstream-integration seam is raised via
        // IRecommendationIntegrationService (promotions→Core HR, bonuses→Payroll, training→Training) — log-only,
        // real cross-module wiring deferred. The per-employee compensation fields are ENCRYPTED AT REST (NFR-3 /
        // P3-4) via the AES-256-GCM IFieldEncryptor value converter in RecommendationConfiguration.ApplyEncryption.
        // Excel export via ClosedXML (reuses the US-PRF-007 approach); PDF is a documented seam.
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddScoped<IRecommendationIntegrationService, LogOnlyRecommendationIntegrationService>();

        // US-ADM-001: Admin Console — system-admin tenant provisioning. Runs in the system/admin context
        // (no resolved tenant) and operates across tenants: creates the tenant + owner user/membership +
        // Tenant Owner role, seeds default master data (built-in roles, Annual/Sick/Casual leave types, a
        // default shift), writes the lifecycle event + structured audit, and dispatches the welcome email.
        // Idempotent on subdomain (NFR-2). Tenant isolation for the new tenant is the existing EF global
        // query filter + TenantInterceptor (no RLS — deferred platform work). The welcome-email send is a
        // log-only seam (mirrors IPayslipEmailSender; real SMTP deferred, TODO US-NTF).
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
        // US-NTF-006 Phase 2b: real (informational) welcome-email delivery via INotificationDispatcher
        // (tenant_welcome_trial / tenant_welcome_active, SystemAnnouncements). No set-password token — a new owner
        // uses self-service Forgot Password. Never throws — a delivery failure cannot block provisioning.
        services.AddScoped<ITenantWelcomeEmailService, RealTenantWelcomeEmailService>();

        // US-ADM-009: System Admin subscription-plan management. Runs in the system/admin context — plans and
        // per-tenant limit overrides are platform tables (no tenant query filter). The code is unique + immutable
        // (FR-3/BR-5), archive sets IsActive=false (AC-4), delete is refused when any tenant references the plan
        // (FR-7/BR-5), and every write is audited. Limits are read live from the plan at runtime so a plan edit
        // benefits existing tenants immediately (AC-3) — the Redis plan cache / 60s propagation (NFR-1/NFR-4) is
        // DEFERRED (not wired). The runtime per-endpoint MODULE-GATING enforcement (BR-6/FR-6) is also DEFERRED —
        // this story stores enabled_modules + derives Tenant.EnabledModules at provisioning, but no middleware
        // 403s a disabled module's API yet.
        services.AddScoped<ISubscriptionPlanService, SubscriptionPlanService>();

        // US-ADM-005: Tenant Admin user + role-assignment management. Runs in the NORMAL resolved-tenant
        // context — isolation (AC-6) is the EF global query filters on UserTenant/Role/UserTenantRole/
        // RefreshToken/UserInvitation (RLS deferred). A brand-new invitee's pending state IS the
        // UserInvitation row (no UserTenant until acceptance, which AUTH/onboarding owns). The plan
        // user-limit (BR-5) is enforced at invite time. Force-password-reset is the one deliberate
        // cross-tenant write (global password → revoke tokens across all tenants by UserId). The invitation/
        // password-reset emails use a log-only seam (mirrors the welcome-email seam; real SMTP deferred, US-NTF).
        services.AddScoped<IUserManagementService, UserManagementService>();
        // US-NTF-006 Phase 2b: real invitation (user_invitation, with a real accept link + raw token) and
        // admin-forced-reset (admin_password_reset, INFORMATIONAL — no link/token) email delivery via
        // INotificationDispatcher. Never throws — a delivery failure cannot block invite / force-reset flows.
        services.AddScoped<IUserManagementNotificationService, RealUserManagementNotificationService>();

        // US-ADM-006: Tenant-Admin company settings (org profile, branding, localization, password/session
        // policy). Operates only on the CURRENT tenant via ITenantContext (AC-5). Reuses the existing
        // IFileStorage abstraction for branding uploads; IDistributedCache is optional (FR-7 cache eviction
        // no-ops when Redis is not wired). Branding magic-byte + size validation is a pure, unit-tested helper.
        services.AddScoped<ITenantSettingsService, TenantSettingsService>();

        // US-ADM-007: Tenant-Admin approval-workflow DEFINITION management (+ the pure WorkflowEvaluator in
        // HRM.Domain). Runs in the NORMAL resolved-tenant context — isolation (BR-7) is the EF global query
        // filter on WorkflowDefinition/WorkflowStep (RLS deferred). Versioning (FR-3: edit-active → new version,
        // prior retained), the one-active-per-type auto-archive (BR-2), the plan limit vs Tenant.MaxWorkflows
        // (FR-4/AC-4), and before/after audit (NFR-4) live in the service. The RUNTIME engine (live request
        // routing through these definitions, per-request WorkflowInstance/StepInstance, SLA Hangfire timers +
        // escalation/BR-4, live delegation/AC-5, Redis cache/NFR-2) is DEFERRED — only the pure condition/parallel
        // EVALUATION (WorkflowEvaluator) is built+tested here, not its invocation from live request flows.
        services.AddScoped<IWorkflowService, WorkflowService>();

        // US-ADM-011 (Phase 1): approval-workflow RUNTIME engine — live instance/step-instance state machine,
        // instance creation on submit (version snapshot), sequential advance with WorkflowEvaluator condition
        // skip, single-winner transactional decisions, real in-flight count. Wired into Leave submission/approval.
        services.AddScoped<IWorkflowRuntime, WorkflowRuntimeService>();

        // US-ADM-011b (AC-5/FR-8): SLA-timer escalation of overdue approval steps (per-tenant, idempotent CAS).
        services.AddScoped<IWorkflowSlaEscalator, WorkflowSlaEscalator>();

        // US-ADM-008: Tenant-Admin audit-log READ + EXPORT. Runs in the normal resolved-tenant context but
        // filters audit_logs EXPLICITLY by ITenantContext.TenantId (that table has NO global query filter; its
        // TenantId is nullable and shared with system rows). Masks sensitive values on read (FR-4), audits the
        // export action itself (BR-4), and surfaces the read-only plan retention window (BR-5). The retention
        // PURGE (FR-6) is a separate cross-tenant service driven by AuditLogPurgeJob (Hangfire). Append-only by
        // code convention (AC-5/NFR-3); DB-role UPDATE/DELETE revocation + RLS are DEFERRED platform infra.
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IAuditLogPurgeService, AuditLogPurgeService>();

        // US-NTF-004: automatic generic change-capture is wired via AuditCaptureInterceptor (see DbContext
        // interceptors above). This registers the GDPR "right to be forgotten" anonymizer (BR-6) that redacts
        // PII in a user's audit rows in place while preserving the append-only trail's structure.
        services.AddScoped<IAuditAnonymizationService, AuditAnonymizationService>();

        // US-ADM-010: Tenant data export on demand. Initiation runs in the resolved-tenant context (Tenant Admin,
        // AC-5) OR the system/admin context with an explicit tenant id (System Admin, AC-6). The status gate
        // (BR-2/BR-3), the concurrent + monthly-3 rate limit (BR-5), the Queued ExportRequest + "DataExport.Requested"
        // audit live in the service; generation (GenerateAsync) is INVOKED by the Hangfire DataExportGenerationJob
        // when the optional IExportJobScheduler is wired (HRM.Api), else directly (tests). The generation does the
        // AsNoTracking per-entity CSV serialization with the auth-secret deny-list (BR-7), the audit-log JSONL,
        // manifest.json with REAL SHA-256 checksums, the ZIP, and the IFileStorage store + (stub) download-link
        // email seam. The retention cleanup (FR-7) is ExportCleanupService, driven by the hourly ExportCleanupJob.
        // Real email + pre-signed S3 URL + the schema PDF + at-rest encryption are DEFERRED (TODO US-NTF / infra).
        services.AddScoped<ITenantDataExportService, TenantDataExportService>();
        services.AddScoped<IExportCleanupService, ExportCleanupService>();
        services.AddScoped<IDataExportNotificationService, RealDataExportNotificationService>();  // US-NTF-006 Phase 4

        // US-ADM-002: System Admin platform monitoring (cross-tenant aggregation + DB/Redis/Hangfire signals).
        // Runs in the system/admin context with IgnoreQueryFilters. IJobQueueMonitor (the Hangfire monitoring
        // seam) is registered in HRM.Api alongside Hangfire so the service does not hard-depend on a running
        // Hangfire server. Real data only — metrics with no source are returned null/empty with status flags.
        services.AddScoped<IPlatformMonitoringService, PlatformMonitoringService>();

        // US-ADM-003: System Admin / System Support tenant-user impersonation (with full audit). Runs in the
        // system/admin context (IgnoreQueryFilters, explicit tenant/user-scoped reads), mints a separate
        // impersonation JWT for the target user (60-min cap, not refreshable), tracks the cross-tenant
        // ImpersonationSession, writes impersonator-attributed audit, and dispatches the tenant-admin
        // notification via the log-only seam (mirrors the welcome-email seam; real delivery deferred, US-NTF).
        services.AddScoped<IImpersonationService, ImpersonationService>();
        // US-NTF-006 Phase 2a: real email + in-app delivery to the target tenant's admins (SecurityAlerts,
        // mandatory/non-suppressible). Never throws — a delivery failure cannot block the impersonation start.
        services.AddScoped<IImpersonationNotificationService, RealImpersonationNotificationService>();

        // US-ADM-004: System Admin tenant lifecycle (suspend / terminate / reactivate / restore + history).
        // Runs in the system/admin context (IgnoreQueryFilters, explicit tenant-id scoping). Each transition
        // validates the BR-1 matrix (pure TenantLifecycleTransitions), refuses the system tenant (BR-2), revokes
        // refresh tokens on suspend (BR-5), and writes a lifecycle event + system audit atomically (FR-7/NFR-4).
        // ITenantDeletionScheduler (the Hangfire seam) is OPTIONAL and registered in HRM.Api, so the service never
        // hard-depends on running Hangfire storage (absent in tests → scheduling is skipped). Notifications use the
        // log-only seam (real delivery deferred, US-NTF). The data-deletion service is provider-aware: ExecuteDelete
        // + a transaction on relational, load+RemoveRange on InMemory — only the target tenant's rows are touched.
        services.AddScoped<ITenantLifecycleService, TenantLifecycleService>();
        services.AddScoped<ITenantDataDeletionService, TenantDataDeletionService>();
        // US-NTF-006 Phase 2a: real delivery to the tenant's billing email + admin emails (raw addresses via the
        // dispatcher's RecipientEmail override; in-app added where an address maps to an active user). Mandatory —
        // account-status changes always deliver. Never throws — a delivery failure cannot block the transition.
        services.AddScoped<ITenantLifecycleNotificationService, RealTenantLifecycleNotificationService>();

        // US-NTF-001: in-app notifications — read + mark-read (panel list, unread-count badge, mark-(all)-read).
        // Runs in the resolved-tenant request scope; scoped to the calling user. The PERSIST + real-time SignalR
        // PUSH dispatcher (INotificationService) lives in HRM.Api alongside the hub (it needs IHubContext).
        services.AddScoped<INotificationReadService, NotificationReadService>();

        // US-NTF-002: email notification templates per tenant. EmailTemplateService is the RESOLUTION + RENDERING
        // seam other modules call (override→default + language fallback, {{placeholder}} merge). NotificationTemplate
        // Service is the tenant-admin CRUD/preview/test-email facade. IEmailSender is the GENERIC send seam.
        // The existing module-specific email seams are intentionally left untouched.
        services.AddScoped<IEmailTemplateService, EmailTemplateService>();
        services.AddScoped<INotificationTemplateService, NotificationTemplateService>();

        // US-NTF-006 FR-8: real delivery is opt-in. When Smtp:Host is configured we use the MailKit-backed
        // SmtpEmailSender (surfaces failures so the Hangfire SendEmailJob can retry); with a BLANK host we keep the
        // log-only seam, so the app starts and tests run with no SMTP server (safe to merge without live SMTP).
        if (string.IsNullOrWhiteSpace(configuration["Smtp:Host"]))
        {
            services.AddScoped<IEmailSender, LogOnlyEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }

        // US-NTF-003: per-user notification preferences. Matrix CRUD runs in the resolved request scope scoped
        // to ICurrentUser; the dispatch-time ShouldDeliver check takes tenant+user explicitly (works from
        // background jobs). IDistributedCache is the SAME optional Redis seam used elsewhere (TenantSettings):
        // present → 5-min cached dispatch lookups (NFR-3), absent → straight to the DB. Redis never hard-required.
        // Per-tenant mandatory-category configuration (FR-4 Tenant-Admin console) is deferred; the static
        // NotificationPreferenceDefaults catalog is the tenant-default baseline (SecurityAlerts mandatory).
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();

        // ── Training & Benefits (US-TRN-001 / US-TRN-002) ──
        services.AddScoped<ITrainingService, TrainingService>();
        services.AddScoped<IBenefitPlanService, BenefitPlanService>();
        services.AddScoped<IBenefitEnrollmentService, BenefitEnrollmentService>();

        // HTML sanitizer (NFR-4 XSS) — stateless/thread-safe, registered as a singleton.
        services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>();

        // File storage (US-CHR-001 FR-6)
        // Dev: local filesystem; Prod: swap to Azure Blob / S3 / MinIO implementation.
        services.AddSingleton<IFileStorage>(sp =>
        {
            var basePath = configuration["FileStorage:BasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
            var logger = sp.GetRequiredService<ILogger<LocalFileStorage>>();
            return new LocalFileStorage(basePath, logger);
        });

        // Virus scanner (US-CHR-001 NFR-3 / ISSUE-101). Pluggable + config-gated: when a ClamAV daemon host
        // is configured (VirusScanning:ClamAv:Host) we wire the real ClamAvVirusScanner (INSTREAM over TCP,
        // fail-closed by default); otherwise we keep the AllowWithLogVirusScanner allow-stub so unconfigured
        // environments/tests behave exactly as before. Mirrors the optional-Redis gate below.
        var clamAvHost = configuration[$"{ClamAvOptions.SectionName}:Host"];
        if (!string.IsNullOrWhiteSpace(clamAvHost))
        {
            services.Configure<ClamAvOptions>(configuration.GetSection(ClamAvOptions.SectionName));
            services.AddSingleton<IVirusScanner, ClamAvVirusScanner>();
        }
        else
        {
            services.AddSingleton<IVirusScanner, AllowWithLogVirusScanner>();
        }

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.InstanceName = configuration["Redis:InstanceName"] ?? "hrm:";
                // Configuration is intentionally NOT set here; the ConnectionMultiplexerFactory below supplies the
                // shared, OTel-instrumented multiplexer instead (RedisCacheOptions prefers the factory when set).
            });
            // Route IDistributedCache onto the shared IConnectionMultiplexer registered by the API host
            // (Program.cs AddSharedRedisMultiplexer) — so its Redis commands are covered by AddRedisInstrumentation
            // and share the single connection pool. Configure<IConnectionMultiplexer> is LAZY: it runs only when
            // RedisCacheOptions is materialized (i.e. when IDistributedCache/RedisCache is first built), so this
            // never resolves the multiplexer unless the cache is actually used. NOTE: the only production host that
            // sets a Redis connection string is HRM.Api, which registers the multiplexer; a Redis-configured non-API
            // host (e.g. a tool) would also need to register IConnectionMultiplexer for the cache to build.
            services.AddOptions<Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions>()
                .Configure<StackExchange.Redis.IConnectionMultiplexer>((o, mux) =>
                    o.ConnectionMultiplexerFactory = () => Task.FromResult(mux));
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Permission cache (NFR-2): Redis-backed when configured so lookups scale across instances and
        // invalidations propagate cross-instance; else the per-instance in-memory cache. Both Singleton.
        // Same Redis gate as the shared multiplexer / token-denylist above.
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IPermissionCache, RedisPermissionCache>();
        }
        else
        {
            services.AddSingleton<IPermissionCache, InMemoryPermissionCache>();
        }

        // BUG-116: shared invalidator for the my-tenants cache (user:{userId}:tenants) so a membership/role
        // change never serves stale authorization data for up to the 5-min TTL. Wraps IDistributedCache, which is
        // registered in BOTH Redis and in-memory modes above, so it needs no Redis gate. Singleton (stateless
        // wrapper), matching the IPermissionCache registrations.
        services.AddSingleton<IMyTenantsCache, MyTenantsCache>();

        // Permission-based authorization
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, TeamScopeAuthorizationHandler>();

        return services;
    }
}
