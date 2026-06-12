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
    }

    // ── Audit ────────────────────────────────────────────────────────
    public static class Audit
    {
        public const string View = "Audit.View";
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

        // Employee
        Employee.ViewOwn, Employee.ViewTeam, Employee.ViewAll,
        Employee.Create, Employee.Edit, Employee.EditOwn, Employee.Delete, Employee.Export,
        Employee.ChangeStatus, Employee.Import, Employee.AssignManager,

        // Leave
        Leave.ViewOwn, Leave.ViewTeam, Leave.ViewAll,
        Leave.Apply, Leave.ApproveTeam, Leave.ApproveAll, Leave.ConfigurePolicy,

        // Attendance
        Attendance.ViewOwn, Attendance.ViewTeam, Attendance.ViewAll,
        Attendance.CheckIn, Attendance.Edit, Attendance.ConfigurePolicy,

        // Payroll
        Payroll.View, Payroll.ViewOwn, Payroll.Run, Payroll.Approve, Payroll.Configure, Payroll.Export,

        // Recruitment
        Recruitment.View, Recruitment.Manage, Recruitment.ApproveOffer,

        // Performance
        Performance.ViewOwn, Performance.ViewTeam, Performance.ViewAll, Performance.Manage,

        // Reports
        Reports.View, Reports.Export,

        // Roles
        Roles.View, Roles.Manage, Roles.AssignUsers,

        // Tenant
        Tenant.ViewSettings, Tenant.ManageSettings, Tenant.ManageUsers, Tenant.ManageBilling,

        // Audit
        Audit.View,

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
            Employee.ViewAll, Employee.Create, Employee.Edit, Employee.Delete, Employee.Export, Employee.ChangeStatus, Employee.Import, Employee.AssignManager,
            Leave.ViewAll, Leave.ApproveAll, Leave.ConfigurePolicy,
            Attendance.ViewAll, Attendance.Edit, Attendance.ConfigurePolicy,
            Payroll.View, Payroll.Run, Payroll.Approve, Payroll.Configure, Payroll.Export,
            Recruitment.View, Recruitment.Manage, Recruitment.ApproveOffer,
            Performance.ViewAll, Performance.Manage,
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
            Employee.ViewAll, Employee.Create, Employee.Edit, Employee.Export, Employee.ChangeStatus, Employee.Import, Employee.AssignManager,
            Leave.ViewAll, Leave.ApproveAll, Leave.ConfigurePolicy,
            Attendance.ViewAll, Attendance.Edit, Attendance.ConfigurePolicy,
            Payroll.View, Payroll.Run,
            Recruitment.View, Recruitment.Manage,
            Performance.ViewAll, Performance.Manage,
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
            Employee.ViewAll, Employee.Create, Employee.Edit, Employee.ChangeStatus, Employee.Import, Employee.AssignManager,
            Leave.ViewAll, Leave.ApproveAll,
            Attendance.ViewAll, Attendance.Edit,
            Recruitment.View, Recruitment.Manage,
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
            Attendance.ViewTeam,
            Performance.ViewTeam,
            Reports.View,
            Training.ViewAll,
        },
        BuiltInRoles.Employee => new[]
        {
            Employee.ViewOwn, Employee.EditOwn,
            EmployeeDocument.ViewOwn,
            Leave.ViewOwn, Leave.Apply,
            Attendance.ViewOwn, Attendance.CheckIn,
            Payroll.ViewOwn,
            Performance.ViewOwn,
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
            Leave.ViewAll,
            Attendance.ViewAll,
            Payroll.View,
            Reports.View, Reports.Export,
        },
        _ => Array.Empty<string>(),
    };
}
