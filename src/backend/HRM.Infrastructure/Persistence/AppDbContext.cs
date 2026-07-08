using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Interfaces;
using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext with multi-tenant global query filters.
/// All tenant-scoped entities are filtered by the current tenant context.
/// </summary>
public sealed class AppDbContext : DbContext, IUnitOfWork
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    // US-ADM-009: system-level, cross-tenant plan-limit overrides — NO tenant query filter
    // (see PlanLimitOverrideConfiguration); managed only from the system-admin console.
    public DbSet<PlanLimitOverride> PlanLimitOverrides => Set<PlanLimitOverride>();
    public DbSet<TenantLifecycleEvent> TenantLifecycleEvents => Set<TenantLifecycleEvent>();
    // US-ADM-004: system-level, cross-tenant — NO tenant query filter (see TenantScheduledJobConfiguration).
    public DbSet<TenantScheduledJob> TenantScheduledJobs => Set<TenantScheduledJob>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserTenantRole> UserTenantRoles => Set<UserTenantRole>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<PasswordHistory> PasswordHistories => Set<PasswordHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    // US-ADM-003: system-level, cross-tenant — NO tenant query filter (see ImpersonationSessionConfiguration).
    public DbSet<ImpersonationSession> ImpersonationSessions => Set<ImpersonationSession>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobTitle> JobTitles => Set<JobTitle>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<EmploymentHistory> EmploymentHistories => Set<EmploymentHistory>();
    public DbSet<EmployeeFieldAuditLog> EmployeeFieldAuditLogs => Set<EmployeeFieldAuditLog>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<FutureDatedStatusChange> FutureDatedStatusChanges => Set<FutureDatedStatusChange>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<BulkImportJob> BulkImportJobs => Set<BulkImportJob>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveEntitlementRule> LeaveEntitlementRules => Set<LeaveEntitlementRule>();
    public DbSet<LeaveEntitlementOverride> LeaveEntitlementOverrides => Set<LeaveEntitlementOverride>();
    public DbSet<LeaveLedger> LeaveLedgerEntries => Set<LeaveLedger>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<LeaveApprovalHistory> LeaveApprovalHistories => Set<LeaveApprovalHistory>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<LeaveCarryForwardTracking> LeaveCarryForwardTrackings => Set<LeaveCarryForwardTracking>();
    public DbSet<CompulsoryLeave> CompulsoryLeaves => Set<CompulsoryLeave>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<AttendanceSettings> AttendanceSettings => Set<AttendanceSettings>();
    public DbSet<AttendanceRegularization> AttendanceRegularizations => Set<AttendanceRegularization>();
    public DbSet<RegularizationApprovalHistory> RegularizationApprovalHistories => Set<RegularizationApprovalHistory>();
    public DbSet<AttendancePeriodLock> AttendancePeriodLocks => Set<AttendancePeriodLock>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftRotationStep> ShiftRotationSteps => Set<ShiftRotationStep>();
    public DbSet<EmployeeShift> EmployeeShifts => Set<EmployeeShift>();
    public DbSet<OvertimeRecord> OvertimeRecords => Set<OvertimeRecord>();
    public DbSet<OvertimeApprovalHistory> OvertimeApprovalHistories => Set<OvertimeApprovalHistory>();
    public DbSet<AttendanceMonthlySummary> AttendanceMonthlySummaries => Set<AttendanceMonthlySummary>();
    public DbSet<LatePolicy> LatePolicies => Set<LatePolicy>();
    public DbSet<ScheduledReportConfig> ScheduledReportConfigs => Set<ScheduledReportConfig>();
    public DbSet<Vacancy> Vacancies => Set<Vacancy>();
    public DbSet<Applicant> Applicants => Set<Applicant>();
    public DbSet<ApplicantStageHistory> ApplicantStageHistories => Set<ApplicantStageHistory>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<InterviewInterviewer> InterviewInterviewers => Set<InterviewInterviewer>();
    public DbSet<InterviewScorecard> InterviewScorecards => Set<InterviewScorecard>();
    public DbSet<ScorecardCriterionRating> ScorecardCriterionRatings => Set<ScorecardCriterionRating>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<ApplicantPortalToken> ApplicantPortalTokens => Set<ApplicantPortalToken>();
    public DbSet<SalaryComponent> SalaryComponents => Set<SalaryComponent>();
    public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();
    public DbSet<SalaryStructureComponent> SalaryStructureComponents => Set<SalaryStructureComponent>();
    public DbSet<EmployeeSalaryComponent> EmployeeSalaryComponents => Set<EmployeeSalaryComponent>();
    public DbSet<SalaryRevisionHistory> SalaryRevisionHistories => Set<SalaryRevisionHistory>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollApprovalHistory> PayrollApprovalHistories => Set<PayrollApprovalHistory>();
    public DbSet<PayrollSlip> PayrollSlips => Set<PayrollSlip>();
    public DbSet<PayrollSlipDetail> PayrollSlipDetails => Set<PayrollSlipDetail>();
    public DbSet<PayslipEmailLog> PayslipEmailLogs => Set<PayslipEmailLog>();
    public DbSet<StatutoryRule> StatutoryRules => Set<StatutoryRule>();
    public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
    public DbSet<TaxSlab> TaxSlabs => Set<TaxSlab>();
    public DbSet<SocialSecurityRule> SocialSecurityRules => Set<SocialSecurityRule>();
    public DbSet<AppraisalCycle> AppraisalCycles => Set<AppraisalCycle>();
    public DbSet<CyclePhase> CyclePhases => Set<CyclePhase>();
    public DbSet<CycleParticipant> CycleParticipants => Set<CycleParticipant>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<SelfAssessment> SelfAssessments => Set<SelfAssessment>();
    public DbSet<SelfAssessmentItem> SelfAssessmentItems => Set<SelfAssessmentItem>();
    public DbSet<SelfAssessmentAttachment> SelfAssessmentAttachments => Set<SelfAssessmentAttachment>();
    public DbSet<ManagerReview> ManagerReviews => Set<ManagerReview>();
    public DbSet<ManagerReviewItem> ManagerReviewItems => Set<ManagerReviewItem>();
    public DbSet<ReviewMeetingNotes> ReviewMeetingNotes => Set<ReviewMeetingNotes>();
    public DbSet<ReviewMeetingNotesAction> ReviewMeetingNotesActions => Set<ReviewMeetingNotesAction>();
    public DbSet<ReviewSignoff> ReviewSignoffs => Set<ReviewSignoff>();
    public DbSet<ReviewerAssignment> ReviewerAssignments => Set<ReviewerAssignment>();
    public DbSet<Feedback360> Feedback360s => Set<Feedback360>();
    public DbSet<Feedback360Item> Feedback360Items => Set<Feedback360Item>();
    public DbSet<Pip> Pips => Set<Pip>();
    public DbSet<PipObjective> PipObjectives => Set<PipObjective>();
    public DbSet<PipCheckpoint> PipCheckpoints => Set<PipCheckpoint>();
    public DbSet<PipEvent> PipEvents => Set<PipEvent>();
    public DbSet<GoalProgressUpdate> GoalProgressUpdates => Set<GoalProgressUpdate>();
    public DbSet<GoalProgressAttachment> GoalProgressAttachments => Set<GoalProgressAttachment>();
    public DbSet<GoalComment> GoalComments => Set<GoalComment>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<RecommendationApprover> RecommendationApprovers => Set<RecommendationApprover>();
    public DbSet<RecommendationEvent> RecommendationEvents => Set<RecommendationEvent>();
    public DbSet<RecommendationBudget> RecommendationBudgets => Set<RecommendationBudget>();
    public DbSet<RecommendationRule> RecommendationRules => Set<RecommendationRule>();
    // US-ADM-007: Approval-workflow definitions + steps (tenant-scoped).
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    // US-ADM-010: tenant data-export requests (tenant-scoped).
    public DbSet<ExportRequest> ExportRequests => Set<ExportRequest>();
    // US-RPT-004: generic HR/leave/attendance report exports (CSV/Excel/PDF) (tenant-scoped).
    public DbSet<HrReportExport> HrReportExports => Set<HrReportExport>();
    // US-ONB-001: Onboarding checklist templates + their tasks (tenant-scoped).
    public DbSet<OnboardingChecklistTemplate> OnboardingChecklistTemplates => Set<OnboardingChecklistTemplate>();
    public DbSet<OnboardingTemplateTask> OnboardingTemplateTasks => Set<OnboardingTemplateTask>();
    // US-ONB-002: Assigned checklist instances, their task instances + the notification outbox (tenant-scoped).
    public DbSet<OnboardingChecklistInstance> OnboardingChecklistInstances => Set<OnboardingChecklistInstance>();
    public DbSet<OnboardingTaskInstance> OnboardingTaskInstances => Set<OnboardingTaskInstance>();
    public DbSet<OnboardingNotificationOutbox> OnboardingNotificationOutbox => Set<OnboardingNotificationOutbox>();
    // US-ONB-004: lite asset register for issuance tracking (tenant-scoped).
    public DbSet<Asset> Assets => Set<Asset>();
    // US-ONB-005: offboarding / exit-clearance instances + their task instances (tenant-scoped).
    public DbSet<OffboardingInstance> OffboardingInstances => Set<OffboardingInstance>();
    public DbSet<OffboardingTaskInstance> OffboardingTaskInstances => Set<OffboardingTaskInstance>();
    // US-ONB-006: exit-interview questionnaire templates + recorded interviews/responses (tenant-scoped).
    public DbSet<ExitInterviewTemplate> ExitInterviewTemplates => Set<ExitInterviewTemplate>();
    public DbSet<ExitInterviewQuestion> ExitInterviewQuestions => Set<ExitInterviewQuestion>();
    public DbSet<ExitInterview> ExitInterviews => Set<ExitInterview>();
    public DbSet<ExitInterviewResponse> ExitInterviewResponses => Set<ExitInterviewResponse>();
    // US-NTF-001: in-app notifications (tenant-scoped, per-recipient).
    public DbSet<Notification> Notifications => Set<Notification>();
    // US-NTF-002: email notification templates. The OVERRIDE table is tenant-scoped (global query filter); the
    // SYSTEM-DEFAULT table is platform-level — NO tenant query filter (see SystemNotificationTemplateConfiguration).
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<SystemNotificationTemplate> SystemNotificationTemplates => Set<SystemNotificationTemplate>();
    // US-NTF-003: per-user notification preferences (tenant-scoped, per-tenant-membership — BR-4/AC-5).
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global query filters for tenant isolation
        modelBuilder.Entity<UserTenant>()
            .HasQueryFilter(ut => !_tenantContext.IsResolved || ut.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Role>()
            .HasQueryFilter(r => r.TenantId == null || !_tenantContext.IsResolved || r.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(rt => !_tenantContext.IsResolved || rt.TenantId == _tenantContext.TenantId);

        // US-ADM-005: UserInvitation tenant isolation + soft-delete filter (AC-6 cross-tenant isolation).
        modelBuilder.Entity<UserInvitation>()
            .HasQueryFilter(i => !i.IsDeleted && (!_tenantContext.IsResolved || i.TenantId == _tenantContext.TenantId));

        modelBuilder.Entity<Tenant>()
            .HasQueryFilter(t => !t.IsDeleted);

        // US-CHR-004: Department tenant isolation + soft-delete filter
        modelBuilder.Entity<Department>()
            .HasQueryFilter(d => !d.IsDeleted && (!_tenantContext.IsResolved || d.TenantId == _tenantContext.TenantId));

        // US-CHR-005: JobTitle tenant isolation + soft-delete filter
        modelBuilder.Entity<JobTitle>()
            .HasQueryFilter(j => !j.IsDeleted && (!_tenantContext.IsResolved || j.TenantId == _tenantContext.TenantId));

        // US-CHR-001: Employee tenant isolation + soft-delete filter
        modelBuilder.Entity<Employee>()
            .HasQueryFilter(e => !e.IsDeleted && (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId));

        // US-CHR-002: EmergencyContact tenant isolation + soft-delete filter
        modelBuilder.Entity<EmergencyContact>()
            .HasQueryFilter(ec => !ec.IsDeleted && (!_tenantContext.IsResolved || ec.TenantId == _tenantContext.TenantId));

        // US-CHR-002: EmploymentHistory tenant isolation + soft-delete filter
        modelBuilder.Entity<EmploymentHistory>()
            .HasQueryFilter(eh => !eh.IsDeleted && (!_tenantContext.IsResolved || eh.TenantId == _tenantContext.TenantId));

        // US-CHR-007: Location tenant isolation + soft-delete filter
        modelBuilder.Entity<Location>()
            .HasQueryFilter(l => !l.IsDeleted && (!_tenantContext.IsResolved || l.TenantId == _tenantContext.TenantId));

        // US-CHR-008: EmployeeDocument tenant isolation + soft-delete filter
        modelBuilder.Entity<EmployeeDocument>()
            .HasQueryFilter(d => !d.IsDeleted && (!_tenantContext.IsResolved || d.TenantId == _tenantContext.TenantId));

        // US-CHR-009: FutureDatedStatusChange tenant isolation + soft-delete filter
        modelBuilder.Entity<FutureDatedStatusChange>()
            .HasQueryFilter(f => !f.IsDeleted && (!_tenantContext.IsResolved || f.TenantId == _tenantContext.TenantId));

        // US-CHR-009: IdempotencyRecord — no soft-delete; tenant isolation only
        modelBuilder.Entity<IdempotencyRecord>()
            .HasQueryFilter(i => !_tenantContext.IsResolved || i.TenantId == _tenantContext.TenantId);

        // EmployeeFieldAuditLog — no soft-delete; tenant isolation only (defense-in-depth)
        modelBuilder.Entity<EmployeeFieldAuditLog>()
            .HasQueryFilter(e => !_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId);

        // US-CHR-010: BulkImportJob tenant isolation + soft-delete filter
        modelBuilder.Entity<BulkImportJob>()
            .HasQueryFilter(b => !b.IsDeleted && (!_tenantContext.IsResolved || b.TenantId == _tenantContext.TenantId));

        // US-CHR-012: CustomFieldDefinition tenant isolation + soft-delete filter
        modelBuilder.Entity<CustomFieldDefinition>()
            .HasQueryFilter(c => !c.IsDeleted && (!_tenantContext.IsResolved || c.TenantId == _tenantContext.TenantId));

        // US-LV-001: LeaveType tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveType>()
            .HasQueryFilter(lt => !lt.IsDeleted && (!_tenantContext.IsResolved || lt.TenantId == _tenantContext.TenantId));

        // US-LV-002: LeaveEntitlementRule tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveEntitlementRule>()
            .HasQueryFilter(r => !r.IsDeleted && (!_tenantContext.IsResolved || r.TenantId == _tenantContext.TenantId));

        // US-LV-002: LeaveEntitlementOverride tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveEntitlementOverride>()
            .HasQueryFilter(o => !o.IsDeleted && (!_tenantContext.IsResolved || o.TenantId == _tenantContext.TenantId));

        // US-LV-002: LeaveLedger tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveLedger>()
            .HasQueryFilter(l => !l.IsDeleted && (!_tenantContext.IsResolved || l.TenantId == _tenantContext.TenantId));

        // US-LV-003: LeaveRequest tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveRequest>()
            .HasQueryFilter(lr => !lr.IsDeleted && (!_tenantContext.IsResolved || lr.TenantId == _tenantContext.TenantId));

        // US-LV-005: LeaveApprovalHistory tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveApprovalHistory>()
            .HasQueryFilter(h => !h.IsDeleted && (!_tenantContext.IsResolved || h.TenantId == _tenantContext.TenantId));

        // US-LV-007: Holiday tenant isolation + soft-delete filter
        modelBuilder.Entity<Holiday>()
            .HasQueryFilter(h => !h.IsDeleted && (!_tenantContext.IsResolved || h.TenantId == _tenantContext.TenantId));

        // US-LV-008: LeaveCarryForwardTracking tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveCarryForwardTracking>()
            .HasQueryFilter(t => !t.IsDeleted && (!_tenantContext.IsResolved || t.TenantId == _tenantContext.TenantId));

        // US-LV-011: CompulsoryLeave tenant isolation + soft-delete filter
        modelBuilder.Entity<CompulsoryLeave>()
            .HasQueryFilter(c => !c.IsDeleted && (!_tenantContext.IsResolved || c.TenantId == _tenantContext.TenantId));

        // US-ATT-001: AttendanceLog tenant isolation + soft-delete filter
        modelBuilder.Entity<AttendanceLog>()
            .HasQueryFilter(a => !a.IsDeleted && (!_tenantContext.IsResolved || a.TenantId == _tenantContext.TenantId));

        // US-ATT-001: AttendanceSettings tenant isolation + soft-delete filter
        modelBuilder.Entity<AttendanceSettings>()
            .HasQueryFilter(s => !s.IsDeleted && (!_tenantContext.IsResolved || s.TenantId == _tenantContext.TenantId));

        // US-ATT-003: AttendanceRegularization tenant isolation + soft-delete filter
        modelBuilder.Entity<AttendanceRegularization>()
            .HasQueryFilter(r => !r.IsDeleted && (!_tenantContext.IsResolved || r.TenantId == _tenantContext.TenantId));

        // US-ATT-009: AttendancePeriodLock (canonical attendance lock, consolidated from the former
        // US-ATT-003 PayrollLockPeriod) tenant isolation + soft-delete filter
        modelBuilder.Entity<AttendancePeriodLock>()
            .HasQueryFilter(p => !p.IsDeleted && (!_tenantContext.IsResolved || p.TenantId == _tenantContext.TenantId));

        // US-ATT-004: RegularizationApprovalHistory tenant isolation + soft-delete filter
        modelBuilder.Entity<RegularizationApprovalHistory>()
            .HasQueryFilter(h => !h.IsDeleted && (!_tenantContext.IsResolved || h.TenantId == _tenantContext.TenantId));

        // US-ATT-005: Shift tenant isolation + soft-delete filter
        modelBuilder.Entity<Shift>()
            .HasQueryFilter(s => !s.IsDeleted && (!_tenantContext.IsResolved || s.TenantId == _tenantContext.TenantId));

        // US-ATT-005: ShiftRotationStep tenant isolation + soft-delete filter
        modelBuilder.Entity<ShiftRotationStep>()
            .HasQueryFilter(rs => !rs.IsDeleted && (!_tenantContext.IsResolved || rs.TenantId == _tenantContext.TenantId));

        // US-ATT-005: EmployeeShift tenant isolation + soft-delete filter
        modelBuilder.Entity<EmployeeShift>()
            .HasQueryFilter(es => !es.IsDeleted && (!_tenantContext.IsResolved || es.TenantId == _tenantContext.TenantId));

        // US-ATT-006: OvertimeRecord tenant isolation + soft-delete filter
        modelBuilder.Entity<OvertimeRecord>()
            .HasQueryFilter(o => !o.IsDeleted && (!_tenantContext.IsResolved || o.TenantId == _tenantContext.TenantId));

        // US-ATT-006: OvertimeApprovalHistory tenant isolation + soft-delete filter
        modelBuilder.Entity<OvertimeApprovalHistory>()
            .HasQueryFilter(h => !h.IsDeleted && (!_tenantContext.IsResolved || h.TenantId == _tenantContext.TenantId));

        // US-ATT-007: AttendanceMonthlySummary tenant isolation + soft-delete filter
        modelBuilder.Entity<AttendanceMonthlySummary>()
            .HasQueryFilter(s => !s.IsDeleted && (!_tenantContext.IsResolved || s.TenantId == _tenantContext.TenantId));

        // US-ATT-008: LatePolicy tenant isolation + soft-delete filter
        modelBuilder.Entity<LatePolicy>()
            .HasQueryFilter(p => !p.IsDeleted && (!_tenantContext.IsResolved || p.TenantId == _tenantContext.TenantId));

        // US-ATT-010: ScheduledReportConfig tenant isolation + soft-delete filter
        modelBuilder.Entity<ScheduledReportConfig>()
            .HasQueryFilter(c => !c.IsDeleted && (!_tenantContext.IsResolved || c.TenantId == _tenantContext.TenantId));

        // US-REC-001: Vacancy tenant isolation + soft-delete filter (AC-4 cross-tenant isolation).
        modelBuilder.Entity<Vacancy>()
            .HasQueryFilter(v => !v.IsDeleted && (!_tenantContext.IsResolved || v.TenantId == _tenantContext.TenantId));

        // US-REC-002: Applicant tenant isolation + soft-delete filter (AC-5 cross-tenant isolation).
        modelBuilder.Entity<Applicant>()
            .HasQueryFilter(a => !a.IsDeleted && (!_tenantContext.IsResolved || a.TenantId == _tenantContext.TenantId));

        // US-REC-003: ApplicantStageHistory tenant isolation + soft-delete filter (AC-5).
        modelBuilder.Entity<ApplicantStageHistory>()
            .HasQueryFilter(h => !h.IsDeleted && (!_tenantContext.IsResolved || h.TenantId == _tenantContext.TenantId));

        // US-REC-005: Interview tenant isolation + soft-delete filter (AC-5).
        modelBuilder.Entity<Interview>()
            .HasQueryFilter(i => !i.IsDeleted && (!_tenantContext.IsResolved || i.TenantId == _tenantContext.TenantId));

        // US-REC-005: InterviewInterviewer tenant isolation + soft-delete filter (AC-5).
        modelBuilder.Entity<InterviewInterviewer>()
            .HasQueryFilter(ii => !ii.IsDeleted && (!_tenantContext.IsResolved || ii.TenantId == _tenantContext.TenantId));

        // US-REC-006: InterviewScorecard tenant isolation + soft-delete filter (AC-4).
        modelBuilder.Entity<InterviewScorecard>()
            .HasQueryFilter(s => !s.IsDeleted && (!_tenantContext.IsResolved || s.TenantId == _tenantContext.TenantId));

        // US-REC-006: ScorecardCriterionRating tenant isolation + soft-delete filter (AC-4).
        modelBuilder.Entity<ScorecardCriterionRating>()
            .HasQueryFilter(r => !r.IsDeleted && (!_tenantContext.IsResolved || r.TenantId == _tenantContext.TenantId));

        // US-REC-007: Offer tenant isolation + soft-delete filter (AC-5 cross-tenant isolation).
        modelBuilder.Entity<Offer>()
            .HasQueryFilter(o => !o.IsDeleted && (!_tenantContext.IsResolved || o.TenantId == _tenantContext.TenantId));

        // US-REC-008: ApplicantPortalToken tenant isolation + soft-delete filter (AC-4 cross-tenant
        // isolation — a token row is only ever readable within its own tenant).
        modelBuilder.Entity<ApplicantPortalToken>()
            .HasQueryFilter(t => !t.IsDeleted && (!_tenantContext.IsResolved || t.TenantId == _tenantContext.TenantId));

        // US-PAY-001: SalaryComponent tenant isolation + soft-delete filter (AC-6 cross-tenant isolation).
        modelBuilder.Entity<SalaryComponent>()
            .HasQueryFilter(c => !c.IsDeleted && (!_tenantContext.IsResolved || c.TenantId == _tenantContext.TenantId));

        // US-PAY-001: SalaryStructure tenant isolation + soft-delete filter (AC-6).
        modelBuilder.Entity<SalaryStructure>()
            .HasQueryFilter(s => !s.IsDeleted && (!_tenantContext.IsResolved || s.TenantId == _tenantContext.TenantId));

        // US-PAY-001: SalaryStructureComponent tenant isolation + soft-delete filter (AC-6).
        modelBuilder.Entity<SalaryStructureComponent>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-002: EmployeeSalaryComponent tenant isolation + soft-delete filter (AC-5 cross-tenant isolation).
        modelBuilder.Entity<EmployeeSalaryComponent>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-002: SalaryRevisionHistory tenant isolation + soft-delete filter (AC-5).
        modelBuilder.Entity<SalaryRevisionHistory>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-003: PayrollRun tenant isolation + soft-delete filter (AC-7).
        modelBuilder.Entity<PayrollRun>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-008: PayrollApprovalHistory tenant isolation + soft-delete filter (BR-8).
        modelBuilder.Entity<PayrollApprovalHistory>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-003: PayrollSlip tenant isolation + soft-delete filter (AC-7).
        modelBuilder.Entity<PayrollSlip>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-003: PayrollSlipDetail tenant isolation + soft-delete filter (AC-7).
        modelBuilder.Entity<PayrollSlipDetail>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-006: StatutoryRule tenant isolation + soft-delete filter (AC-4/FR-8 cross-tenant isolation).
        modelBuilder.Entity<StatutoryRule>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-006: TaxSlab tenant isolation + soft-delete filter (AC-4).
        modelBuilder.Entity<TaxSlab>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-006: SocialSecurityRule tenant isolation + soft-delete filter (AC-4).
        modelBuilder.Entity<SocialSecurityRule>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-007: PayrollAdjustment tenant isolation + soft-delete filter (AC-5/FR-8 cross-tenant isolation).
        modelBuilder.Entity<PayrollAdjustment>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PAY-011: PayslipEmailLog tenant isolation + soft-delete filter (AC-5 cross-tenant isolation).
        modelBuilder.Entity<PayslipEmailLog>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-001: AppraisalCycle tenant isolation + soft-delete filter (NFR-2 cross-tenant isolation).
        modelBuilder.Entity<AppraisalCycle>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-004: CyclePhase tenant isolation + soft-delete filter (NFR-2 cross-tenant isolation).
        modelBuilder.Entity<CyclePhase>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-004: CycleParticipant tenant isolation + soft-delete filter (NFR-2 cross-tenant isolation).
        modelBuilder.Entity<CycleParticipant>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-001: Goal tenant isolation + soft-delete filter (NFR-2 cross-tenant isolation).
        modelBuilder.Entity<Goal>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-002: SelfAssessment tenant isolation + soft-delete filter (NFR-2 cross-tenant isolation).
        modelBuilder.Entity<SelfAssessment>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-002: SelfAssessmentItem tenant isolation + soft-delete filter (NFR-2).
        modelBuilder.Entity<SelfAssessmentItem>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-002: SelfAssessmentAttachment tenant isolation + soft-delete filter (NFR-2/NFR-4).
        modelBuilder.Entity<SelfAssessmentAttachment>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-003: ManagerReview tenant isolation + soft-delete filter (NFR-2 cross-tenant isolation).
        modelBuilder.Entity<ManagerReview>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-003: ManagerReviewItem tenant isolation + soft-delete filter (NFR-2).
        modelBuilder.Entity<ManagerReviewItem>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-006: ReviewMeetingNotes tenant isolation + soft-delete filter (NFR-2).
        modelBuilder.Entity<ReviewMeetingNotes>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-006: ReviewMeetingNotesAction tenant isolation + soft-delete filter (NFR-2).
        modelBuilder.Entity<ReviewMeetingNotesAction>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-006: ReviewSignoff tenant isolation + soft-delete filter (NFR-2). Immutable append-only.
        modelBuilder.Entity<ReviewSignoff>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-005: ReviewerAssignment tenant isolation + soft-delete filter (NFR-2 cross-tenant isolation).
        modelBuilder.Entity<ReviewerAssignment>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-005: Feedback360 tenant isolation + soft-delete filter (NFR-2).
        modelBuilder.Entity<Feedback360>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-005: Feedback360Item tenant isolation + soft-delete filter (NFR-2).
        modelBuilder.Entity<Feedback360Item>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-008: Pip tenant isolation + soft-delete filter (NFR-2 cross-tenant isolation).
        modelBuilder.Entity<Pip>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-008: PipObjective tenant isolation + soft-delete filter (NFR-2).
        modelBuilder.Entity<PipObjective>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-008: PipCheckpoint tenant isolation + soft-delete filter (NFR-2). Append-only history.
        modelBuilder.Entity<PipCheckpoint>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-008: PipEvent tenant isolation + soft-delete filter (NFR-2). Immutable append-only audit log.
        modelBuilder.Entity<PipEvent>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-009: GoalProgressUpdate tenant isolation + soft-delete filter (NFR-2). Append-only history.
        modelBuilder.Entity<GoalProgressUpdate>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-009: GoalProgressAttachment tenant isolation + soft-delete filter (NFR-2).
        modelBuilder.Entity<GoalProgressAttachment>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-009: GoalComment tenant isolation + soft-delete filter (NFR-2). Manager/HR comment thread.
        modelBuilder.Entity<GoalComment>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-010: Recommendation tenant isolation + soft-delete filter (NFR-2 cross-tenant isolation).
        modelBuilder.Entity<Recommendation>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-010: RecommendationApprover tenant isolation + soft-delete filter (NFR-2). Approval chain (FR-4).
        modelBuilder.Entity<RecommendationApprover>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-010: RecommendationEvent tenant isolation + soft-delete filter (NFR-2). Immutable append-only log.
        modelBuilder.Entity<RecommendationEvent>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-010: RecommendationBudget tenant isolation + soft-delete filter (NFR-2). Budget tracking (FR-8).
        modelBuilder.Entity<RecommendationBudget>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-PRF-010: RecommendationRule tenant isolation + soft-delete filter (NFR-2). Auto-gen rules (FR-2).
        modelBuilder.Entity<RecommendationRule>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ADM-007: WorkflowDefinition tenant isolation + soft-delete filter (BR-7 cross-tenant isolation).
        modelBuilder.Entity<WorkflowDefinition>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ADM-007: WorkflowStep tenant isolation + soft-delete filter (BR-7).
        modelBuilder.Entity<WorkflowStep>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ADM-010: ExportRequest tenant isolation + soft-delete filter (AC-5 cross-tenant isolation).
        modelBuilder.Entity<ExportRequest>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-RPT-004: HrReportExport tenant isolation + soft-delete filter (AC-5 cross-tenant download isolation).
        modelBuilder.Entity<HrReportExport>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-001: OnboardingChecklistTemplate tenant isolation + soft-delete filter (AC-5/BR-5).
        modelBuilder.Entity<OnboardingChecklistTemplate>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-001: OnboardingTemplateTask tenant isolation + soft-delete filter (AC-5).
        modelBuilder.Entity<OnboardingTemplateTask>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-002: OnboardingChecklistInstance tenant isolation + soft-delete filter (NFR-2).
        modelBuilder.Entity<OnboardingChecklistInstance>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-002: OnboardingTaskInstance tenant isolation + soft-delete filter (AC-4 soft-delete, NFR-2).
        modelBuilder.Entity<OnboardingTaskInstance>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-004: Asset register tenant isolation + soft-delete filter (BR-5 soft-delete, AC-5/NFR-2).
        modelBuilder.Entity<Asset>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-002: OnboardingNotificationOutbox tenant isolation + soft-delete filter (NFR-2/NFR-3).
        modelBuilder.Entity<OnboardingNotificationOutbox>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-005: OffboardingInstance tenant isolation + soft-delete filter (AC-6/NFR-2).
        modelBuilder.Entity<OffboardingInstance>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-005: OffboardingTaskInstance tenant isolation + soft-delete filter (AC-6/NFR-2).
        modelBuilder.Entity<OffboardingTaskInstance>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-006: ExitInterviewTemplate tenant isolation + soft-delete filter (FR-6/NFR-2/AC-5).
        modelBuilder.Entity<ExitInterviewTemplate>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-006: ExitInterviewQuestion tenant isolation + soft-delete filter (FR-6/NFR-2/AC-5).
        modelBuilder.Entity<ExitInterviewQuestion>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-006: ExitInterview tenant isolation + soft-delete filter (FR-6/NFR-2/AC-5).
        modelBuilder.Entity<ExitInterview>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-ONB-006: ExitInterviewResponse tenant isolation + soft-delete filter (FR-6/NFR-2/AC-5).
        modelBuilder.Entity<ExitInterviewResponse>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-NTF-001: Notification tenant isolation + soft-delete filter (AC-6/NFR-2 cross-tenant isolation).
        modelBuilder.Entity<Notification>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-NTF-002: NotificationTemplate (tenant OVERRIDE) isolation + soft-delete filter (AC-5/NFR-2). The
        // SystemNotificationTemplate (platform default) is deliberately NOT filtered here so resolution can fall
        // back to it from any tenant.
        modelBuilder.Entity<NotificationTemplate>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));

        // US-NTF-003: NotificationPreference tenant isolation + soft-delete filter (AC-5/BR-4 cross-tenant
        // isolation — a user's preferences are independent per tenant membership).
        modelBuilder.Entity<NotificationPreference>()
            .HasQueryFilter(x => !x.IsDeleted && (!_tenantContext.IsResolved || x.TenantId == _tenantContext.TenantId));
    }
}
