---
id: TC-LV-ISO-014
user_story: US-LV-004
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-014: API rejects pending-queue requests without a valid tenant context

## 1. Test Objective
Verify that the pending queue endpoint requires a resolvable tenant context: a request that cannot resolve to a tenant (missing/unknown subdomain, reserved subdomain, or a tenant mismatch versus the authenticated user) is rejected and never returns cross-tenant or unscoped data.

## 2. Related Requirements
- User Story: US-LV-004
- Non-Functional Requirements: NFR-3
- Related: US-AUTH-007 (tenant resolution from subdomain)

## 3. Preconditions
- Tenant "acme" is active; manager "Robert Lee" (`Leave.Approve.Team`) is authenticated there.
- The `TenantResolutionMiddleware` runs before authorization.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unknown subdomain | nosuchtenant | No matching tenant |
| Missing tenant header | (X-Tenant-Subdomain absent, non-subdomain host) | Unresolvable in dev |
| Mismatched context | X-Tenant-Subdomain: globex + Robert's acme JWT | Tenant mismatch |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/pending` with `X-Tenant-Subdomain: nosuchtenant` | Tenant resolution fails; request rejected (400/404) -- no queue data returned. |
| 2 | Call the endpoint with no resolvable tenant context (missing header on a non-subdomain host in dev) | Rejected; no data returned; the global query filter does not silently return unscoped rows. |
| 3 | Call with `X-Tenant-Subdomain: globex` but Robert's acme JWT | Context resolves to the authenticated tenant (acme), not globex; the response contains only acme data or the request is rejected -- never globex data. |
| 4 | Verify no unscoped fallback | There is no code path where an unresolved tenant returns all tenants' pending requests. |
| 5 | Confirm a valid acme context succeeds (control) | `X-Tenant-Subdomain: acme` + Robert's acme JWT returns 200 with acme data only. |

## 6. Postconditions
- No data mutated.
- Without a valid tenant context, the pending queue returns no data.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
