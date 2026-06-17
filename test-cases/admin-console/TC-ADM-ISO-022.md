---
id: TC-ADM-ISO-022
user_story: US-ADM-008
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-022: Cross-tenant audit_id injection on detail/export -> 404 not 403

## 1. Test Objective
Verify AC-1 / BR-3: supplying another tenant's `audit_id` to the detail endpoint (IDOR attempt) returns 404 — not 403 — so existence is not disclosed (the EF query filter scopes the foreign row out before authorization can confirm it exists). The export filter parameters likewise cannot be manipulated to pull another tenant's rows.

## 2. Related Requirements
- User Story: US-ADM-008
- Acceptance Criteria: AC-1, AC-3 (detail scoped), AC-4 (export scoped)
- Business Rules: BR-3 (strict tenant scope)

## 3. Preconditions
- Tenant Alpha audit row A-aud-1; Tenant Beta audit row B-aud-1.
- Dana is `TenantAdmin` of Alpha.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| foreign audit_id | B-aud-1 (Beta) | injected by Dana |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana (Alpha), GET audit detail for B-aud-1 | 404 Not Found (existence not disclosed) — NOT 403. |
| 2 | As Dana, attempt export with a body/param referencing Beta's tenant or a Beta audit_id | The export returns only Alpha rows; Beta data never appears. |
| 3 | Confirm own-tenant still works | GET detail for A-aud-1 returns 200. |
| 4 | Note RLS deferral | DB-layer RLS is deferred (TC-ADM-ISO-024); isolation today is app + EF query filter. |

## 6. Postconditions
- No cross-tenant disclosure; no state change.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
