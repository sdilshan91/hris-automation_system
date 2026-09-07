# Backfill prompt — SURVEY + AUDIT on existing findings

> Paste the block below into a **fresh session**. It is self-contained; it assumes no prior context.

---

Backfill the missing **SURVEY** and **AUDIT** on findings in `docs/QA/TEST-FINDINGS.md`, in this repo
(`/mnt/d/WORK/hris-automation_system`, an Angular 20 + ASP.NET Core 10 + PostgreSQL multi-tenant HRM SaaS).

## The rule you are enforcing

Engineering-Discipline rule **#7** (`CLAUDE.md`; detail in `.claude/rules/ledgers.md`): every
`BUG`/`ISSUE`/`ENH` must carry both —

- **SURVEY — how big is it?** How many call sites / files / services / modules exhibit this, **and the
  unit you counted**. One instance, or a class? A finding with no count cannot be sized, tiered, or
  de-duplicated, and its remedy cannot be scoped.
- **AUDIT — is it true?** Every claim verified against `src/` with `file:line` evidence, **in both
  directions**: confirm the defect *and* confirm the premise the finding asserts. **A finding's own
  text is a claim, not evidence.**

## Why this matters — read before starting, it calibrates the work

These are measured outcomes from 2026-09-07, not hypotheticals:

- `ISSUE-150` asserted *"no security exposure today… LOW (traceability, not a live defect)."* Auditing
  its three claims found **2 of 3 false**, and the false one was hiding `BUG-533` — a live permission
  bypass serving compensation figures to two personas the permission catalogue **deliberately** excludes.
  The finding's own severity rationale is what stopped anyone looking for months.
- `ENH-018`'s **own proposed remedy was a false-green**: seeding bank data would have turned
  TC-PAY-009-02/-08 green while the feature stayed unreachable in production, because no capture API exists.
- `ISSUE-449` carried **three conflicting counts** (276 / 369 / 93) for one question, purely because no
  source stated its unit.

**Expect to find that some findings are wrong.** That is the most valuable outcome, not a failure of the
task. When a finding's premise does not survive the audit, **rewrite the entry and say so explicitly** —
do not quietly delete the claim. Report contradictions in both directions (a row claiming work that is
already done, and a row understating what remains — the second is the more expensive error, because it
survives the fix).

## Scope and priority

⚠ **The working file is NOT all live.** `docs/QA/TEST-FINDINGS.md` holds **267 entries but only **219** of them are
live** (`OPEN`/`DEFERRED`). The rest must be **excluded from the backfill** — backfilling a resolved
finding is wasted work:

- **31 terminal-status entries** (`RESOLVED` / `CLOSED` / `OBSOLETE`) that belong in `TEST-FINDINGS-RESOLVED.md`.
- **7 with statuses outside the documented vocabulary** — `RECLASSIFIED`, `MERGED INTO`, `PARKED AT THE
  DECISION GATE`, `NEEDS-DECISION` (`ISSUE-032`, `ISSUE-355`, `ISSUE-499`, `BUG-489`, `DECISION-477/478/480`).
  Judge each: a `MERGED INTO` entry is a pointer and needs no survey; a `PARKED` decision does.
- **10 family sub-entries** (`### BUG-003 NOTE …`, `### BUG-003 EXTENSION …`). These belong to their
  parent finding under the family rule and are **not** separate findings — the script excludes them.

⚠ **41 findings carry a BARE header** (`### BUG-308` with the title on a later `- **Title:**` line
rather than after the id). An earlier version of the script below silently dropped all of them —
including `ISSUE-418`. If you write your own matcher, handle both header shapes or you will under-count
by ~19%.

**Filter to `OPEN`/`DEFERRED` before you start.** The true target:

| severity | live | missing survey+audit |
|---|---|---|
| **CRIT** | 3 | **3** |
| **HIGH** | 29 | **25** |
| MED | 88 | 83 |
| LOW | 79 | 78 |
| unrated | 20 | 19 |
| **total** | **219** | **208** |

Work **strictly in this order**, stopping to report at each boundary:

1. **CRIT + HIGH — 28 entries.** This is the tractable, high-value slice.
2. Stop. Report. Do not continue into MED/LOW (180 more) without being asked — it is not a batch job.

Reproduce the numbers yourself before starting (do not trust the ones above — measure the current commit):

```bash
python3 - <<'PYEOF'
import re,subprocess
s=subprocess.run(['git','show','origin/test/local-subdomains:docs/QA/TEST-FINDINGS.md'],
                 capture_output=True,text=True).stdout
TERM=('RESOLVED','WONTFIX','RETRACTED','DUPLICATE','CLOSED','OBSOLETE')
counts={}; miss={}; names={}; skipped=0; odd=[]; family=0
for b in re.split(r'\n(?=### (?:BUG|ISSUE|ENH|DECISION)-\d+)', s):
    head=b.split('\n')[0]
    m=re.match(r'### ((?:BUG|ISSUE|ENH|DECISION)-\d+)', head)
    if not m: continue
    # family sub-entries (BUG-003 NOTE / EXTENSION ...) belong to their parent, not the worklist
    if re.search(r'\b(NOTE|note|EXTENSION|EXTENDED)\b', head): family+=1; continue
    fid=m.group(1)
    # title may follow the id on the header line, OR live on a later "- **Title:**" line
    t=re.sub(r'^### \S+\s*[-.:·—]?\s*','',head).strip()
    if not t:
        tl=re.search(r'^- \*\*(?:Title|Summary)[^:]*:\*\*\s*(.+)$', b, re.M) \
           or re.search(r'^\| \*\*Title\*\* \| (.+?) \|', b, re.M)
        t=(tl.group(1) if tl else '(title in body - read the entry)')
    st=re.search(r'^- \*\*Type / Severity / Status:\*\*(.+)$', b, re.M)
    txt=(st.group(1) if st else '').upper()
    if any(x in txt for x in TERM): skipped+=1; continue
    if 'OPEN' not in txt and 'DEFERRED' not in txt: odd.append(fid); continue
    sv=re.search(r'\b(CRIT|HIGH|MED|LOW)\b', txt); sv=sv.group(1) if sv else 'unrated'
    a_=bool(re.search(r'\.(cs|ts|js|sql|yml|md)[`\'"]?:\d+', b))
    v_=bool(re.search(r'\b\d+\s+(sites?|files?|call sites?|instances?|places?|services?)\b',b,re.I)) \
       or bool(re.search(r'\b\d+\s+of\s+\d+\b', b))
    counts[sv]=counts.get(sv,0)+1
    if not (a_ and v_):
        miss[sv]=miss.get(sv,0)+1
        names.setdefault(sv,[]).append(fid+" - "+t[:70])
print("LIVE: %d | terminal skipped: %d | family sub-entries: %d | odd status: %d %s"
      % (sum(counts.values()), skipped, family, len(odd), odd))
for k in ['CRIT','HIGH','MED','LOW','unrated']:
    if k in counts: print("  %-8s %4d live, %4d missing" % (k, counts[k], miss.get(k,0)))
print("\n>>> WORKLIST - CRIT then HIGH:")
for k in ['CRIT','HIGH']:
    for x in names.get(k,[]): print("  %-5s %s" % (k,x))
PYEOF
```

The detector is a **heuristic** (it looks for `file.cs:123` patterns and for "N sites/files/services").
Treat its output as a worklist, not a verdict — read each entry and judge whether the survey and audit are
genuinely present. Some entries will have real evidence in a form the regex misses; say so and move on.

## What to write into each finding

Add to the body (keep the existing schema; do not restructure entries):

```markdown
- **SURVEY:** <N> <unit> — e.g. "3 of 8 upload paths call the validator (non-test `src/backend`)".
  Say what you counted and what you excluded. If it is genuinely a single site, say "1 site" and why
  it cannot recur elsewhere.
- **AUDIT (2026-XX-XX):** <claim-by-claim verdict with file:line>. State CONFIRMED / PARTIALLY TRUE /
  FALSE per claim. Where a claim is false, say what is true instead.
```

If you cannot complete one within reasonable effort, write `SURVEY: not done` / `AUDIT: not done` with a
one-line reason. **A visible gap is the goal; an implied one is what this rule exists to prevent.**

## Hard boundaries — do not cross these

- **You may edit finding bodies. You may NOT change a finding's Status to `RESOLVED`/`FIXED`/`VERIFIED`.**
  Only `/verify-fix` may do that, and only on re-run TC evidence. `WONTFIX` is human-only.
- **Do not edit `src/`.** This is a documentation task. If the audit reveals a defect worth fixing, file
  or extend a finding — do not fix it inline.
- **Never weaken or skip a test** to make anything pass.
- **`docs/QA/TEST-FINDINGS.md` is ledger-locked**: only **one open PR** may touch it at a time. Check with
  `gh pr list --state open --json number,files` before starting, and batch your work into **one** PR.

## Repo-specific traps that will cost you time

- **Next free finding ID: scan BOTH `docs/QA/TEST-FINDINGS.md` and `docs/QA/TEST-FINDINGS-RESOLVED.md`.**
  The ledger was split 2026-09-01. Also note `BUG-522` exists in `src/` with **no ledger entry**
  (`ISSUE-524`), so a naive max+1 can collide.
- **The summary table at the top of `TEST-FINDINGS.md` is asserted by a test.** `LedgerTraceabilityTests`
  fails if the counts drift. **Recount, never hand-edit to go green.**
- **Run tests only via** `bash scripts/run-backend-tests.sh src/backend/HRM.sln [args]` — never bare
  `dotnet test` (ISSUE-312), and always pass the `.sln` path, because running it from the repo root while
  inside a worktree silently tests the **wrong tree** and reports a false green (ISSUE-492).
  For this task: `--filter "FullyQualifiedName~LedgerTraceability"` is enough.
- **Branch from `origin/test/local-subdomains`, NOT `origin/main`** — main is ~987 commits stale.
- **Work in your own git worktree on your own branch** (Engineering-Discipline rule #9).
- Several findings cite **stale line numbers** (e.g. `ISSUE-116` cites `InterviewService.cs:418`; the real
  read is `:474`). Correct them as you audit — that is part of the audit.
- Ledgers are **claims, not evidence**, in both directions. `STATUS.md` currently contradicts itself about
  whether RLS is enabled (`:200` says ON, `:297` says OFF; `appsettings.json:48-50` ships **`true`**).

## Definition of done

- Every live (`OPEN`/`DEFERRED`) CRIT and HIGH finding either has both a SURVEY and an AUDIT, or carries an explicit
  `not done` marker with a reason.
- Any finding whose premise the audit falsified is **rewritten**, with the correction stated plainly.
- Summary table recounted; `LedgerTraceabilityTests` green.
- One PR, docs-only, describing: how many entries you touched, **how many premises turned out to be wrong**,
  and any new findings the audit surfaced (file those too — with their own survey and audit).
- Report anything you had to leave open rather than guessing.
