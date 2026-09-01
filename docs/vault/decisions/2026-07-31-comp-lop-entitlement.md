---
date: 2026-07-31
status: accepted
deciders: product owner (via /orchestrate session)
tags: [compensation, payroll, leave, entitlement, adr-lite]
---

# Three architecture decisions — offer compensation, LOP ownership, entitlement seams

Taken 2026-07-31 after the deferred-AC verification sweep surfaced [[TEST-FINDINGS-RESOLVED#BUG-292|BUG-292]], [[TEST-FINDINGS-RESOLVED#ISSUE-357|ISSUE-357]] and
[[TEST-FINDINGS-RESOLVED#ISSUE-358|ISSUE-358]]. In each case the **larger, architecturally-correct** option was chosen over the cheaper
one, deliberately.

## D1 — The salary STRUCTURE is decided at OFFER time (BUG-292)

**Decision:** add `SalaryStructureId` to `Offer`. It is chosen when compensation is agreed and approved;
applicant→employee conversion carries it through, and the new employee is created payroll-ready.

**Why not the cheaper options.** A picker on the conversion form would have fixed the data loss in a
fraction of the work, but it places a compensation decision *after* approval — the structure that
determines actual take-home would never have been part of what anyone signed off. A per-grade default
removes the decision entirely and fails silently: a wrong default produces a wrong payslip that nobody
notices until someone is paid incorrectly, which is the same silent-wrongness class as the bug being
fixed. Assigning salary post-conversion leaves a window where an employee exists with no structure, and
a payroll run catching them in it pays nothing.

**The principle:** the money a candidate agreed to and the money payroll pays must derive from ONE
approved record. Anything else is two decisions that are merely expected to match.

**Consequence:** Offer entity + migration + offer form + approval flow all change. Accepted.

## D2 — LEAVE owns the authoritative LOP figure, fed by attendance (ISSUE-357)

**Decision:** attendance remains the source of raw absence FACTS. The leave module applies POLICY (covered
by approved leave? balance available? paid or unpaid?) and produces the authoritative LOP. Payroll reads
that figure.

**Why.** Today `PayrollRunProcessor` reads `AttendanceMonthlySummary.LopDays` directly, which means the
attendance module is silently making a leave-policy decision it does not own and cannot see the inputs
for — it has no visibility of approvals or balances. Declaring attendance authoritative would have been
free (it matches production behaviour exactly) but institutionalises that inversion and drops US-LV-011.
Keeping both rails with reconciliation at payroll makes the reconciliation logic the new failure point.

**Ordering is load-bearing and non-negotiable:** repoint payroll to the leave-side figure FIRST, with a
reconciliation check proving the two agree, and only THEN wire a real `IAttendanceProvider`. Doing it in
the other order mints leave-side LOP rows for days payroll is still deducting via attendance — a live
double-deduction. This is a money path; it gets real-Postgres arms and mutation verification.

## D3 — Every sellable entitlement gets a pre-registered enforcement seam (ISSUE-358)

**Decision:** define and land the gate for each flag now, ahead of the feature:
- **SCIM** → reserved `/scim/v2` route prefix (same shape as the `CustomReportBuilder` gate)
- **CustomDomain** → `TenantResolutionMiddleware`: refuse custom-host resolution unless entitled
- **Sandbox** → tenant provisioning

**Why not simply pull them from the plan editor.** That removes the mis-selling risk and nothing else —
it leaves no obstacle to the eventual feature shipping unenforced, which is precisely the hole
[[TEST-FINDINGS-RESOLVED#ISSUE-356|ISSUE-356]] was filed for. Pre-registering is the only option where a future implementer *cannot*
accidentally ship an unenforced paid feature.

**Standing rule this establishes:** a flag may not become sellable in the plan editor until its
enforcement seam exists. Sellable-but-inert is a billing exposure, not a tidiness problem.


---

## D2-b — the two-rail LOP split is the INTENDED design, not a temporary state (decided 2026-08-02)

**Decision:** stop at the current split. **Attendance owns absence-derived LOP** (unapproved absence + lateness
penalties); **leave owns policy-derived LOP** (approved-but-unpaid leave). Both feed payroll through
`LopService.GetPayrollLopDaysAsync`. HR-assigned / system-generated / compulsory LOP stay on the attendance
rail, where they already deduct correctly because those rows carry `Status = HrAssigned` (not `Approved`) and
therefore fall through to `ABSENT`.

**Why not complete the unification D2 originally envisaged.** The rails are now *provably* disjoint — the
disjointness arm pins one employee, one month, with absence + lateness + unpaid + paid + HR-assigned LOP
summing to exactly 3.5 — and every category is deducted exactly once. Re-plumbing `PayrollRunProcessor` to move
working deductions around buys internal consistency at the cost of real money-path risk and no user-visible
gain. Reopening a correct money path for tidiness is how it stops being correct.

**Consequence:** [[TEST-FINDINGS-RESOLVED#ISSUE-357|ISSUE-357]] closes as *decided-not-built* rather than outstanding, and US-LV-011's auto-LOP
AC is superseded — the outcome it wanted (unpaid absence reduces pay) is delivered, by a different route.
Revisit only if a third LOP source appears.

## D4 — quantify BUG-293's historical under-deduction before deciding anything (decided 2026-08-02)

**Decision:** build a read-only exposure report — the same shape as BUG-291's — showing which employees, which
periods, and how much was under-deducted. Take no corrective action until the numbers exist.

**Why.** The choice between recovery, write-off and a bounded cut-off cannot be made sensibly without knowing
whether this is hundreds or hundreds of thousands, or whether the affected population is a handful of edge
cases or systemic. Note this points the OPPOSITE way to BUG-291: money owed **to** the business rather than to
employees, so recovery would mean clawing back salary already paid — an employee-relations and, in many
jurisdictions, legal matter that must not begin on unverified figures.

## D5 — the leave-configuration screens get a discoverable nav group (decided 2026-08-02)

**Decision:** add a "Leave configuration" navigation group covering entitlement rules, holidays, carry-forward
and the BUG-291 exposure screen — all four of which were reachable only by direct URL.

**Why this over just sending Finance a link** (the narrower option): the discoverability gap is not specific to
the exposure report. Four admin screens were effectively invisible, and the exposure report only made that
visible. Fixing the cluster addresses the actual problem rather than the instance of it.
