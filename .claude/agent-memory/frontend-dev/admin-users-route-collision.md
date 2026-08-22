---
name: admin-users-route-collision
description: app.routes already has admin/users/* leaf routes; a new admin/users lazy children route with a :param child must be registered AFTER them
metadata:
  type: feedback
---

When mounting a NEW lazy `admin/users` feature (US-ADM-005 user management) under
MainLayout, the route tree ALREADY contains more-specific leaf routes from auth
stories: `admin/users/lockout` (US-AUTH-010) and `admin/users/:userId/sessions`
(US-AUTH-009). The new feature's children include a `:userTenantId` detail child.

Rule: register the `admin/users` `loadChildren` block AFTER those specific leaf
routes in `app.routes.ts` so Angular's top-to-bottom match keeps `…/lockout` and
`…/:userId/sessions` winning over the feature's `:userTenantId` child.

**Why:** Angular Router is first-match-wins, ordered. If the lazy `admin/users`
parent sits above them, `admin/users/lockout` resolves to the feature's
`:userTenantId='lockout'` detail page instead of the lockout component.

**How to apply:** any time you add a lazy feature whose path is a prefix of
existing leaf routes, place it below the leaves. Also note the tenant
user-mgmt endpoints are at `/api/v1/users` + `/api/v1/invitations` (NOT
`/api/v1/system`), and assignable roles came from `/users/assignable-roles`,
distinct from the existing US-AUTH-006 RolesService at `/api/v1/tenant/roles`.
See [[admin-console-api-root]].
