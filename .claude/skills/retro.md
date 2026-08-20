---
name: retro
description: Engineering retrospective from git history + the findings ledgers over a time window. Summarizes what shipped, velocity/quality trends vs the previous retro, notable incidents, and concrete action items — plus a skill-friction pass that proposes improvements to the agent system itself. Written to the shared vault so trends accumulate. Read-only on code and on `.claude/`. Use weekly, at a milestone, or before a planning session.
user_invocable: true
---

# Retro (engineering retrospective)

> Adapted for this repo (MIT) from the gstack `/retro` skill. Retargeted to our git history, PRs,
> `docs/QA/` ledgers, and the Obsidian vault as the trend store.

Turns raw git/PR/ledger activity over a window into an honest retrospective: what actually shipped,
whether quality and velocity are trending up or down **vs the previous retro**, what hurt, and a
short list of action items — plus a **skill-friction pass** that turns recurring friction into proposed
improvements to the agent system itself (`.claude/skills/`, `.claude/agents/`). Read-only on the codebase
*and* on `.claude/` — it only writes the retro note.

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
5. **Skill-friction evidence** — feeds the skill-friction pass below. Read only what the window touched:
   - `git log --since=<date> --diff-filter=A --name-only -- '.claude/agent-memory/*/feedback-*.md'` —
     corrections that got promoted to persistent agent memory this window.
   - `git log --since=<date> --oneline -- .claude/skills .claude/agents .claude/hooks` — churn in the
     agent system itself; a skill file edited twice in one window is a friction signal.
   - Root causes in the window's `TEST-FINDINGS.md` entries — **cluster them**; a root-cause class that
     recurs is a process defect, not N separate bugs.
   - Stories reverted `[~]`→`[ ]` in `docs/BA/STATUS.md` (the remediation loop gave up), and any
     `OUT-OF-LANE:` blocks filed by `/auto-heal`.
   - `.claude/hooks/vault-compliance.log` if present (gitignored) — agents skipping the vault contract.

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

## Skill-friction pass (agent-system self-improvement)

The other passes ask *how is the product doing*. This one asks **how is the agent system doing** — and it
is the only place in this repo where friction is fed back into [.claude/skills/](.claude/skills/) and
[.claude/agents/](.claude/agents/). The vault and `.claude/agent-memory/` capture *domain* knowledge;
nothing else improves the instructions themselves.

**The bar: two independent occurrences.** Never propose a change from a single incident — a one-off
correction belongs in `.claude/agent-memory/{agent}/`, not in a skill. Propose a diff only when the same
friction appears in **≥2 stories, findings, or sessions** in the window (or once here plus once cited in
the previous retro). State which occurrences you are counting.

| Signal | Likely fix |
|---|---|
| The same root-cause class in ≥2 findings (InMemory-masks-Postgres, FE/BE contract drift, tenant split) | A rule in the owning **agent** definition, or a step in the verify gate |
| A documented rule the agent keeps violating | **Structural enforcement, not louder prose** — a `PreToolUse` hook, a gate step, an artefact the agent must emit. A rule that failed twice fails a third time however it is worded |
| A skill file edited twice in one window | The skill is ambiguous or wrong — fix the ambiguity; don't stack another clause on top |
| The remediation loop gave up on a story | A missing precondition in [implement-all.md](.claude/skills/implement-all.md), or a gate that cannot be satisfied as written |
| A section that never fired across many sessions | **Propose deleting it.** Ask "what can we remove?" as deliberately as "what should we add?" |

**Never propose weakening a safety rule.** The report-only boundary (`/test-all`, the read-only
auditors), the test-integrity rule, the decision gate, the 3-attempt remediation cap and the `PreToolUse`
guards exist *because* agents push against them — recurring friction with a guard rail is evidence it is
working, not evidence it is wrong. If a guard is genuinely mis-scoped (blocking legitimate work), report
it as a **scoping** fix with the false-positive cases named — never as "relax the rule."

**Report-only, capped at 3.** This pass emits *proposed diffs* in the retro note — file, section, and the
exact before/after text — ranked, at most three. It does **not** edit `.claude/skills/`,
`.claude/agents/`, `.claude/hooks/` or `CLAUDE.md`; the human applies what they agree with. More than
three proposals means you are below the two-occurrence bar. Same contract as `@principal-advisor` and
`/gap-analysis`: the agent reports, the human decides.

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

## Skill friction
- **{friction}** — seen in {occurrence 1}, {occurrence 2}.
  **Proposed:** `{file}` § {section} — {change}. Why structural, not prose: {reason}.
- _(or: nothing cleared the two-occurrence bar this window.)_

## Action items
- [ ] {owned, concrete}
```

Then print a 3-line summary to the user: shipped headline, the one trend that matters most, and the
top action item — plus one line naming any skill-friction proposals awaiting their approval.

## Rules

- **Read-only on code.** This skill reads git/PRs/ledgers and writes exactly one retro note (plus
  the folder). It does not edit `src/`, tests, or open PRs.
- **Read-only on the agent system too.** The skill-friction pass proposes diffs to `.claude/**` inside
  the note; it never applies them. Applying one is a separate, explicitly-requested step.
- **Evidence over vibes.** Every trend claim cites a number (findings delta, PR count) — no
  "things felt slow." If the data is thin (short window, few commits), say so rather than padding.
- **No secrets/PII** in the note (vault rule). Reference findings by ID, not by dumping content.
- Complements `/auto-heal` (which re-sorts the living plan continuously); `/retro` is the periodic
  step-back that spots trends a single sweep can't.
- The skill-friction pass is the deliberate, scoped alternative to an always-on observer skill: same
  closed loop (friction → skill improvement), but on the weekly cadence, with no shared append-only log
  for parallel subagents to race on, and with every `.claude/**` edit human-approved.
