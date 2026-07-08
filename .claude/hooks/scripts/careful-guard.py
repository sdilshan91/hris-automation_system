#!/usr/bin/env python3
"""
PreToolUse guardrail (Bash): force a confirmation prompt before a DESTRUCTIVE
shell command runs.

Why this exists: this project's `.claude/settings.json` sets
`defaultMode: "bypassPermissions"` so long `/implement-all` / `/test-all` loops
run unattended — which means the `permissions.ask` list (rm, force-push,
reset --hard, kubectl, docker…) is BYPASSED and gives no protection during those
runs. PreToolUse hooks, however, still fire in bypass mode (that is why
secret-guard / test-integrity-guard are hooks, not permissions). This restores a
speed-bump on the genuinely irreversible commands, and additionally catches SQL
`DROP`/`TRUNCATE` and `git checkout .`/`git restore .` discards that the
prefix-based permission list can't see (they live inside `psql -c "..."` etc.).

Adapted (MIT) from the gstack `/careful` skill's check-careful.sh, reimplemented
in Python to match this repo's other hooks and run on Windows.

Behaviour:
  - Reads the Claude Code hook payload as JSON on stdin; inspects Bash only.
  - Returns permissionDecision "ask" (a confirmation prompt) for a destructive
    command — this OVERRIDES bypassPermissions and asks the human.
  - Exempts recursive-delete of well-known build artefacts (node_modules, dist,
    bin, obj, .angular, coverage, __pycache__, .next, .cache) so routine cleanup
    doesn't nag.
  - Fails OPEN: any error, or no match, exits 0 (never blocks a legit command).

Override: set CLAUDE_DISABLE_CAREFUL=1 to bypass entirely.
"""
import sys
import os
import json
import re


def _allow():
    # Empty output = no decision = fall through to normal permission handling.
    sys.exit(0)


def _ask(reason):
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "ask",
            "permissionDecisionReason": "[careful] " + reason,
        }
    }))
    sys.exit(0)


# Recursive-delete targets that are safe to remove without a prompt.
_SAFE_RM_TARGETS = (
    "node_modules", "dist", "bin", "obj", ".angular", ".next", ".nuxt",
    "coverage", "__pycache__", ".cache", ".turbo", "build", ".playwright-artifacts",
)

# (name, regex, reason) — checked in order; first match wins.
_RM_RECURSIVE = re.compile(r"\brm\s+(?:-[a-zA-Z]*r[a-zA-Z]*|--recursive)\b")
_PATTERNS = [
    ("rm_recursive", _RM_RECURSIVE,
     "recursive delete (rm -r) permanently removes files."),
    ("drop", re.compile(r"\bdrop\s+(?:table|database|schema)\b", re.I),
     "SQL DROP permanently deletes database objects."),
    ("truncate", re.compile(r"\btruncate\s+(?:table\s+)?\w", re.I),
     "SQL TRUNCATE deletes all rows from a table."),
    ("git_force_push", re.compile(r"\bgit\s+push\b[^\n]*?(?:--force\b|-f\b)"),
     "git force-push rewrites remote history; collaborators can lose work."),
    ("git_reset_hard", re.compile(r"\bgit\s+reset\s+--hard\b"),
     "git reset --hard discards all uncommitted changes."),
    ("git_discard", re.compile(r"\bgit\s+(?:checkout|restore)\s+\.(?:\s|$)"),
     "this discards all uncommitted changes in the working tree."),
    ("kubectl_delete", re.compile(r"\bkubectl\s+delete\b"),
     "kubectl delete removes Kubernetes resources; may impact a live environment."),
    ("docker_destructive", re.compile(r"\bdocker\s+(?:rm\s+-f|system\s+prune|volume\s+rm)\b"),
     "Docker force-remove / prune can delete running containers, volumes, or cached images."),
    ("db_drop_ef", re.compile(r"\bdotnet\s+ef\s+database\s+drop\b", re.I),
     "dotnet ef database drop deletes the entire database."),
]


def _is_safe_artifact_rm(cmd):
    """True if the command is `rm -r ...` whose every target is a known build artefact."""
    if not _RM_RECURSIVE.search(cmd):
        return False
    # Strip everything up to and including the rm + its flags, look at the targets.
    tail = re.sub(r"^.*?\brm\s+(?:-[a-zA-Z]+\s+|--recursive\s+)*", "", cmd)
    targets = [t for t in tail.split() if not t.startswith("-")]
    if not targets:
        return False
    for t in targets:
        base = t.rstrip("/").replace("\\", "/").split("/")[-1].strip("'\"")
        if base not in _SAFE_RM_TARGETS:
            return False
    return True


def main():
    if os.environ.get("CLAUDE_DISABLE_CAREFUL") == "1":
        _allow()

    raw = sys.stdin.read().strip()
    if not raw:
        _allow()

    data = json.loads(raw)
    if (data.get("tool_name") or data.get("toolName") or "") not in ("Bash", ""):
        # Only guard Bash. (Some payloads omit tool_name when the matcher already scoped it.)
        pass
    tool_input = data.get("tool_input", {}) or {}
    cmd = tool_input.get("command") or ""
    if not cmd:
        _allow()

    if _is_safe_artifact_rm(cmd):
        _allow()

    for _name, rx, reason in _PATTERNS:
        if rx.search(cmd):
            _ask(reason + "\n\nCommand:\n  " + cmd.strip()[:400]
                 + "\n\nConfirm to proceed, or set CLAUDE_DISABLE_CAREFUL=1 to silence this guard.")
    _allow()


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Fail open — a broken guard must never block a legitimate command.
        sys.exit(0)
