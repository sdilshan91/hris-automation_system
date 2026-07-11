---
name: retro
description: Engineering retrospective from git history + the findings ledgers over a time window. Summarizes what shipped, velocity/quality trends vs the previous retro, notable incidents, and concrete action items — written to the shared vault so trends accumulate. Read-only on code. Use weekly, at a milestone, or before a planning session.
user_invocable: true
---

# Retro (engineering retrospective)

> Adapted for this repo (MIT) from the gstack `/retro` skill. Retargeted to our git history, PRs,
> `docs/QA/` ledgers, and the Obsidian vault as the trend store.

Turns raw git/PR/ledger activity over a window into an honest retrospective: what actually shipped,
whether quality and velocity are trending up or down **vs the previous retro**, what hurt, and a
short list of action items. Read-only on the codebase — it only writes the retro note.

## Usage

```
/retro                    # last 7 days (default)
/retro --since 2026-07-01 # explicit start date
/retro --since v1.4.0     # since a tag/ref
/retro --sprint           # last 14 days
```

## Inputs to gather (read-only)

1. **Commits & PRs in the window** — `git log --since=<date> --stat`, merged PRs
   (`gh pr list --state merged --search "merged:>=<date>"` or the GitHub MCP). Group by area
   (module / `src/backend` / `src/frontend` / `.claude` tooling / docs).
2. **Findings deltas** — diff `docs/QA/TEST-FINDINGS.md` and `TEST-STATUS.md` over the window:
   how many findings **opened** vs **RESOLVED**, by severity; net open count; any new **CRITICAL/HIGH**.
   (Use `git log -p --since=<date> -- docs/QA/TEST-FINDINGS.md` to see the churn.)
3. **Incidents** — any new notes in `docs/vault/incidents/` this window.
4. **Previous retro** — read the most recent `docs/vault/retros/*.md` to compute **trend deltas**
   (this is what makes a retro more than a changelog). If none exists, this is the baseline.

## What to produce

Be candid — this follows the advisor stance, not a hype reel. Lead with what's actually true.

- **Shipped** — the meaningful features/fixes that landed (PRs), grouped by area. Not raw LOC (AI
  inflates it); count *logical* deliverables — stories completed, findings resolved, tooling added.
- **Quality trend** — findings opened vs resolved this window and the direction vs last retro. Is
  the open-findings backlog growing or shrinking? Any regression (a RESOLVED finding reopened)?
  Are CRITICAL/HIGH being cleared faster than they appear?
- **Velocity trend** — stories/PRs merged vs the previous window. Flag if it's dropping *and why*
  (blocked on a decision gate? stuck in a remediation loop? one hard bug eating the week?).
- **What hurt** — the top 1-3 friction points (a flaky suite, a recurring root-cause class like
  the InMemory-masks-Postgres or BUG-003 tenant-split themes, a decision-gate stall). Name them.
- **What went well** — genuine wins worth repeating (say it plainly; don't manufacture balance).
- **Action items** — 3-5 concrete, owned, checkable next steps. Not "improve quality" — e.g.
  "clear the 4 open HIGH findings in Payroll before starting Training module."

## Output

Write to `docs/vault/retros/{YYYY-MM-DD}.md` (create the folder if absent) with vault frontmatter,
and cross-link the previous retro with `[[YYYY-MM-DD]]` so backlinks form a timeline:

```markdown
---
type: retro
window: {start} → {today}
---

# Retro {today}

**Window:** {start} → {today}  ·  **Prev:** [[{previous-retro-date}]]

## Shipped
- ...

## Quality trend
- Findings: +{opened} / −{resolved} (net {±N} open). CRITICAL/HIGH: ...
- vs last retro: {better/worse + why}

## Velocity trend
- {N} PRs / {M} stories merged (prev: {…}). {commentary}

## What hurt
- ...

## What went well
- ...

## Action items
- [ ] {owned, concrete}
```

Then print a 3-line summary to the user: shipped headline, the one trend that matters most, and the
top action item.

## Rules

- **Read-only on code.** This skill reads git/PRs/ledgers and writes exactly one retro note (plus
  the folder). It does not edit `src/`, tests, or open PRs.
- **Evidence over vibes.** Every trend claim cites a number (findings delta, PR count) — no
  "things felt slow." If the data is thin (short window, few commits), say so rather than padding.
- **No secrets/PII** in the note (vault rule). Reference findings by ID, not by dumping content.
- Complements `/auto-heal` (which re-sorts the living plan continuously); `/retro` is the periodic
  step-back that spots trends a single sweep can't.
