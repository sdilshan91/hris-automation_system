---
id: TC-NTF-ISO-016
user_story: US-NTF-004
module: Notifications & Audit
priority: high
type: security
status: fail
created: 2026-06-17
---

# TC-NTF-ISO-016: Bulk + PII + auth + RTBF capture paths all stamp the correct tenant_id (no cross-tenant bleed across capture types)

## 1. Test Objective
Verify that every audit capture path -- entity write (interceptor), PII ReadSensitive, auth events,
data export, and GDPR anonymization -- consistently stamps the correct tenant_id and never writes a
row into another tenant's scope, even under interleaved/concurrent multi-tenant activity. Closes the
isolation gap across the non-SaveChanges capture paths.

## 2. Related Requirements
- User Story: US-NTF-004
- Acceptance Criteria: AC-5 (tenant isolation), AC-2 (PII read audit), AC-4 (auth audit)
- Non-Functional: NFR-2 (tenant isolation across all capture paths), NFR-5 (bulk non-blocking)
- Functional Requirements: FR-3 (auth audit), FR-4 (PII read audit), FR-5 (export audit), FR-8 (tenant_id from session); Business Rules: BR-6 (RTBF scoped)

## 3. Preconditions
- Tenants A and B both active with authenticated users.
- A harness can interleave activity from both tenants (e.g. concurrent requests).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A activity | create + salary read + login + export | mixed capture paths |
| Tenant B activity | create + salary read + login + export | run concurrently |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Interleave: Tenant A and Tenant B each perform an entity create, a PII ReadSensitive, an auth login, and a data export | All actions complete; audit rows produced for each |
| 2 | Inspect entity-write (interceptor) audit rows | Tenant A's rows carry tenant_id A, Tenant B's carry tenant_id B -- no swap under concurrency |
| 3 | Inspect ReadSensitive audit rows | Each carries the acting tenant's tenant_id; no Tenant A PII-read row leaks into Tenant B scope |
| 4 | Inspect auth audit rows | Each auth row carries the correct tenant_id from the resolving context |
| 5 | Inspect export audit rows | Each export row carries the correct tenant_id and the exporter's tenant data only |
| 6 | Run a GDPR RTBF anonymization for a Tenant A subject | Only Tenant A rows for that subject are redacted; Tenant B audit rows are untouched (BR-6 scoped) |

## 6. Postconditions
- Every capture path is tenant-stamped correctly; concurrent multi-tenant activity shows no cross-tenant bleed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
