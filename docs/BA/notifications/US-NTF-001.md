---
id: US-NTF-001
module: Notifications & Audit
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 6
---

# US-NTF-001: In-App Notification System (Real-Time via SignalR)

## 1. Description
**As an** Employee (or any authenticated user),
**I want to** receive real-time in-app notifications for events relevant to me (leave approvals, task assignments, onboarding tasks, payroll completion, etc.) via a notification bell with a dropdown panel,
**So that** I am immediately aware of important actions and updates without needing to refresh the page or check email.

## 2. Preconditions
- The user is authenticated and has an active session within their tenant.
- A SignalR connection is established from the Angular SPA to the `/hubs/notifications` hub.
- The notification system is operational and the Redis backplane is available.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The user is logged in and the SPA has loaded | The Angular app bootstraps | A SignalR WebSocket connection is established to `/hubs/notifications`, authenticated via JWT (query string or Authorization header). The client joins tenant-scoped (`t:{tenantId}:user:{userId}`) and role-scoped (`t:{tenantId}:role:{role}`) groups. |
| AC-2 | A manager approves the employee's leave request | The approval is saved | The employee receives a real-time notification within 2 seconds: a badge count increments on the bell icon, and if the notification panel is open, the new notification slides in at the top with a subtle animation. |
| AC-3 | The user clicks the notification bell icon | The dropdown panel opens | A list of recent notifications is displayed (paginated, most recent first), each showing: icon (type-specific), title, brief message, timestamp (relative, e.g., "2 min ago"), and read/unread status (unread = bold text with blue dot). |
| AC-4 | The user clicks on a specific notification (e.g., "Leave approved") | They interact with it | The notification is marked as read, the unread badge count decrements, and the user is navigated to the relevant resource (e.g., the leave request detail page). |
| AC-5 | The user clicks "Mark All as Read" | The action is processed | All unread notifications are marked as read, the badge count resets to zero, and the change is persisted to the database. |
| AC-6 | User A in Tenant A receives a notification | User B in Tenant B is connected | User B does not receive User A's notification; SignalR group naming (`t:{tenantId}:user:{userId}`) ensures tenant and user isolation. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL establish a SignalR WebSocket connection on app bootstrap, using JWT for authentication.
- FR-2: The system SHALL join the user to tenant-scoped and user-scoped SignalR groups (`t:{tenantId}:user:{userId}`, `t:{tenantId}:role:{roleName}`).
- FR-3: The system SHALL persist all notifications in a `notification` table (tenant-scoped) with fields: notification_id, tenant_id, user_id, type, title, message, resource_type, resource_id, is_read, created_at.
- FR-4: The system SHALL push new notifications to connected clients in real-time via SignalR.
- FR-5: The system SHALL display an unread count badge on the notification bell icon (max display "99+").
- FR-6: The system SHALL support notification pagination (20 per page) with infinite scroll.
- FR-7: The system SHALL provide a "Mark as Read" action per notification and a "Mark All as Read" bulk action.
- FR-8: The system SHALL navigate the user to the relevant resource when a notification is clicked.
- FR-9: The system SHALL gracefully handle SignalR disconnections with automatic reconnection (exponential backoff).
- FR-10: The system SHALL use the Redis backplane for SignalR to support multiple API instances.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Real-time notification delivery latency SHALL be <= 2 seconds from event creation to client display.
- NFR-2: All notification data SHALL be isolated by tenant via PostgreSQL RLS and EF Core global query filters.
- NFR-3: The SignalR hub SHALL support at least 5,000 concurrent connections per API instance.
- NFR-4: The notification panel SHALL be fully responsive from 360px to 4K resolution.
- NFR-5: The notification system SHALL gracefully degrade to polling (every 30 seconds) if WebSocket connections are not available.
- NFR-6: The notification bell and panel SHALL meet WCAG 2.1 AA accessibility standards (keyboard navigable, ARIA live region for new notifications).

## 6. Business Rules
- BR-1: Notifications are scoped to the user's current tenant membership; switching tenants changes the notification context.
- BR-2: Notifications older than 90 days are archived (moved to cold storage by a Hangfire job).
- BR-3: A maximum of 1000 notifications are retained per user in the hot table; older ones are purged.
- BR-4: System-generated notifications (e.g., overdue reminders) are sent via the Notification Dispatcher, not directly via SignalR.
- BR-5: Cross-tenant group names in SignalR are rejected at the hub level.

## 7. Data Requirements
**Notification record:**
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| notification_id | uuid (PK) | Yes | UUIDv7 |
| tenant_id | uuid | Yes | RLS-enforced |
| user_id | uuid | Yes | Recipient |
| type | varchar(50) | Yes | e.g., "leave_approved", "task_assigned", "payroll_completed" |
| title | varchar(200) | Yes | Short headline |
| message | varchar(500) | Yes | Brief description |
| resource_type | varchar(50) | No | e.g., "LeaveRequest", "OnboardingTask" |
| resource_id | varchar(100) | No | ID of the linked resource |
| is_read | boolean | Yes | Default: false |
| created_at | timestamptz | Yes | Auto-set |

**Output (panel):** Paginated list of notification records ordered by `created_at` DESC.

## 8. UI/UX Notes
- Notification bell icon in the top navigation bar (right side), with a red badge showing unread count.
- Badge animates (subtle bounce) when a new notification arrives.
- Dropdown panel: max height 480px with scroll, 380px wide on desktop; full-width bottom sheet on mobile.
- Each notification item: type-specific icon on the left (color-coded), title in bold (if unread), message text, and relative timestamp on the right.
- Unread items have a blue dot indicator and slightly tinted background.
- "Mark All as Read" link at the top of the panel, "View All" link at the bottom navigating to a full notifications page.
- Smooth slide-in animation (200ms ease-out) for new notifications appearing in the panel.
- On mobile (< 768px): bell icon in the mobile header; panel opens as a full-screen overlay or bottom sheet.
- Sound notification (optional, toggleable by user) on new notification arrival.

## 9. Dependencies
- Authentication module: JWT-based authentication for SignalR hub.
- Redis: Required for SignalR backplane in multi-instance deployments.
- All modules that raise domain events (Leave, Attendance, Onboarding, Payroll, etc.) feed into the notification system.

## 10. Assumptions & Constraints
- SignalR uses the `/hubs/notifications` endpoint with JWT authentication via query string.
- Redis is deployed and accessible for the SignalR backplane (as per dev-instructions: `localhost:6379`).
- The Angular SPA handles reconnection logic using the SignalR client library's built-in retry mechanism.
- Only free/open-source libraries are used (`@microsoft/signalr` npm package for the Angular client).
- The system uses PostgreSQL with RLS as defense-in-depth for tenant isolation.

## 11. Test Hints
- **Happy path:** Trigger a leave approval; verify the approver's action results in a real-time notification for the employee within 2 seconds.
- **Badge count:** Send 5 notifications to a user; verify badge shows "5". Mark 2 as read; verify badge shows "3".
- **Mark All as Read:** Send 10 notifications; click "Mark All as Read"; verify badge = 0 and all records updated in DB.
- **Navigation:** Click on a "leave_approved" notification; verify navigation to the leave request detail page.
- **Tenant isolation:** Send notifications to users in Tenant A and B; verify SignalR groups are isolated and no cross-tenant delivery occurs.
- **Reconnection:** Simulate SignalR disconnection (kill Redis briefly); verify client reconnects and receives any missed notifications on reconnect.
- **Pagination:** Generate 50 notifications; verify initial load shows 20, scrolling loads the next 20.
- **Responsive:** Test notification panel at 360px width; verify it opens as full-screen or bottom sheet.
- **Accessibility:** Verify ARIA live region announces new notifications to screen readers.
