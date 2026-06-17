---
id: TC-ADM-ISO-013
user_story: US-ADM-005
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-013: Token-revocation scoping is correct per action — deactivate/end-sessions tenant-only vs force-reset global

## 1. Test Objective
Verify the isolation boundary of the THREE session-affecting actions on a dual-tenant user, ensuring each revokes exactly the intended scope and nothing more:
- Deactivate (AC-4) and End-All-Sessions (FR-5): revoke refresh tokens for THIS tenant only — other-tenant sessions survive.
- Force Password Reset (AC-5): revoke ALL tokens across ALL tenants (global credential) — this is the deliberate cross-tenant case.
This guards against both over-revocation (deactivation bleeding into another tenant) and under-revocation (force-reset missing a tenant).

## 2. Related Requirements
- User Story: US-ADM-005
- Acceptance Criteria: AC-4, AC-5
- Functional Requirements: FR-5
- Business Rules: BR-1

## 3. Preconditions
- User "Sam" has active memberships and live refresh tokens in BOTH Acme (A) and Beta (B).
- Tenant Admin "Dana" acts from Acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| user | sam | dual-tenant |
| acting tenant | Acme | A |
| neighbor | Beta | B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, End-All-Sessions for Sam | Acme tokens revoked; Beta tokens STILL VALID; Sam's Acme membership still `active`. |
| 2 | Restore a fresh Acme session for Sam; then Deactivate Sam in Acme | Acme tokens revoked + membership `disabled`; Beta tokens STILL VALID + Beta membership `active`. |
| 3 | Restore fresh sessions both sides; then Force Password Reset for Sam | BOTH Acme AND Beta tokens revoked; `password_changed_at` NULL — global scope. |
| 4 | After each step, attempt to use Beta tokens | Steps 1-2: Beta session works (tenant-only revoke). Step 3: Beta session rejected (global revoke). |
| 5 | Confirm Beta membership status throughout | Beta membership never becomes `disabled` from an Acme action (no over-reach). |

## 6. Postconditions
- Revocation scope matches the action's intent exactly: tenant-scoped for deactivate/end-sessions, global for force-reset. No cross-tenant over- or under-revocation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
