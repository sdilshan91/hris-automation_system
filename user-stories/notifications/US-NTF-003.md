---
id: US-NTF-003
module: Notifications & Audit
priority: Should Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-NTF-003: Notification Preferences per User

## 1. Description
**As an** Employee (or any authenticated user),
**I want to** configure my notification preferences to control which events I receive notifications for and through which channels (in-app, email),
**So that** I am not overwhelmed by irrelevant notifications and can focus on the alerts that matter most to me.

## 2. Preconditions
- The user is authenticated and has an active session within their tenant.
- The notification system is operational (US-NTF-001, US-NTF-002).
- Default notification preferences are set at the tenant level.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The user navigates to Profile > Notification Preferences | They view the preferences page | A matrix is displayed with rows for each notification category (e.g., "Leave Updates", "Attendance Alerts", "Payroll Notifications", "Onboarding Tasks", "System Announcements") and columns for each channel (In-App, Email), with toggle switches for each cell. |
| AC-2 | The user disables email notifications for "Leave Updates" | They save their preferences | Future leave-related events (approval, rejection, balance updates) are delivered only via in-app notifications; no email is sent. The preference is persisted with `tenant_id` and `user_id`. |
| AC-3 | The user attempts to disable all channels for a mandatory notification category (e.g., "Security Alerts") | They toggle it off | The system prevents disabling and displays: "Security alerts cannot be disabled. This is required by your organization's policy." |
| AC-4 | The Tenant Admin configures a notification category as "mandatory" for all users | An employee tries to opt out of that category | The opt-out toggle is disabled (greyed out) with a tooltip: "This notification is required by your organization." |
| AC-5 | User preferences are set in Tenant A | The same user logs into Tenant B (cross-tenant user) | Notification preferences for Tenant B are independent; Tenant A preferences do not apply. Each tenant membership has its own preference set. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a notification preference matrix per user, per tenant membership.
- FR-2: The system SHALL support notification categories: Leave Updates, Attendance Alerts, Payroll Notifications, Onboarding/Offboarding, Performance Reviews, Recruitment Updates, System Announcements, Security Alerts.
- FR-3: The system SHALL support channel toggles: In-App and Email (SMS in Phase 2).
- FR-4: The system SHALL enforce mandatory notification categories that cannot be opted out of (configured by Tenant Admin).
- FR-5: The system SHALL cascade preferences: System defaults < Tenant defaults < User preferences. User preferences override tenant defaults.
- FR-6: The system SHALL apply preferences at dispatch time: the Notification Dispatcher checks user preferences before sending to each channel.
- FR-7: The system SHALL provide a "Reset to Defaults" option that restores tenant-level defaults.
- FR-8: The system SHALL set `tenant_id` and `user_id` on all preference records.
- FR-9: The system SHALL support a "Quiet Hours" setting (e.g., no email notifications between 10 PM and 7 AM in user's timezone).

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Preference page load time SHALL be <= 500 ms (P95).
- NFR-2: Preference data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: Preference lookup during notification dispatch SHALL be cached in Redis (TTL 5 minutes) to avoid per-notification DB queries.
- NFR-4: The preferences page SHALL be fully responsive from 360px to 4K resolution.
- NFR-5: The preferences page SHALL meet WCAG 2.1 AA accessibility standards (toggle switches with ARIA labels).

## 6. Business Rules
- BR-1: New users inherit the tenant's default notification preferences.
- BR-2: Mandatory notification categories (marked by Tenant Admin) cannot be disabled by users.
- BR-3: At least one channel (in-app or email) must remain enabled for non-mandatory categories; a user cannot disable all channels for a category.
- BR-4: Preferences are per-tenant-membership; cross-tenant users have independent preferences per tenant.
- BR-5: "Quiet Hours" only affects email delivery timing (emails are queued and sent after quiet hours end); in-app notifications are always real-time.
- BR-6: Preference changes take effect immediately for future notifications; already-queued notifications are not affected.

## 7. Data Requirements
**Preference record:**
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| preference_id | uuid (PK) | Yes | UUIDv7 |
| tenant_id | uuid | Yes | RLS-enforced |
| user_id | uuid | Yes | |
| category | varchar(50) | Yes | Notification category key |
| channel_in_app | boolean | Yes | Default: true |
| channel_email | boolean | Yes | Default: true |
| is_mandatory | boolean | Yes | Set by Tenant Admin; user cannot change |
| quiet_hours_start | time | No | e.g., 22:00 |
| quiet_hours_end | time | No | e.g., 07:00 |
| quiet_hours_timezone | varchar(50) | No | IANA timezone |

**Output:** User's complete preference matrix.

## 8. UI/UX Notes
- Preference matrix: table layout with category names as rows and channel toggles as columns.
- Toggle switches: styled with Tailwind (green = on, grey = off). Mandatory categories show a lock icon and disabled toggle.
- "Quiet Hours" section: collapsible card below the matrix with start/end time pickers and timezone selector.
- "Reset to Defaults" button with confirmation dialog.
- Auto-save on each toggle change (debounced 500ms) with a subtle "Saved" toast; no explicit save button needed.
- On mobile (< 768px): matrix collapses to a card list per category, each card showing channel toggles inline.
- Category descriptions shown as tooltips or info icons explaining what events are included.

## 9. Dependencies
- US-NTF-001: In-app notification system must exist for channel delivery.
- US-NTF-002: Email notification templates must exist for email channel delivery.
- Authentication module: User must be authenticated with valid tenant context.
- Redis: For caching preference lookups during notification dispatch.

## 10. Assumptions & Constraints
- Tenant Admins configure mandatory categories via the Tenant Admin Console (S35.2.11).
- The Notification Dispatcher reads user preferences from Redis cache (populated on first access or preference change).
- SMS channel is out of scope for Phase 1 but the data model accommodates it.
- Only free/open-source libraries are used.
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Disable email for "Leave Updates"; trigger a leave approval; verify in-app notification sent but no email.
- **Mandatory category:** Attempt to disable "Security Alerts"; verify the toggle is locked and a tooltip explains why.
- **Default inheritance:** Create a new user; verify they inherit tenant default preferences.
- **Reset to Defaults:** Customize preferences, then reset; verify all toggles match tenant defaults.
- **Cross-tenant isolation:** Set preferences for User A in Tenant X; log into Tenant Y as User A; verify independent preferences.
- **Quiet Hours:** Set quiet hours 22:00-07:00; trigger an email notification at 23:00; verify it is queued and sent after 07:00.
- **Redis cache:** Change a preference; verify Redis cache is invalidated and the next dispatch reflects the change.
- **Responsive:** Test the preference matrix at 360px; verify it collapses to a card list.
- **Accessibility:** Navigate all toggles using keyboard; verify ARIA labels announce category and channel state.
