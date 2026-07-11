# Admin Console (System Admin) — execution results (2026-06-19)

US-ADM-001 System Admin Console: tenant provisioning + directory. Backend gate is
`[RequirePermission("Tenant.Provision")]` (permission, NOT role) — admin token holds it.

| TC area | Title | Layer | Verdict | Evidence |
|----|-------|-------|---------|----------|
| US-ADM-001 | List tenants (directory) | API | ✅ PASS | `GET /api/v1/system/tenants` → **200** (both `platform` and `admin` tenant-context headers) |
| US-ADM-001 | Active subscription plans | API | ✅ PASS | `GET /api/v1/system/tenants/plans` → **200** |
| US-ADM-001 | Subdomain availability check | API | ✅ PASS | `GET /api/v1/system/tenants/subdomain-availability?subdomain=acme` → **200** |
| US-ADM-001 | Open Tenant Console (provision UI) | UI | ❌ FAIL | SystemAdmin → `/admin/tenants` redirects to **`/forbidden`** (BUG-1: guard checks `'System Admin'`, role is `SystemAdmin`) |
| US-ADM-001 | Tenant Console reachable from nav | UI | ❌ FAIL | No "Tenants" item in sidebar at all (BUG-2) |

## Key conclusion
**Backend System Admin Console = delivered & working. Frontend = unreachable** (BUG-1 role-string +
BUG-2 missing nav). The provisioning feature is fully built but the platform admin cannot use it in the UI.

## Pending (next batches)
- **Provision a tenant via API** (US-ADM-001 happy path) — also seeds `acme` + a Tenant Admin to unlock
  tenant-scoped UI tests. (Write op — execute as an explicit, recorded test step.)
- Monitoring (US-ADM-002), lifecycle suspend/terminate/reactivate (US-ADM-004), plans CRUD (US-ADM-009),
  tenant user mgmt (US-ADM-005), company settings (US-ADM-006), workflows (US-ADM-007), audit log
  (US-ADM-008), data export (US-ADM-010). Each: API layer (admin token) + UI layer (persona).
- NOTE: all 41 `blocked`-status designed TCs live in this module — review why.
