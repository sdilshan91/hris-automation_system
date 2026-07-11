---
id: TC-ATT-136
user_story: US-ATT-010
module: Attendance
priority: high
type: integration
status: pass
created: 2026-06-15
---

# TC-ATT-136: Scheduled report config CRUD + Hangfire generation (daily/weekly/monthly); EMAIL delivery DEFERRED on US-NTF; BR-6 recipient timezone

## 1. Test Objective
Verify scheduled-report configuration and generation (FR-8, BR-6): HR can create / read / update / delete a `scheduled_report_config` (report type, frequency, filters, recipients, delivery time, format, active flag), and the Hangfire recurring job auto-generates the configured report on schedule, tenant-scoped, with delivery timing respecting the recipient's timezone. The actual EMAIL DELIVERY is DEFERRED on the Notification System (US-NTF) -- the generate + queue/dispatch SEAM is verified now.

## 2. Related Requirements
- User Story: US-ATT-010
- Functional Requirements: FR-8 (scheduled report delivery: auto-generated + emailed daily/weekly/monthly via Hangfire)
- Non-Functional: NFR-6 (Hangfire processes scheduled reports off-peak without impacting production)
- Business Rules: BR-6 (scheduled reports respect the recipient's timezone for delivery timing)
- Data: §7 scheduled_report_config (config_id, tenant_id, report_type, frequency, filters jsonb, recipients[], delivery_time, format, is_active, created_by, audit)
- API: GET/POST/PUT/DELETE /api/v1/attendance/reports/scheduled

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" authenticated with `Reports.View.All`.
- Recipients with known timezones (one in Asia/Colombo, one in Europe/London).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| report_type | MONTHLY_SUMMARY | pre-built type |
| frequency | MONTHLY | DAILY/WEEKLY/MONTHLY |
| filters | {departmentId: Engineering} | jsonb |
| recipients | [Priya, Ravi] | user IDs |
| delivery_time | 08:00 | recipient-local (BR-6) |
| format | XLSX | CSV/XLSX/PDF |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /reports/scheduled` with the config body | 201; a scheduled_report_config row created with tenant_id stamped (TenantInterceptor), created_by = Priya, is_active = true, the filters persisted as jsonb. |
| 2 | `GET /reports/scheduled` | the new config is listed for the tenant; other tenants' configs are not (isolation in TC-ATT-ISO-013). |
| 3 | `PUT /reports/scheduled/{id}` changing frequency MONTHLY -> WEEKLY and toggling is_active | the config updates; the Hangfire recurring-job registration is updated to the new cadence (an inactive config does not fire). |
| 4 | `DELETE /reports/scheduled/{id}` | the config is removed and its recurring job de-registered. |
| 5 | Trigger the Hangfire generation job for an active config | the report is GENERATED for the configured type/period/filters/format, tenant context injected into the job (per §10) -- the output matches the same report run interactively. |
| 6 | Verify the delivery SEAM | on generation, a delivery message is QUEUED to each recipient referencing the generated report + format -- recipient/payload/tenant-scope verified; the in-app/email DELIVERY is DEFERRED on US-NTF. **Reported to caller.** |
| 7 | BR-6 recipient timezone | the scheduled fire/delivery time is computed in the recipient's timezone (08:00 Asia/Colombo for Priya, 08:00 Europe/London for Ravi are distinct UTC instants) -- the timezone is honored, not a single server-local time. |
| 8 | Validation | invalid frequency / empty recipients / unknown report_type / bad delivery_time rejected with 400. |

## 6. Postconditions
- Scheduled-report configs are CRUD-able and tenant-scoped; the Hangfire job generates the report on schedule with a queued delivery seam; no cross-tenant config visible.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Performance test
- [ ] Multi-tenant isolation
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Email delivery (FR-8) DEFERRED on US-NTF:** the Notification System is not built. This TC verifies the config CRUD, the Hangfire GENERATE step (tenant-scoped, off-peak per NFR-6), and the delivery SEAM (recipient/payload/tenant-scope); the in-app/email dispatch + the generated-file persistence (blob) are DEFERRED -- consistent with US-ATT-007 TC-ATT-095 large-export delivery and the module's notification-seam precedent. **Reported to caller.**
- BR-6 recipient-timezone delivery depends on a per-user timezone being available (Core HR / user profile); the timezone-honoring logic is verified, the source CONDITIONAL on that field existing (mirrors the module-wide tenant-timezone deferral). **Reported to caller.**
- Authz (HR-only config management) in TC-ATT-140; tenant isolation of the config table in TC-ATT-ISO-013; the scheduled-report setup form a11y in TC-ATT-141.
