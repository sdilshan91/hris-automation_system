"""
PreToolUse guardrail (Write|Edit): BLOCK a write that reaches OUT of the worktree
the caller is working in, back into the shared checkout.

ISSUE-512. Sub-agents are given their own git worktree precisely so their edits are
isolated (Engineering-Discipline rule #8). That held for `src/` — but three agents in
one session wrote `.claude/agent-memory/` into the SHARED main checkout instead, where
the files sat uncommitted, outside any branch, invisible to the PR that produced them.
One agent then ran `git checkout --` against a main-tree file while another session had
uncommitted edits there. Nothing was lost that time; the mechanism for losing it was
demonstrated.

Unlike freeze-guard, this needs no arming and has no global state. The fence is derived
per-invocation from the CALLER'S OWN cwd, so any number of concurrent worktrees are each
fenced to themselves without coordinating through a shared file or env var — which is
exactly why freeze-guard could not be reused here.

Scope, deliberately narrow:
  * Only fires when cwd is INSIDE .claude/worktrees/<name>/. A session working in the
    main checkout (the orchestrator) is unaffected — it must be able to write anywhere.
  * Only blocks targets inside the repo. Scratchpad and /tmp writes are untouched.
  * Reads of any kind are untouched; this is a Write|Edit hook.

Fails OPEN on any error, missing field, or unparseable input.
"""

import sys
import os
import json

MARKER = "/.claude/worktrees/"


def _allow():
    sys.exit(0)


def _deny(reason):
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": "[worktree-fence] " + reason,
        }
    }))
    sys.exit(0)


def _norm(p):
    try:
        p = os.path.realpath(p)
    except Exception:
        p = os.path.abspath(p)
    return p.replace("\\", "/").rstrip("/")


def _worktree_of(path):
    """The .claude/worktrees/<name> prefix containing `path`, or None."""
    lowered = path.lower()
    idx = lowered.find(MARKER)
    if idx == -1:
        return None
    rest = path[idx + len(MARKER):]
    name = rest.split("/", 1)[0]
    if not name:
        return None
    return path[:idx + len(MARKER)] + name


def main():
    if os.environ.get("CLAUDE_DISABLE_WORKTREE_FENCE"):
        _allow()  # every guard in this repo documents an override; a silent bypass is not one

    raw = sys.stdin.read().strip()
    if not raw:
        _allow()

    try:
        data = json.loads(raw)
    except Exception:
        _allow()

    tool_input = data.get("tool_input", {}) or {}
    file_path = tool_input.get("file_path") or tool_input.get("filePath") or ""
    if not file_path:
        _allow()

    cwd = _norm(data.get("cwd") or os.getcwd())
    caller_wt = _worktree_of(cwd)
    if not caller_wt:
        _allow()  # not working in a worktree → not our business

    if not os.path.isabs(file_path):
        file_path = os.path.join(cwd, file_path)
    target = _norm(file_path)

    # Inside the caller's own worktree → always fine.
    if target == caller_wt or target.startswith(caller_wt + "/"):
        _allow()

    # Repo root is derived from the worktree path itself, NOT from CLAUDE_PROJECT_DIR or
    # cwd. Both are wrong here: the env var is often unset for a sub-agent, and cwd IS the
    # worktree — so falling back to it made "outside the repo" match everything and the
    # guard allowed the very write it exists to block. Caught by the scenario tests below.
    repo = caller_wt[:caller_wt.lower().find(MARKER)]
    if not (target == repo or target.startswith(repo + "/")):
        _allow()

    target_wt = _worktree_of(target)
    where = ("another worktree (%s)" % target_wt) if target_wt else "the SHARED main checkout"

    _deny(
        "blocked writing to %s:\n  %s\n\n"
        "You are working in:\n  %s\n\n"
        "Write inside your own worktree instead, so the change rides the same branch and PR "
        "as the work that produced it. This includes .claude/agent-memory/ — memory written "
        "to the shared checkout sits uncommitted, outside any branch, and is one `git checkout` "
        "away from being lost (ISSUE-512).\n\n"
        "If you genuinely need to edit the shared checkout, hand the change back to the "
        "orchestrator rather than reaching across."
        % (where, target, caller_wt)
    )


if __name__ == "__main__":
    main()
