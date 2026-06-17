---
id: TC-ADM-ISO-014
user_story: US-ADM-006
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-014: Settings are inherently tenant-scoped (ITenantContext); cross-tenant access → 404/empty

## 1. Test Objective
Verify AC-5 and the cross-tenant Test Hint: all settings reads/writes operate ONLY on the current tenant via `ITenantContext` — there is no `tenant_id` parameter to manipulate. Any attempt to reach another tenant's settings (by injecting a tenant id in the route/body/query, or by guessing another tenant's identifiers) is rejected and returns 404/empty, never another tenant's data. Reads are confined by EF global query filters; writes are stamped by `TenantInterceptor`. Tenant A's settings are neither visible to nor affected by Tenant B.

## 2. Related Requirements
- User Story: US-ADM-006
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1 (keyed by `ITenantContext.TenantId`)
- Business Rules: BR-1, BR-6
- Test Hints: "As Tenant A admin, attempt to read tenant_setting for Tenant B via API manipulation; verify 404 or empty result."

## 3. Preconditions
- Tenant Acme (A) admin "Dana" and Tenant Beta (B) admin "Bob"; both tenants have distinct org/branding/localization/policy settings.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acting tenant | Acme (A) | resolved from subdomain / X-Tenant-Subdomain |
| neighbor | Beta (B) | distinct settings |
| injection vectors | body `tenant_id`, route id, query `tenantId` | must be ignored/rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, `GET` org profile | Returns ONLY Acme's profile; never Beta's. |
| 2 | As Dana, `PUT` org profile with a body field `tenant_id = Beta` | The injected tenant id is ignored; the write targets Acme only; Beta's profile is unchanged. |
| 3 | As Dana, append `?tenantId=Beta` or a Beta identifier on any settings route | 404/empty; Beta's settings are not disclosed. |
| 4 | As Dana, `GET` branding | Returns Acme's branding URLs only; no Beta URLs. |
| 5 | Compare Acme vs Beta after Dana's writes | Beta's org/branding/localization/policy settings are byte-for-byte unchanged. |
| 6 | As Bob (Beta), confirm Beta state | Beta sees only Beta's settings; Dana's changes never leaked in. |

## 6. Postconditions
- All settings access confined to the acting tenant; cross-tenant attempts return 404/empty; no leakage or cross-write.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
