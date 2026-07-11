---
name: test-us
description: Execute all test cases for ONE user story against the running stack, record per-TC verdicts, and LOG any bugs/issues/enhancements (severity, root cause, repro). REPORT-ONLY — never fixes. Single-shot; does not touch the loop tracker.
user_invocable: true
---

# Test One User Story (report-only)

Executes the test cases bound to a single user story and triages the results. The testing counterpart to
`/implement-story`. **It identifies defects; it does not fix them** — fixing is a separate, human-decided step.

## Usage

```
/test-us US-ADM-003
/test-us US-CHR-007
```

## Relationship to the other skills

- `/test-us` = manual one-shot for a specific story you name. **Does NOT** update `docs/QA/TEST-STATUS.md`.
- `/test-all` = picks the next untested story from `docs/QA/TEST-STATUS.md`, runs essentially this same
  flow, and flips the tracker afterward.
- Both are **report-only** and drive the `@test-runner` agent. Neither edits `src/` or opens a PR.

## Process

1. **Validate input** — ID matches `US-[A-Z]+-\d{3}` and the story is **implemented** (`[x]` in
   `docs/BA/STATUS.md`). If the story isn't built yet → stop: "nothing to test."
2. **Pre-flight the stack** — confirm API `http://localhost:5000` and FE `http://localhost:4200` respond.
   If down, STOP and tell the user how to start it (do not fabricate verdicts). Note whether Docker is up
   (Testcontainers-backed backend integration needs it).
3. **Gather the TCs** — find every `docs/QA/**/TC-*.md` whose `user_story:` == the target US. Read the
   story's ACs for context.
4. **Dispatch `@test-runner`** with: the US id, the list of TC files, the running-stack URLs, and the
   personas/credentials. It executes each TC (bound automated test → else API/UI probe), records a verdict,
   and appends findings to `docs/QA/TEST-FINDINGS.md`. Pass the explicit contract:
   ```
   Execute the TCs for US-{ID}. REPORT-ONLY: do NOT edit src/, do NOT fix anything, do NOT open a PR.
   For each TC pick the layer by its test-type: bound automated test if present; API (curl+JWT); UI
   functional/a11y/cross-browser (Playwright MCP + @axe-core/playwright + firefox/webkit); front-end
   performance (Chrome DevTools MCP — lighthouse_audit / perf trace); API load (k6). Record PASS/FAIL/BLOCKED
   and flip the TC `status:` frontmatter. Only mark `blocked: tooling-not-wired` for OWASP ZAP / OpenAPI
   contract gate (not yet wired); if a browser MCP is disconnected, `blocked: <mcp>-down` — never fake it.
   For every FAIL/defect, append a finding to docs/QA/TEST-FINDINGS.md with the full schema
   (type, severity, layer, module, US, TC, title, root-cause+confidence, repro steps, evidence).
   Do NOT weaken any test to go green. Return a per-TC verdict table + new finding IDs.
   ```
5. **Report** — print the per-TC verdict table, the new finding IDs (with severity), and the ledger path.
   Recommend a fix priority order, but **do not fix** and **do not ask to fix** — the user decides.

## Guardrails (inherited, non-negotiable)
- REPORT-ONLY. No `src/` edits, no test weakening, no PRs, no remediation loop.
- A blocked TC stays `blocked` with a reason; never invent a pass.
- Findings are `OPEN`; you never set downstream fix states.
