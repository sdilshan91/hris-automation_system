---
id: US-LV-006
module: Leave Management
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-LV-006: Leave Balance Dashboard for Employee

## 1. Description
**As an** Employee,
**I want to** view a dashboard showing my current leave balances for all leave types, along with my leave history and upcoming leaves,
**So that** I can plan my time off and understand how much leave I have available.

## 2. Preconditions
- Employee is authenticated and has an active employee record.
- Leave types and entitlements have been configured for the tenant.
- Leave balances have been computed (via accrual or upfront allocation).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | Employee navigates to the Leave Dashboard | The page loads | A summary card for each active leave type is displayed showing: entitlement, used, pending, balance remaining, and a visual progress bar |
| AC-2 | Employee clicks on a leave type balance card | The detail view opens | A ledger/transaction history is shown with all accruals, usages, adjustments, carry-forwards, and expirations for the current leave year |
| AC-3 | Employee views the dashboard | They check the "Upcoming Leaves" section | All approved and pending future leave requests are listed with dates, type, status, and days |
| AC-4 | Employee views the dashboard on a mobile device (360px) | The page renders | All balance cards stack vertically, remain readable, and the progress bars scale correctly |
| AC-5 | Employee has no leave balance data (new joiner, no accrual yet) | They open the dashboard | A friendly empty state is shown: "Your leave balances are being set up. Please check back soon." with an illustration |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: API endpoint: `GET /api/v1/leaves/my-balance` — returns all leave type balances for the authenticated employee within their tenant.
- FR-2: Response per leave type: `leaveTypeId`, `leaveTypeName`, `color`, `entitlement`, `used`, `pending`, `balance`, `carryForward`, `expired`.
- FR-3: API endpoint: `GET /api/v1/leaves/my-ledger?leaveTypeId={id}&year={year}` — returns the full transaction log.
- FR-4: API endpoint: `GET /api/v1/leaves/my-upcoming` — returns approved and pending future leaves.
- FR-5: Balance values sourced from Redis cache (`tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`); fallback to computed from `leave_ledger` on cache miss.
- FR-6: Leave history section with filterable list of past leave requests (approved, rejected, cancelled).

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Balance API must respond within 200ms (P95) using Redis cache.
- NFR-2: Dashboard page must achieve Largest Contentful Paint (LCP) under 2.5 seconds.
- NFR-3: All data tenant-isolated via EF Core filters + PostgreSQL RLS.
- NFR-4: Accessible (WCAG 2.1 AA): progress bars have aria-labels, color is not the sole indicator (text values always visible).

## 6. Business Rules
- BR-1: Balance = Entitlement + Carry Forward - Used - Expired + Adjustments.
- BR-2: "Pending" days are shown separately and not deducted from "balance" until approved.
- BR-3: Only active leave types are shown; deactivated types with a remaining balance are shown in a collapsed "Archived" section.
- BR-4: Leave year boundaries are tenant-configurable (calendar year or fiscal year).
- BR-5: Employee can view balances for previous leave years (read-only, via year selector).

## 7. Data Requirements
- **Source table:** `leave_ledger` — aggregated by `employee_id`, `leave_type_id`, `transaction_type` for the selected leave year.
- **Cache:** Redis hash `tenant:{tenantId}:leave_balance:{employeeId}` with fields per leave type.
- **Leave history:** Query from `leave_request` table filtered by employee and leave year.

## 8. UI/UX Notes (Notion-like)
- Dashboard layout: Grid of balance cards (2-3 per row on desktop, 1 per row on mobile).
- Each card: Leave type name with color accent, circular/arc progress indicator, numeric values (entitlement / used / remaining).
- Upcoming leaves: Clean timeline-style list with date chips and status badges.
- Ledger view: Table with alternating row colors, transaction type badges (green for accrual, red for used, blue for adjustment).
- Year selector as a discrete pill-group at the top (e.g., [2024] [2025] [2026]).
- Empty state: Centered illustration with friendly message, consistent with Notion's empty-state patterns.
- Smooth loading skeleton animations while data fetches.

## 9. Dependencies
- **US-LV-001**: Leave types must be configured.
- **US-LV-002**: Entitlements and balances must be computed.
- **US-LV-003**: Leave requests must be submittable (for history and upcoming data).
- **Redis**: For cached balance data.

## 10. Assumptions & Constraints
- The dashboard is the default landing view within the Leave module for the Employee persona.
- Balance computation is eventually consistent (small delay between approval and cache update is acceptable).
- Only free/open-source charting libraries may be used for progress indicators (e.g., ngx-charts or custom SVG).

## 11. Test Hints
- Test balance calculation: Verify the displayed balance matches: entitlement + carry_forward - used - expired + adjustments.
- Test pending display: Submit a leave request; verify "pending" count increases but "balance" does not decrease until approval.
- Test year selector: Switch to previous year; verify balances reflect that year's data.
- Test empty state: Create a new employee with no ledger entries; verify the empty state message renders.
- Test mobile responsiveness: Open dashboard on 360px viewport; verify cards stack and are readable.
- Test tenant isolation: Employee in Tenant A must see only their own balance data.
- Test cache fallback: Clear Redis cache for an employee; verify balance is computed from DB and re-cached.
