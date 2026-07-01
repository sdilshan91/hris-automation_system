---
id: TC-NTF-ISO-008
user_story: US-NTF-002
module: Notifications & Audit
priority: high
type: security
status: fail
created: 2026-06-17
exec_note: "2026-06-30 API iso-fixture probe: template SELECTION is context-scoped (isoa's custom 'leave_approved' subject is INVISIBLE under the isob header -> isob sees the default, isCustom:false) — the selection logic itself respects the resolved tenant. BUT the resolved tenant is the spoofable X-Tenant-Subdomain header: isoa-admin token + isob header PUT customized ISOB's template (isob now shows 'XLEAK-INTO-ISOB', isCustom:true). 'Strictly within the recipient's tenant' is breached at the resolution layer. BUG-003 / ISSUE-189-191 write-leak class."
---

# TC-NTF-ISO-008: Email render/send pipeline selects templates strictly within the recipient's tenant

## 1. Test Objective
Verify that the send-time template-resolution pipeline (running in the Hangfire outbox worker) selects
the override/default strictly within the recipient's tenant, so an email for a Tenant B recipient can
never be rendered from a Tenant A template even though both tenants share the same event_key.

## 2. Related Requirements
- User Story: US-NTF-002
- Acceptance Criteria: AC-5 (Tenant A customization never used for Tenant B)
- Functional Requirements: FR-2 (placeholder resolution), FR-6 (fall back to default), FR-10
- Non-Functional: NFR-2 (tenant isolation)

## 3. Preconditions
- Tenant A has a custom "Leave Approved" override; Tenant B has no override (default).
- Pending leave-approval events exist for both `empA` (Tenant A) and `empB` (Tenant B).
- The outbox/Hangfire email worker is running.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| event_key | leave_approved | shared key across tenants |
| Tenant A custom subject | "Acme Corp: leave approved" | |
| Tenant B expected | system default subject | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Approve `empA`'s leave (Tenant A) | The worker resolves the Tenant A override; email subject = "Acme Corp: leave approved" |
| 2 | Approve `empB`'s leave (Tenant B) | The worker resolves Tenant B's template; email = system default subject, NOT Tenant A's custom subject |
| 3 | Process both events interleaved/concurrently | Each email is rendered against its OWN tenant's template; no cross-tenant template selection under concurrency |
| 4 | Inspect the worker's template lookup | Lookup is scoped by the recipient's tenant_id (event payload / tenant-scoped context), not a global query |
| 5 | Verify no leakage of tenant-specific data | Tenant A branding/placeholders (e.g. logoUrl, companyName) never appear in Tenant B's email |

## 6. Postconditions
- Every email is rendered from its own tenant's template; no cross-tenant render leakage.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
