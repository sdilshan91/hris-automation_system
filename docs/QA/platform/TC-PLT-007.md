---
id: TC-PLT-007
user_story: US-PLT-005
module: Platform
priority: high
type: security
status: automated
created: 2026-07-24
automated: 2026-07-24
defect:
  - DF-enc-http-authz
---

# TC-PLT-007: System encryption key-rotation endpoints are gated by Tenant.Lifecycle — a read-only Support caller is 403 on both the re-encrypt trigger and the report (real HTTP → controller)

## 1. Test Objective
Verify authorization on the system encryption key-rotation endpoints (`POST /api/v1/system/encryption/reencrypt`
and `GET /api/v1/system/encryption/report`) over real HTTP through the `[RequirePermission("Tenant.Lifecycle")]`
filter to the controller. These endpoints trigger / report a **fleet-wide** field re-encryption, so the only
thing between read-only support staff and that trigger is the `Tenant.Lifecycle` gate — held only by the
platform SystemAdmin. The read-only System Support role holds `Tenant.ViewLifecycle` but NOT
`Tenant.Lifecycle`. The test proves the SystemAdmin is admitted (GET → 200, route wired) and a
Support-equivalent caller is denied (403) on BOTH the trigger and the report — a 403 (not 404) proving the
`[RequirePermission]` filter runs BEFORE the handler and the route exists.

## 2. Related Requirements
- User Story: US-PLT-005 — the administrative-control surface for AC-4 key management; the re-encrypt sweep
  itself is TC-PLT-004.
- Finding: DF-enc-http-authz (the encryption admin endpoints must deny a non-SystemAdmin at the HTTP layer).
- Security requirement: authorization / least-privilege on a destructive fleet-wide operation.

## 3. Preconditions
- The `HttpApi` integration collection (`ApiTestFactory`) — a real in-process API host with a seeded platform
  SystemAdmin (`admin@hrm.local` on the `platform` subdomain) and the ability to mint a client holding an
  arbitrary permission set.
- Executed on the HTTP-harness run.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| SystemAdmin | admin@hrm.local / platform subdomain | holds `Tenant.Lifecycle` |
| Support-equivalent caller | permissions = `Tenant.ViewLifecycle` only | lacks `Tenant.Lifecycle` |
| Reencrypt route | POST /api/v1/system/encryption/reencrypt | fleet-wide trigger |
| Report route | GET /api/v1/system/encryption/report | key-usage report |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | As the platform SystemAdmin, `GET /api/v1/system/encryption/report`. | `200 OK` — the SystemAdmin holds `Tenant.Lifecycle`, the gate admits it, and the route is wired (positive is the read-only report only; a re-encrypt POST is deliberately not fired on the shared container). | `Report_AsSystemAdmin_Returns200` |
| 2 | As a caller with `Tenant.ViewLifecycle` only, `POST …/reencrypt`. | `403 Forbidden` — `Tenant.ViewLifecycle` cannot trigger a fleet-wide re-encryption; only `Tenant.Lifecycle` (SystemAdmin) can. 403 (not 404) proves the filter runs before the handler. | `Reencrypt_AsSupportWithoutLifecyclePermission_Returns403` |
| 3 | As a caller with `Tenant.ViewLifecycle` only, `GET …/report`. | `403 Forbidden` — the key-usage report is also gated by `Tenant.Lifecycle`, which System Support lacks. | `Report_AsSupportWithoutLifecyclePermission_Returns403` |

## 6. Postconditions
- Only the platform SystemAdmin (`Tenant.Lifecycle`) can trigger or view the fleet-wide encryption
  re-encryption; read-only Support staff are denied at the HTTP authorization layer, before any handler runs.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test (Support caller denied on both endpoints)
- [ ] Boundary test
- [x] Security test (authorization / least-privilege on a destructive fleet-wide operation; 403-not-404 proves gate-before-handler)
- [ ] Multi-tenant isolation (these are system-scope platform endpoints, not tenant-scoped)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by:** `HRM.Tests/Integration/Http/AdminEncryptionAuthorizationApiTests` (3 facts), carrying `[Trait("TC", "TC-PLT-007")]` and `[Collection("HttpApi")]`. Runs on the HTTP-harness integration pass.
