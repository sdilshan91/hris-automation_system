---
id: TC-PRF-ISO-002
user_story: US-PRF-001
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-002: Goal APIs reject missing/invalid/mismatched tenant context and block cross-tenant IDOR (NFR-2)

## 1. Test Objective
Verify NFR-2: every goal endpoint requires a valid resolved tenant context. Requests with no tenant context, an invalid/unknown subdomain, or a tenant context that mismatches the JWT's tenant are rejected. Cross-tenant IDOR (operating on another tenant's goal by ID) is blocked.

## 2. Related Requirements
- User Story: US-PRF-001
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1

## 3. Preconditions
- acme has Asha's goal set (goal IDs known); globex exists with its own manager.
- A valid manager JWT for acme is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| No tenant | request without subdomain/X-Tenant-Subdomain | resolution fails |
| Invalid tenant | subdomain `doesnotexist` | unknown tenant |
| Mismatched | globex subdomain + acme JWT | context/JWT tenant mismatch |
| IDOR | globex JWT + acme goal id | cross-tenant object reference |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET/POST .../performance/goals` with a valid JWT but NO resolvable tenant context | Rejected (401/400) — no tenant resolved; the request never reads/writes goals. |
| 2 | Same with an invalid subdomain `doesnotexist.yourhrm.com` | Rejected — unknown tenant; no data returned. |
| 3 | Present an acme-issued JWT against the globex tenant context (mismatch) | Rejected (401/403) — tenant context must match the JWT tenant; no cross-tenant access. |
| 4 | As a globex-authenticated user, `GET/PUT/DELETE .../goals/{acme_goal_id}` (IDOR) | 404/403 — the acme goal is not visible/operable from globex; no read, edit, or delete occurs. |
| 5 | Verify persistence | acme's goals are unchanged after every rejected attempt. |

## 6. Postconditions
- All goal endpoints fail closed without valid, matching tenant context; cross-tenant IDOR is blocked.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
