#!/usr/bin/env python3
"""
PreToolUse ADVISORY: warn (never block) when a C# Write/Edit introduces a common .NET
anti-pattern. Unlike the deny-guards (secret / test-integrity / config-protection), this
one is NON-BLOCKING — it surfaces a note the model can act on and moves on.

Why advisory, not blocking:
  - These patterns are code-smells, not hard errors — `DateTime.Now` is occasionally
    legitimate; a hard block would fight real code.
  - Agents commit via GitHub MCP push_files (not local git), so a git pre-commit hook
    would never fire on agent work — this catches it at Write/Edit time instead.
  - A blocking guard here could wedge the unattended /implement-all loop, which also
    cannot `git commit --no-verify` past the no-verify-guard. Advisory can't wedge.

What it checks (the four mechanically-detectable patterns from
docs/DEV/references/dotnet-common-antipatterns.md):
  - DateTime.Now / DateTime.UtcNow   -> inject TimeProvider
  - new HttpClient()                 -> use IHttpClientFactory / the ResilientClient
  - async void (non event-handler)   -> return async Task
  - .Result / .GetAwaiter().GetResult() -> await all the way (sync-over-async)

Behaviour:
  - Reads the Claude Code hook payload as JSON on stdin.
  - Only inspects the PENDING content of a *.cs Write/Edit (content / new_string) — not
    the file on disk, so it sees the change being made.
  - Emits `additionalContext` (advisory) and always allows. Never denies.

Override: set CLAUDE_DISABLE_ANTIPATTERN_ADVISOR=1 to silence.
Fails open (a broken advisor must never disrupt a legitimate edit).

Reference content vendored (MIT) from codewithmukesh/dotnet-claude-kit
(hooks/pre-commit-antipattern.sh + knowledge/common-antipatterns.md).
"""
import sys
import os
import re
import json

# (compiled regex, one-line advice). Kept to the high-signal, low-false-positive set.
CHECKS = [
    (re.compile(r"\bDateTime\.(Now|UtcNow)\b"),
     "DateTime.Now/UtcNow -> inject TimeProvider (FakeTimeProvider in tests); date-dependent code needs a clock seam."),
    (re.compile(r"\bnew\s+HttpClient\s*\(\s*\)"),
     "new HttpClient() -> use IHttpClientFactory / the named ResilientClient (socket exhaustion + no Polly resilience)."),
    (re.compile(r"\basync\s+void\b"),
     "async void -> return async Task (async void swallows exceptions and can't be awaited); OK only for event handlers."),
    (re.compile(r"\.Result\b|\.GetAwaiter\(\)\.GetResult\(\)"),
     "sync-over-async (.Result / .GetAwaiter().GetResult()) -> await all the way (deadlock risk in ASP.NET)."),
]

# async void is legitimate for event handlers — skip a match on a line mentioning EventArgs.
_ASYNC_VOID = re.compile(r"\basync\s+void\b")


def _allow():
    sys.exit(0)


def _pending_content(tool_input):
    """The text being written: Write.content, or Edit.new_string (single + multi-edit)."""
    parts = []
    for key in ("content", "new_string", "newString"):
        val = tool_input.get(key)
        if isinstance(val, str):
            parts.append(val)
    edits = tool_input.get("edits")
    if isinstance(edits, list):
        for e in edits:
            if isinstance(e, dict):
                v = e.get("new_string") or e.get("newString")
                if isinstance(v, str):
                    parts.append(v)
    return "\n".join(parts)


def main():
    if os.environ.get("CLAUDE_DISABLE_ANTIPATTERN_ADVISOR") == "1":
        _allow()

    raw = sys.stdin.read().strip()
    if not raw:
        _allow()

    data = json.loads(raw)
    tool_input = data.get("tool_input", {}) or {}
    file_path = tool_input.get("file_path") or tool_input.get("filePath") or ""
    if not file_path or not file_path.replace("\\", "/").lower().endswith(".cs"):
        _allow()

    content = _pending_content(tool_input)
    if not content:
        _allow()

    hits = []
    for rx, advice in CHECKS:
        matched = False
        if rx is CHECKS[2][0]:  # async void — exclude event-handler lines
            for line in content.splitlines():
                if _ASYNC_VOID.search(line) and "EventArgs" not in line:
                    matched = True
                    break
        else:
            matched = bool(rx.search(content))
        if matched:
            hits.append(advice)

    if not hits:
        _allow()

    basename = os.path.basename(file_path.replace("\\", "/"))
    note = (
        "antipattern-advisor (non-blocking) noticed possible .NET anti-pattern(s) in the "
        "pending edit to '%s':\n" % basename
        + "\n".join("  - " + h for h in hits)
        + "\n\nThis is advisory only — the edit was allowed. Prefer the GOOD forms in "
        "docs/DEV/references/dotnet-common-antipatterns.md. If the usage is deliberate "
        "(e.g. a genuine event handler), ignore this. Silence with "
        "CLAUDE_DISABLE_ANTIPATTERN_ADVISOR=1."
    )
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "additionalContext": note,
        }
    }))
    sys.exit(0)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Fail open — a broken advisor must never disrupt a legitimate edit.
        sys.exit(0)
