#!/usr/bin/env python3
"""
PreToolUse guardrail: block an Edit/Write that WEAKENS a linter/formatter config.

Why this exists: complements test-integrity-guard. An agent under pressure to make a
green gate (`npm run lint`, `ng lint`, prettier, markdownlint) can "pass" not by fixing
the code but by editing the lint/format config that judges it — disabling a rule,
loosening prettier, silencing markdownlint. test-integrity-guard only watches *test*
files; this closes the sibling hole for *config* files. Steers the agent back to fixing
the source.

Behaviour:
  - Reads the Claude Code hook payload as JSON on stdin.
  - Only acts when the edited file's basename is a known lint/format config.
  - Allows FIRST-TIME creation (no existing file to weaken — legitimate bootstrap).
  - Denies a modification of an existing config file. Fails CLOSED on a stat error
    other than "not found" (never silently weakened), but fails OPEN on any exception.

Deliberately NOT protected: pyproject.toml / package.json — they carry project metadata
and dependencies alongside any tool config, so a blanket block would stop legitimate work.

Override: set CLAUDE_DISABLE_CONFIG_GUARD=1 to bypass (a genuine, intended config change).

Ported (MIT) from ECC's scripts/hooks/config-protection.js, adapted to this repo's
Python guard convention (secret-guard.py / test-integrity-guard.py).
"""
import sys
import os
import json

# Lint/format config files an agent might weaken to fake a passing gate.
PROTECTED_FILES = {
    # ESLint (legacy + v9 flat config)
    ".eslintrc", ".eslintrc.js", ".eslintrc.cjs", ".eslintrc.json",
    ".eslintrc.yml", ".eslintrc.yaml",
    "eslint.config.js", "eslint.config.mjs", "eslint.config.cjs",
    "eslint.config.ts", "eslint.config.mts", "eslint.config.cts",
    # Prettier
    ".prettierrc", ".prettierrc.js", ".prettierrc.cjs", ".prettierrc.json",
    ".prettierrc.yml", ".prettierrc.yaml",
    "prettier.config.js", "prettier.config.cjs", "prettier.config.mjs",
    # Stylelint
    ".stylelintrc", ".stylelintrc.json", ".stylelintrc.yml",
    # Markdownlint
    ".markdownlint.json", ".markdownlint.yaml", ".markdownlintrc",
    # Ruff (Python) — standalone config only; pyproject.toml intentionally excluded
    ".ruff.toml", "ruff.toml",
    # EditorConfig
    ".editorconfig",
}


def _allow():
    sys.exit(0)


def _deny(reason):
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))
    sys.exit(0)


def main():
    if os.environ.get("CLAUDE_DISABLE_CONFIG_GUARD") == "1":
        _allow()

    raw = sys.stdin.read().strip()
    if not raw:
        _allow()

    data = json.loads(raw)
    tool_name = data.get("tool_name", "")
    tool_input = data.get("tool_input", {}) or {}
    file_path = tool_input.get("file_path") or tool_input.get("filePath") or ""
    if not file_path:
        _allow()

    basename = os.path.basename(file_path.replace("\\", "/"))
    if basename not in PROTECTED_FILES:
        _allow()

    # Allow first-time creation — there's no existing config to weaken. Only genuine
    # "not found" (ENOENT) counts as absent; any other stat error leaves exists=True so
    # the guard is never silently bypassed by a permission/loop error on the path.
    exists = True
    try:
        os.lstat(file_path)
    except FileNotFoundError:
        exists = False
    except OSError:
        exists = True

    if not exists:
        _allow()

    _deny(
        "config-protection-guard blocked this %s to '%s':\n" % (tool_name or "edit", file_path)
        + "  - it modifies a linter/formatter config (%s).\n\n" % basename
        + "Project rule: fix the source code to satisfy the lint/format rules — do not "
        "weaken the config that judges it (the config-file sibling of the "
        "never-weaken-a-test rule). If this is a legitimate, intended config change, "
        "re-run with CLAUDE_DISABLE_CONFIG_GUARD=1."
    )


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Fail open — a broken guard must never block a legitimate edit.
        sys.exit(0)
