---
type: decision
status: accepted
created: 2026-08-11
deciders: sdilshan91 (product owner), Claude (analysis)
---

# Goal ownership stays individual-only; the documented org→department→individual cascade is withdrawn

## Context

§11.9 and **US-PRF-001 FR-4** promise a three-tier goal cascade: organisation → department → individual.
The data model cannot represent it. `Goal.EmployeeId` is **non-nullable**, so every goal must belong to a
person and a department-owned or company-owned objective has nowhere to live. The gap analysis recorded this
as **GAP-012 / decision-gate item "Goal cascading org/department tier"** and explicitly refused to guess:
building an owner tier and amending the requirement are very different sizes, and picking wrongly is
expensive in both directions.

Two facts shaped the decision:

1. **The UI is missing either way.** Pass B rated the cascade "COVERED" on the strength of backend code, but
   8 of 11 Performance stories have no frontend at all. So amending the document does not remove a working
   feature — nothing is reachable today regardless.
2. **Nobody has asked for it.** There is no story, ticket, or customer commitment for department-owned
   objectives beyond the §11.9 sentence itself. The cascade appears to be aspirational text rather than a
   captured requirement.

## Decision

**Goals remain individual-owned.** `Goal.EmployeeId` stays non-nullable. §11.9 and US-PRF-001 FR-4 are
**amended** to describe individual goals with optional manager alignment, and the org→department tier is
recorded here as withdrawn rather than deferred — a deferral implies a schedule, and there is none.

## Alternatives considered

- **Introduce a polymorphic owner tier (org / department / employee).** Makes the documented cascade real.
  Rejected as **L-sized for no current demand**: a nullable owner column plus a discriminator, a migration,
  and then every goal query, permission check, and rollup rule has to decide what a department-owned goal
  means for progress aggregation, review linkage, and the employee's own goal list. It also invites a second
  round of ambiguity (does a department goal roll up to an org goal automatically?) that no requirement
  answers. Revisit if department-level OKRs become a customer commitment.
- **Leave the contradiction in place.** Rejected. It is the more corrosive option: the document keeps
  claiming a capability, the next audit re-raises it as a gap, and someone eventually builds the L-sized
  change to satisfy a sentence rather than a user.

## Consequences

- **Easier:** the Performance frontend work (GAP-012) is unblocked — it can be built against the model that
  exists rather than waiting on a model decision.
- **Harder:** if department-owned objectives are later sold, this becomes a migration on live goal data, not
  a greenfield design. That cost is accepted knowingly.
- **Accepted:** the tech doc loses a feature claim. Pass B's "COVERED" rating for the cascade is **downgraded**
  regardless of this decision, because it was based on backend code for a feature with no UI.
- **Not affected:** individual goal setting, manager alignment, progress tracking, and review linkage all work
  as documented and are untouched.

## Links
- Related code: `HRM.Domain/Entities/Goal.cs` (`EmployeeId` non-nullable) · `HRM.Application/Features/Performance/`
- Related stories: US-PRF-001 FR-4 · §11.9
- Related gaps: GAP-012 · GAP-021 (US-PRF-011 calibration) · decision-gate item "Goal cascading org/department tier"
- See also: [[ADR-2026-08-11-uptime-is-platform-not-per-tenant]] — decided in the same pass, same principle
  (a requirement the system cannot honour is corrected, not left standing)
