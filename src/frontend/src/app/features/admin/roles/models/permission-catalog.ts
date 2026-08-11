import { IPermissionGroup } from './role.models';

/**
 * Permission catalog: the source of truth for all available permissions.
 * Follows the pattern Module.Action[.Scope] per FR-1 of US-AUTH-006.
 *
 * This catalog grows as new modules are added. The role management UI
 * renders these grouped by module with checkboxes per action.
 */
export const PERMISSION_CATALOG: IPermissionGroup[] = [
  {
    module: 'Employee',
    label: 'Employees',
    icon: 'people',
    permissions: [
      { key: 'Employee.View.All', label: 'View all employees', description: 'View employee profiles across all departments' },
      { key: 'Employee.View.Team', label: 'View team employees', description: 'View employee profiles within own team' },
      { key: 'Employee.View.Own', label: 'View own profile', description: 'View own employee profile' },
      { key: 'Employee.Create', label: 'Create employee', description: 'Create new employee records' },
      { key: 'Employee.Edit', label: 'Edit all employees', description: 'Edit any employee record' },
      { key: 'Employee.Edit.Own', label: 'Edit own profile', description: 'Edit own employee profile' },
      { key: 'Employee.Delete', label: 'Delete employee', description: 'Delete employee records' },
      { key: 'Employee.Import', label: 'Bulk import', description: 'Import employees from file' },
      { key: 'Employee.Export', label: 'Export data', description: 'Export employee data' },
    ],
  },
  {
    module: 'Leave',
    label: 'Leave Management',
    icon: 'calendar',
    permissions: [
      { key: 'Leave.View.All', label: 'View all leave requests', description: 'View leave requests and balances across the organisation' },
      { key: 'Leave.View.Team', label: 'View team leaves', description: 'View leave requests of team members' },
      { key: 'Leave.Apply', label: 'Apply for leave', description: 'Submit own leave requests' },
      { key: 'Leave.Approve.Team', label: 'Approve team leaves', description: 'Approve or reject leave requests of direct reports' },
      { key: 'Leave.Approve.All', label: 'Approve all leaves', description: 'Approve or reject any leave request' },
      { key: 'Leave.ConfigurePolicy', label: 'Configure leave policies', description: 'Manage leave types, policies, and entitlements' },
    ],
  },
  {
    module: 'Attendance',
    label: 'Attendance',
    icon: 'clock',
    permissions: [
      { key: 'Attendance.View.All', label: 'View all attendance', description: 'View attendance records across the organisation' },
      { key: 'Attendance.View.Team', label: 'View team attendance', description: 'View attendance of team members' },
      { key: 'Attendance.CheckIn', label: 'Check in/out', description: 'Record own attendance check-in and check-out' },
      { key: 'Attendance.Edit', label: 'Edit attendance', description: 'Modify attendance records' },
      { key: 'Attendance.ConfigurePolicy', label: 'Configure attendance', description: 'Manage attendance policies and schedules' },
    ],
  },
  {
    module: 'Payroll',
    label: 'Payroll',
    icon: 'currency',
    permissions: [
      { key: 'Payroll.View', label: 'View payroll', description: 'View payroll runs and payslips' },
      { key: 'Payroll.Run', label: 'Run payroll', description: 'Execute payroll processing runs' },
      { key: 'Payroll.Approve', label: 'Approve payroll', description: 'Approve payroll runs for disbursement' },
      { key: 'Payroll.Configure', label: 'Configure payroll', description: 'Manage salary structures, components, and tax rules' },
      { key: 'Payroll.Export', label: 'Export payroll', description: 'Export payroll data and reports' },
      { key: 'Payroll.ViewSensitive', label: 'View sensitive payroll data', description: 'Reveal masked bank account numbers and salary PII (audited)' },
    ],
  },
  {
    module: 'Recruitment',
    label: 'Recruitment',
    icon: 'recruitment',
    permissions: [
      { key: 'Recruitment.View', label: 'View recruitment', description: 'View job postings and applications' },
      { key: 'Recruitment.Manage', label: 'Manage recruitment', description: 'Create and manage job postings, candidates, and pipeline' },
      { key: 'Recruitment.ApproveOffer', label: 'Approve offers', description: 'Approve offer letters before they are sent' },
    ],
  },
  {
    module: 'Performance',
    label: 'Performance',
    icon: 'star',
    permissions: [
      { key: 'Performance.View.All', label: 'View all performance', description: 'View performance reviews and goals across the organisation' },
      { key: 'Performance.View.Team', label: 'View team performance', description: 'View performance of team members' },
      { key: 'Performance.Review.All', label: 'Conduct any review', description: 'Create and submit performance reviews for any employee' },
      { key: 'Performance.Manage', label: 'Configure performance', description: 'Manage review cycles, templates, and rating scales' },
    ],
  },
  {
    module: 'Reports',
    label: 'Reports & Analytics',
    icon: 'chart',
    permissions: [
      { key: 'Reports.View', label: 'View reports', description: 'Access standard reports and dashboards' },
      { key: 'Reports.View.All', label: 'View all report rows', description: 'View report rows across all departments' },
      { key: 'Reports.View.Team', label: 'View team report rows', description: 'View report rows within own team' },
      { key: 'Reports.Export', label: 'Export reports', description: 'Export reports to file formats' },
    ],
  },
  {
    module: 'Admin',
    label: 'Administration',
    icon: 'settings',
    permissions: [
      { key: 'Tenant.ViewSettings', label: 'View admin settings', description: 'Access tenant administration settings' },
      { key: 'Roles.Manage', label: 'Manage roles', description: 'Create, edit, and delete custom roles and assign roles to users' },
      { key: 'Tenant.ManageUsers', label: 'Manage users', description: 'Invite, deactivate, and manage user accounts' },
      { key: 'Tenant.ManageSettings', label: 'Configure tenant', description: 'Manage tenant settings, branding, and subscription' },
      { key: 'Audit.View', label: 'View audit log', description: 'Access the tenant audit trail' },
    ],
  },
];

/** Flat list of all permission keys for validation */
export const ALL_PERMISSION_KEYS: string[] = PERMISSION_CATALOG.flatMap(
  (group) => group.permissions.map((p) => p.key)
);
