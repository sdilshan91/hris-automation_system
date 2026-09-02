---
name: feedback-tenant-isolation-test-invariant
description: For unresolved-tenant/isolation tests, assert "dropping tenant resolution changes nothing the caller sees" — NOT "every tenant route must refuse", which has false positives
metadata:
  type: feedback
---

When writing a test that proves tenant isolation fails CLOSED (e.g. an unresolved tenant hitting
`/api/v1/tenant/*`), do **not** assert "every route must return a refusal status". Assert the
invariant: **removing tenant resolution must not change what the caller can see** — the unresolved
response must either be a refusal, or be byte-identical (modulo the `ApiResponse.timestamp` field)
to the same caller's response *with* its tenant resolved.

**Why:** the naive "must refuse" rule has real false positives, measured on 2026-09-02 against the
live route surface. Four `/api/v1/tenant/*` GET routes legitimately answer 200 with no tenant
resolved: three serve global catalogues (`roles/permissions`, `job-titles/employment-types`,
`employees/import/template`) and `auth-settings` is scoped by the JWT's `ICurrentUser.TenantId`
rather than by `ITenantContext`, so it returns the caller's OWN row. Encoding "must refuse" would
have forced a hand-maintained exemption list — and an exemption list is how a security test gets
quietly widened later until it stops detecting anything. The equivalence invariant needs no
exemptions: benign routes satisfy it on their own merits, and a route that stops being benign starts
failing with no test edit.

**How to apply:** any time the assertion you reach for is a *status code*, ask whether the security
property is really about the status or about the *data delta*. For isolation work it is almost always
the delta. Two supporting rules that made this test non-vacuous and are worth reusing:
- Give the caller `PermissionCatalog.AllPermissions`, or authz denies on your behalf and the sweep is
  green for the wrong reason.
- Establish reachability first (sweep WITH a resolved tenant; only assert on routes that answered
  2xx), so routes that 400 on a missing query param aren't counted as fail-closed wins.

Related: [[project-gap001-tenant-fail-open]].
