---
name: fe-iuser-no-employeeid
description: The frontend IUser has no employeeId; FE cannot derive "is this the assigned interviewer/employee" itself — backend must signal it
metadata:
  type: feedback
---

`IUser` in `core/auth/auth.models.ts` is only `{ userId, email, displayName,
avatarUrl?, mfaEnabled }` — NO employeeId. The JWT claims (`ITokenClaims`) carry
`sub`, roles, permissions, tenant ids, but no employee id either.

**Why:** so FE features that gate on "is the current user THIS employee / the
assigned interviewer" (anti-bias in US-REC-006, manager-only views, etc.) cannot
compute the answer client-side. Trying to match by name/email is brittle.

**How to apply:** for ownership/assignment gates, have the backend return an
explicit flag on the resource DTO (e.g. `isMine`, `isAssignedInterviewer`,
`canViewOthers`, `hasSubmittedOwn`) and have the FE merely REFLECT it. The backend
is the security authority anyway (RLS + identity from auth context). Default the
flag to the permissive value when absent so a plain recruiter/admin view is never
accidentally hidden. Used in [[no-chart-lib-comparison-table]]'s US-REC-006 panel.
