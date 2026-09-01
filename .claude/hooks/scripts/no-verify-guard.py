#!/usr/bin/env python3
"""
PreToolUse guardrail: block a Bash command that BYPASSES git hooks.

Why this exists: this repo enforces discipline through git hooks and through the
PreToolUse guards themselves. An agent can skip pre-commit / commit-msg / pre-push hooks
with `git commit --no-verify` (or `-n`) or `git -c core.hooksPath=/dev/null ...`. Under an
unattended /implement-all run that is exactly the kind of shortcut that makes a red gate
look green. careful-guard covers destructive commands; this covers hook-bypass.

Detects, on Bash commands only:
  - `--no-verify` on a git subcommand that supports it (commit/push/merge/cherry-pick/
    rebase/am), and `-n` shorthand on `git commit`.
  - `-c core.hooksPath=...` overrides (case-insensitive; git config keys are).

Robustness: the command is tokenized with shlex (punctuation_chars=True) so shell
operators (`&&`, `||`, `;`, `|`) become their own tokens and quoted strings stay intact.
Each pipeline/segment is scoped to its own leading command, so a commit *message* that
merely contains the text "--no-verify" (e.g. `git commit -m "note about --no-verify"`)
is a single token and never matches — no false block.

Behaviour: denies with a specific reason; fails OPEN on any parse error or exception.

Override: set CLAUDE_DISABLE_NOVERIFY_GUARD=1 to bypass.

Ported (MIT) from ECC's scripts/hooks/block-no-verify.js, adapted to this repo's Python
guard convention (careful-guard.py).
"""
import sys
import os
import json
import re
import shlex

GIT_SUBCMDS_WITH_NO_VERIFY = {"commit", "push", "merge", "cherry-pick", "rebase", "am"}
OPERATOR_TOKENS = {";", "|", "||", "&", "&&", "(", ")", "<", ">", ">>", "&>", "|&"}
HOOKSPATH_KEY = "core.hookspath="  # compared lowercase; git config keys are case-insensitive


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


def _basename(tok):
    return tok.replace("\\", "/").split("/")[-1].lower()


def _segments(tokens):
    """Split a flat token list on shell operators into per-command segments."""
    seg = []
    for t in tokens:
        if t in OPERATOR_TOKENS:
            if seg:
                yield seg
                seg = []
        else:
            seg.append(t)
    if seg:
        yield seg


# Command wrappers that can precede `git` without changing what runs. Without stripping
# these, `env git commit --no-verify` sails past a guard that only inspects seg[0] — which
# is how this hook could be bypassed by a five-character prefix (verified 2026-09-01:
# env / nice / time / sudo / bash -c all returned "allow" on a --no-verify commit).
_WRAPPERS = frozenset({
    "env", "nice", "ionice", "time", "nohup", "stdbuf", "timeout",
    "sudo", "doas", "command", "exec", "builtin", "xargs",
})
_SHELLS = frozenset({"bash", "sh", "zsh", "dash", "ksh"})
_ASSIGN_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*=")


def _strip_wrappers(seg):
    """Drop leading VAR=VAL assignments and command wrappers so seg[0] is the real program.

    Never skips past a `git` token: a wrapper flag's value is only consumed when it is not
    itself git, so `nice -n 10 git commit` resolves but `nice git ...` cannot be skipped over.
    """
    i, n = 0, len(seg)
    while i < n:
        tok = seg[i]
        if _ASSIGN_RE.match(tok) and not tok.startswith("-"):
            i += 1
            continue
        if _basename(tok) in _WRAPPERS:
            i += 1
            while i < n and seg[i].startswith("-"):
                i += 1
                if i < n and not seg[i].startswith("-") and _basename(seg[i]) not in ("git", "git.exe"):
                    i += 1
            continue
        break
    return seg[i:]


def _inline_shell_commands(seg):
    """Yield token lists for `bash -c "<script>"` payloads so the script is inspected too."""
    if not seg or _basename(seg[0]) not in _SHELLS:
        return
    for i, tok in enumerate(seg):
        if tok == "-c" and i + 1 < len(seg):
            try:
                lex = shlex.shlex(seg[i + 1], posix=True, punctuation_chars=True)
                lex.whitespace_split = True
                yield list(lex)
            except ValueError:
                return
            return


def _check_git_segment(seg):
    """Return a block reason if this git command segment bypasses hooks, else None."""
    sub = None
    i = 1  # seg[0] is the git executable
    n = len(seg)
    while i < n:
        t = seg[i]
        low = t.lower()

        # -c core.hooksPath=...  (global flag; appears before the subcommand)
        if t == "-c":
            nxt = seg[i + 1] if i + 1 < n else ""
            if nxt.lower().startswith(HOOKSPATH_KEY):
                return "overrides core.hooksPath, disabling git hooks"
            i += 2
            continue
        if low.startswith("-c" + HOOKSPATH_KEY):
            return "overrides core.hooksPath, disabling git hooks"

        # first non-flag token after `git` is the subcommand
        if sub is None and not t.startswith("-"):
            sub = low
            i += 1
            continue

        if t == "--no-verify" and sub in GIT_SUBCMDS_WITH_NO_VERIFY:
            return "uses --no-verify, skipping git hooks on `git %s`" % sub

        # `-n` is --no-verify shorthand for commit only
        if sub == "commit" and (t == "-n" or (t.startswith("-n") and t[1:].isalpha())):
            return "uses -n (--no-verify), skipping git hooks on `git commit`"

        i += 1
    return None


def main():
    if os.environ.get("CLAUDE_DISABLE_NOVERIFY_GUARD") == "1":
        _allow()

    raw = sys.stdin.read().strip()
    if not raw:
        _allow()

    data = json.loads(raw)
    tool_input = data.get("tool_input", {}) or {}
    command = tool_input.get("command") or ""
    if not command or "git" not in command:
        _allow()

    try:
        lex = shlex.shlex(command, posix=True, punctuation_chars=True)
        lex.whitespace_split = True
        tokens = list(lex)
    except ValueError:
        _allow()  # unparseable shell -> fail open

    def _scan(tok_list, depth=0):
        for seg in _segments(tok_list):
            seg = _strip_wrappers(seg)
            if not seg:
                continue
            if depth < 3:
                for inner in _inline_shell_commands(seg):
                    hit = _scan(inner, depth + 1)
                    if hit:
                        return hit
            if _basename(seg[0]) in ("git", "git.exe"):
                hit = _check_git_segment(seg)
                if hit:
                    return hit
        return None

    for _once in (0,):
            reason = _scan(tokens)
            if reason:
                _deny(
                    "no-verify-guard blocked this Bash command:\n"
                    + "  - it %s.\n\n" % reason
                    + "Project rule: git hooks (pre-commit / commit-msg / pre-push) must not "
                    "be bypassed to make a gate pass — fix the underlying failure instead. If "
                    "you genuinely need to skip hooks, re-run with "
                    "CLAUDE_DISABLE_NOVERIFY_GUARD=1."
                )
    _allow()


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Fail open — a broken guard must never block a legitimate command.
        sys.exit(0)
