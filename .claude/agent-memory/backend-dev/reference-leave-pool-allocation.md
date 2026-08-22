---
name: reference-leave-pool-allocation
description: "DF-19/ISSUE-045 pool-aware carry-forward — persisted FIFO allocation (LeaveLedger.Pool + LeaveCarryForwardTracking.ConsumedDays), deduction split, cancel restore, expiry reads counter"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 3a5584a2-6aef-4635-b49d-f2c0c577ea72
  modified: 2026-07-22T00:55:21.629Z
---

DF-19 / ISSUE-045 (branch `feat/df-19-pool-aware-carryforward`): made BR-4 FIFO carry-forward
allocation PERSISTED instead of derived, so cancel restores each pool exactly and expiry agrees.

**Model (decided, do not deviate):** deduction consumes CarryForward days before Accrual days.
- `LeavePool` enum {CarryForward, Accrual} in `HRM.Domain/Enums/LeavePool.cs`.
- `LeaveLedger.Pool` (nullable) + `LeaveLedger.CarryForwardTrackingId` (nullable) — set only on the
  per-pool Used deduction rows and matching Adjusted restore rows. NULL on legacy rows + non-pool
  entries (Accrual/CarryForward credits, Encashed, Expired). Pool persists as enum-name string(20).
- `LeaveCarryForwardTracking.ConsumedDays` numeric(7,2) NOT NULL default 0 — the persisted FIFO
  counter. Live remaining = MAX(0, CarriedDays − ConsumedDays − ExpiredDays).
- Migration `20260722005029_LeavePoolAllocation` backfills ConsumedDays =
  LEAST(carried, MAX(0, used-in-to-year)) — the SAME MIN the old `RemainingCarriedDays` used, so
  existing buckets keep identical expiry. No RLS change (columns only, not a new table).

**Where the money math lives (`LeaveRequestService`):**
- `AppendPooledDeductionAsync` — shared by ApproveAsync + StageLeaveApprovalAsync (workflow path).
  Splits into ≤2 Used rows chaining BalanceAfter; net == −TotalDays; final BalanceAfter == projected;
  bumps `tracking.ConsumedDays`. No-carry path = single Accrual-tagged row, byte-identical to pre-DF-19.
- `CancelAsync` restore = per-pool: loads THIS request's Used rows (`LeaveRequestId==id`), mirrors each
  as positive Adjusted with same Pool+bucket, decrements ConsumedDays (floor 0), re-opens a Consumed
  bucket if remaining>0, PRESERVES original ExpiryDate. If bucket already terminal-`Expired` → restore
  as Accrual/null (can't un-expire) + LogWarning. **No linked Used rows → single untagged Adjusted
  (legacy fallback, byte-identical).** `BuildRestoreRow` helper.
- **`PoolRowTickOffset = 10` (1µs):** the 2nd split row is stamped +1µs so `GetLedgerBalanceAsync`
  (latest-OccurredAt wins) deterministically returns the FINAL row's BalanceAfter. 1µs is the smallest
  increment surviving PG timestamptz truncation. Single-row paths take NO offset (byte-identical).

**Expiry (`LeaveCarryForwardService.ProcessExpiryAsync`):** now reads ConsumedDays
(`remaining = MAX(0, CarriedDays − ConsumedDays − ExpiredDays)`), NOT `GetUsedInYearAsync` (deleted —
the derived sum double-counted a later-cancelled request). `RemainingCarriedDays` calculator kept
(only its own unit tests use it now); preview API (`ComputeUnusedBalanceAsync`) unaffected — it sums
by EntryType, pool-agnostic, so no job↔preview disagreement.

**Test gotcha:** the cancel unit + reconciliation tests seed Used rows WITHOUT `LeaveRequestId`, so
cancel hits the legacy single-Adjusted fallback → they stayed green UN-changed (the task's prediction
that CancelLeaveRequestServiceTests:205 would split was wrong for that reason). Only the two carry-
forward EXPIRY tests changed: consumption now expressed via a `ConsumeCarried()` helper that bumps
ConsumedDays (expiry no longer reads the seeded new-year Used ledger). See [[green-suite-is-not-evidence]].
