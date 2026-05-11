---
id: US-LV-001
module: Leave Management
priority: Must Have
persona: HR Officer / Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-LV-001: Configure Leave Types Per Tenant

## 1. Description
**As an** HR Officer or Tenant Admin,
**I want to** configure leave types (e.g., Annual, Sick, Casual, Maternity, Paternity, Bereavement, Unpaid) specific to my organization,
**So that** the leave policy is enforced automatically and employees can only apply for leave types that are valid for our tenant.

## 2. Preconditions
- User is authenticated and has `Leave.Configure` or `Tenant.Admin` permission.
- Tenant has been provisioned and onboarding wizard Step 4 (leave types & holidays) is accessible.
- Core HR module is active with at least one department and job title configured.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer is on the Leave Types configuration page | They create a new leave type with name, code, color, annual entitlement, accrual frequency, carry-forward rules, and probation eligibility | The leave type is saved and scoped to the current tenant only; other tenants cannot see it |
| AC-2 | A leave type already exists | HR Officer edits the entitlement or carry-forward settings | Changes are saved with audit trail (before/after captured) and take effect for the next accrual cycle |
| AC-3 | HR Officer attempts to create a leave type with a duplicate name within the same tenant | They submit the form | A validation error is displayed: "A leave type with this name already exists" |
| AC-4 | HR Officer deactivates a leave type | They toggle the status to inactive | Employees can no longer apply for that type, but existing approved requests remain unaffected |
| AC-5 | A leave type requires supporting documents (e.g., medical certificate for Sick Leave > 2 days) | HR Officer configures the "documents required" rule with a day threshold | The system enforces document upload when employees apply for leave exceeding the threshold |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: CRUD operations for leave types scoped to `tenant_id`.
- FR-2: Each leave type must support the following configurable fields: name, code, color tag, description, annual entitlement (days), accrual frequency (monthly/quarterly/yearly/upfront), carry-forward limit (days), carry-forward expiry (months), probation eligibility (boolean), encashment policy (boolean + max days), half-day support (boolean), hourly support (boolean), documents required (boolean + day threshold), gender applicability (all/male/female), maximum consecutive days, negative balance allowed (boolean + limit).
- FR-3: Leave types must be orderable (display_order) for UI presentation.
- FR-4: System must seed default leave types during tenant onboarding (Step 4 of onboarding wizard) that the tenant admin can customize.
- FR-5: Soft delete support — deactivated leave types are hidden from application forms but retained for historical reporting.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Leave type list API response must complete within 200ms (P95) with Redis caching; cache invalidated on write.
- NFR-2: All leave type data must be tenant-isolated via both EF Core global query filters and PostgreSQL RLS policies.
- NFR-3: Configuration changes must be audit-logged with before/after JSON snapshots.
- NFR-4: UI must be fully responsive from 360px to 4K, following Notion-like design aesthetics.

## 6. Business Rules
- BR-1: Leave type names must be unique within a tenant (case-insensitive).
- BR-2: A leave type cannot be hard-deleted if any leave requests reference it; it can only be deactivated.
- BR-3: Entitlement values must be positive numbers; zero entitlement is allowed for unpaid leave types.
- BR-4: Gender-specific leave types (e.g., Maternity) must only appear for employees matching the configured gender.
- BR-5: Changes to leave type configuration do not retroactively affect already-approved leave requests.

## 7. Data Requirements
- **Table:** `leave_type`
- **Key columns:** `leave_type_id (uuid PK)`, `tenant_id (uuid FK, NOT NULL)`, `name (varchar(100))`, `code (varchar(20))`, `color (varchar(7))`, `annual_entitlement (numeric(5,2))`, `accrual_frequency (varchar(20))`, `carry_forward_limit (numeric(5,2))`, `carry_forward_expiry_months (int)`, `probation_eligible (boolean)`, `documents_required (boolean)`, `document_day_threshold (int)`, `encashable (boolean)`, `max_encash_days (numeric(5,2))`, `half_day_allowed (boolean)`, `hourly_allowed (boolean)`, `gender (varchar(10))`, `max_consecutive_days (int)`, `negative_balance_allowed (boolean)`, `negative_balance_limit (numeric(5,2))`, `display_order (int)`, `is_active (boolean)`, `is_deleted (boolean)`, `created_at`, `created_by`, `updated_at`, `updated_by`.
- **RLS policy:** `tenant_isolation_select` and `tenant_isolation_modify` on `leave_type`.
- **Index:** `leave_type(tenant_id, is_active, display_order)`.

## 8. UI/UX Notes (Notion-like)
- Leave types displayed in a clean table/card list with color-coded tags, inline toggle for active/inactive status.
- Create/Edit via a slide-over panel (Notion-style) rather than a full page navigation.
- Subtle hover effects, smooth transitions (200ms ease), clean whitespace.
- Form fields grouped logically: Basic Info, Entitlement Rules, Carry-Forward, Document Rules, Advanced.
- Mobile: Stack fields vertically; collapse advanced sections into accordion.
- Drag-and-drop reordering of leave types for display order.

## 9. Dependencies
- **US-AUTH-***: User must be authenticated with valid JWT containing `tenant_id`.
- **US-CORE-***: Core HR module must be set up (departments, job titles) for entitlement-by-level rules (US-LV-002).
- **US-TENANT-***: Tenant provisioning and onboarding wizard must be functional.

## 10. Assumptions & Constraints
- Leave types are tenant-specific; there is no global/system-wide leave type catalog (each tenant configures independently).
- Default leave types seeded during onboarding are merely suggestions; tenants can delete/modify them.
- Only free and open-source UI libraries are permitted (Angular Material + Tailwind CSS).
- The system uses UUIDv7 for all primary keys.

## 11. Test Hints
- Verify tenant isolation: Create leave types in Tenant A, confirm they are invisible to Tenant B via API and direct DB query with different RLS context.
- Test duplicate name validation (case-insensitive): "Annual Leave" vs "annual leave".
- Test deactivation: Deactivate a leave type, verify it disappears from the employee apply-leave dropdown but remains in historical reports.
- Test audit trail: Modify a leave type and verify `audit_log` contains correct before/after JSON.
- Test Redis cache invalidation: Update a leave type, immediately fetch the list, and confirm the updated data is returned.
- Test onboarding wizard seeding: Provision a new tenant and verify default leave types are created.
