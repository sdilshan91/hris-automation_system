# QA — BLOCKERS

Open, QA-surfaced blockers. **Source of truth = [`TEST-FINDINGS.md`](TEST-FINDINGS.md)**
(full schema: type · severity · status · layer · root cause · repro). This is a thin index.

- **Open HIGH/MED findings** → filter [`TEST-FINDINGS.md`](TEST-FINDINGS.md) for `Status: OPEN`. The active plan's P1 = the HIGH cluster (14).
- **Blocked test cases** → `[b]` rows in [`TEST-STATUS.md`](TEST-STATUS.md) (persona/data/rig gaps, not code).
- **Missing-coverage gaps** — Training&Benefits / US-ADM-011 / US-NTF-006 have thin TC coverage → P0 of the active plan.

_Fixing is a separate, human-decided cycle (`/fix-finding`, `/implement-story`) — QA only reports._
