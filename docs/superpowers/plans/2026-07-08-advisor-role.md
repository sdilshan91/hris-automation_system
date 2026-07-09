# `/advisor` Technical-Consultant Role — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a report-only `/advisor` skill + read-only `@principal-advisor` agent that produces an evidence-anchored technical advisory covering the 3 gaps nothing owns today — tech-radar currency, ADR-drift, complexity/dead-code — plus light synthesis linking existing auditors.

**Architecture:** A thin `/advisor` orchestrator skill dispatches the read-only `@principal-advisor` synthesis agent. The agent runs a deterministic `currency-scan.py` (dependency currency) + drives `npx knip` / `dotnet build` warnings / the `crap-analysis` skill (dead-code/complexity) + reads `docs/vault/decisions/*.md` against the code (ADR-drift), then returns a compact ranked advisory. The skill writes the artifacts. Nothing edits `src/`.

**Tech Stack:** Claude Code skill + agent markdown; Python 3 (scanner, matching existing `.claude/hooks/scripts/*.py`); `dotnet list package`, `npm outdated`/`npm audit`, `npx knip`; the vendored `dotnet-skills:crap-analysis` skill; `microsoft-learn` MCP + `WebSearch` for tech scouting.

## Global Constraints

- **Report-only — advise + document.** Never edit `src/`, delete code, bump dependencies, or wire fitness tests. Writes only to `advisory-reports/`, `docs/radar/`, and proposed ADRs in `docs/vault/decisions/`; folds actionable items into `/auto-heal` + `test-cases/TEST-FINDINGS.md`.
- **Evidence-or-it-doesn't-exist.** Every finding cites a tool output / `file:line` / CVE / CRAP number / drifted-ADR reference; carries a confidence rating and a cost-of-inaction; output separates "What the tools measured" (facts) from "What I recommend" (rated judgment).
- **Degrade gracefully.** If Knip / CRAP / Roslyn analyzers are not wired, run what's present and *flag the gap* — never fail the run.
- **Version-aware.** Verify claims against `microsoft-learn` MCP / official docs; be .NET 10 / Angular 20 aware; new tech defaults to Assess/Hold on the radar.
- **Follow existing conventions:** review-agent frontmatter (`name`, `description`, explicit `tools` allowlist, `model: claude-opus-4-8`, `maxTurns`, `memory: project`, NO `permissionMode`, NO Write/Edit for read-only auditors); skill frontmatter (`name`, `description`, `user_invocable: true`); Python scanner style mirrors `.claude/skills/plan-audit/scan.py`.
- **v1 = 3 passes only.** No full-9-area synthesis, no ArchUnitNET authoring, no paid-tool drivers, no auto-fixes.

---

## File Structure

- Create `.claude/skills/advisor/currency-scan.py` — deterministic dependency-currency scanner (the only unit-tested code).
- Create `.claude/skills/advisor/test_currency_scan.py` — unit tests for the scanner's parsers.
- Create `.claude/agents/review/principal-advisor.md` — read-only synthesis agent.
- Create `.claude/skills/advisor.md` — orchestrator skill.
- Create `docs/radar/tech-radar.md` — seed radar (Adopt/Trial/Assess/Hold).
- Create `advisory-reports/.gitkeep` — output directory.
- Modify `CLAUDE.md` — skills table row, agent-team table row, directory-tree entries.

---

### Task 1: Dependency-currency scanner (`currency-scan.py`)

Deterministic scanner: runs `dotnet list package` (outdated/vulnerable/deprecated) + `npm outdated`/`npm audit`, normalizes each into a common record, emits JSON. Pure parser functions are unit-tested against fixtures; the `main()` shells out to the real tools with graceful degradation.

**Files:**
- Create: `.claude/skills/advisor/currency-scan.py`
- Test: `.claude/skills/advisor/test_currency_scan.py`

**Interfaces:**
- Produces: `parse_dotnet(json_text: str, kind: str) -> list[dict]` where kind ∈ {"outdated","vulnerable","deprecated"}; `parse_npm_outdated(json_text: str) -> list[dict]`; `parse_npm_audit(json_text: str) -> list[dict]`. Each record: `{"ecosystem","package","current","latest","kind","severity","detail"}` (severity "" when N/A).
- Later tasks (the agent) consume the emitted JSON: `{ "dotnet": [...records], "npm": [...records], "tools_run": {...}, "gaps": [...] }` on stdout.

- [ ] **Step 1: Write the failing test**

```python
# .claude/skills/advisor/test_currency_scan.py
import json, importlib.util, os
spec = importlib.util.spec_from_file_location(
    "cs", os.path.join(os.path.dirname(__file__), "currency-scan.py"))
cs = importlib.util.module_from_spec(spec); spec.loader.exec_module(cs)

DOTNET_OUTDATED = json.dumps({"projects": [{"frameworks": [{"topLevelPackages": [
    {"id": "AutoMapper", "resolvedVersion": "13.0.1", "latestVersion": "15.1.1"}]}]}]})
DOTNET_VULN = json.dumps({"projects": [{"frameworks": [{"topLevelPackages": [
    {"id": "AutoMapper", "resolvedVersion": "13.0.1",
     "vulnerabilities": [{"severity": "High", "advisoryurl": "https://x/GHSA-rvv3"}]}]}]}]})
NPM_AUDIT = json.dumps({"vulnerabilities": {"lodash": {
    "name": "lodash", "severity": "high", "via": [{"title": "Proto pollution", "url": "https://y"}]}}})

def test_parse_dotnet_outdated():
    r = cs.parse_dotnet(DOTNET_OUTDATED, "outdated")
    assert r == [{"ecosystem": "dotnet", "package": "AutoMapper", "current": "13.0.1",
                  "latest": "15.1.1", "kind": "outdated", "severity": "", "detail": ""}]

def test_parse_dotnet_vulnerable():
    r = cs.parse_dotnet(DOTNET_VULN, "vulnerable")
    assert len(r) == 1 and r[0]["kind"] == "vulnerable" and r[0]["severity"] == "High"
    assert "GHSA-rvv3" in r[0]["detail"]

def test_parse_npm_audit():
    r = cs.parse_npm_audit(NPM_AUDIT)
    assert r == [{"ecosystem": "npm", "package": "lodash", "current": "", "latest": "",
                  "kind": "vulnerable", "severity": "high", "detail": "Proto pollution https://y"}]

if __name__ == "__main__":
    test_parse_dotnet_outdated(); test_parse_dotnet_vulnerable(); test_parse_npm_audit()
    print("ALL PASS")
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `python .claude/skills/advisor/test_currency_scan.py`
Expected: FAIL — `FileNotFoundError`/`ModuleNotFoundError` (currency-scan.py absent) or `AttributeError: module 'cs' has no attribute 'parse_dotnet'`.

- [ ] **Step 3: Write the minimal implementation**

```python
#!/usr/bin/env python3
"""Deterministic dependency-currency scanner for the /advisor skill.
Runs dotnet + npm currency/vuln checks, normalizes to one record shape, emits JSON.
Report-only: shells out to read-only listing commands; writes nothing. Fails open
(a missing tool becomes a 'gaps' entry, never an error)."""
import sys, os, json, subprocess

def _dig(obj, *keys):
    for k in keys:
        obj = obj.get(k, {}) if isinstance(obj, dict) else {}
    return obj

def parse_dotnet(json_text, kind):
    out = []
    try:
        data = json.loads(json_text or "{}")
    except ValueError:
        return out
    for proj in data.get("projects", []):
        for fw in proj.get("frameworks", []) or []:
            for pkg in fw.get("topLevelPackages", []) or []:
                rec = {"ecosystem": "dotnet", "package": pkg.get("id", ""),
                       "current": pkg.get("resolvedVersion", ""),
                       "latest": pkg.get("latestVersion", ""),
                       "kind": kind, "severity": "", "detail": ""}
                vulns = pkg.get("vulnerabilities") or []
                if vulns:
                    rec["severity"] = vulns[0].get("severity", "")
                    rec["detail"] = vulns[0].get("advisoryurl", "")
                if pkg.get("deprecationReasons"):
                    rec["detail"] = ",".join(pkg["deprecationReasons"])
                out.append(rec)
    return out

def parse_npm_outdated(json_text):
    out = []
    try:
        data = json.loads(json_text or "{}")
    except ValueError:
        return out
    for name, info in (data or {}).items():
        out.append({"ecosystem": "npm", "package": name,
                    "current": info.get("current", ""), "latest": info.get("latest", ""),
                    "kind": "outdated", "severity": "", "detail": ""})
    return out

def parse_npm_audit(json_text):
    out = []
    try:
        data = json.loads(json_text or "{}")
    except ValueError:
        return out
    for name, info in (data.get("vulnerabilities") or {}).items():
        via = info.get("via") or []
        first = next((v for v in via if isinstance(v, dict)), {})
        detail = (first.get("title", "") + " " + first.get("url", "")).strip()
        out.append({"ecosystem": "npm", "package": info.get("name", name),
                    "current": "", "latest": "", "kind": "vulnerable",
                    "severity": info.get("severity", ""), "detail": detail})
    return out

def _run(cmd, cwd):
    try:
        p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, timeout=180)
        return p.stdout
    except Exception:
        return None

def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    be = os.path.join(root, "src", "backend")
    fe = os.path.join(root, "src", "frontend")
    dotnet, npm, gaps, ran = [], [], [], {}
    for kind, flag in (("outdated", "--outdated"), ("vulnerable", "--vulnerable"),
                       ("deprecated", "--deprecated")):
        out = _run(["dotnet", "list", "package", flag, "--format", "json"], be)
        ran[f"dotnet-{kind}"] = out is not None
        if out is None:
            gaps.append(f"dotnet list package {flag} unavailable (SDK <8 or dotnet missing)")
        else:
            dotnet += parse_dotnet(out, kind)
    o = _run(["npm", "outdated", "--json"], fe); ran["npm-outdated"] = o is not None
    npm += parse_npm_outdated(o) if o else []
    a = _run(["npm", "audit", "--json"], fe); ran["npm-audit"] = a is not None
    npm += parse_npm_audit(a) if a else []
    if o is None and a is None:
        gaps.append("npm outdated/audit unavailable (npm missing or no node_modules)")
    print(json.dumps({"dotnet": dotnet, "npm": npm, "tools_run": ran, "gaps": gaps}, indent=2))

if __name__ == "__main__":
    main()
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `python .claude/skills/advisor/test_currency_scan.py`
Expected: `ALL PASS`

- [ ] **Step 5: Smoke-run against the repo (graceful degradation)**

Run: `python .claude/skills/advisor/currency-scan.py . | python -c "import sys,json; d=json.load(sys.stdin); print('dotnet',len(d['dotnet']),'npm',len(d['npm']),'gaps',d['gaps'])"`
Expected: prints counts + any gaps; **never errors** (AutoMapper HIGH should appear under dotnet vulnerable if the SDK supports `--format json`; otherwise a gap line).

- [ ] **Step 6: Commit**

```bash
git add .claude/skills/advisor/currency-scan.py .claude/skills/advisor/test_currency_scan.py
git commit -m "feat(advisor): deterministic dependency-currency scanner + tests"
```

---

### Task 2: Output scaffolding (radar seed + advisory dir)

**Files:**
- Create: `docs/radar/tech-radar.md`
- Create: `advisory-reports/.gitkeep`

**Interfaces:**
- Produces: the `docs/radar/tech-radar.md` path (skill/agent update it) and the `advisory-reports/` dir (skill writes `{scope}-{date}.md` here).

- [ ] **Step 1: Create the radar seed** — write `docs/radar/tech-radar.md`:

```markdown
# Tech Radar — HRM SaaS

> Living artifact maintained by `/advisor`. Rings: **Adopt** (proven, use freely) ·
> **Trial** (ready, prove on a project) · **Assess** (worth exploring) · **Hold** (don't start
> anything new with this). New tech defaults to **Assess/Hold** — an Adopt/Trial move must state
> fit-for-this-stack + migration cost. Cross-references `docs/TOOLING-ADOPTION-PLAN.md` and the ADRs.

_Last updated: (pending first /advisor run)_

## Languages & Frameworks
| Item | Ring | Movement | Note (fit-for-our-stack) |
|------|------|----------|--------------------------|

## Platforms & Infra
| Item | Ring | Movement | Note |
|------|------|----------|------|

## Tools
| Item | Ring | Movement | Note |
|------|------|----------|------|

## Techniques
| Item | Ring | Movement | Note |
|------|------|----------|------|
```

- [ ] **Step 2: Create the advisory output dir**

Run: `mkdir -p advisory-reports && printf '# Advisory reports\n\nGenerated by /advisor (report-only). One file per run: {scope}-{YYYY-MM-DD}.md\n' > advisory-reports/.gitkeep`
Expected: `advisory-reports/.gitkeep` exists.

- [ ] **Step 3: Verify**

Run: `test -f docs/radar/tech-radar.md && test -f advisory-reports/.gitkeep && echo OK`
Expected: `OK`

- [ ] **Step 4: Commit**

```bash
git add docs/radar/tech-radar.md advisory-reports/.gitkeep
git commit -m "feat(advisor): seed tech-radar + advisory-reports scaffolding"
```

---

### Task 3: `@principal-advisor` synthesis agent

**Files:**
- Create: `.claude/agents/review/principal-advisor.md`

**Interfaces:**
- Consumes: `currency-scan.py` output (Task 1); `docs/radar/tech-radar.md` + `advisory-reports/` (Task 2).
- Produces: invoked by the `/advisor` skill (Task 4) via the Agent tool; returns a structured advisory (the skill writes it to disk).

- [ ] **Step 1: Write the agent file** with EXACTLY this frontmatter, then the body per the content checklist below:

```yaml
---
name: principal-advisor
description: "Read-only technical-consultant synthesis agent. Runs the /advisor v1 passes (dependency-currency scan, ADR-drift check, complexity/dead-code) and ingests existing auditor reports, then returns ONE ranked, evidence-anchored advisory. REPORT-ONLY — never edits src/, deletes code, or bumps deps. Use via the /advisor skill."
tools:
  - Read
  - Glob
  - Grep
  - Bash
  - WebSearch
  - WebFetch
  - mcp__microsoft-learn__microsoft_docs_search
  - mcp__microsoft-learn__microsoft_docs_fetch
model: claude-opus-4-8
maxTurns: 40
memory: project
---
```

Body MUST contain these sections (drawn verbatim from the spec — no placeholders):
1. `# Principal Advisor Agent (read-only)` + one-line role.
2. `## Execution Contract (non-negotiable)` — REPORT-ONLY (no `src/` edits, no deletes, no dep bumps); **evidence-or-it-doesn't-exist**; verify with tools + cite `file:line`/CVE/CRAP/ADR; version-aware (verify via microsoft-learn before asserting).
3. `## Passes` with three subsections:
   - **Tech-radar / currency:** run `python "$CLAUDE_PROJECT_DIR/.claude/skills/advisor/currency-scan.py" .`; read `docs/TOOLING-ADOPTION-PLAN.md`; scout via WebSearch + microsoft-learn (new tech → Assess/Hold, justify fit + migration cost).
   - **ADR-drift:** `Glob docs/vault/decisions/*.md`; for each, verify Decision/Consequences vs code/config with concrete checks (examples verbatim: RLS-planned vs `appsettings*.json` `Rls:Enabled=false`; Gitleaks advisory vs `.github/workflows/gitleaks.yml` `--exit-code`; GlitchTip DSN wired in `appsettings.Development.json`). Flag drifted/stale/superseded.
   - **Complexity/dead-code:** `npx knip` (FE), `dotnet build` warnings (`IDE0051` class), `crap-analysis` skill; **cross-check every dead-code candidate against `@integration-enforcer` wiring** to filter DI/MediatR/EF/route false positives; output candidates ONLY. If a tool is unwired, add a `gaps` line, don't fail.
4. `## Synthesis & honesty` — dedupe; rank by severity × effort × blast-radius; every finding: evidence + confidence + cost-of-inaction; split "What the tools measured" vs "What I recommend"; **adversarial self-pass** (drop generic/trend-chasing/version-blind items); cap findings; "would a senior engineer bother?" filter.
5. `## Output format` — a fenced advisory template: header (scope/date/verdict) → "Measured facts" table → "Recommendations" (ranked, each: title · evidence `file:line`/CVE/CRAP · confidence · cost-of-inaction · owner-skill) → "Tech-radar deltas" → "ADR-drift" → "Gaps (tools not wired)".
6. `## Out-of-lane discovery contract (auto-heal)` — the standard `OUT-OF-LANE:` block (type · severity · where · what · why-out-of-lane · suggested action) feeding `/auto-heal` + `TEST-FINDINGS.md`.
7. `## Rules` — restate report-only; dead-code = candidates only; no ledger spam.

- [ ] **Step 2: Verify frontmatter parses + required sections present**

Run:
```bash
python -c "import re,sys; t=open('.claude/agents/review/principal-advisor.md',encoding='utf-8').read(); \
import yaml; fm=yaml.safe_load(t.split('---')[1]); assert fm['name']=='principal-advisor'; \
assert 'Write' not in fm['tools'] and 'Edit' not in fm['tools']; \
assert all(s in t for s in ['Execution Contract','ADR-drift','adversarial','Out-of-lane','cost-of-inaction']); \
print('agent OK')"
```
Expected: `agent OK` (fails loudly if Write/Edit present or a required section is missing).

- [ ] **Step 3: Commit**

```bash
git add .claude/agents/review/principal-advisor.md
git commit -m "feat(advisor): principal-advisor read-only synthesis agent"
```

---

### Task 4: `/advisor` orchestrator skill

**Files:**
- Create: `.claude/skills/advisor.md`

**Interfaces:**
- Consumes: `@principal-advisor` (Task 3), `currency-scan.py` (Task 1), scaffolding (Task 2).
- Produces: the `/advisor` user command; writes `advisory-reports/{scope}-{date}.md`, updates `docs/radar/tech-radar.md`, proposes ADRs.

- [ ] **Step 1: Write the skill file** with EXACTLY this frontmatter:

```yaml
---
name: advisor
description: "Technical-consultant advisory (REPORT-ONLY). Produces an evidence-anchored, ranked advisory over 3 net-new passes — tech-radar/dependency currency, ADR-drift, complexity/dead-code — plus light synthesis that LINKS (never re-runs) the existing auditors. Writes advisory-reports/, updates docs/radar/tech-radar.md, proposes ADRs. Never edits src, deletes code, or bumps deps. Use for a periodic tech-health + decision-currency review."
user_invocable: true
---
```

Body MUST contain (no placeholders):
1. `# Technical Advisor (report-only)` + the "not a mega-agent; synthesizes, doesn't duplicate" framing.
2. `## Usage` fenced block: `/advisor` · `--radar` · `--adr` · `--deadcode` · `--module CHR`.
3. `## Process`: (a) parse flags → which passes; (b) **delegate to `@principal-advisor`** with the pass set + module scope (keep the conclusion, not raw output); (c) write `advisory-reports/{scope}-{YYYY-MM-DD}.md` from the agent's synthesis; (d) update `docs/radar/tech-radar.md`; (e) for ADR-drift, draft proposed ADR updates in `docs/vault/decisions/` (status `proposed`) for human acceptance; (f) fold actionable items into `/auto-heal` + `test-cases/TEST-FINDINGS.md`; (g) print a 3-line summary.
4. `## Report-only boundary` — restate: writes only advisory/radar/proposed-ADR; never edits `src/`, deletes, or bumps deps; dead-code = candidates for human confirmation.
5. `## Relationship to other tooling` — complements/links `/security-audit`, `/design-review`, `integration-enforcer`, `test-authenticator`, `fault-diagnosis`; forward-looking vs `/retro`'s backward-looking; feeds `/auto-heal`.
6. `## Graceful degradation` — if Knip/CRAP/analyzers unwired, run what's present and flag the gap (points at Wave 2/3 of `docs/TOOLING-ADOPTION-PLAN.md`).

- [ ] **Step 2: Verify frontmatter + required content**

Run:
```bash
python -c "import yaml; t=open('.claude/skills/advisor.md',encoding='utf-8').read(); \
fm=yaml.safe_load(t.split('---')[1]); assert fm['name']=='advisor' and fm['user_invocable'] is True; \
assert all(s in t for s in ['--radar','--adr','--deadcode','principal-advisor','advisory-reports','tech-radar','Report-only']); \
print('skill OK')"
```
Expected: `skill OK`

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/advisor.md
git commit -m "feat(advisor): /advisor orchestrator skill"
```

---

### Task 5: Wire into CLAUDE.md

**Files:**
- Modify: `CLAUDE.md` (Skills table; Agent Team table; the `.claude/skills/` and `.claude/agents/review/` directory-tree listings)

- [ ] **Step 1: Add the skills-table row** — after the `/plan-audit` row (or the last analysis skill), insert:

```
| `/advisor [--radar\|--adr\|--deadcode\|--module]` | Local | **Technical-consultant advisory (REPORT-ONLY).** Evidence-anchored, ranked advisory over 3 net-new passes — tech-radar/dependency currency, ADR-drift, complexity/dead-code — plus light synthesis that links (never re-runs) the existing auditors. Writes `advisory-reports/`, updates `docs/radar/tech-radar.md`, proposes ADRs; folds actionable items into `/auto-heal`. Never edits src / deletes / bumps deps. Drives `@principal-advisor`. |
```

- [ ] **Step 2: Add the agent-team-table row** — after `@integration-enforcer`:

```
| `@principal-advisor` | **Read-only technical-consultant synthesizer.** Runs the /advisor v1 passes (dependency currency, ADR-drift, complexity/dead-code) + ingests existing auditor reports → ONE ranked, evidence-anchored advisory. REPORT-ONLY — never edits code/opens PRs. | _(no branch — advisory only)_ | _none (read-only: Read/Glob/Grep/Bash/WebSearch/WebFetch + microsoft-learn)_ |
```

- [ ] **Step 3: Add directory-tree entries** — under `.claude/skills/` add `│   │   ├── advisor.md            # Technical-consultant advisory (report-only); + advisor/currency-scan.py`; under `.claude/agents/review/` add `│   │   ├── principal-advisor.md   # Read-only technical-consultant synthesizer`.

- [ ] **Step 4: Verify**

Run: `grep -q '/advisor' CLAUDE.md && grep -q 'principal-advisor' CLAUDE.md && echo OK`
Expected: `OK`

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(advisor): register /advisor + @principal-advisor in CLAUDE.md"
```

---

### Task 6: End-to-end smoke verification

**Files:** none (verification only)

- [ ] **Step 1: Scanner produces real data**

Run: `python .claude/skills/advisor/currency-scan.py . | python -c "import sys,json;d=json.load(sys.stdin);print('OK' if isinstance(d['dotnet'],list) and isinstance(d['npm'],list) and 'gaps' in d else 'BAD')"`
Expected: `OK`

- [ ] **Step 2: Both artifacts parse (frontmatter + sections)**

Run: the two `python -c` verifier one-liners from Task 3 Step 2 and Task 4 Step 2.
Expected: `agent OK` and `skill OK`.

- [ ] **Step 3: ADR-drift target exists (so a real run has something to check)**

Run: `ls docs/vault/decisions/*.md | head`
Expected: at least `ADR-2026-07-08-saas-data-governance-posture.md` — confirming the ADR-drift pass has a live decision to check.

- [ ] **Step 4: Manual `/advisor --radar` dry check (human-run once)**

Invoke `/advisor --radar` in a Claude Code session; confirm it (a) runs `currency-scan.py`, (b) updates `docs/radar/tech-radar.md`, (c) writes an `advisory-reports/*.md` with a "Measured facts" section citing real package data, and (d) flags any unwired tools under "Gaps." No `src/` file is modified.

- [ ] **Step 5: Commit any smoke fixes** (only if a verifier surfaced an issue)

```bash
git add -A && git commit -m "fix(advisor): smoke-test corrections"
```

---

## Self-Review

**Spec coverage:** §4.1 skill → Task 4 · §4.2 agent → Task 3 · §5A currency → Task 1 + agent pass · §5B ADR-drift → agent pass (Task 3) · §5C dead-code → agent pass (Task 3) · §6 honesty contract → agent §4 (Task 3) · §7 outputs → Task 2 + skill (Task 4) · §8 DRIVE/RECOMMEND → agent passes + rules · §9 relationships → skill §5 · §10 success criteria → Task 6 smoke. **No gap.**

**Placeholder scan:** currency-scan.py + tests are complete code; markdown files specify exact frontmatter + itemized content (every section, command, and rule enumerated, not "add appropriate…"). No TBD/TODO.

**Type consistency:** `parse_dotnet(json_text, kind)`, `parse_npm_outdated(json_text)`, `parse_npm_audit(json_text)` and the record shape `{ecosystem,package,current,latest,kind,severity,detail}` are identical across Task 1's interface block, test, and implementation. The emitted JSON shape `{dotnet,npm,tools_run,gaps}` is consumed by the agent (Task 3 tech-radar pass) as written.
