---
name: nav-gate-invariant-new-item
description: Adding a sidebar nav item must declare EXACTLY the gate the router enforces (roles AND permission are separate compared fields); plus the per-module route allowlist to update
metadata:
  type: project
---

`main-layout.nav-gate-invariant.spec.ts` (BUG-493) asserts, over **every** `NAV_ITEMS` entry, that
the gate declared on the item equals the gate the router actually enforces — derived at runtime by
walking `appRoutes` and introspecting `guard.effectiveRoles` / `guard.requiredPermissions` /
`guard.moduleKey`. Add an item with a mismatched gate and it fails by name.

**Why:** two systems were answering "may this persona use this feature?" and drifted both ways —
a link shown to someone the route bounces to `/forbidden` (ISSUE-210), and a working page hidden
from a Tenant Owner who could enter it. The invariant replaced spot-fixes that went green the moment
someone added the next item.

**How to apply when adding a nav item:**
- `roles` compared = `intersect()` of every `roleGuard.effectiveRoles` on the chain — **effective**,
  so it includes `'Tenant Owner'` (`TENANT_SUPER_ROLES` widens every non-system guard). Parent ∩ child:
  `/attendance`'s parent guard omits `HR Manager`, so listing it in a child grants nothing.
- `permissions` and `roles` are **separate compared fields**, and `visibleNavItems()` **ANDs** them.
  So a child gated by `permissionGuard` under a parent gated by `roleGuard` must declare BOTH
  `tenantRoles: [<parent effective roles>]` **and** `permission: '<the permission>'`. That is not
  drift — it is the exact mirror.
- Gate on the **permission** when the backend controller does (`[RequirePermission("X")]`), rather
  than transcribing the roles that happen to hold it: it follows a tenant that re-assigns the
  permission to a custom role. `permissionGuard` exists for this.
- A new child route also needs adding to its module's route allowlist in the per-module nav spec
  (e.g. `ATTENDANCE_SUBROUTES` in `main-layout.nav-attendance.spec.ts`) — that list is the
  "no dead/typo link" allowlist and its comment says it must name every real child path. Adding a
  genuinely-existing route to it is registration, not weakening.
- Do **not** ship the route without a nav item: URL-only reachability is the orphan defect
  ISSUE-208 / ISSUE-372 exist to prevent.

Verified on ISSUE-438 (`/attendance/settings`): all 30 `main-layout.nav-*` specs green.
