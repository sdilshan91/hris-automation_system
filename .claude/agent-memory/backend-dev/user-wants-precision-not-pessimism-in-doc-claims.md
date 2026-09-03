---
name: user-wants-precision-not-pessimism-in-doc-claims
description: When correcting an overclaiming code comment, state the exact conditions under which it IS true — do not overcorrect into "it is not enforced"
metadata:
  type: feedback
---

When a code comment asserts a security/compliance property the code cannot actually know holds,
correct it by **naming the exact conditions under which it does hold** — not by flipping it to a
blanket denial. The user's phrasing: *"Precision, not pessimism."*

**Why:** an overcorrected comment ("append-only is not enforced") is just as false as the
overclaim, and it actively discourages engineers from relying on a control that genuinely works
where the platform is provisioned. Both directions destroy the reader's ability to reason.

**How to apply:**
- Split the claim into what is **unconditionally** true (e.g. no mutating endpoint exists, in any
  environment) and what is **conditionally** true (e.g. a DB privilege revoke that requires an ops
  bootstrap script to have been run *and* the app to authenticate as the specific role it targets).
  State both, labelled.
- Distinguish the **mechanism** from a correlated **indicator**. A feature flag that happens to be
  flipped at the same time as the real control is not the control; say so explicitly, or the next
  reader will "fix" the wrong thing.
- Say plainly what no test can prove (e.g. that a given *environment* ran a DBA bootstrap step),
  so the residual gap is documented rather than implied.
- Separately: **do not make the app own role DDL.** Auto-applying an ops/bootstrap SQL script at
  startup is off the table here by design. A startup *verification* (assert privileges match the
  intended set) is a legitimate idea but is an ops decision — file it as a `DECISION` finding
  rather than building it.

Related: [[feedback-guards-must-be-mutation-proven]]
