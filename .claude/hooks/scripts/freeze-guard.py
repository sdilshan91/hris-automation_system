#!/usr/bin/env python3
"""
PreToolUse guardrail (Write|Edit): BLOCK edits to files outside an armed
"freeze boundary" directory.

Why this exists: during focused debugging or a surgical fix, you often want the
agent to touch ONLY one area (e.g. src/backend/HRM.Application/Features/Payroll)
and never wander into unrelated files "while it's here" — a recurring
scope-creep failure. When armed, this denies any Write/Edit whose target is
outside the frozen directory. Dormant (no-op) until armed, so it costs nothing
when unused.

Adapted (MIT) from the gstack `/freeze` skill's check-freeze.sh, reimplemented in
Python to match this repo's other hooks and run on Windows.

Arming / disarming (no slash command needed):
  - Arm:    write the ABSOLUTE path of the allowed directory into
            .claude/hooks/.freeze-dir   (relative to the repo root), OR set the
            env var CLAUDE_FREEZE_DIR to that path.
              e.g.  echo "d:/WORK/hris-automation_system/src/frontend" > .claude/hooks/.freeze-dir
  - Disarm: delete .claude/hooks/.freeze-dir  (or empty it / unset the env var).

The state file is gitignored (see .gitignore) so a freeze never travels in a
commit. Fails OPEN on any error or when not armed.
"""
import sys
import os
import json


def _allow():
    sys.exit(0)


def _deny(reason):
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": "[freeze] " + reason,
        }
    }))
    sys.exit(0)


def _norm(p):
    """Absolute, forward-slashed, lowercased path with symlinks/.. resolved where possible."""
    try:
        p = os.path.realpath(p)
    except Exception:
        p = os.path.abspath(p)
    return p.replace("\\", "/").rstrip("/").lower()


def _freeze_dir():
    env = os.environ.get("CLAUDE_FREEZE_DIR")
    if env and env.strip():
        return env.strip()
    project = os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()
    state = os.path.join(project, ".claude", "hooks", ".freeze-dir")
    try:
        with open(state, "r", encoding="utf-8") as fh:
            val = fh.read().strip()
            return val or None
    except OSError:
        return None


def main():
    raw = sys.stdin.read().strip()
    if not raw:
        _allow()

    boundary = _freeze_dir()
    if not boundary:
        _allow()  # not armed → no-op

    data = json.loads(raw)
    tool_input = data.get("tool_input", {}) or {}
    file_path = tool_input.get("file_path") or tool_input.get("filePath") or ""
    if not file_path:
        _allow()  # can't tell → don't block

    project = os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()
    if not os.path.isabs(file_path):
        file_path = os.path.join(project, file_path)

    target = _norm(file_path)
    fence = _norm(boundary)

    if target == fence or target.startswith(fence + "/"):
        _allow()

    _deny(
        "blocked writing to:\n  %s\nA freeze boundary is armed — only edits inside\n  %s\n"
        "are allowed. Disarm by deleting .claude/hooks/.freeze-dir (or unset CLAUDE_FREEZE_DIR)."
        % (target, fence)
    )


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Fail open — a broken guard must never block a legitimate edit.
        sys.exit(0)
