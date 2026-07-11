---
id: US-CHR-012
module: Core HR
priority: Could Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-CHR-012: Custom Fields per Tenant

## 1. Description
**As a** Tenant Admin,
**I want to** define custom fields for employee records (and potentially other entities) specific to my organization,
**So that** the system can capture tenant-specific data points (e.g., employee T-shirt size, internal project code, union membership) without requiring platform-level schema changes.

## 2. Preconditions
- The user is authenticated with Tenant Admin role within their tenant.
- The tenant's subscription plan allows custom fields (plan limit: e.g., 5 / 20 / unlimited per entity, as per plan configuration).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A Tenant Admin navigates to Settings > Custom Fields | The page loads | A list of configured custom fields is displayed, grouped by entity (Employee), showing field name, field type, required/optional, and current usage count. An "Add Custom Field" button is available. |
| AC-2 | The Tenant Admin creates a custom field with name "T-Shirt Size", type "Dropdown", options ["S", "M", "L", "XL"], and marks it as optional | They save | The custom field definition is stored for the tenant; the field immediately appears on the employee creation form (US-CHR-001) and employee profile edit form (US-CHR-002) within the "Custom Fields" section. |
| AC-3 | An HR Officer fills in the "T-Shirt Size" custom field when creating an employee | They save the employee | The value is stored in the `custom_fields` JSONB column on the employee record; the value is retrievable and editable on the employee profile. |
| AC-4 | The Tenant Admin has reached the plan's custom field limit (e.g., 5 fields on the Starter plan) | They attempt to add another field | The system blocks with: "You have reached the maximum number of custom fields (5) for your current plan. Upgrade to add more." |
| AC-5 | The Tenant Admin deactivates an existing custom field | They toggle it off | The field is hidden from forms and the directory, but existing data in the JSONB column is preserved. Reactivating the field restores visibility with the previously stored values intact. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL allow Tenant Admins to define custom fields per entity type (Employee in Phase 1; extensible to other entities in future).
- FR-2: The system SHALL support the following field types: Text (short), Text (long/multiline), Number, Date, Dropdown (single select), Dropdown (multi-select), Checkbox (boolean), Email, Phone, URL.
- FR-3: The system SHALL store custom field definitions in a tenant-scoped configuration table.
- FR-4: The system SHALL store custom field values in the `custom_fields` JSONB column on the entity record (e.g., employee table).
- FR-5: The system SHALL validate custom field values against their defined type, required/optional status, and dropdown options before saving.
- FR-6: The system SHALL enforce plan-level limits on the number of custom fields per entity.
- FR-7: The system SHALL support deactivating (hiding) custom fields without deleting stored data.
- FR-8: The system SHALL support reordering custom fields to control display order on forms.
- FR-9: The system SHALL render custom fields dynamically on relevant forms (employee creation, employee profile edit).
- FR-10: The system SHALL include custom fields in the employee directory export (US-CHR-003) and bulk import template (US-CHR-010).
- FR-11: The system SHALL index the `custom_fields` JSONB column with a GIN index for efficient querying.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Custom field configuration API response time SHALL be <= 400 ms for reads, <= 800 ms for writes (P95).
- NFR-2: All custom field definitions and values SHALL be tenant-isolated via RLS and EF Core global query filters.
- NFR-3: Querying employees by custom field values (via JSONB operators) SHALL complete within 500 ms for up to 10,000 employees (aided by GIN index).
- NFR-4: The custom fields management page SHALL be fully responsive (360px to 4K).
- NFR-5: Custom field definition changes SHALL be audited.
- NFR-6: Custom field rendering on forms SHALL not degrade page load time by more than 200 ms.

## 6. Business Rules
- BR-1: Custom field names must be unique within a tenant + entity combination.
- BR-2: Custom field definitions are tenant-specific; each tenant has its own set of custom fields.
- BR-3: Deleting a custom field definition does not remove stored values from JSONB; the data remains but is inaccessible via the UI.
- BR-4: Plan limits are enforced at the time of field creation: 5 (Starter), 20 (Professional), unlimited (Enterprise).
- BR-5: Custom field types cannot be changed after creation if data exists (to prevent data corruption). The field must be deactivated and a new one created.
- BR-6: Dropdown options can be added but not removed if they are in use by existing records.
- BR-7: Custom fields are not searchable in the full-text employee search (Phase 1); they are filterable via advanced filters.

## 7. Data Requirements
**Custom Field Definition table:**
| Column | Type | Required | Notes |
|--------|------|----------|-------|
| custom_field_id | uuid (PK) | Auto | |
| tenant_id | uuid (FK) | Auto | Set from session |
| entity_type | varchar(50) | Yes | "employee" (Phase 1) |
| field_name | varchar(100) | Yes | Unique per tenant + entity |
| field_key | varchar(100) | Yes | Slugified, used as JSONB key |
| field_type | varchar(30) | Yes | text, textarea, number, date, dropdown, multi_select, checkbox, email, phone, url |
| is_required | boolean | Yes | Default: false |
| options | jsonb | No | For dropdown/multi_select: ["S", "M", "L", "XL"] |
| display_order | int | Yes | For form rendering order |
| is_active | boolean | Yes | Default: true |
| created_at / updated_at | timestamptz | Auto | |
| created_by / updated_by | uuid | Auto | |
| is_deleted | boolean | Auto | Default: false |

**Employee table (existing):**
| Column | Type | Notes |
|--------|------|-------|
| custom_fields | jsonb | e.g., `{"tshirt_size": "L", "project_code": "PRJ-42"}` |

## 8. UI/UX Notes (Notion-like, cards-based)
- Custom Fields management page (Settings > Custom Fields): card-based table listing all defined fields with drag-and-drop reordering (via drag handle icon on the left).
- "Add Custom Field" button opens a slide-over panel or modal with fields: Field Name, Field Key (auto-generated from name, editable), Field Type (visual selector with icons for each type), Required toggle, Options (for dropdown types: inline tag input), Display Order.
- Field type selector: visual cards/icons for each type (similar to Notion property type selector).
- For dropdown options: tag-input component where the admin types an option and presses Enter to add it; existing options shown as removable chips.
- Preview panel: as the admin configures the field, a live preview shows how it will appear on the employee form.
- On the employee form: custom fields render in a dedicated "Additional Information" card section, styled consistently with other form sections.
- Plan limit indicator: "3 of 5 custom fields used" progress bar near the top of the management page.
- On mobile: drag-and-drop reordering replaced by up/down arrow buttons; form fields stack vertically.
- Smooth animations: slide-over panel entrance (300ms), drag-and-drop reorder with list animation.

## 9. Dependencies
- US-CHR-001: Custom fields appear on the employee creation form.
- US-CHR-002: Custom fields appear on the employee profile edit form.
- US-CHR-003: Custom fields included in employee directory export.
- US-CHR-010: Custom fields included in bulk import template and processing.
- Subscription/Plan module: Plan limits determine how many custom fields a tenant can create.

## 10. Assumptions & Constraints
- JSONB storage is used rather than an EAV (Entity-Attribute-Value) pattern for performance and simplicity.
- GIN indexes on the `custom_fields` JSONB column provide efficient querying.
- Custom field types are fixed in Phase 1; a "formula" or "relation" type may be added in Phase 2.
- Only free/open-source libraries are used.
- The `field_key` is auto-generated as a URL-safe slug from the field name and is immutable after creation.

## 11. Test Hints
- **Create custom field:** Define a "T-Shirt Size" dropdown field; verify it appears on the employee form.
- **Store and retrieve value:** Create an employee with custom field values; reload the profile; verify values persist and display.
- **Plan limit enforcement:** Set plan limit to 5; create 5 fields; attempt 6th; expect block with upgrade message.
- **Type validation:** Define a Number field; attempt to store "abc"; expect validation error.
- **Required field:** Mark a custom field as required; attempt to create an employee without it; expect validation error.
- **Deactivate field:** Deactivate a field; verify it no longer appears on forms; verify stored data is preserved in JSONB; reactivate; verify data reappears.
- **Tenant isolation:** Define custom fields in Tenant A; verify they do not appear in Tenant B's forms.
- **Dropdown options:** Add options; assign one to an employee; attempt to remove that option; expect warning that it's in use.
- **GIN index performance:** Create 5,000 employees with custom field data; query by custom field value; verify response within 500 ms.
- **Export/Import:** Add custom field columns to export; verify they appear. Upload an import file with custom field columns; verify values are stored.
