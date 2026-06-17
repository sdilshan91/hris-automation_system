namespace HRM.Domain.Authorization;

/// <summary>
/// Source-of-truth permission catalog for the HRM platform.
/// Permissions follow the pattern: Module.Action[.Scope]
/// This catalog is the single source of truth; UI and role management
/// enumerate permissions from here. New modules register their permissions
/// by adding constants and updating AllPermissions.
/// </summary>
public static class PermissionCatalog
{
    // ── Department Module (US-CHR-004) ────────────────────────────────
    public static class Department
    {
        public const string View = "Department.View";
        public const string Create = "Department.Create";
        public const string Edit = "Department.Edit";
        public const string Deactivate = "Department.Deactivate";
    }

    // ── Job Title Module (US-CHR-005) ──────────────────────────────────
    public static class JobTitle
    {
        public const string View = "JobTitle.View";
        public const string Create = "JobTitle.Create";
        public const string Edit = "JobTitle.Edit";
        public const string Deactivate = "JobTitle.Deactivate";
    }

    // ── Location Module (US-CHR-007) ─────────────────────────────────
    public static class Location
    {
        public const string View = "Location.View";
        public const string Create = "Location.Create";
        public const string Edit = "Location.Edit";
        public const string Deactivate = "Location.Deactivate";
    }

    // ── Employee Document Module (US-CHR-008) ──────────────────────
    public static class EmployeeDocument
    {
        public const string View = "EmployeeDocument.View";
        public const string ViewOwn = "EmployeeDocument.ViewOwn";
        public const string Upload = "EmployeeDocument.Upload";
        public const string Delete = "EmployeeDocument.Delete";
    }

    // ── Employee Module ──────────────────────────────────────────────
    public static class Employee
    {
        public const string ViewOwn = "Employee.View.Own";
        public const string ViewTeam = "Employee.View.Team";
        public const string ViewAll = "Employee.View.All";
        public const string Create = "Employee.Create";
        public const string Edit = "Employee.Edit";
        public const string EditOwn = "Employee.Edit.Own";
        public const string Delete = "Employee.Delete";
        public const string Export = "Employee.Export";
        /// <summary>
        /// Permission to change employee status (US-CHR-009 BR-2).
        /// Granted to HR Officer and Tenant Admin roles.
        /// </summary>
        public const string ChangeStatus = "Employee.ChangeStatus";

        /// <summary>
        /// Permission to bulk import employees (US-CHR-010).
        /// Granted to HR Officer and Tenant Admin roles.
        /// </summary>
        public const string Import = "Employee.Import";

        /// <summary>
        /// Permission to assign/unassign reporting managers (US-CHR-011).
        /// Granted to HR Officer and Tenant Admin roles.
        /// </summary>
        public const string AssignManager = "Employee.AssignManager";
    }

    // ── Custom Field Module (US-CHR-012) ────────────────────────────
    public static class CustomField
    {
        public const string View = "CustomField.View";
        public const string Create = "CustomField.Create";
        public const string Edit = "CustomField.Edit";
        public const string Deactivate = "CustomField.Deactivate";
    }

    // ── Leave Module ─────────────────────────────────────────────────
    public static class Leave
    {
        public const string ViewOwn = "Leave.View.Own";
        public const string ViewTeam = "Leave.View.Team";
        public const string ViewAll = "Leave.View.All";
        public const string Apply = "Leave.Apply";
        public const string ApproveTeam = "Leave.Approve.Team";
        public const string ApproveAll = "Leave.Approve.All";
        public const string ConfigurePolicy = "Leave.ConfigurePolicy";

        /// <summary>
        /// HR management of Loss-of-Pay / compulsory leave (US-LV-011): assign-lop, compulsory bulk
        /// assignment, LOP override, and the payroll LOP summary. Granted to Tenant Admin, HR Manager,
        /// HR Officer. Chosen over reusing Leave.ConfigurePolicy because that is not granted to HR
        /// Officer, whom the story explicitly authorises (HR.Officer).
        /// </summary>
        public const string ManageLop = "Leave.ManageLop";

        /// <summary>
        /// Access to leave reports and analytics (US-LV-012 BR-1, §2). Gates the report/analytics/export
        /// endpoints; the handler then applies the BR-2 row-level role scope (HR sees all, manager sees
        /// team, employee sees self). Granted to Tenant Admin, HR Manager, HR Officer, and Auditor —
        /// the roles that already hold Reports.View. Chosen as a dedicated per-feature permission
        /// (the story names "Leave.Reports") rather than reusing the cross-module Reports.View, mirroring
        /// the per-resource pattern used by LeaveType.* / Holiday.* / Leave.ManageLop.
        /// </summary>
        public const string Reports = "Leave.Reports";
    }

    // ── Leave Type Configuration (US-LV-001) ─────────────────────────
    public static class LeaveType
    {
        public const string View = "LeaveType.View";
        public const string Create = "LeaveType.Create";
        public const string Edit = "LeaveType.Edit";
        public const string Deactivate = "LeaveType.Deactivate";
    }

    // ── Holiday Calendar (US-LV-007) ─────────────────────────────────
    public static class Holiday
    {
        public const string View = "Holiday.View";
        public const string Create = "Holiday.Create";
        public const string Edit = "Holiday.Edit";
        public const string Deactivate = "Holiday.Deactivate";
        public const string Import = "Holiday.Import";
    }

    // ── Attendance Module ────────────────────────────────────────────
    public static class Attendance
    {
        public const string ViewOwn = "Attendance.View.Own";
        public const string ViewTeam = "Attendance.View.Team";
        public const string ViewAll = "Attendance.View.All";
        public const string CheckIn = "Attendance.CheckIn";
        public const string Edit = "Attendance.Edit";
        public const string ConfigurePolicy = "Attendance.ConfigurePolicy";

        /// <summary>US-ATT-003: submit a regularization request for one's own attendance.</summary>
        public const string RegularizeSelf = "Attendance.Regularize.Self";

        /// <summary>
        /// US-ATT-004: approve/reject attendance regularization requests for one's direct reports.
        /// The literal name the story (US-ATT-004 §2) specifies. Added to the catalog and granted to
        /// the Manager role (the approver persona) plus HR Officer / HR Manager / Tenant Admin (who
        /// hold Attendance.Edit + Attendance.View.All and act as escalation/HR approvers — and as the
        /// route for BR-6 self-approvals that must go to a supervisor or HR). DbInitializer reconciles
        /// role permissions on startup so existing tenants pick this up.
        /// </summary>
        public const string ApproveTeam = "Attendance.Approve.Team";

        /// <summary>
        /// US-ATT-005: create/update/delete/clone shift definitions and assign shifts to employees.
        /// The story names the HR-level <c>Attendance.*.All</c>; that is not a concrete catalog entry,
        /// so a dedicated <c>Attendance.Shift.Manage</c> permission is added (mirroring how the prior
        /// attendance permissions Attendance.Regularize.Self / Attendance.Approve.Team named concrete
        /// strings instead of wildcards). Granted to HR Officer / HR Manager / Tenant Admin / Tenant
        /// Owner — the HR roles that already hold Attendance.Edit. DbInitializer reconciles built-in
        /// role permissions on startup so existing tenants pick it up.
        /// </summary>
        public const string ManageShift = "Attendance.Shift.Manage";

        /// <summary>
        /// US-ATT-009: lock/unlock an attendance period before/after a payroll run (AC-4/AC-5/FR-3).
        /// An HR action. The story names the HR persona without a concrete permission string; following
        /// the ATT-005 precedent a dedicated <c>Attendance.Lock.Manage</c> is added (rather than reusing
        /// the cross-cutting Attendance.View.All read permission for a mutating lock action). Granted to
        /// HR Officer / HR Manager / Tenant Admin / Tenant Owner — the HR roles that already hold
        /// Attendance.Edit. The read endpoints (payroll-data, period-lock GET, reconciliation) reuse the
        /// existing Attendance.View.All. DbInitializer reconciles built-in role permissions on startup.
        /// </summary>
        public const string ManageLock = "Attendance.Lock.Manage";
    }

    // ── Payroll Module ───────────────────────────────────────────────
    public static class Payroll
    {
        public const string View = "Payroll.View";
        public const string ViewOwn = "Payroll.View.Own";
        public const string Run = "Payroll.Run";
        public const string Approve = "Payroll.Approve";
        public const string Configure = "Payroll.Configure";
        public const string Export = "Payroll.Export";
    }

    // ── Recruitment Module ───────────────────────────────────────────
    public static class Recruitment
    {
        public const string View = "Recruitment.View";
        public const string Manage = "Recruitment.Manage";
        public const string ApproveOffer = "Recruitment.ApproveOffer";
    }

    // ── Performance Module ───────────────────────────────────────────
    public static class Performance
    {
        public const string ViewOwn = "Performance.View.Own";
        public const string ViewTeam = "Performance.View.Team";
        public const string ViewAll = "Performance.View.All";
        public const string Manage = "Performance.Manage";

        /// <summary>
        /// US-PRF-001 (BR-4): set/edit/delete goals for one's own direct reports. Granted to the Manager
        /// role (the goal-setting persona). The team-scoped counterpart of <see cref="SetGoalAll"/>.
        /// </summary>
        public const string SetGoalTeam = "Performance.SetGoal.Team";

        /// <summary>
        /// US-PRF-001 (BR-4): set/edit/delete goals for ANY employee in the tenant (HR override).
        /// Granted to HR Officer / HR Manager / Tenant Admin.
        /// </summary>
        public const string SetGoalAll = "Performance.SetGoal.All";

        /// <summary>
        /// US-PRF-002 (NFR-2): an employee reads + writes ONLY their OWN self-assessment for the active
        /// cycle. Granted to the Employee role (the self-assessment persona). The service additionally
        /// scopes every read/write to the caller's own employee record, so the permission gates entry but
        /// never lets one employee see another's data.
        /// </summary>
        public const string ReadSelf = "Performance.Read.Self";

        /// <summary>
        /// US-PRF-003 (BR-2): a manager rates their own DIRECT REPORTS' performance against their goals.
        /// Granted to the Manager role (the reviewing persona). The team-scoped counterpart of
        /// <see cref="ReviewAll"/>. The service additionally enforces the direct-report check (BR-2) so the
        /// permission gates entry but never lets a manager review a non-report.
        /// </summary>
        public const string ReviewTeam = "Performance.Review.Team";

        /// <summary>
        /// US-PRF-003 (BR-3): HR rates ANY employee in the tenant and can REOPEN submitted reviews (AC-5).
        /// Granted to HR Officer / HR Manager / Tenant Admin.
        /// </summary>
        public const string ReviewAll = "Performance.Review.All";

        /// <summary>
        /// US-PRF-004 (BR-1): create / edit / clone / transition / cancel appraisal cycles and publish
        /// results. The OR-partner of <see cref="SetGoalAll"/> for cycle management. Granted to HR Officer /
        /// HR Manager / Tenant Admin.
        /// </summary>
        public const string PublishAll = "Performance.Publish.All";
    }

    // ── Reports Module ───────────────────────────────────────────────
    public static class Reports
    {
        public const string View = "Reports.View";
        public const string Export = "Reports.Export";
    }

    // ── Roles & Permissions (Admin) ──────────────────────────────────
    public static class Roles
    {
        public const string View = "Roles.View";
        public const string Manage = "Roles.Manage";
        public const string AssignUsers = "Roles.AssignUsers";
    }

    // ── Tenant Administration ────────────────────────────────────────
    public static class Tenant
    {
        public const string ViewSettings = "Tenant.ViewSettings";
        public const string ManageSettings = "Tenant.ManageSettings";
        public const string ManageUsers = "Tenant.ManageUsers";
        public const string ManageBilling = "Tenant.ManageBilling";

        /// <summary>
        /// US-ADM-001 (BR-1): provision a new tenant from the System Admin Console — list tenants and
        /// check subdomain availability. A SYSTEM-level capability: only the platform SystemAdmin role
        /// (which is seeded with every catalog permission) holds it. SystemSupport is intentionally NOT
        /// granted this permission, so it cannot provision tenants. The admin endpoints additionally run
        /// only in the system/admin context, so a tenant-scoped TenantOwner who nominally holds this
        /// permission can never reach them.
        /// </summary>
        public const string Provision = "Tenant.Provision";

        /// <summary>
        /// US-ADM-004 (BR-7): suspend / terminate / reactivate / restore a tenant from the System Admin
        /// Console. A SYSTEM-level, DESTRUCTIVE capability — only the platform SystemAdmin role (seeded with
        /// every catalog permission) holds it; System Support does NOT. The endpoints additionally run only in
        /// the system/admin context, so a tenant-scoped role that nominally held this could never reach them.
        /// </summary>
        public const string Lifecycle = "Tenant.Lifecycle";

        /// <summary>
        /// US-ADM-004 (BR-7): VIEW a tenant's lifecycle-event history. Held by SystemAdmin AND the read-only
        /// System Support role (which can view history but cannot initiate transitions). System-context only.
        /// </summary>
        public const string ViewLifecycle = "Tenant.ViewLifecycle";
    }

    // ── Platform Monitoring (US-ADM-002) ─────────────────────────────
    public static class Monitoring
    {
        /// <summary>
        /// US-ADM-002 (BR-1): view the System Admin platform-health + tenant-usage monitoring dashboard. A
        /// SYSTEM-level, READ-ONLY capability (the whole story is read-only) — only the platform SystemAdmin
        /// role (seeded with every catalog permission) holds it, and the endpoints additionally run only in the
        /// system/admin context. Mirrors <see cref="Tenant.Provision"/>: a tenant-scoped role that nominally
        /// held this could never reach the admin-context endpoints.
        ///
        /// NOTE: the story names a read-only "System Support" role. This platform seeds only a single
        /// "SystemAdmin" system role today (DbInitializer) — there is no separate System Support role to grant.
        /// When that role is introduced (its own US), grant it this permission for read-only monitoring access.
        /// </summary>
        public const string View = "Monitoring.View";
    }

    // ── Audit ────────────────────────────────────────────────────────
    public static class Audit
    {
        public const string View = "Audit.View";
    }

    // ── Impersonation (US-ADM-003) ───────────────────────────────────
    public static class Impersonation
    {
        /// <summary>
        /// US-ADM-003 (BR-1): initiate / end a tenant-user impersonation session and list impersonation
        /// targets. A SYSTEM-level capability held by the platform <c>SystemAdmin</c> role (seeded with every
        /// catalog permission) AND the read-only <c>System Support</c> system role. The endpoints additionally
        /// run only in the system/admin context, so a tenant-scoped role that nominally held this could never
        /// reach them. Whether a started session is READ-ONLY is decided server-side (System Support ⇒ always
        /// read-only; a Suspended target tenant ⇒ read-only), not by a permission.
        /// </summary>
        public const string Initiate = "Impersonation.Initiate";
    }

    // ── Notifications ────────────────────────────────────────────────
    public static class Notifications
    {
        public const string ViewOwn = "Notifications.View.Own";
        public const string ManageTemplates = "Notifications.ManageTemplates";
    }

    // ── Training ─────────────────────────────────────────────────────
    public static class Training
    {
        public const string ViewOwn = "Training.View.Own";
        public const string ViewAll = "Training.View.All";
        public const string Manage = "Training.Manage";
    }

    // ── Benefits ─────────────────────────────────────────────────────
    public static class Benefits
    {
        public const string ViewOwn = "Benefits.View.Own";
        public const string ViewAll = "Benefits.View.All";
        public const string Manage = "Benefits.Manage";
    }

    // ── Onboarding ───────────────────────────────────────────────────
    public static class Onboarding
    {
        public const string View = "Onboarding.View";
        public const string Manage = "Onboarding.Manage";
    }

    /// <summary>
    /// Flat list of every permission string in the catalog.
    /// Used for validation and for populating UI permission trees.
    /// </summary>
    public static IReadOnlyList<string> AllPermissions { get; } = new[]
    {
        // Department
        Department.View, Department.Create, Department.Edit, Department.Deactivate,

        // Job Title
        JobTitle.View, JobTitle.Create, JobTitle.Edit, JobTitle.Deactivate,

        // Location
        Location.View, Location.Create, Location.Edit, Location.Deactivate,

        // Employee Document
        EmployeeDocument.View, EmployeeDocument.ViewOwn, EmployeeDocument.Upload, EmployeeDocument.Delete,

        // Custom Field
        CustomField.View, CustomField.Create, CustomField.Edit, CustomField.Deactivate,

        // Employee
        Employee.ViewOwn, Employee.ViewTeam, Employee.ViewAll,
        Employee.Create, Employee.Edit, Employee.EditOwn, Employee.Delete, Employee.Export,
        Employee.ChangeStatus, Employee.Import, Employee.AssignManager,

        // Leave
        Leave.ViewOwn, Leave.ViewTeam, Leave.ViewAll,
        Leave.Apply, Leave.ApproveTeam, Leave.ApproveAll, Leave.ConfigurePolicy, Leave.ManageLop, Leave.Reports,

        // Leave Type
        LeaveType.View, LeaveType.Create, LeaveType.Edit, LeaveType.Deactivate,

        // Holiday Calendar
        Holiday.View, Holiday.Create, Holiday.Edit, Holiday.Deactivate, Holiday.Import,

        // Attendance
        Attendance.ViewOwn, Attendance.ViewTeam, Attendance.ViewAll,
        Attendance.CheckIn, Attendance.Edit, Attendance.ConfigurePolicy, Attendance.RegularizeSelf, Attendance.ApproveTeam, Attendance.ManageShift, Attendance.ManageLock,

        // Payroll
        Payroll.View, Payroll.ViewOwn, Payroll.Run, Payroll.Approve, Payroll.Configure, Payroll.Export,

        // Recruitment
        Recruitment.View, Recruitment.Manage, Recruitment.ApproveOffer,

        // Performance
        Performance.ViewOwn, Performance.ViewTeam, Performance.ViewAll, Performance.Manage,
        Performance.SetGoalTeam, Performance.SetGoalAll, Performance.ReadSelf,
        Performance.ReviewTeam, Performance.ReviewAll, Performance.PublishAll,

        // Reports
        Reports.View, Reports.Export,

        // Roles
        Roles.View, Roles.Manage, Roles.AssignUsers,

        // Tenant
        Tenant.ViewSettings, Tenant.ManageSettings, Tenant.ManageUsers, Tenant.ManageBilling, Tenant.Provision,
        Tenant.Lifecycle, Tenant.ViewLifecycle,

        // Monitoring
        Monitoring.View,

        // Audit
        Audit.View,

        // Impersonation
        Impersonation.Initiate,

        // Notifications
        Notifications.ViewOwn, Notifications.ManageTemplates,

        // Training
        Training.ViewOwn, Training.ViewAll, Training.Manage,

        // Benefits
        Benefits.ViewOwn, Benefits.ViewAll, Benefits.Manage,

        // Onboarding
        Onboarding.View, Onboarding.Manage,
    };

    /// <summary>
    /// All permission strings grouped by module name (the first segment before '.').
    /// Useful for rendering the permission tree in the UI.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByModule { get; } =
        AllPermissions
            .GroupBy(p => p[..p.IndexOf('.')])
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.ToList());

    /// <summary>
    /// Quick O(1) membership check.
    /// </summary>
    public static bool IsValid(string permission) => _permissionSet.Contains(permission);

    private static readonly HashSet<string> _permissionSet = new(AllPermissions);

    /// <summary>
    /// Built-in tenant role names (seeded per tenant, not editable).
    /// </summary>
    public static class BuiltInRoles
    {
        public const string TenantOwner = "Tenant Owner";
        public const string TenantAdmin = "Tenant Admin";
        public const string HRManager = "HR Manager";
        public const string HROfficer = "HR Officer";
        public const string Manager = "Manager";
        public const string Employee = "Employee";
        public const string Recruiter = "Recruiter";
        public const string Auditor = "Auditor";

        public static IReadOnlyList<string> All { get; } = new[]
        {
            TenantOwner, TenantAdmin, HRManager, HROfficer,
            Manager, Employee, Recruiter, Auditor
        };
    }

    /// <summary>
    /// System role names (exist only in the system tenant).
    /// </summary>
    public static class SystemRoles
    {
        public const string SystemSuperAdmin = "System Super Admin";
        public const string SystemSupport = "System Support";
        public const string SystemBilling = "System Billing";
        public const string SystemCompliance = "System Compliance";

        public static IReadOnlyList<string> All { get; } = new[]
        {
            SystemSuperAdmin, SystemSupport, SystemBilling, SystemCompliance
        };
    }

    /// <summary>
    /// Returns the default permissions for a built-in role.
    /// Tenant Owner gets all permissions.
    /// </summary>
    public static IReadOnlyList<string> DefaultPermissionsFor(string roleName) => roleName switch
    {
        BuiltInRoles.TenantOwner => AllPermissions,
        BuiltInRoles.TenantAdmin => new[]
        {
            Department.View, Department.Create, Department.Edit, Department.Deactivate,
            JobTitle.View, JobTitle.Create, JobTitle.Edit, JobTitle.Deactivate,
            Location.View, Location.Create, Location.Edit, Location.Deactivate,
            EmployeeDocument.View, EmployeeDocument.Upload, EmployeeDocument.Delete,
            CustomField.View, CustomField.Create, CustomField.Edit, CustomField.Deactivate,
            LeaveType.View, LeaveType.Create, LeaveType.Edit, LeaveType.Deactivate,
            Holiday.View, Holiday.Create, Holiday.Edit, Holiday.Deactivate, Holiday.Import,
            Employee.ViewAll, Employee.Create, Employee.Edit, Employee.Delete, Employee.Export, Employee.ChangeStatus, Employee.Import, Employee.AssignManager,
            Leave.ViewAll, Leave.ApproveAll, Leave.ConfigurePolicy, Leave.Reports,
            Attendance.ViewAll, Attendance.Edit, Attendance.ConfigurePolicy, Attendance.ApproveTeam, Attendance.ManageShift, Attendance.ManageLock,
            Payroll.View, Payroll.Run, Payroll.Approve, Payroll.Configure, Payroll.Export,
            Recruitment.View, Recruitment.Manage, Recruitment.ApproveOffer,
            Performance.ViewAll, Performance.Manage, Performance.SetGoalAll, Performance.ReviewAll, Performance.PublishAll,
            Reports.View, Reports.Export,
            Roles.View, Roles.Manage, Roles.AssignUsers,
            Tenant.ViewSettings, Tenant.ManageSettings, Tenant.ManageUsers, Tenant.ManageBilling,
            Audit.View,
            Notifications.ManageTemplates,
            Training.ViewAll, Training.Manage,
            Benefits.ViewAll, Benefits.Manage,
            Onboarding.View, Onboarding.Manage,
        },
        BuiltInRoles.HRManager => new[]
        {
            Department.View, Department.Create, Department.Edit, Department.Deactivate,
            JobTitle.View, JobTitle.Create, JobTitle.Edit, JobTitle.Deactivate,
            Location.View, Location.Create, Location.Edit, Location.Deactivate,
            EmployeeDocument.View, EmployeeDocument.Upload, EmployeeDocument.Delete,
            CustomField.View, CustomField.Create, CustomField.Edit, CustomField.Deactivate,
            LeaveType.View, LeaveType.Create, LeaveType.Edit, LeaveType.Deactivate,
            Holiday.View, Holiday.Create, Holiday.Edit, Holiday.Deactivate, Holiday.Import,
            Employee.ViewAll, Employee.Create, Employee.Edit, Employee.Export, Employee.ChangeStatus, Employee.Import, Employee.AssignManager,
            Leave.ViewAll, Leave.ApproveAll, Leave.ConfigurePolicy, Leave.Reports,
            Attendance.ViewAll, Attendance.Edit, Attendance.ConfigurePolicy, Attendance.ApproveTeam, Attendance.ManageShift, Attendance.ManageLock,
            Payroll.View, Payroll.Run,
            Recruitment.View, Recruitment.Manage,
            Performance.ViewAll, Performance.Manage, Performance.SetGoalAll, Performance.ReviewAll, Performance.PublishAll,
            Reports.View, Reports.Export,
            Training.ViewAll, Training.Manage,
            Benefits.ViewAll, Benefits.Manage,
            Onboarding.View, Onboarding.Manage,
        },
        BuiltInRoles.HROfficer => new[]
        {
            Department.View, Department.Create, Department.Edit, Department.Deactivate,
            JobTitle.View, JobTitle.Create, JobTitle.Edit, JobTitle.Deactivate,
            Location.View, Location.Create, Location.Edit, Location.Deactivate,
            EmployeeDocument.View, EmployeeDocument.Upload, EmployeeDocument.Delete,
            LeaveType.View, LeaveType.Create, LeaveType.Edit, LeaveType.Deactivate,
            Holiday.View, Holiday.Create, Holiday.Edit, Holiday.Deactivate, Holiday.Import,
            Employee.ViewAll, Employee.Create, Employee.Edit, Employee.ChangeStatus, Employee.Import, Employee.AssignManager,
            Leave.ViewAll, Leave.ApproveAll, Leave.Reports,
            Attendance.ViewAll, Attendance.Edit, Attendance.ApproveTeam, Attendance.ManageShift, Attendance.ManageLock,
            Recruitment.View, Recruitment.Manage,
            Performance.ViewAll, Performance.SetGoalAll, Performance.ReviewAll, Performance.PublishAll,
            Reports.View,
            Training.ViewAll,
            Onboarding.View, Onboarding.Manage,
        },
        BuiltInRoles.Manager => new[]
        {
            Department.View,
            JobTitle.View,
            Location.View,
            Employee.ViewTeam,
            Leave.ViewTeam, Leave.ApproveTeam,
            Holiday.View,
            Attendance.ViewTeam, Attendance.ApproveTeam,
            Performance.ViewTeam, Performance.SetGoalTeam, Performance.ReviewTeam,
            Reports.View,
            Training.ViewAll,
        },
        BuiltInRoles.Employee => new[]
        {
            Employee.ViewOwn, Employee.EditOwn,
            EmployeeDocument.ViewOwn,
            Leave.ViewOwn, Leave.Apply,
            Holiday.View,
            Attendance.ViewOwn, Attendance.CheckIn, Attendance.RegularizeSelf,
            Payroll.ViewOwn,
            Performance.ViewOwn, Performance.ReadSelf,
            Notifications.ViewOwn,
            Training.ViewOwn,
            Benefits.ViewOwn,
        },
        BuiltInRoles.Recruiter => new[]
        {
            Recruitment.View, Recruitment.Manage,
            Employee.ViewAll,
        },
        BuiltInRoles.Auditor => new[]
        {
            Audit.View,
            Employee.ViewAll,
            Leave.ViewAll, Leave.Reports,
            Attendance.ViewAll,
            Payroll.View,
            Reports.View, Reports.Export,
        },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// US-ADM-003 (BR-1/AC-6): the MINIMAL permission set for the platform "System Support" system role. It can
    /// initiate impersonation (read-only is enforced server-side, not by permission) and read the monitoring
    /// dashboard, but holds NO destructive or tenant-provisioning capability. Kept deliberately small so a
    /// read-only support session is real and testable; the role lives in the system tenant (TenantId = the
    /// platform tenant) alongside SystemAdmin.
    /// </summary>
    public static IReadOnlyList<string> SystemSupportPermissions { get; } = new[]
    {
        Impersonation.Initiate,
        Monitoring.View,
        Tenant.ViewLifecycle, // US-ADM-004 BR-7: view lifecycle history, but NOT Tenant.Lifecycle (cannot transition).
    };
}
