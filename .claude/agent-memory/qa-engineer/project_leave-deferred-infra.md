---
name: project-leave-deferred-infra
description: Leave Management TCs must mark Redis balance cache and holiday calendar as deferred/blocked dependencies, not coverage gaps
metadata:
  type: project
---

In the Leave Management module, two cross-cutting dependencies recur across test cases and must be written as conditional/deferred rather than failures:

- **Redis balance cache (NFR-2/NFR-3, FR-6) is DEFERRED.** Per `docs/vault/modules/leave-management.md`, no entity uses a cache layer yet; balance is read from the LeaveLedger running total. TCs touching cached balances (e.g. real-time balance display, cache-key isolation) should verify against the DB-fallback path and the documented key pattern `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`, marking the cache portion deferred.
- **Holiday calendar (US-LV-007) gates holiday-exclusion.** Working-day calc TCs must mark the holiday-exclusion expectation as BLOCKED on US-LV-007 if that story isn't implemented, while weekend exclusion (tenant work-week config) still passes independently.

**Why:** Keeps coverage summaries honest (PASS with NOTE, not silent gaps) and avoids writing TCs that will fail purely because downstream infra isn't built — matching how US-LV-001/US-LV-002 TCs were already authored.

**How to apply:** When writing or reviewing Leave Management TCs, default these two to deferred/conditional and cite the vault. See [[project-leave-tc-conventions]].
