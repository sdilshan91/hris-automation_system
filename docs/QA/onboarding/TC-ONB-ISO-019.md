---
id: TC-ONB-ISO-019
user_story: US-ONB-005
module: Onboarding / Offboarding
priority: high
type: security
status: pass
created: 2026-06-17
---

# TC-ONB-ISO-019: Offboarding lookup/cache keys + F&F notification payload are tenant-scoped

## 1. Test Objective
Verify NFR-2: any cached offboarding/clearance lookup is tenant-scoped (no cross-tenant cache collision), and the F&F settlement trigger notification dispatched to Payroll carries the originating tenant_id so Payroll processes it only within that tenant's boundary.

## 2. Related Requirements
- User Story: US-ONB-005
- Acceptance Criteria: AC-6
- Functional Requirements: FR-6 (F&F trigger), FR-8 (tenant_id)
- Non-Functional Requirements: NFR-2
- Cross-cutting: mandatory multi-tenant isolation

> PLATFORM NOTE: If no distributed cache is wired for offboarding lookups yet, assert the equivalent always-tenant-filtered property and flag the target cache-key shape `onboarding:offboarding:{tenant_id}:{employee_id}` to the caller (mirrors TC-ONB-ISO-004/011/015).

## 3. Preconditions
- Tenant A (`acme`) and Tenant B (`globex`) each have an offboarding in progress with overlapping employee identifiers.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| cache key (target) | onboarding:offboarding:{tenant_id}:{employee_id} | tenant-scoped |
| F&F notification | { tenant_id: T-acme, employee_id: E300, ... } | tenant-stamped payload |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load acme's offboarding dashboard, then globex's, for employees with the same local id | Each resolves from a distinct tenant-scoped entry; no key collision; no cross-tenant data returned. |
| 2 | Complete an acme offboarding and inspect the F&F notification dispatched to Payroll | Payload carries tenant_id=T-acme; Payroll receives it scoped to acme only (FR-6, FR-8). |
| 3 | Confirm globex Payroll never receives the acme F&F trigger | The notification is not visible/processable in the globex tenant context (NFR-2). |
| 4 | (CONDITIONAL) If no offboarding lookup cache is wired | Assert dashboards/lookups are always recomputed under the tenant query filter (equivalent property); flag the target key shape to the caller. |

## 6. Postconditions
- Offboarding caches and the F&F notification payload are tenant-scoped; no cross-tenant cache or notification leakage.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
