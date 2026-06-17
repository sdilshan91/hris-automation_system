---
id: TC-ONB-ISO-010
user_story: US-ONB-003
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-010: EF query filter blocks reads; completions/uploads/outbox tenant-stamped (RLS deferred)

## 1. Test Objective
Verify NFR-2 at the persistence layer: the EF Core global query filter prevents a task/completion query in one tenant context from returning another tenant's onboarding rows, and the `TenantInterceptor` auto-stamps `tenant_id` on every new completion record, attachment metadata row, and overdue/HR notification outbox row from the session context (never from user input).

## 2. Related Requirements
- User Story: US-ONB-003
- Acceptance Criteria: AC-3, AC-5
- Functional Requirements: FR-7, FR-8
- Non-Functional Requirements: NFR-2
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: Isolation is enforced by EF global query filters (read) + `TenantInterceptor` (write stamping). The story's RLS expectation ("raw SQL without app.current_tenant_id -> zero rows") is CONDITIONAL/deferred — step 4 documents it as future hardening, not a gate for today. STORY MISMATCH to flag: NFR-2 names Postgres RLS; only the app (ITenantContext) + EF layers exist now.

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) exist; each has task instances and completion/attachment/outbox data.
- Test access to the persistence layer with a scoped `ITenantContext`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| entities | task instance, completion record, attachment metadata, notification outbox | tenant-scoped |
| A context | acme TenantId | drives query filter + stamping |
| B context | globex TenantId | control |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With the acme context, query task/completion rows | Only acme rows returned; globex rows excluded by the global query filter (NFR-2). |
| 2 | Complete a task + upload a document under the acme context | New completion record, attachment metadata, and HR notification outbox row are all stamped `tenant_id`=acme by `TenantInterceptor` (FR-7); no tenant_id read from request input. |
| 3 | Run the overdue Hangfire job under the acme context | Overdue notification outbox rows are stamped acme; never matched against or written for globex tasks. |
| 4 | (CONDITIONAL — RLS deferred) Run raw SQL against the tables without a tenant GUC | Documented future-RLS expectation: zero rows without `app.current_tenant_id`. RLS not enabled today — record as deferred; do NOT fail the suite on its absence (flag to caller). |
| 5 | Attempt to override `tenant_id` via the completion payload | Supplied value ignored; interceptor stamps the session tenant (FR-7). |

## 6. Postconditions
- Reads are tenant-filtered; all writes (completions, attachments, outbox) carry the session tenant; RLS remains a documented deferred hardening step.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
