---
id: TC-ADM-ISO-025
user_story: US-ADM-009
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-025: Plans are system-level — tenant context cannot read or write plans; cross-tenant context injection rejected

## 1. Test Objective
Verify FR-1 + BR-1 + NFR-5 + the module isolation convention: `subscription_plan` is a SYSTEM-level entity managed only in the System Admin context (`admin.yourhrm.com`). A request arriving in a resolved-TENANT context (e.g. `acme.yourhrm.com` with `X-Tenant-Subdomain: acme`) cannot list, read, create, edit, archive, or delete plans. Cross-context injection (sending a tenant subdomain to plan endpoints) does not disclose or mutate plan data.

## 2. Related Requirements
- User Story: US-ADM-009
- Functional Requirements: FR-1 (system-admin-context-only CRUD)
- Business Rules: BR-1 (SystemAdmin only)
- Non-Functional Requirements: NFR-5 (tenant admins cannot view/modify plans)

## 3. Preconditions
- System Admin "Pat" (system context); Tenant Admin "Dana" at `acme.yourhrm.com`.
- Plans `starter`, `growth` exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| system context | admin.* | plan management allowed for SystemAdmin |
| tenant context | acme.yourhrm.com + X-Tenant-Subdomain: acme | plan management denied |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Pat in SYSTEM context, GET/POST/PUT plan endpoints | Allowed (SystemAdmin) — baseline. |
| 2 | As Dana in TENANT context, GET the plans list/detail | 403/404 — plan management is not exposed in tenant context; no plan data leaks to a tenant. |
| 3 | As Dana, attempt POST create / PUT update / archive / DELETE a plan | 403/404 — rejected; no plan mutated. |
| 4 | Send a plan-management request with a tenant `X-Tenant-Subdomain` header injected | The system-context requirement is enforced — tenant context cannot reach plan management; no disclosure/mutation. |
| 5 | Confirm no side effects | Plans `starter`/`growth` unchanged after all tenant-context attempts. |

## 6. Postconditions
- Plans remain system-only; tenant context cannot read or mutate them.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
