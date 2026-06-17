---
id: TC-ONB-ISO-022
user_story: US-ONB-006
module: Onboarding / Offboarding
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ONB-ISO-022: EF query filter blocks reads; interview writes/versions/outbox/audit tenant-stamped (RLS deferred)

## 1. Test Objective
Verify AC-5, FR-6 and NFR-2: every exit interview write (record, edit/version, self-service submission, HR-notify outbox intent, and audit/PII-access entries) is auto-stamped with the session `TenantId` by the `TenantInterceptor`, and reads are constrained by the EF Core global query filter. The story's PostgreSQL RLS layer (NFR-2) is a deferred platform extension; this test asserts the EF mechanism in force today.

## 2. Related Requirements
- User Story: US-ONB-006
- Acceptance Criteria: AC-5
- Functional Requirement: FR-6 (tenant_id from session)
- Non-Functional Requirement: NFR-2 (EF query filters + TenantInterceptor; RLS deferred)

## 3. Preconditions
- Two tenants `acme` (T-acme) and `globex` (T-globex) each with offboarding instances and templates.
- Ability to inspect persisted rows (interview, version, outbox intent, audit) and their `tenant_id`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme caller | acme HR Officer | T-acme |
| globex caller | globex HR Officer | T-globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme, record an exit interview, create an edit version, and (as the employee) submit a self-service interview | All resulting rows — interview, version, self-service record — carry tenant_id = T-acme stamped by the interceptor (FR-6). |
| 2 | Inspect the HR-notify outbox intent and the exit-interview-completed / PII-access audit entries | All are tenant-stamped T-acme. |
| 3 | As globex, list exit interviews and analytics | EF query filter returns only globex rows; acme rows are excluded (NFR-2, AC-5). |
| 4 | (CONDITIONAL / DEFERRED) Run a raw SQL read without the app tenant GUC set | RLS expectation ("zero rows without app.current_tenant_id") is DEFERRED — Postgres RLS is not yet wired; isolation today is enforced by the EF query filter. Flag to caller. |

## 6. Postconditions
- All exit interview writes are tenant-stamped; reads are EF-filtered per tenant; RLS remains a documented future hardening step.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
