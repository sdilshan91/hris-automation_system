---
id: TC-AUTH-121
user_story: US-AUTH-012
module: Authentication
priority: medium
type: performance
status: pass
created: 2026-07-24
---

# TC-AUTH-121: Boundary -- 20 Entra `tid`s and 20 email domains accepted and persisted without degradation

## 1. Test Objective
Verify NFR-4: the `allowed_entra_tenant_ids` and `allowed_email_domains` list fields each support at least 20 entries without performance degradation or truncation. A save with 20 valid GUIDs and 20 valid domains persists all 40 entries and reads them back intact, within the write SLA.

## 2. Related Requirements
- User Story: US-AUTH-012
- Non-Functional Requirements: NFR-4
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" plan has `Sso = true`; `admin-a@acme.com` is a tenant admin.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| allowed_entra_tenant_ids | 20 distinct well-formed GUIDs | Boundary count (CR-AUTH-001 multi-directory) |
| allowed_email_domains | 20 distinct RFC-valid domains | Boundary count |
| Write SLA (P95) | <= 800ms | Platform write SLA |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Build a payload with 20 distinct valid `tid` GUIDs and 20 distinct valid domains; `PUT /api/v1/tenant/auth-settings`. | HTTP 200 OK within the write SLA (P95 <= 800ms); no truncation error. |
| 2 | Re-read `GET /api/v1/tenant/auth-settings`. | All 20 `tid`s and all 20 domains are returned, order/content intact, none dropped. |
| 3 | Re-open the SSO card in the UI. | The card renders all 40 chips without visible lag or layout breakage. |
| 4 | Add a 21st valid `tid` and save (probe just past the stated floor). | Save succeeds (NFR-4 is a minimum of 20, not a hard cap) OR a clear, documented limit is enforced -- no silent truncation. |

## 6. Postconditions
- Large allow-lists persist and round-trip intact; no data loss at the 20-entry boundary.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
