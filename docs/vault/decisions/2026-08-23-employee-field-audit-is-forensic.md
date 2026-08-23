---
type: decision
status: accepted
created: 2026-08-23
deciders: human (C3 / GAP-025)
---

# `employee_field_audit_logs` is a forensic side-table, by design

## Context

The table had **four writers and zero production readers**. Every read in the codebase was in a test. The
options were: give it a reader (a per-field change-history UI), declare it a forensic side-table, or fold it
into `audit_logs` and drop it.

## Decision

**It stays write-only by design, and the guard tests are the fix — not a UI.**

Its rows carry per-field `BeforeSnapshot`/`AfterSnapshot` JSON, including **masked PII** (national ID stored
last-4 only — see `EmployeeService`'s ISSUE-293 handling). Those snapshots must not surface in the
US-NTF-005 audit viewer, which everyone holding audit permissions can read. Folding the table into
`audit_logs` would do exactly that, and would need a data migration besides.

## Consequences

A reader-less table's real hazard is **not** the missing screen. It is that **nothing notices when a write
stops happening**. Answering "forensic side-table" without addressing that would have left the table exactly
as fragile as it was, with a decision recorded to make it look considered.

So the decision came with three obligations, all now met:

1. **Every field-audit write is paired with a central `audit_logs` row.** Three sites were unpaired —
   `EmployeeService.UpdateProfileAsync`, `EmployeeService.CreateAsync` (which wrote *no* audit at all,
   anywhere) and `EmployeeStatusService.ApplyPendingFutureDatedChangesAsync`. Compliance reads the central
   trail; the forensic table holds the values.
2. **The central row carries the action, resource and section NAMES — never the values.** That is what keeps
   the PII in the forensic table where the decision put it.
3. **`EmployeeFieldAuditPairingGuardTests` blocks the next unpaired write**, and a positive arm pins that the
   known writers still exist — so the arm cannot be satisfied by deleting all the writes.

### What was wrong before

`Employee` is `IAuditExempt`, so `AuditCaptureInterceptor` deliberately skips it and employee changes are
recorded by hand. The consequence had gone unnoticed: **editing** an employee left nothing in the audit
viewer, while merely **viewing** that profile logged `Employee.ProfileViewed`. Creating one left nothing at
all. A termination scheduled for next month was invisible; the identical termination applied today was fully
visible — which of the two you got depended on nothing but the effective date.

## Revisit this if

- Anyone asks for per-field employee history **in the product**. That is a real feature (with its own
  permission question about who may see masked PII), not a gap — build it deliberately rather than by
  quietly pointing a screen at this table.
- The forensic snapshots ever start carrying unmasked PII. The reasoning above depends on masking.

## Links
- Related code: `EmployeeService.CreateAsync` / `UpdateProfileAsync`,
  `EmployeeStatusService.ApplyPendingFutureDatedChangesAsync`, `ReportingStructureService.AddManagerAudit`,
  `EmployeeFieldAuditPairingGuardTests`
- Related findings: `GAP-025` · `ISSUE-025` (the status-change pairing that came first) · `BUG-023`
  (the manager-assignment pairing)
- Related notes: [[core-hr]]
