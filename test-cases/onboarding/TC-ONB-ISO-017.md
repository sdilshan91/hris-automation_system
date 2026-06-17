---
id: TC-ONB-ISO-017
user_story: US-ONB-005
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-017: Missing/invalid tenant context + cross-tenant offboarding ID injection -> 404

## 1. Test Objective
Verify AC-6 and FR-8: requests without a resolvable tenant context are rejected, and a caller in Tenant B who injects a known Tenant A offboarding-instance / clearance-task ID receives 404 (existence not disclosed), never 403 and never the record.

## 2. Related Requirements
- User Story: US-ONB-005
- Acceptance Criteria: AC-6
- Functional Requirements: FR-8 (tenant_id from session context)
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: Cross-tenant ID injection returns 404 (not 403) so the existence of another tenant's record is not disclosed — consistent with TC-ONB-ISO-002/006/009/013. The EF global query filter makes the row invisible to the other tenant, so the lookup resolves to "not found".

## 3. Preconditions
- Tenant A has offboarding instance OB-A300 (id known to the tester) and clearance task TK-A1.
- Tenant B HR Officer authenticated; tenant resolved from subdomain/header.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| injected instance id | OB-A300 | belongs to T-acme |
| injected task id | TK-A1 | belongs to T-acme |
| no-tenant request | (subdomain/header stripped) | unresolved tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send a request with no resolvable tenant context (no subdomain / no X-Tenant-Subdomain) | Rejected — tenant cannot be resolved; no offboarding data returned. |
| 2 | As the globex HR Officer, GET the acme instance by id OB-A300 | 404 Not Found — not 403, no record body (AC-6). |
| 3 | As globex, PATCH/approve the acme clearance task TK-A1 by id | 404 Not Found; acme task status unchanged (AC-6, FR-8). |
| 4 | As globex, attempt "Complete Offboarding" on OB-A300 by id | 404 Not Found; no termination/deactivation on the acme employee. |
| 5 | Verify acme-side state | OB-A300 and TK-A1 are unchanged. |

## 6. Postconditions
- No tenant context => no data; cross-tenant ID injection yields 404 with zero side effects.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
