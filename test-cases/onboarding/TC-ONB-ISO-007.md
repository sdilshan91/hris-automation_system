---
id: TC-ONB-ISO-007
user_story: US-ONB-002
module: Onboarding / Offboarding
priority: critical
type: security
status: fail
created: 2026-06-17
---

# TC-ONB-ISO-007: EF query filter blocks cross-tenant reads; writes (incl. outbox) tenant-stamped (RLS deferred)

## 1. Test Objective
Verify NFR-2 / NFR-3 at the persistence layer: the EF Core global query filter prevents an assignment query in one tenant context from returning another tenant's checklist/task instances, and the `TenantInterceptor` auto-stamps `tenant_id` on every new checklist instance, task instance, and notification outbox row from the session context (never from user input). Responsible-party resolution is also confined to the assigning tenant.

## 2. Related Requirements
- User Story: US-ONB-002
- Acceptance Criteria: AC-2, AC-5
- Functional Requirements: FR-3, FR-7
- Non-Functional Requirements: NFR-2, NFR-3
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: Isolation is enforced by EF global query filters (read) + `TenantInterceptor` (write stamping). The story's RLS expectation ("raw SQL without app.current_tenant_id returns zero rows") is CONDITIONAL/deferred — step 4 documents it as a future hardening assertion, not a gate for today. STORY MISMATCH to flag: NFR-2 names Postgres RLS; only the app + EF layers exist now.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) exist; each has an active template and employees.
- Test access to the persistence layer with a scoped `ITenantContext`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Entities | onboarding_checklist_instance, onboarding_task_instance, notification outbox | tenant-scoped |
| A context | acme TenantId | drives query filter + stamping |
| B context | globex TenantId | control |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With the acme tenant context, query checklist/task instances | Only acme rows returned; globex rows excluded by the global query filter (NFR-2). |
| 2 | Assign a checklist under the acme context | New checklist instance, task instances, and notification outbox rows are all stamped `tenant_id` = acme by `TenantInterceptor` (FR-7, NFR-3); no `tenant_id` is read from request input. |
| 3 | Confirm responsible-party resolution scope | Manager/IT/HR/Employee resolution selects only acme users; no globex user can be resolved (FR-3). |
| 4 | (CONDITIONAL — RLS deferred) Run raw SQL against the tables without setting a tenant GUC | Documented expectation under future Postgres RLS: zero rows without `app.current_tenant_id`. Today RLS is not enabled — record as deferred; do NOT fail the suite on its absence (flag to caller). |
| 5 | Attempt to override `tenant_id` via the request payload | The supplied value is ignored; the interceptor stamps the session tenant (FR-7). |

## 6. Postconditions
- Reads are tenant-filtered; all writes (including outbox) carry the session tenant; RLS remains a documented deferred hardening step.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
