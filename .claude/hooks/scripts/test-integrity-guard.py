#!/usr/bin/env python3
"""
PreToolUse guardrail: block an Edit/Write that DISABLES or DELETES tests.

Why this exists: the project's #1 discipline rule — "never weaken, skip, or delete a
test to go green" (CLAUDE.md / implement-all remediation loop) — was prose that nothing
enforced. Under an unattended /implement-all run with acceptEdits, an agent under
pressure could silence a failing test. This turns that rule into a hard stop.

Detects, on test files only:
  - Introduction of skip/focus markers (xit, fit, fdescribe, .skip, .only, pending,
    [Fact(Skip=...)], [Theory(Skip=...)], Skip = "...", [Ignore]).
  - Net removal of test declarations (it()/test()/[Fact]/[Theory]) in the edited region.

Behaviour:
  - Reads the Claude Code hook payload as JSON on stdin.
  - Edit: compares old_string -> new_string. Write: compares the file on disk -> content.
  - Denies with a specific reason; fails OPEN on any error or non-test file.

Override: set CLAUDE_DISABLE_TEST_GUARD=1 to bypass (e.g. genuinely removing a test for
a removed feature).
"""
import sys
import os
import json
import re

# Test files: *.spec.ts/js, *.test.ts/js, *Tests.cs / *Test.cs, or under a tests dir.
TEST_FILE = re.compile(
    r"\.(spec|test)\.(ts|tsx|js|jsx|mjs)$|tests?\.cs$|/__tests__/|/tests?/|\.tests/",
    re.I,
)

# Skip / focus markers that disable or narrow the suite.
DISABLE = re.compile(
    r"\bxit\s*\(|\bxdescribe\s*\(|\bxtest\s*\(|\bfit\s*\(|\bfdescribe\s*\("
    r"|\b(?:it|describe|test)\s*\.\s*(?:skip|only)\b|\bpending\s*\("
    r"|Skip\s*=|\[Ignore\b",
    re.I,
)

# A single executable test case (grouping `describe(` is intentionally not counted).
DECL = re.compile(r"\b(?:it|test)\s*\(|\[Fact\b|\[Theory\b", re.I)


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
    if os.environ.get("CLAUDE_DISABLE_TEST_GUARD") == "1":
        _allow()

    raw = sys.stdin.read().strip()
    if not raw:
        _allow()

    data = json.loads(raw)
    tool_name = data.get("tool_name", "")
    tool_input = data.get("tool_input", {}) or {}
    file_path = tool_input.get("file_path") or tool_input.get("filePath") or ""

    norm = file_path.replace("\\", "/")
    if not TEST_FILE.search(norm):
        _allow()

    # Determine old vs new content for the change.
    if "new_string" in tool_input or "new_str" in tool_input:
        old = tool_input.get("old_string") or tool_input.get("old_str") or ""
        new = tool_input.get("new_string") or tool_input.get("new_str") or ""
    else:
        # Write: whole-file replace. Compare against what's on disk (if it exists).
        new = tool_input.get("content") or ""
        old = ""
        try:
            with open(file_path, "r", encoding="utf-8") as fh:
                old = fh.read()
        except Exception:
            old = ""  # new file -> nothing to weaken

    reasons = []

    if len(DISABLE.findall(new)) > len(DISABLE.findall(old)):
        reasons.append(
            "introduces a skip/focus marker (xit/fit/.skip/.only/pending/[Fact(Skip)]/[Ignore]) "
            "that disables or narrows the test suite"
        )

    removed = len(DECL.findall(old)) - len(DECL.findall(new))
    if removed > 0:
        reasons.append(
            "removes %d test case%s (it()/test()/[Fact]/[Theory]) from the edited region"
            % (removed, "s" if removed != 1 else "")
        )

    if reasons:
        _deny(
            "test-integrity-guard blocked this %s to '%s':\n" % (tool_name or "edit", file_path)
            + "\n".join("  - " + r for r in reasons)
            + "\n\nProject rule: never weaken, skip, or delete a test to make the suite pass — "
            "fix the code under test instead. If a test is genuinely obsolete (e.g. its feature "
            "was removed), re-run with CLAUDE_DISABLE_TEST_GUARD=1."
        )
    _allow()


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Fail open — a broken guard must never block a legitimate edit.
        sys.exit(0)
