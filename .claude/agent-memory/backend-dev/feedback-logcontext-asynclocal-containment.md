---
name: logcontext-asynclocal-containment
description: Serilog LogContext pushes inside an `async` method cannot leak to the caller, so "the scope was released" test arms stay green even with the `using` removed — pick the mutation accordingly
metadata:
  type: feedback
---

A `Serilog.Context.LogContext.PushProperty(...)` performed **inside an `async` method** can never leak to
that method's caller, disposed or not. Do not write a test arm that claims to prove the `using`/pop, and do
not accept one at face value in review.

**Why:** `AsyncTaskMethodBuilder.Start` captures and restores the caller's `ExecutionContext` around the
state machine, and `LogContext` is backed by an `AsyncLocal`. So the write is structurally contained at the
async method boundary. Measured on GAP-024 (`TenantJobRunner.RunForTenantAsync`): deleting the `using`
left both "attribution is released" arms **green**, while deleting the *push* turned the attribution arm
red. The `using` there is defence-in-depth, not the mechanism.

**How to apply:**
- Containment only holds while the push is inside the async seam. A push from a **synchronous** method
  (`TenantContext.SetTenant` / `SetSystemContext`, a middleware helper, an `IServerFilter.OnPerforming`)
  *does* escape to its caller — that is precisely why `JobLogContextFilter` needs its explicit
  `OnPerformed` disposal, and why its leak arms are real while an async runner's are not.
- When mutation-checking log-scope work, the mutations with discriminating power are: **remove the push**,
  **remove the `Guid.Empty` guard**, and **move the push to a synchronous seam** — not "remove the `using`".
- Repo rule this protects: `Guid.Empty` is logged as ABSENT, never as `tenant_id=00000000-...`, because an
  all-zero id reads like a real scope during an incident. `TenantContext.SetSystemContext()` leaves
  `TenantId == Guid.Empty`, so the tenant *context* is the one seam you must never attribute from.

Related: [[reference-fresh-scope-rls-writes]], [[feedback-relational-guard-for-raw-sql-transactions]].
