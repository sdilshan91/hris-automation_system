---
id: US-ATT-001
module: Attendance
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-001: Employee Clock-In from Browser with Optional Geolocation

## 1. Description
**As an** Employee,
**I want to** clock in from my browser with optional geolocation capture,
**So that** my attendance is recorded accurately and my employer can verify my work location when required by tenant policy.

## 2. Preconditions
- Employee must be authenticated and have an active session (JWT valid).
- Employee must have the `Attendance.Clock.Self` permission.
- Employee's tenant must have the Attendance module enabled.
- Employee must be assigned to an active shift (or a default shift must exist for the tenant).
- The current date must be a working day for the employee (unless tenant policy allows clock-in on holidays).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee is logged in and has not clocked in today | Employee clicks the "Clock In" button | A new `attendance_log` record is created with `clock_in` set to the current UTC timestamp, `tenant_id` set from session context, and the UI displays a confirmation with the local time |
| AC-2 | Employee is logged in and has already clocked in today without clocking out | Employee attempts to clock in again | The system prevents a duplicate clock-in and displays an error message: "You have already clocked in. Please clock out first." |
| AC-3 | Tenant policy requires geolocation for clock-in | Employee clicks "Clock In" | The browser requests location permission; if granted, latitude and longitude are captured and stored in the `attendance_log` record; if denied, the clock-in is blocked with a message explaining the requirement |
| AC-4 | Tenant policy has geolocation as optional | Employee clicks "Clock In" without granting location permission | The clock-in proceeds successfully without location data |
| AC-5 | Tenant policy enforces an IP allowlist for clock-in | Employee attempts to clock in from a non-allowed IP address | The system rejects the clock-in and displays: "Clock-in is only allowed from authorized network locations." |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall create an `attendance_log` record with `attendance_log_id` (UUID), `tenant_id`, `employee_id`, `clock_in` (timestamptz), and nullable geolocation fields (`clock_in_latitude`, `clock_in_longitude`).
- FR-2: The system shall prevent multiple active clock-in records for the same employee on the same calendar day (tenant timezone).
- FR-3: When the tenant's `geo_fence_enabled` setting is true, the system shall validate that the employee's coordinates fall within the configured geo-fence radius of any allowed location.
- FR-4: When the tenant's `ip_allowlist_enabled` setting is true, the system shall validate the request's source IP against the configured allowlist before allowing clock-in.
- FR-5: The system shall record the employee's IP address and user agent in the attendance log for audit purposes.
- FR-6: The system shall update the Redis cache key `att:{tenant_id}:{employee_id}:{date}` with the clock-in status for fast dashboard lookups.
- FR-7: The clock-in timestamp shall be stored in UTC; the UI shall display it converted to the employee's location timezone.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Clock-in API response time must be <= 500ms at P95.
- NFR-2: The clock-in endpoint must enforce tenant isolation via PostgreSQL RLS policies on `attendance_log`.
- NFR-3: The geolocation prompt must comply with browser security standards (HTTPS required, user consent).
- NFR-4: The clock-in action must be idempotent within a 5-second window to prevent double-click submissions.
- NFR-5: The UI must be fully responsive, supporting clock-in from mobile browsers (360px minimum width).

## 6. Business Rules
- BR-1: An employee can have at most one open (un-clocked-out) attendance record at any time.
- BR-2: Geolocation enforcement is a tenant-level configuration (`attendance_settings.require_geolocation`).
- BR-3: IP allowlist enforcement is a tenant-level configuration (`attendance_settings.ip_allowlist_enabled`).
- BR-4: If the tenant has a grace period configured (e.g., 15 minutes), clock-ins within the grace period after shift start are not marked as late.
- BR-5: Clock-in is only permitted for active employees (not terminated, not on long leave).
- BR-6: If the tenant requires a selfie photo on clock-in (`attendance_settings.require_photo`), the photo must be captured and stored before the clock-in is accepted.

## 7. Data Requirements
**Input:**
| Field | Type | Required | Notes |
|-------|------|----------|-------|
| latitude | decimal(10,7) | Conditional | Required if tenant geo policy is mandatory |
| longitude | decimal(10,7) | Conditional | Required if tenant geo policy is mandatory |
| selfie_photo | base64 string | Conditional | Required if tenant photo policy is enabled |

**Output (attendance_log record):**
| Field | Type | Notes |
|-------|------|-------|
| attendance_log_id | UUID | PK |
| tenant_id | UUID | FK, RLS-enforced |
| employee_id | UUID | FK |
| clock_in | timestamptz | UTC |
| clock_out | timestamptz | Nullable, set on clock-out |
| clock_in_latitude | decimal(10,7) | Nullable |
| clock_in_longitude | decimal(10,7) | Nullable |
| clock_in_ip | varchar(50) | Source IP |
| clock_in_user_agent | varchar(500) | Browser user agent |
| clock_in_photo_url | varchar(500) | Nullable, S3/blob path |
| source | varchar(20) | 'WEB', 'MOBILE_WEB' |
| created_at | timestamptz | Audit |
| created_by | UUID | Audit |

## 8. UI/UX Notes (Notion-like)
- The clock-in button should be prominently placed on the employee dashboard as a large, primary-action card with a clean Notion-like design.
- Use a smooth transition animation when clock-in is confirmed (e.g., button transforms from "Clock In" to a live timer showing elapsed work time).
- If geolocation is required, show a small map preview with the captured location after successful clock-in.
- On mobile, the clock-in card should be full-width and easily tappable (minimum 48px touch target).
- Use a subtle success toast notification (not a modal) to confirm clock-in.
- Display the current shift name and expected start time near the clock-in button for context.
- If IP restriction blocks clock-in, show a non-intrusive inline error with a help link.

## 9. Dependencies
- US-ATT-005 (Shift Management): Employee must have an assigned shift to determine expected clock-in time.
- Authentication module: Valid JWT with `tenant_id` and `employee_id` claims.
- Tenant Admin module: Attendance settings (geolocation, IP allowlist, photo requirement) must be configurable.
- Core HR module: Employee record must exist and be active.

## 10. Assumptions & Constraints
- The browser Geolocation API is available on all supported browsers (latest 2 versions of Chrome, Edge, Firefox, Safari).
- Geolocation accuracy depends on the device; the system accepts whatever accuracy the browser provides.
- No biometric hardware integration in Phase 1 (out of scope per technical document S3.2).
- Clock-in is browser-based only; native mobile app is out of scope for Phase 1.
- The system uses the tenant's configured timezone to determine "today" for duplicate detection.
- PostgreSQL RLS ensures that an employee in Tenant A can never see or create attendance records in Tenant B.

## 11. Test Hints
- Test clock-in with geolocation enabled and browser permission granted: verify coordinates are stored.
- Test clock-in with geolocation enabled and browser permission denied: verify clock-in is blocked.
- Test clock-in with geolocation optional and permission denied: verify clock-in succeeds without coordinates.
- Test duplicate clock-in prevention: clock in, then attempt to clock in again without clocking out.
- Test IP allowlist enforcement: mock requests from allowed and disallowed IPs.
- Test geo-fence validation: provide coordinates outside the allowed radius and verify rejection.
- Test multi-tenant isolation: create attendance records for two tenants and verify RLS prevents cross-tenant access.
- Test concurrent clock-in requests (race condition): send two simultaneous clock-in requests and verify only one record is created.
- Test on mobile viewport (360px width): verify the clock-in button is accessible and functional.
