---
id: TC-ONB-ISO-018
user_story: US-ONB-005
module: Onboarding / Offboarding
priority: critical
type: security
status: fail
created: 2026-06-17
---

# TC-ONB-ISO-018: EF query filter blocks reads; offboarding writes + clearance updates + audit tenant-stamped (RLS deferred)

## 1. Test Objective
Verify AC-6, FR-8 and NFR-2: every offboarding write (instance, exit tasks, clearance decisions, completion side-effects, audit) is auto-stamped with the session `TenantId` by the `TenantInterceptor`, and reads are constrained by the EF global query filter. Client-supplied tenant_id is ignored (session wins).

## 2. Related Requirements
- User Story: US-ONB-005
- Acceptance Criteria: AC-6
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-2

> PLATFORM NOTE: AC-6/NFR-2 name PostgreSQL RLS as a defense-in-depth layer. This codebase enforces isolation via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**; Postgres RLS is a DEFERRED platform extension (same family as Auth/Leave/Payroll/Admin and prior ONB stories). Step 4's "raw SQL without app.current_tenant_id -> zero rows" RLS expectation is CONDITIONAL/deferred and flagged to the caller; the EF mechanism is what is asserted as in force today.

## 3. Preconditions
- Tenant A (`acme`) HR Officer authenticated; Tenant B (`globex`) exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| session tenant | T-acme | from resolution middleware |
| spoofed body tenant_id | T-globex | must be ignored |
| employee | E300 (acme) | offboarded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme, initiate offboarding while injecting `tenant_id=T-globex` in the request body | Instance + tasks persisted with tenant_id=T-acme (session), NOT the spoofed value (FR-8). |
| 2 | Approve a clearance and complete the offboarding | Clearance rows, completion effects, and audit entries all stamped tenant_id=T-acme by the interceptor. |
| 3 | Query the same data as a globex user | EF global query filter returns zero acme rows (NFR-2). |
| 4 | (CONDITIONAL — RLS deferred) Run a raw SQL SELECT against the offboarding tables without setting `app.current_tenant_id` | DEFERRED: when Postgres RLS is enabled this returns zero rows; today RLS is not wired, so isolation relies on the EF filter (assert the EF behavior in steps 1-3; flag RLS to the caller). |

## 6. Postconditions
- All offboarding writes are tenant-stamped from the session; cross-tenant reads return nothing; RLS remains a documented future hardening step.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
