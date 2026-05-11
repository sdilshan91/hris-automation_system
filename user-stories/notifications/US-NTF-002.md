---
id: US-NTF-002
module: Notifications & Audit
priority: Must Have
persona: Tenant Admin
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-NTF-002: Email Notification Templates per Tenant

## 1. Description
**As a** Tenant Admin,
**I want to** customize email notification templates for various HR events (leave approval, onboarding welcome, payroll published, etc.) with my organization's branding and content,
**So that** all automated emails sent from the platform reflect my company's tone, branding, and specific communication requirements.

## 2. Preconditions
- The Tenant Admin is authenticated and has an active session within their tenant.
- The tenant's subscription plan includes email notifications.
- Default system email templates exist as a baseline for all event types.
- An SMTP relay or transactional email service is configured.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Tenant Admin navigates to Settings > Notifications & Email > Templates | They view the template list | A list of all available notification event types is displayed (e.g., "Leave Approved", "Onboarding Welcome", "Payslip Published", "Password Reset"), each showing whether it uses the system default or a custom tenant override. |
| AC-2 | Tenant Admin opens a template for editing (e.g., "Leave Approved") | They modify the subject line, HTML body, and plain-text body using a rich text editor | The editor supports placeholder variables (e.g., `{{employee.firstName}}`, `{{leave.startDate}}`, `{{tenant.companyName}}`), shown in a reference panel. A live preview renders the template with sample data. |
| AC-3 | Tenant Admin saves a customized template | The template is persisted | The tenant-specific override is stored with `tenant_id` from the session context. Future emails for this event type use the custom template instead of the system default. |
| AC-4 | Tenant Admin clicks "Reset to Default" on a customized template | The action is confirmed | The tenant override is removed (soft-deleted), and future emails revert to the system default template. An audit record is created. |
| AC-5 | Tenant A customizes their "Leave Approved" template | Tenant B views their templates | Tenant B still sees the system default for "Leave Approved"; Tenant A's customization is invisible due to RLS and EF Core tenant filters. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a template editor with a rich text editor (HTML body), plain-text body, and subject line fields.
- FR-2: The system SHALL support placeholder variables resolved at send time (e.g., `{{employee.firstName}}`, `{{leave.type}}`, `{{tenant.companyName}}`, `{{tenant.logoUrl}}`).
- FR-3: The system SHALL display a variable reference panel listing all available placeholders for the selected event type.
- FR-4: The system SHALL provide a live preview that renders the template with sample data.
- FR-5: The system SHALL support per-language template variants (e.g., English + secondary language per the tenant's i18n config).
- FR-6: The system SHALL fall back to the system default template if no tenant override exists.
- FR-7: The system SHALL allow tenant admins to configure a custom sender domain (e.g., `hr@acme.com`) with SPF/DKIM setup guidance.
- FR-8: The system SHALL send a test email to the admin's address on demand for preview purposes.
- FR-9: The system SHALL log template changes in the tenant audit log.
- FR-10: The system SHALL set `tenant_id` from the session context on all template overrides.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Template editor page load time SHALL be <= 1 second (P95).
- NFR-2: All template data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: Email rendering (template + data merge) SHALL complete within 200 ms per email.
- NFR-4: The template editor SHALL be fully responsive from 360px to 4K resolution.
- NFR-5: The editor SHALL meet WCAG 2.1 AA accessibility standards.
- NFR-6: Template changes SHALL be audited via EF Core `SaveChangesInterceptor`.

## 6. Business Rules
- BR-1: System default templates are read-only for tenants; tenants create overrides that take precedence.
- BR-2: Every event type must have at least a system default template; the system never sends emails without a template.
- BR-3: Templates must include both HTML and plain-text versions for email client compatibility.
- BR-4: Custom sender domains require DNS verification (SPF/DKIM); until verified, the platform default sender is used.
- BR-5: Template placeholders that cannot be resolved at send time are replaced with empty strings (not raw placeholder text).
- BR-6: A maximum of 2 language variants per template per tenant (configurable based on plan).

## 7. Data Requirements
**Template record:**
| Field | Type | Required | Validation |
|-------|------|----------|------------|
| template_id | uuid (PK) | Yes | UUIDv7 |
| tenant_id | uuid | Yes (null = system default) | RLS-enforced |
| event_key | varchar(100) | Yes | e.g., "leave_approved", "onboarding_welcome" |
| language | varchar(10) | Yes | ISO 639-1, default "en" |
| subject | varchar(200) | Yes | Supports placeholders |
| body_html | text | Yes | HTML with placeholders |
| body_text | text | Yes | Plain-text with placeholders |
| is_active | boolean | Yes | Default: true |
| version | int | Yes | Auto-incremented on update |

**Output:** Template record with rendered preview.

## 8. UI/UX Notes
- Template list: table/card view with event name, status badge ("Default" / "Custom"), language, and last modified date.
- Template editor: split-pane layout -- editor on the left, live preview on the right (desktop). On mobile, tab-based toggle between editor and preview.
- Rich text editor: use a free/open-source WYSIWYG editor (e.g., Quill, TipTap) with toolbar for formatting, links, and images.
- Placeholder insertion: clickable variable names in the reference panel that insert at cursor position.
- "Send Test Email" button with a modal to enter the recipient address.
- "Reset to Default" button with a confirmation dialog.
- Version history: expandable section showing previous versions with diff highlighting.
- On mobile (< 768px): full-width layout, tabs for editor/preview/variables.

## 9. Dependencies
- Authentication module: User must be authenticated as Tenant Admin.
- SMTP / SendGrid / ACS: Email delivery infrastructure must be configured.
- US-NTF-001: In-app notifications work alongside email notifications.
- Tenant Admin Console (Technical Doc S35.2.11): Template editor is part of the admin console.

## 10. Assumptions & Constraints
- System default templates are seeded during platform deployment and managed by System Admins.
- The rich text editor uses a free/open-source library (Quill or TipTap).
- Email dispatch uses the outbox pattern: template rendering happens in the Hangfire worker, not inline.
- SPF/DKIM setup for custom sender domains requires the tenant to add DNS records; the system provides instructions but cannot automate DNS changes.
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Customize the "Leave Approved" template for Tenant A; trigger a leave approval; verify the email uses the custom template with correct placeholders resolved.
- **Fallback:** Do not customize any template for Tenant B; trigger the same event; verify the system default template is used.
- **Live preview:** Enter template with placeholders; verify preview renders with sample data correctly.
- **Reset to Default:** Customize a template, then reset; verify future emails use the system default.
- **Tenant isolation:** Customize a template in Tenant A; verify Tenant B cannot see or use it.
- **Test email:** Click "Send Test Email"; verify the email arrives at the specified address with correct rendering.
- **Language variant:** Create English and Spanish variants; trigger the event for a user with Spanish preference; verify the Spanish template is used.
- **Audit trail:** Modify a template; verify an audit log entry is created with before/after states.
