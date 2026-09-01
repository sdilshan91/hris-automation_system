# Automation hooks — full reference

> Moved out of [CLAUDE.md](../../CLAUDE.md) on 2026-09-01, where the summary table lives.
> Hooks are **enforcement**: they fire on their trigger whether or not their rationale is in
> an agent's context, so the detail below is for the human changing them, not for the agent
> being governed by them.
>
> Definitions live in [.claude/settings.json](../../.claude/settings.json) (`hooks` key) and
> [.claude/hooks/](../../.claude/hooks/). Every guard **fails open** — a hook that errors must
> never wedge an unattended `/implement-all` loop. Each has a documented override env var so a
> deliberate exception is one variable away, and a silent bypass is not.

## Full hook table

| Hook | Trigger | Action |
|------|---------|--------|
| `post-user-story-commit` | User story files committed | Notifies dev + QA agents to start |
| `post-dev-commit` | Frontend/backend code committed | Notifies QA to review test cases |
| `sound notifications` | `Stop`, `Notification`, `PermissionRequest`, `SubagentStop` | Plays a short sound via `python .claude/hooks/scripts/hooks.py` so you know when a long `/implement-all` run finishes or needs you. Toggle per-hook in `.claude/hooks/config/hooks-config.json` (or git-ignored `…local.json`); disable all via `disableAllHooks` in `settings.local.json`. Needs Python 3. |
| `secret-guard` | `PreToolUse` on `Write\|Edit` | **Enforces** Critical Rule #6. Blocks a write whose *pending* content contains a hardcoded secret (Postgres `Password=…`, DB connection URLs with creds, `Jwt:PrivateKey`, private-key blocks, GitHub/AWS tokens, JWTs). Exempts gitignored secret files (`.env`, `*.local.json`). Fails open. Override for one run with `CLAUDE_DISABLE_SECRET_GUARD=1`. |
| `test-integrity-guard` | `PreToolUse` on `Write\|Edit` | **Enforces** the "never weaken/skip/delete a test to go green" rule. Blocks edits to test files (`*.spec.ts`, `*Tests.cs`, …) that introduce skip/focus markers (`xit`/`fit`/`.skip`/`.only`/`[Fact(Skip)]`/`[Ignore]`) or remove test cases. Fails open. Override with `CLAUDE_DISABLE_TEST_GUARD=1`. |
| `careful-guard` | `PreToolUse` on `Bash` | **Speed-bump on irreversible commands.** Returns `ask` (forces a prompt even under `bypassPermissions`, which the `permissions.ask` list can't do during unattended loops) for `rm -r`, SQL `DROP`/`TRUNCATE`, `git push --force`, `git reset --hard`, `git checkout/restore .`, `kubectl delete`, `docker rm -f`/`prune`, `dotnet ef database drop`. Exempts recursive-delete of build artefacts (`node_modules`, `dist`, `bin`, `obj`, `.angular`, `coverage`…). Fails open. Override with `CLAUDE_DISABLE_CAREFUL=1`. Adapted (MIT) from gstack `/careful`. |
| `freeze-guard` | `PreToolUse` on `Write\|Edit` | **Edit-scope fence (dormant until armed).** When a boundary is armed, blocks any Write/Edit outside it — stops scope-creep into unrelated files during a focused fix/debug. **Arm:** `echo "<abs-dir>" > .claude/hooks/.freeze-dir` (or set `CLAUDE_FREEZE_DIR`). **Disarm:** delete that file. State file is gitignored (never travels in a commit). Fails open when unarmed. Adapted (MIT) from gstack `/freeze`. |
| `config-protection-guard` | `PreToolUse` on `Write\|Edit` | **Config-file sibling of `test-integrity-guard`.** Blocks edits that *weaken a lint/format config* to fake a green gate — `eslint.config.*`/`.eslintrc*`, `.prettierrc*`, `.stylelintrc*`, `.markdownlint*`, `.editorconfig`, `ruff.toml`. Allows **first-time creation** (nothing to weaken); `pyproject.toml`/`package.json` deliberately unprotected (carry metadata/deps). Fails open. Override with `CLAUDE_DISABLE_CONFIG_GUARD=1`. Ported (MIT) from ECC `config-protection.js`. |
| `antipattern-advisor` | `PreToolUse` on `Write\|Edit` | **Advisory (NON-blocking) .NET code-smell nudge.** On a `*.cs` write, greps the *pending* content for four mechanically-detectable anti-patterns (`DateTime.Now`/`UtcNow` → TimeProvider · `new HttpClient()` → IHttpClientFactory/ResilientClient · non-event-handler `async void` · `.Result`/`.GetAwaiter().GetResult()` sync-over-async) and surfaces a note the model can act on — it **never denies** (unlike the deny-guards), so it can't wedge the `/implement-all` loop. Catches smells at write-time because agents commit via GitHub MCP `push_files`, which no git pre-commit hook would see. Backed by [docs/DEV/references/dotnet-common-antipatterns.md](docs/DEV/references/dotnet-common-antipatterns.md). Fails open. Silence with `CLAUDE_DISABLE_ANTIPATTERN_ADVISOR=1`. Adapted (MIT) from codewithmukesh/dotnet-claude-kit. |
| `no-verify-guard` | `PreToolUse` on `Bash` | **Blocks git-hook bypass.** Denies `git commit/push/merge/… --no-verify` (and `git commit -n`) and `-c core.hooksPath=…` overrides so pre-commit/commit-msg/pre-push hooks can't be skipped to force a red gate green. shlex-tokenized so a commit *message* mentioning `--no-verify` is not a false block. Fails open. Override with `CLAUDE_DISABLE_NOVERIFY_GUARD=1`. Ported (MIT) from ECC `block-no-verify.js`. |
| `vault-compliance-advisor` | `SubagentStop` | **Advisory (NON-blocking) shared-memory nudge.** Enforces the **Agent contract** above the way `antipattern-advisor` enforces code smells: when a *writing* agent (`backend-dev`, `frontend-dev`, `qa-engineer`, `business-analyst`) finishes a run that changed ≥3 files under `src/`, `docs/BA/` or `docs/QA/` but wrote **nothing** to `docs/vault/` or `.claude/agent-memory/`, it surfaces a note and appends a line to `.claude/hooks/vault-compliance.log` (gitignored) so unattended `/implement-all` runs leave a reviewable trail. Reads the subagent's own transcript (`<session>/subagents/agent-*.jsonl`) and resolves the agent from its `.meta.json` sidecar. **Read-only auditors are deliberately out of scope** — their contracts forbid writing files; `test-runner` too (its lane is the `docs/QA/` ledgers). Never blocks by default, so it cannot wedge the loop; opt in to blocking with `CLAUDE_VAULT_ENFORCE=1`. Threshold via `CLAUDE_VAULT_MIN_WRITES`. Fails open. Silence with `CLAUDE_DISABLE_VAULT_ADVISOR=1`. Chosen over an external auto-capture memory daemon (e.g. `claude-mem`) to keep knowledge as reviewable markdown in git — no extra service, vector DB, or LLM spend. |

## Design rules

- **Deny-guards vs advisors.** `secret-guard`, `test-integrity-guard`, `careful-guard`,
  `config-protection-guard`, `freeze-guard` and `no-verify-guard` can **block** a call.
  `antipattern-advisor` and `vault-compliance-advisor` deliberately **never** deny — an
  advisory hook that could block would be able to wedge the autonomous loop.
- **Write-time, not commit-time.** Agents commit through the GitHub MCP `push_files`, which no
  git pre-commit hook ever sees. Anything that must be caught has to be caught on `Write`/`Edit`.
- **Fail open, always.** Every guard exits 0 on its own error.
