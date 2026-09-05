---
type: decision
status: proposed
created: 2026-09-05
deciders: product owner + Claude (CI cost review)
tags: [ci, testing, cost, adr-lite]
---

# CI gates are keyed on a scope tag, and the backend/frontend rule is deliberately asymmetric

## Context

The CI classifier was binary — `docs_only` true or false — meaning "the narrow docs gate" or
"absolutely everything". Measured consequences on this repo:

- A PR touching four files under `src/frontend` ran the full **5,600-test** backend suite and a real
  PostgreSQL migration apply: about **25 minutes of a 30-minute gate** proving nothing about that
  diff. Observed live on [[BUG-493]]'s PR (#628).
- `CLAUDE.md` and `.claude/**.md` fall outside `^docs/.+\.md$`, so a one-line rule edit triggered the
  **full** gate. This session alone had ~6 such PRs — roughly 3 hours of CI.

Separately, the backend job itself runs 37-44 minutes because every `*PostgresTests` class uses
`IAsyncLifetime`, which xUnit fires **per test**: 398 container starts across 93 classes, each
replaying 149 migrations. That is a *different* problem, recorded as `ISSUE-453`, and is **not**
what this ADR addresses.

## Decision

The classifier emits **one scope tag** — `docs | frontend | backend | fullstack` — and the jobs key
off it. The tag is written to the job summary so a reviewer can see and challenge the decision rather
than inferring it from which jobs happen to be grey.

| tag | runs |
|---|---|
| `docs` | docs gate only |
| `frontend` | frontend + E2E; **backend and migrations skipped** |
| `backend` | backend + migrations + E2E, **and the frontend job** |
| `fullstack` | everything |

**The asymmetry is the decision, not an oversight.** A frontend change cannot break the backend, so
`frontend` skips backend and migrations. A backend change *can* break the frontend:
`npm run api:types:check` lives in the **frontend** job and is what catches a C# DTO change failing to
reach the generated TypeScript models. It caught real drift on #624 the day before this was written,
and the FE↔BE drift it guards once broke 9 of 13 modules. So `backend` still runs the frontend job.

The saving from skipping it would have been ~3 minutes; the risk was a silent contract break. **The
originally-proposed "backend-only → just `dotnet test`" is simultaneously the smallest saving
available and the only unsafe one.**

## Alternatives considered

- **Symmetric skipping (backend-only skips frontend)** — the intuitive design, and rejected on the
  evidence above. Cheapest to state, most expensive to be wrong about.
- **Leave the binary classifier** — zero risk, but pays ~25 min on every frontend PR and ~28 min on
  every rule/skill edit, in a repo where multiple agents already contend for the gate.
- **Path-filtered jobs via `paths:` on the workflow trigger** — rejected: a skipped *job* and a skipped
  *workflow* differ for required checks, and it moves the decision out of a place where it can be
  printed and reviewed.

## Consequences

- Frontend-only PRs lose ~25 minutes of gate; markdown/rule PRs lose ~28.
- Anything not positively recognised falls to `fullstack`. Over-running costs minutes; under-running
  ships a defect behind a green tick. `.github/**` classifies as `fullstack`, so the workflow can
  never skip its own gate.
- **A coupling was discovered and must be preserved:** widening the classifier to cover `.claude/**.md`
  is only safe *because* `ClaudeMdAccuracyTests` was added to the docs-gate filter in the same commit.
  The docs gate had omitted it, and that omission was harmless only by accident — those files used to
  trigger the full gate, which ran the guard anyway. Separating the two changes would silently stop
  running the CLAUDE.md guard on exactly the commits that edit CLAUDE.md.
- **This is not theoretical.** On 2026-09-05, a broken markdown link (`[[ISSUE-148]](c)`, where "(c)"
  meant a sub-item and parsed as a link target) shipped in #627 behind a green tick — because #627 was
  docs-only and the docs gate omitted that very test.
- `ISSUE-453` (container-per-test) remains the larger cost inside the backend job and is untouched here.

## Links
- Related code: `.github/workflows/ci-gate.yml`, `scripts/ci-classify-selftest.sh` (16 cases)
- Related findings: `ISSUE-453`, `ISSUE-454`, `ISSUE-492`
- PR: #629 (held for human review — CI config)
