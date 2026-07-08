---
name: plan-audit
description: Cross-doc plan auditor. Scans every plan/status/ledger doc, computes real %-complete + remaining points against git/ledger ground truth, and detects drift — stale checkboxes (merged-not-closed), status conflicts, duplicate tracking of one item across docs, and unverified "done" claims — into a single regenerated dashboard. REPORT-ONLY: never edits a source plan. Use to see true progress and to find overlap/duplicate effort across the ~30 tracking docs.
user_invocable: true
---

# Plan Audit (cross-doc reconciler)

Answers "what's actually done, what's left, and where do our plans disagree or duplicate each
other?" — deterministically, by parsing every plan/status/ledger doc and cross-checking its claims
against **ground truth** (merged PR#s in git, ledger `RESOLVED`, TC `status:`).

**Why it exists:** this repo has ~30 tracking docs in 6 formats that drift out of sync — the same
item (e.g. BUG-003) is tracked in six docs that disagree on its status, and every summary count is
self-admittedly stale. Hand-counting has repeatedly failed. This tool replaces the hand count with
a parser + a ground-truth diff.

**REPORT-ONLY.** It writes exactly one file — `test-cases/PLAN-AUDIT.md` (regenerated in place) —
plus its own scratch JSON. It **never edits a source plan/board/ledger**. Reconciliation edits are
delegated to the existing write-side seams (`/auto-heal`, `/verify-fix`, a human). This is the
**read-side** counterpart to [`/auto-heal`](auto-heal.md), which owns *writing* the living plan.

## Usage

```
/plan-audit                 # full audit; regenerate the dashboard
/plan-audit --living         # only the actively-maintained boards/ledgers (fast)
/plan-audit --module CHR     # scope to one module's IDs
/plan-audit --drift-only     # skip the metrics tables, emit only drift/duplicate findings
```

## How it runs (hybrid: deterministic scan → sub-agent narrative)

### 1. Deterministic scan
Run the scanner and capture its JSON to the scratchpad:

```bash
python .claude/skills/plan-audit/scan.py --root . --pretty > "$SCRATCH/plan-audit.json"
```

(Pass `--living-only` / `--module X` through from the skill args.) The scanner
([scan.py](plan-audit/scan.py)) deterministically:
- parses the **structured** docs — the two checkbox boards (`user-stories/STATUS.md`,
  `test-cases/TEST-STATUS.md`, each under its own glyph vocabulary), the findings ledger
  (`TEST-FINDINGS.md`), `BUG-STATUS.md`, and every `TC-*.md` frontmatter — into normalized items
  (`{id, norm_status, pr_refs, doc, line, lifecycle}`);
- **auto-discovers** every other `*PLAN*/*STATUS*/*TRIAGE*/*DECISIONS*/MATRIX` doc, tags it
  `historical`, and flags plan-like ones not in the registry as **UNREGISTERED-DOC**;
- computes **per-doc %-complete** and a **dedup roll-up by ID** (canonical status = highest-
  precedence across living docs; `SKIPPED`/`UNKNOWN` excluded from the denominator);
- cross-checks PR claims against **merged git history** (`git log` merge + squash subjects);
- emits deterministic **drift**: `STALE-CHECKBOX`, `STATUS-CONFLICT`, `DUPLICATE-TRACKING`,
  `UNVERIFIED-CLAIM`.

### 2. Reconciler sub-agent
Dispatch **one `Explore`/`general-purpose` sub-agent** with the JSON path and this brief. It must
NOT re-read the 2000 structured items — the scanner already did that. Its job:
- **Read only the freeform table docs** the scanner left at `norm_status: UNKNOWN` but with IDs
  (e.g. `COMPLETION-PLAN-*.md`, `FIX-FINDINGS-PLAN-*.md`, `PRODUCT-DECISIONS-NEEDED-*.md`,
  `MEDLOW-TRIAGE-*.md`) to resolve their per-item status where it matters, and to judge **ID-less**
  work items the parser can't join.
- **Roll TC files up by module** (there are ~2000 — never list them individually; report
  `CHR: 412/470 pass` etc.).
- Rank `DUPLICATE-TRACKING` by doc-count (worst offenders first) and summarize the long tail rather
  than dumping all ~160.
- For each drift item, name the **suggested owner-skill** (see below) — but never perform the edit.
- Write the dashboard (next section).

### 3. Output
Write `test-cases/PLAN-AUDIT.md` (overwrite in place) and print a 3-line console summary: overall
health verdict, the single most important drift class, and the count of remaining points by kind.

## Dashboard format (`test-cases/PLAN-AUDIT.md`)

```markdown
# Plan Audit — {date}

**Base:** {branch} @ {short-sha}  ·  **Merged PRs cross-checked:** {n}  ·  **Verdict:** {one line}

## Program roll-up (deduped by ID)
| Kind | Done | In-progress | Todo | Blocked | % done |
|------|------|-------------|------|---------|--------|
| Stories (US) | … |
| Findings (BUG/ISSUE/ENH) | … |
| Test cases (TC) | … (rolled up by module) |

## Per-plan status
| Doc | Lifecycle | % complete | Remaining | Notes |
| living boards/ledgers first, then historical snapshots (labeled) |

## Drift & duplicates  ← the point of this report
### Stale checkboxes (merged-not-closed)   → owner: /verify-fix
| ID | Resolved by | Still open in | 
### Status conflicts (docs disagree)        → owner: /auto-heal
### Duplicate tracking (same item, N docs)  → owner: consolidate / /auto-heal
### Unverified "done" claims (PR not merged) → owner: human
### Unregistered plan docs (classify or retire) → owner: human

## Remaining points (actionable, by kind & priority)
…

## Suggested actions (REPORT-ONLY — this skill performs none of them)
- {ID}: {which skill/human should reconcile it}
```

## Status vocabulary (how glyphs/words normalize)

The scanner maps each doc's vocabulary into one enum — **DONE / IN_PROGRESS / TODO / BLOCKED /
SKIPPED / UNKNOWN**. The two boards use the *same glyphs for different meanings*, so they are parsed
under separate vocabularies (`board-impl` vs `board-test`); the ledger maps
`RESOLVED/VERIFIED/FIXED→DONE`, `OPEN→TODO`, `WONTFIX→SKIPPED`; TC frontmatter maps
`pass→DONE`, `blocked→BLOCKED`, `fail/draft→TODO`. To register a new plan doc or fix a mapping, edit
`REGISTRY` / `VOCAB` in [scan.py](plan-audit/scan.py).

## Boundaries & relationships

- **Report-only.** Writes only `test-cases/PLAN-AUDIT.md`. Never mutates a source plan, board, or
  ledger. If it finds a stale checkbox, it *reports* it and names `/verify-fix`; it does not flip it.
- **vs [`/auto-heal`](auto-heal.md):** auto-heal is the *write-side* maintainer (files findings,
  folds them into the COMPLETION-PLAN, re-prioritizes) and is event-driven. `/plan-audit` is the
  *read-side* auditor across **all** docs (drift, duplication, stale counts) and is on-demand. The
  audit's drift list is an **input to** `/auto-heal`.
- **vs `/retro`:** retro summarizes *velocity/quality trends over a window*; plan-audit is a
  *point-in-time truth reconciliation* of the plans themselves. Complementary.
- **vs `/verify-fix`:** verify-fix is the authorized closer that flips a TC/STATUS after a merge —
  the correct owner for the `STALE-CHECKBOX` items this audit surfaces.

## Design rationale (kept here instead of a separate spec)

Deterministic scanner + LLM reconciler was chosen so the **numbers are trustworthy** (hand counts
are exactly what fails here) while still handling the bespoke table docs and ID-less items an LLM is
better at. Scope is deliberately **report-only** to avoid colliding with `/auto-heal`'s write-lock on
the living docs and to honor this repo's report-only discipline. Not built (YAGNI): auto-fixing,
plan consolidation/rewrite, and a persistent DB — all out of scope for v1.
