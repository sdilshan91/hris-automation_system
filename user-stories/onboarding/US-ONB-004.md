---
id: US-ONB-004
module: Onboarding / Offboarding
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ONB-004: Asset Issuance Tracking During Onboarding

## 1. Description
**As an** HR Officer,
**I want to** record and track assets (laptop, ID card, access badge, phone, etc.) issued to a new hire during onboarding,
**So that** the organization maintains an accurate register of company assets assigned to each employee, enabling proper recovery during offboarding.

## 2. Preconditions
- The HR Officer is authenticated and has an active session within their tenant.
- The employee record exists with an active or probation status.
- The Asset Management module (lite) is enabled for the tenant's subscription plan.
- An asset register with available assets exists (or ad-hoc asset entry is permitted).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer opens an employee's onboarding checklist containing an "Asset Issuance" task | They click "Issue Asset" | A form opens allowing selection of asset type (dropdown), asset tag/serial number, condition, and issue date. Multiple assets can be added in a single session. |
| AC-2 | HR Officer records the issuance of a laptop with serial number and condition "New" | They save the asset issuance | The asset is linked to the employee, the asset status in the register changes to "assigned", the onboarding task status updates to "completed", and an audit record is created with before/after states. |
| AC-3 | HR Officer attempts to assign an asset already assigned to another employee | They try to save | The system displays: "This asset (Serial: XYZ) is currently assigned to [Employee Name]. Please return it first or select a different asset." |
| AC-4 | The employee views their profile | They navigate to the "Assets" tab | A list of all assets currently assigned to them is displayed, including type, serial number, issue date, and condition. |
| AC-5 | An asset issuance record is created in Tenant A | A user in Tenant B queries the asset register | No Tenant A assets are visible; RLS and EF Core filters enforce tenant isolation. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide an asset issuance form linked to onboarding checklist tasks of type "asset_issuance".
- FR-2: The system SHALL maintain an asset register with fields: asset_id, asset_type, asset_tag, serial_number, brand, model, purchase_date, condition, status (available/assigned/returned/disposed), assigned_employee_id.
- FR-3: The system SHALL validate that an asset is in "available" status before allowing issuance.
- FR-4: The system SHALL update the asset status to "assigned" and link it to the employee upon issuance.
- FR-5: The system SHALL support bulk issuance (multiple assets to one employee in a single form submission).
- FR-6: The system SHALL allow attaching an acknowledgment document (e.g., signed receipt) to the issuance record.
- FR-7: The system SHALL set `tenant_id` from the session context on all asset records.
- FR-8: The system SHALL record asset issuance as an audit event with before/after state.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Asset issuance API response time SHALL be <= 600 ms (P95).
- NFR-2: All asset data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: The asset form SHALL be fully responsive from 360px to 4K resolution.
- NFR-4: Acknowledgment document uploads SHALL be limited to 10 MB and scanned for malware.
- NFR-5: The operation SHALL be atomic: asset status update and employee linkage happen in a single database transaction.

## 6. Business Rules
- BR-1: An asset can be assigned to only one employee at a time.
- BR-2: Asset types are configurable per tenant (e.g., Laptop, Phone, ID Card, Access Badge, Vehicle).
- BR-3: Assets must have a unique asset_tag or serial_number within the tenant.
- BR-4: When an asset is returned (during offboarding), its status reverts to "available" or "disposed".
- BR-5: The asset register uses soft delete; assets are never hard-deleted via the UI.
- BR-6: The employee's self-service view of assets is read-only.

## 7. Data Requirements
**Input fields:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| employee_id | uuid | Yes | Must exist in tenant |
| assets[].asset_type | varchar(50) | Yes | Must be a configured type |
| assets[].asset_tag | varchar(100) | Yes | Unique per tenant |
| assets[].serial_number | varchar(100) | No | Unique per tenant if provided |
| assets[].brand | varchar(100) | No | |
| assets[].model | varchar(100) | No | |
| assets[].condition | varchar(20) | Yes | New, Good, Fair, Poor |
| assets[].issue_date | date | Yes | Cannot be in the future |
| assets[].notes | text | No | Max 500 chars |
| acknowledgment_doc | file | No | PDF/JPEG/PNG, max 10 MB |

**Output:** List of issued asset records with asset_id, employee linkage, and updated status.

## 8. UI/UX Notes
- Asset issuance form embedded within the onboarding checklist task card, expanding inline when clicked.
- Searchable asset dropdown with type-ahead showing available assets (filtered by type).
- "Add Another Asset" button to issue multiple assets in one session; each asset is a removable card.
- Condition selector uses colored chips (green = New, blue = Good, yellow = Fair, red = Poor).
- Acknowledgment upload: drag-and-drop zone with file preview.
- On mobile (< 768px): single-column layout, full-width cards.
- Success toast: "N asset(s) issued to [Employee Name]."

## 9. Dependencies
- US-ONB-002: Onboarding checklist must be assigned with asset issuance tasks.
- US-CHR-001: Employee record must exist.
- Authentication module: User must be authenticated with valid tenant context.
- File & Document Management (Technical Doc S26): For acknowledgment document storage.

## 10. Assumptions & Constraints
- The Asset Management module provides a "lite" asset register; full asset lifecycle management (depreciation, maintenance) is out of scope for Phase 1.
- Tenant admins configure asset types via the Tenant Admin Console master data section.
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Issue a laptop and ID card to a new hire; verify both asset records linked to employee with status "assigned".
- **Double assignment:** Issue an asset already assigned to another employee; expect validation error.
- **Unique asset tag:** Create two assets with the same asset_tag in the same tenant; expect rejection.
- **Tenant isolation:** Issue assets in Tenant A and B; verify cross-tenant queries return nothing.
- **Bulk issuance:** Issue 3 assets in a single submission; verify all 3 persisted in a single transaction.
- **Acknowledgment upload:** Attach a signed PDF receipt; verify stored at tenant-isolated path.
- **Employee view:** Log in as the employee; verify assets are visible but read-only.
- **Audit trail:** Verify asset issuance creates an audit log entry with before/after state.
