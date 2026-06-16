---
id: TC-PRF-ISO-030
user_story: US-PRF-008
module: Performance Management
priority: critical
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-030: PIP APIs reject missing/invalid/mismatched tenant context + block cross-tenant IDOR (view/checkpoint/extend/outcome/escalation/report) (NFR-2)

## 1. Test Objective
Verify NFR-2: every PIP API endpoint requires a valid, resolved tenant context and rejects requests with no tenant context, an invalid/unknown subdomain, or a tenant context that mismatches the authenticated user's tenant. Additionally, a caller authenticated in Tenant B cannot act on a Tenant A PIP by id (cross-tenant IDOR) on any operation -- view, record-checkpoint, extend, set-outcome, confirm-escalation, acknowledge, or export-report.

## 2. Related Requirements
- User Story: US-PRF-008
- Non-Functional Requirements: NFR-2
- Acceptance Criteria: AC-2, AC-3, AC-4, AC-5 (the operations gated)
- Functional Requirements: FR-4, FR-5, FR-7, FR-8

## 3. Preconditions
- Tenant "acme" has a PIP for Sam Lee with a known pipId/checkpointId.
- Tenant "globex" has an HR Officer with `Performance.Review.All`, authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme PIP | Sam Lee's PIP | target of IDOR probes |
| globex HR | authenticated | acting cross-tenant |
| Tenant header probes | none / invalid / mismatched | resolution checks |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call a PIP endpoint with NO tenant context (no subdomain / no `X-Tenant-Subdomain`) | Rejected -- request fails tenant resolution; no PIP data returned or mutated. |
| 2 | Call with an INVALID/unknown subdomain | Rejected -- tenant does not resolve; 400/404; no data. |
| 3 | Authenticate as globex HR but send a MISMATCHED acme tenant context | Rejected -- the resolved tenant must match the JWT tenant; mismatch is denied (no privilege escalation across tenants). |
| 4 | As globex HR (valid globex context), `GET .../performance/pips/{acme_pipId}` | 403 / 404 -- cross-tenant IDOR read blocked. |
| 5 | As globex HR, attempt write IDOR: record-checkpoint / extend / set-outcome / confirm-escalation / acknowledge / export-report against acme's pipId | Each returns 403 / 404 -- no acme PIP is read or mutated by a globex caller (cross-tenant IDOR on every mutation). |
| 6 | Confirm no side effects | No acme PIP/checkpoint/escalation/history row is created, modified, or leaked by any of the above attempts. |

## 6. Postconditions
- PIP APIs require a valid, matching tenant context and block cross-tenant IDOR on read and all mutations. No cross-tenant leakage or mutation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
