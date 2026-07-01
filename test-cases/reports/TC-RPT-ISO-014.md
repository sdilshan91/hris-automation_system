---
id: TC-RPT-ISO-014
user_story: US-RPT-004
module: Reports & Analytics
priority: critical
type: security
status: pass
exec_note: "2026-06-30 API iso-fixture probe: export-DOWNLOAD channel IS isolated — foreign/random exportId -> HTTP 404 'export_not_found' under own tenant; isoa export history empty (no cross-tenant rows visible). Isolation holds for the stored-export download path."
created: 2026-06-17
---

# TC-RPT-ISO-014: No-tenant-context export rejected; cross-tenant exportId injection -> 404 (not 403); spoofed tenant ignored (AC-5, NFR-3)

## 1. Test Objective
Verify the export endpoints require a resolved tenant context and resist tenant spoofing: a request
with no resolvable tenant is rejected; injecting another tenant's exportId into the download path
returns 404 (existence not disclosed) rather than 403; and a client-supplied/spoofed tenant identifier
(header/body/claim) is IGNORED -- the server uses the authenticated `ITenantContext`, not client input.
Validates AC-5, NFR-3, and the platform's resolution -> ITenantContext -> EF-filter chain.

## 2. Related Requirements
- User Story: US-RPT-004
- Acceptance Criteria: AC-5
- Non-Functional: NFR-3 (tenant isolation)

> PLATFORM ACCURACY: tenant is resolved by `TenantResolutionMiddleware` (subdomain / dev
> `X-Tenant-Subdomain`) into the scoped `ITenantContext`; reads are constrained by EF global query
> filters. Cross-tenant resource-id access asserts 404 (not 403) so existence is not disclosed,
> consistent with TC-RPT-ISO-002/-010. PostgreSQL RLS (NFR-7) is deferred defense-in-depth.

## 3. Preconditions
- Tenant A and Tenant B active. `hrA` authenticated in Tenant A; `EXP-B-1` exists in Tenant B.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant B exportId | EXP-B-1 | injected by hrA |
| spoofed header | X-Tenant-Subdomain: tenantB | must be ignored when authenticated as A |
| no-context request | (unresolvable tenant) | rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `POST /reports/{type}/export` with no resolvable tenant context | Rejected (no tenant -> 400/401/403 per platform); no export created |
| 2 | As `hrA`, `GET /reports/exports/EXP-B-1/download` (B's id) | 404 Not Found -- existence not disclosed (NOT 403), per platform convention |
| 3 | As `hrA`, resend with spoofed `X-Tenant-Subdomain: tenantB` | Spoof ignored; request still resolves to Tenant A; EXP-B-1 still 404 |
| 4 | As `hrA`, send an export request with a body/claim carrying tenantId=tenantB | Server uses authenticated ITenantContext (A); the spoofed tenantId is ignored |
| 5 | Confirm only A's data is ever returned to `hrA` | All responses scoped to Tenant A regardless of injected identifiers |

## 6. Postconditions
- Export endpoints require resolved tenant; cross-tenant ids 404; spoofed tenant ignored.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
