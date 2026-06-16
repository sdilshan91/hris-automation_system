using HRM.Application.Common.Interfaces;
using HRM.Domain.Interfaces;
using HRM.Infrastructure.Caching;
using HRM.Infrastructure.Identity;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
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
        // Tenant context (scoped per request)
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // EF Core interceptors
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<TenantInterceptor>();

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

            // Add interceptors
            var tenantInterceptor = serviceProvider.GetRequiredService<TenantInterceptor>();
            var auditInterceptor = serviceProvider.GetRequiredService<AuditInterceptor>();
            options.AddInterceptors(tenantInterceptor, auditInterceptor);
        });

        // Register UnitOfWork
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());

        // TOTP service (singleton — no per-request state)
        services.AddSingleton<ITotpService, TotpService>();

        // Note: JwtService is registered in Program.cs alongside JWT authentication config.
        // Auth service
        services.AddScoped<IAuthService, AuthService>();

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

        // Leave notification seam — log-only until the notification service exists (FR-6).
        services.AddScoped<ILeaveNotificationService, LogOnlyLeaveNotificationService>();

        // US-REC-001: Recruitment — vacancy lifecycle + anonymous public careers page.
        services.AddScoped<IVacancyService, VacancyService>();
        services.AddScoped<IPublicCareersService, PublicCareersService>();

        // US-REC-002: Recruitment — applicant submission (public + internal) + recruiter reads.
        services.AddScoped<IApplicantService, ApplicantService>();
        // Recruitment notification seam — log-only until the notification platform exists (FR-5/FR-7).
        services.AddScoped<IRecruitmentNotificationService, LogOnlyRecruitmentNotificationService>();

        // US-REC-009: Recruitment dashboard + analytics (read-only aggregation, no new entities).
        services.AddScoped<IRecruitmentDashboardService, RecruitmentDashboardService>();

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
        services.AddScoped<IPayrollApprovalService, PayrollApprovalService>();  // US-PAY-008
        services.AddScoped<IPayrollNotificationService, LogOnlyPayrollNotificationService>();

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
        services.AddScoped<IPayslipEmailSender, LogOnlyPayslipEmailSender>();

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
        services.AddScoped<IPerformanceNotificationService, LogOnlyPerformanceNotificationService>();

        // US-PRF-002: Performance — employee self-assessment (get-my / save-draft / submit). Every read/write
        // is scoped to the calling employee's own record + tenant (NFR-2); the self-assessment-window gate
        // (BR-1/AC-4), all-goals-rated + comment-length submit rules (BR-2/FR-3), weighted-score calc (FR-4)
        // and submitted-lock (BR-3) are enforced in the service. The reminder service (FR-7/AC-5) is driven by
        // the SelfAssessmentReminderJob Hangfire job and dispatches via the shared performance notification seam.
        services.AddScoped<ISelfAssessmentService, SelfAssessmentService>();
        services.AddScoped<ISelfAssessmentReminderService, SelfAssessmentReminderService>();

        // US-PRF-003: Performance — manager performance review (workspace / save-draft / submit / reopen /
        // team dashboard). The manager-review-window gate (BR-1/AC-5), the direct-report scope (BR-2) and
        // HR-override + reopen (BR-3/AC-5), the all-goals-rated + comment-length submit rules (AC-3/FR-3), the
        // weighted-manager-score and the FINAL combined-score blend (BR-4) are enforced in the service.
        // ComputeFinalScore is the single extension point US-PRF-005 (360 feedback, BR-5) will widen.
        services.AddScoped<IManagerReviewService, ManagerReviewService>();

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

        // US-PRF-004: Performance — full appraisal-cycle management (create/edit/clone/transition/cancel +
        // dashboard + active-cycle). The phase-sequencing/in-range rules (FR-2/BR-3) are in the validators;
        // participant scoping (FR-3), BR-4 (no two active same-type cycles per employee), the FR-7 status
        // state machine, BR-5 (scale-lock on Active), BR-6 (cancel-needs-reason + notify) and BR-2
        // (delete-only-Draft-without-reviews) are enforced in the service. The GoalSetting/SelfAssessment/
        // ManagerReview phases are kept in sync with the legacy cycle window columns, so US-PRF-001/002/003
        // windows are now phase-driven. Hangfire scheduling (FR-5) is an OPTIONAL seam (ICyclePhaseScheduler,
        // bound in HRM.Api) — absent in tests, where the service simply skips scheduling.
        services.AddScoped<IAppraisalCycleService, AppraisalCycleService>();

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

        // Virus scanner (US-CHR-001 NFR-3)
        // TODO(prod): Wire ClamAV or equivalent production scanner.
        services.AddSingleton<IVirusScanner, AllowWithLogVirusScanner>();

        // Permission cache (in-memory default; TODO: swap to Redis for production — see NFR-2)
        services.AddSingleton<IPermissionCache, InMemoryPermissionCache>();

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = configuration["Redis:InstanceName"] ?? "hrm:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Permission-based authorization
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, TeamScopeAuthorizationHandler>();

        return services;
    }
}
