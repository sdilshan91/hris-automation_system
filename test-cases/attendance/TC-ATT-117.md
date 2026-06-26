---
id: TC-ATT-117
user_story: US-ATT-008
module: Attendance
priority: critical
type: security
status: pass
created: 2026-06-14
---

# TC-ATT-117: AuthN/AuthZ on late/early endpoints -- late-policy is HR-only, report scope enforced by role, my-score self-scoped; unauthenticated/unauthorized requests rejected; input sanitised

## 1. Test Objective
Verify authentication and role-based authorization across the US-ATT-008 endpoints: late_policy GET/PUT is HR-only (employees/managers denied write); the late/early report enforces team scope for managers and all scope for HR (a manager cannot read non-team employees); my-score is self-scoped (no reading another employee's score); unauthenticated requests are rejected (401); and report filter inputs (departmentId, employeeId, date params) are sanitised against injection.

## 2. Related Requirements
- User Story: US-ATT-008
- Functional Requirements: FR-4 (policy config -- HR), FR-5 (my-score -- self), FR-6 (report -- team vs all)
- Non-Functional: NFR-2 (tenant/data isolation -- companion to TC-ATT-ISO-011)
- APIs: GET/PUT /late-policy; GET /late-early/report; GET /late-early/my-score

## 3. Preconditions
- Tenant "acme"; HR "Priya" (Attendance.Read.All + policy-manage); manager "Mark" (team scope); employee "Asha" (self only); an unauthenticated client.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| HR principal | Priya | full report + policy write |
| Manager principal | Mark | team report only |
| Employee principal | Asha | my-score only |
| injection probes | `'; DROP TABLE`, `<script>`, oversized employeeId | filter sanitisation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Unauthenticated `GET /late-early/report` and `GET /late-policy` and `PUT /late-policy` | 401 Unauthorized for all; no data returned or mutated. |
| 2 | As Asha (employee), `PUT /late-policy` | 403 Forbidden -- policy management is HR-only (FR-4). |
| 3 | As Mark (manager), `PUT /late-policy` | 403 Forbidden -- managers cannot edit the tenant late policy. |
| 4 | As Mark, `GET /late-early/report?scope=all` or targeting a non-team employeeId | Restricted to his team (403 or coerced to team) -- a manager cannot read non-team late/early data (FR-6). |
| 5 | As Asha, `GET /late-early/my-score?month=...` for ANOTHER employee's id | The endpoint ignores the supplied id and returns Asha's own score (self-scope); no other employee's score is exposed. |
| 6 | As Priya (HR), all three endpoints | 200 -- full report (all scope), policy read/write, and (HR self) my-score succeed within tenant acme. |
| 7 | Inject SQL/XSS payloads into report filters (departmentId/employeeId/from/to) | Inputs are parameterised/validated -- no SQL error, no script reflection; invalid ids yield empty/400, not data leakage. |
| 8 | Audit | Policy updates (PUT) are recorded in the audit log with actor + timestamp + before/after (consistent with module audit practice). |

## 6. Postconditions
- Only authorized roles access each endpoint within their scope; unauthenticated/unauthorized requests are rejected; filter inputs are sanitised; policy changes are audited. No unintended state change.

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
- The exact permission strings (e.g. `Attendance.Read.All`, an attendance-policy-manage permission) should be confirmed against the PermissionCatalog -- prior ATT stories added concrete strings rather than wildcards (US-ATT-005 TC-ATT-063, US-ATT-007 TC-ATT-098). **Reported to caller** to confirm the policy-manage permission name.
- Cross-TENANT isolation (Tenant A vs Tenant B late policy/records) is covered by TC-ATT-ISO-011; this TC covers intra-tenant role/scope authorization.
