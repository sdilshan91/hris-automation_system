---
id: TC-NTF-ISO-015
user_story: US-NTF-004
module: Notifications & Audit
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-NTF-ISO-015: EF filter blocks cross-tenant audit reads; interceptor stamps tenant_id on every audit write (RLS deferred)

## 1. Test Objective
Verify the two-layer isolation mechanism for audit data that is in force today: (1) the EF Core global
query filter excludes other tenants' audit rows on read, and (2) the write path stamps the session
tenant_id on every audit row from the resolved context (never a client value). Documents the
PostgreSQL RLS layer named in NFR-2 as deferred defense-in-depth.

## 2. Related Requirements
- User Story: US-NTF-004
- Acceptance Criteria: AC-5 (tenant-isolated audit visibility)
- Non-Functional: NFR-2 (audit isolated by tenant; RLS deferred -> EF query filter + tenant stamping)
- Functional Requirements: FR-8 (tenant_id set from authenticated session context)

## 3. Preconditions
- Tenants A and B active, each with audit rows.
- A test harness can switch the resolved ITenantContext between A and B.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A context | tenant_id A | read/write scope A |
| Tenant B context | tenant_id B | read/write scope B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With Tenant A context resolved, read audit rows via the normal (filtered) query | Only Tenant A rows returned; Tenant B rows excluded by the global query filter |
| 2 | Trigger an audited action under Tenant A context | The new audit row's tenant_id == Tenant A, stamped from the session/resolution context (not from any client field) |
| 3 | Switch to Tenant B context; trigger an audited action | The new audit row's tenant_id == Tenant B; Tenant A rows remain invisible to Tenant B reads |
| 4 | Attempt to set tenant_id on an audit write to a foreign tenant via a client-supplied value | The supplied value is ignored; the row carries the resolved session tenant_id |
| 5 | [CONDITIONAL / DEFERRED -- RLS] Execute a raw SQL `SELECT * FROM audit_log` WITHOUT setting `app.tenant_id` | EXPECTED once RLS is provisioned: zero rows (RLS blocks unscoped reads). NOTE: RLS is a deferred platform extension; today isolation relies on the EF query filter + tenant stamping -- mark the raw-SQL RLS expectation CONDITIONAL |

## 6. Postconditions
- Reads are EF-filtered per tenant; writes are tenant-stamped from session; RLS tracked as deferred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
