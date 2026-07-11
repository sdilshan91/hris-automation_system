---
id: TC-ATT-127
user_story: US-ATT-009
module: Attendance
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-ATT-127: AuthN/AuthZ on payroll-integration endpoints -- payroll-data/period-lock/unlock/reconciliation are HR-only, unauthenticated rejected, inputs sanitised, lock/unlock audited

## 1. Test Objective
Verify authentication and role-based authorization across the US-ATT-009 endpoints: the payroll-data pull, the period-lock create, the unlock, and the reconciliation view are HR-Officer-only (regular employees and managers are denied); unauthenticated requests are rejected (401); request inputs (month, employeeIds, periodStart/periodEnd, lock id) are validated/sanitised against injection; and lock/unlock actions are recorded in the audit log (FR-4).

## 2. Related Requirements
- User Story: US-ATT-009
- Functional Requirements: FR-1 (HR-initiated payroll pull), FR-3 (HR Attendance Lock), FR-4 (audit lock/unlock)
- Non-Functional: NFR-2/NFR-3 (isolation -- companion to TC-ATT-ISO-012)
- APIs: GET /payroll-data; GET/POST /period-lock; POST /period-lock/{id}/unlock; GET /reconciliation

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" (payroll-integration permission); manager "Mark"; employee "Asha"; an unauthenticated client.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| HR principal | Priya | full access |
| Manager principal | Mark | denied |
| Employee principal | Asha | denied |
| injection probes | `'; DROP TABLE`, `<script>`, oversized employeeIds, malformed month | input sanitisation |
| malformed period | periodEnd < periodStart, non-date | lock validation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Unauthenticated `GET /payroll-data`, `GET/POST /period-lock`, `POST /period-lock/{id}/unlock`, `GET /reconciliation` | 401 Unauthorized for all; no data returned or mutated. |
| 2 | As Asha (employee), call each of the four endpoints | 403 Forbidden -- payroll integration is HR-only (FR-1/FR-3). |
| 3 | As Mark (manager), `POST /period-lock` and `GET /payroll-data` | 403 Forbidden -- managers cannot lock periods or pull payroll data. |
| 4 | As Priya (HR), all endpoints | 200/201 within tenant acme -- pull, lock, unlock, reconciliation succeed. |
| 5 | Inject SQL/XSS payloads into month / employeeIds / period filters | Inputs parameterised/validated -- no SQL error, no script reflection; invalid values yield 400, not data leakage. |
| 6 | `POST /period-lock` with periodEnd < periodStart or non-date values | 400 validation error; no lock row created. |
| 7 | Verify audit | LOCK and UNLOCK actions recorded with actor (Priya) + timestamp + period (FR-4); entries immutable (consistent with module audit practice). |
| 8 | Attempt to unlock a lock_id belonging to a different tenant (id-guessing) | Not found / forbidden -- no cross-tenant lock manipulation (full isolation in TC-ATT-ISO-012). |

## 6. Postconditions
- Only HR accesses the payroll-integration endpoints within tenant acme; unauthenticated/unauthorized requests rejected; inputs sanitised; lock/unlock audited. No unintended state change.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The exact permission string for payroll-integration (e.g. an `Attendance.Payroll.Manage` / period-lock permission) should be confirmed against the PermissionCatalog -- prior ATT stories added concrete strings, not wildcards (US-ATT-005 TC-ATT-063, US-ATT-007 TC-ATT-098, US-ATT-008 TC-ATT-117). **Reported to caller** to confirm the permission name.
- Cross-TENANT isolation (Tenant A vs Tenant B payroll-data/lock) is covered by TC-ATT-ISO-012; this TC covers intra-tenant role/authn authorization.
