# HRM SaaS — User Stories Index (IEEE 830)

> Generated: 2026-05-11
> Standard: IEEE 830-1998 / ISO/IEC/IEEE 29148:2018
> Total Stories: 102 | Total Acceptance Criteria: ~525

## Summary

| # | Module | Directory | Stories | Priority Breakdown |
|---|--------|-----------|---------|-------------------|
| 1 | [Authentication & Authorization](#1-authentication--authorization) | `authentication/` | 10 | 7 Must, 3 Should |
| 2 | [Core HR](#2-core-hr) | `core-hr/` | 12 | 7 Must, 4 Should, 1 Could |
| 3 | [Leave Management](#3-leave-management) | `leave-management/` | 12 | 7 Must, 5 Should |
| 4 | [Attendance](#4-attendance) | `attendance/` | 10 | 7 Must, 3 Should |
| 5 | [Recruitment](#5-recruitment) | `recruitment/` | 10 | 8 Must, 2 Should |
| 6 | [Payroll](#6-payroll) | `payroll/` | 12 | 10 Must, 2 Should |
| 7 | [Performance Management](#7-performance-management) | `performance/` | 10 | 4 Must, 5 Should, 1 Could |
| 8 | [Admin Console](#8-admin-console) | `admin-console/` | 10 | 8 Must, 2 Should |
| 9 | [Onboarding / Offboarding](#9-onboarding--offboarding) | `onboarding/` | 6 | 4 Must, 2 Should |
| 10 | [Notifications & Audit](#10-notifications--audit) | `notifications/` | 5 | 4 Must, 1 Should |
| 11 | [Reports & Analytics](#11-reports--analytics) | `reports/` | 5 | 4 Must, 1 Should |

---

## 1. Authentication & Authorization

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-AUTH-001](authentication/US-AUTH-001.md) | Admin login with username and password | Must Have | Tenant Admin |
| [US-AUTH-002](authentication/US-AUTH-002.md) | JWT token issuance and refresh token flow | Must Have | All Users |
| [US-AUTH-003](authentication/US-AUTH-003.md) | User logout and token invalidation | Must Have | All Users |
| [US-AUTH-004](authentication/US-AUTH-004.md) | Password reset flow | Must Have | All Users |
| [US-AUTH-005](authentication/US-AUTH-005.md) | Multi-factor authentication (TOTP) | Should Have | Tenant Admin |
| [US-AUTH-006](authentication/US-AUTH-006.md) | Role-based access control (RBAC) per tenant | Must Have | Tenant Admin |
| [US-AUTH-007](authentication/US-AUTH-007.md) | Tenant resolution from subdomain | Must Have | System |
| [US-AUTH-008](authentication/US-AUTH-008.md) | Cross-tenant user switching | Should Have | Cross-Tenant User |
| [US-AUTH-009](authentication/US-AUTH-009.md) | Session management and concurrent session limits | Should Have | Tenant Admin |
| [US-AUTH-010](authentication/US-AUTH-010.md) | Account lockout after failed attempts | Must Have | System |

## 2. Core HR

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-CHR-001](core-hr/US-CHR-001.md) | Add new employee with personal information | Must Have | HR Officer |
| [US-CHR-002](core-hr/US-CHR-002.md) | View and edit employee profile | Must Have | HR Officer / Employee |
| [US-CHR-003](core-hr/US-CHR-003.md) | Employee directory with search and filters | Must Have | HR Officer / Manager |
| [US-CHR-004](core-hr/US-CHR-004.md) | Create and manage departments | Must Have | Tenant Admin |
| [US-CHR-005](core-hr/US-CHR-005.md) | Create and manage job titles and positions | Must Have | Tenant Admin |
| [US-CHR-006](core-hr/US-CHR-006.md) | Organization tree/hierarchy visualization | Should Have | HR Officer |
| [US-CHR-007](core-hr/US-CHR-007.md) | Manage office locations | Should Have | Tenant Admin |
| [US-CHR-008](core-hr/US-CHR-008.md) | Employee document management | Should Have | HR Officer / Employee |
| [US-CHR-009](core-hr/US-CHR-009.md) | Employee status management | Must Have | HR Officer |
| [US-CHR-010](core-hr/US-CHR-010.md) | Bulk employee import via CSV/Excel | Should Have | HR Officer |
| [US-CHR-011](core-hr/US-CHR-011.md) | Employee reporting structure | Must Have | HR Officer |
| [US-CHR-012](core-hr/US-CHR-012.md) | Custom fields per tenant | Could Have | Tenant Admin |

## 3. Leave Management

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-LV-001](leave-management/US-LV-001.md) | Configure leave types per tenant | Must Have | HR Officer |
| [US-LV-002](leave-management/US-LV-002.md) | Set yearly leave entitlements | Must Have | HR Officer |
| [US-LV-003](leave-management/US-LV-003.md) | Employee applies for leave | Must Have | Employee |
| [US-LV-004](leave-management/US-LV-004.md) | Manager views pending leave queue | Must Have | Manager |
| [US-LV-005](leave-management/US-LV-005.md) | Manager approves or rejects leave | Must Have | Manager |
| [US-LV-006](leave-management/US-LV-006.md) | Leave balance dashboard | Must Have | Employee |
| [US-LV-007](leave-management/US-LV-007.md) | Holiday calendar management | Should Have | HR Officer |
| [US-LV-008](leave-management/US-LV-008.md) | Leave carry-forward and expiry rules | Should Have | HR Officer |
| [US-LV-009](leave-management/US-LV-009.md) | Team leave calendar view | Should Have | Manager |
| [US-LV-010](leave-management/US-LV-010.md) | Leave cancellation by employee | Must Have | Employee |
| [US-LV-011](leave-management/US-LV-011.md) | Compulsory leave / LOP handling | Should Have | HR Officer |
| [US-LV-012](leave-management/US-LV-012.md) | Leave reports and analytics | Should Have | HR Officer |

## 4. Attendance

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-ATT-001](attendance/US-ATT-001.md) | Employee clock-in with optional geolocation | Must Have | Employee |
| [US-ATT-002](attendance/US-ATT-002.md) | Employee clock-out with hours auto-calculation | Must Have | Employee |
| [US-ATT-003](attendance/US-ATT-003.md) | Attendance regularization request | Must Have | Employee |
| [US-ATT-004](attendance/US-ATT-004.md) | Manager approves/rejects regularization | Must Have | Manager |
| [US-ATT-005](attendance/US-ATT-005.md) | Shift management and assignment | Must Have | HR Officer |
| [US-ATT-006](attendance/US-ATT-006.md) | Overtime tracking and approval | Should Have | Employee |
| [US-ATT-007](attendance/US-ATT-007.md) | Monthly attendance summary | Must Have | HR Officer |
| [US-ATT-008](attendance/US-ATT-008.md) | Late arrival and early departure tracking | Should Have | HR Officer |
| [US-ATT-009](attendance/US-ATT-009.md) | Attendance integration with payroll | Must Have | HR Officer |
| [US-ATT-010](attendance/US-ATT-010.md) | Attendance dashboard and reports | Should Have | HR Officer |

## 5. Recruitment

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-REC-001](recruitment/US-REC-001.md) | Create and publish job vacancy | Must Have | Recruiter |
| [US-REC-002](recruitment/US-REC-002.md) | Applicant submits application with resume | Must Have | Applicant |
| [US-REC-003](recruitment/US-REC-003.md) | Recruiter views applicant pipeline | Must Have | Recruiter |
| [US-REC-004](recruitment/US-REC-004.md) | Move applicant through pipeline stages | Must Have | Recruiter |
| [US-REC-005](recruitment/US-REC-005.md) | Schedule interviews and notify participants | Must Have | Recruiter |
| [US-REC-006](recruitment/US-REC-006.md) | Interviewer submits scorecard | Must Have | Interviewer |
| [US-REC-007](recruitment/US-REC-007.md) | Generate and send offer letter | Must Have | Recruiter |
| [US-REC-008](recruitment/US-REC-008.md) | Applicant tracks application status | Should Have | Applicant |
| [US-REC-009](recruitment/US-REC-009.md) | Recruitment dashboard and analytics | Should Have | HR Officer |
| [US-REC-010](recruitment/US-REC-010.md) | Convert applicant to employee record | Must Have | HR Officer |

## 6. Payroll

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-PAY-001](payroll/US-PAY-001.md) | Configure salary structure and components | Must Have | Tenant Admin |
| [US-PAY-002](payroll/US-PAY-002.md) | Assign salary structure to employee | Must Have | HR Officer |
| [US-PAY-003](payroll/US-PAY-003.md) | Run monthly payroll | Must Have | HR Officer |
| [US-PAY-004](payroll/US-PAY-004.md) | Generate individual payslips | Must Have | HR Officer |
| [US-PAY-005](payroll/US-PAY-005.md) | Employee views and downloads payslips | Must Have | Employee |
| [US-PAY-006](payroll/US-PAY-006.md) | Statutory deductions configuration | Must Have | Tenant Admin |
| [US-PAY-007](payroll/US-PAY-007.md) | Payroll adjustments (bonus, deductions) | Must Have | HR Officer |
| [US-PAY-008](payroll/US-PAY-008.md) | Payroll approval workflow | Must Have | HR Officer |
| [US-PAY-009](payroll/US-PAY-009.md) | Payroll reports and analytics | Should Have | HR Officer |
| [US-PAY-010](payroll/US-PAY-010.md) | Attendance/leave integration into payroll | Must Have | HR Officer |
| [US-PAY-011](payroll/US-PAY-011.md) | Bulk payslip email distribution | Should Have | HR Officer |
| [US-PAY-012](payroll/US-PAY-012.md) | Payroll history and audit trail | Must Have | HR Officer |

## 7. Performance Management

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-PRF-001](performance/US-PRF-001.md) | Manager sets goals/KPIs for team | Must Have | Manager |
| [US-PRF-002](performance/US-PRF-002.md) | Employee self-rates against goals | Must Have | Employee |
| [US-PRF-003](performance/US-PRF-003.md) | Manager rates employee performance | Must Have | Manager |
| [US-PRF-004](performance/US-PRF-004.md) | HR creates appraisal cycles | Must Have | HR Officer |
| [US-PRF-005](performance/US-PRF-005.md) | 360-degree review | Should Have | HR Officer |
| [US-PRF-006](performance/US-PRF-006.md) | Review meeting notes and sign-off | Should Have | Manager |
| [US-PRF-007](performance/US-PRF-007.md) | Performance dashboard and analytics | Should Have | HR Officer |
| [US-PRF-008](performance/US-PRF-008.md) | Performance improvement plan (PIP) | Should Have | HR Officer |
| [US-PRF-009](performance/US-PRF-009.md) | Goal tracking with progress updates | Should Have | Employee |
| [US-PRF-010](performance/US-PRF-010.md) | Performance-based recommendations | Could Have | HR Officer |

## 8. Admin Console

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-ADM-001](admin-console/US-ADM-001.md) | System Admin provisions new tenant | Must Have | System Admin |
| [US-ADM-002](admin-console/US-ADM-002.md) | Monitor platform health and usage | Must Have | System Admin |
| [US-ADM-003](admin-console/US-ADM-003.md) | Impersonate tenant user with audit | Must Have | System Admin |
| [US-ADM-004](admin-console/US-ADM-004.md) | Suspend/terminate a tenant | Must Have | System Admin |
| [US-ADM-005](admin-console/US-ADM-005.md) | Manage users and role assignments | Must Have | Tenant Admin |
| [US-ADM-006](admin-console/US-ADM-006.md) | Configure company settings | Must Have | Tenant Admin |
| [US-ADM-007](admin-console/US-ADM-007.md) | Manage approval workflows | Should Have | Tenant Admin |
| [US-ADM-008](admin-console/US-ADM-008.md) | View audit logs | Must Have | Tenant Admin |
| [US-ADM-009](admin-console/US-ADM-009.md) | Manage subscription plans | Must Have | System Admin |
| [US-ADM-010](admin-console/US-ADM-010.md) | Tenant data export on demand | Should Have | Tenant Admin |

## 9. Onboarding / Offboarding

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-ONB-001](onboarding/US-ONB-001.md) | Create onboarding checklist template | Must Have | HR Officer |
| [US-ONB-002](onboarding/US-ONB-002.md) | Assign onboarding checklist to new hire | Must Have | HR Officer |
| [US-ONB-003](onboarding/US-ONB-003.md) | New hire completes onboarding tasks | Must Have | Employee |
| [US-ONB-004](onboarding/US-ONB-004.md) | Asset issuance tracking | Should Have | HR Officer |
| [US-ONB-005](onboarding/US-ONB-005.md) | Offboarding/exit checklist and clearance | Must Have | HR Officer |
| [US-ONB-006](onboarding/US-ONB-006.md) | Exit interview recording | Should Have | HR Officer |

## 10. Notifications & Audit

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-NTF-001](notifications/US-NTF-001.md) | In-app notification system (SignalR) | Must Have | Employee |
| [US-NTF-002](notifications/US-NTF-002.md) | Email notification templates per tenant | Must Have | Tenant Admin |
| [US-NTF-003](notifications/US-NTF-003.md) | Notification preferences per user | Should Have | Employee |
| [US-NTF-004](notifications/US-NTF-004.md) | Audit trail for all data changes | Must Have | Tenant Admin |
| [US-NTF-005](notifications/US-NTF-005.md) | Audit log viewer with filters | Must Have | Tenant Admin |

## 11. Reports & Analytics

| ID | Title | Priority | Persona |
|----|-------|----------|---------|
| [US-RPT-001](reports/US-RPT-001.md) | Pre-built HR reports | Must Have | HR Officer |
| [US-RPT-002](reports/US-RPT-002.md) | Leave and attendance reports | Must Have | HR Officer |
| [US-RPT-003](reports/US-RPT-003.md) | Payroll reports and summaries | Must Have | HR Officer |
| [US-RPT-004](reports/US-RPT-004.md) | Export reports to CSV/PDF/Excel | Must Have | HR Officer |
| [US-RPT-005](reports/US-RPT-005.md) | Dashboard with KPI widgets | Should Have | HR Officer |
