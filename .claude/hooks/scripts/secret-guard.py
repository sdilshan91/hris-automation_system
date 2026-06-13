#!/usr/bin/env python3
"""
PreToolUse guardrail: block a Write/Edit that would introduce a hardcoded secret
into a tracked file.

Why this exists: CLAUDE.md Critical Rule #6 ("Secrets in .env only") is prose that
nothing enforced — and a Postgres password was once committed into .claude/settings.json.
This scans the PENDING content (Write.content / Edit.new_string), NOT the file on disk,
so it catches a secret *before* it is written.

Behaviour:
  - Reads the Claude Code hook payload as JSON on stdin.
  - Only inspects Write/Edit tool input.
  - Denies (permissionDecision: "deny") with a clear reason if a secret is detected.
  - Fails OPEN: any error, or no match, exits 0 (never breaks a legitimate edit).

Override: set CLAUDE_DISABLE_SECRET_GUARD=1 to bypass (e.g. writing a deliberate
example/placeholder).
"""
import sys
import os
import json
import re


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


# (name, regex, skip_in_test_files) — high-confidence patterns run everywhere;
# the noisier generic ones are skipped in test/mock files where fake creds are normal.
PATTERNS = [
    ("PostgreSQL/connection-string password",
     re.compile(r"(?:Host|Server)\s*=[^\"'\n]*?Password\s*=\s*([^;\"'\s]+)", re.I), False),
    ("DB connection URL with inline credentials",
     re.compile(r"(?:postgres|postgresql|mysql|mongodb|redis)://[^:@/\s]+:[^@/\s]+@", re.I), False),
    ("Private key block",
     re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----"), False),
    ("JWT signing key (Jwt:PrivateKey)",
     re.compile(r"\"?Jwt[:_\"]*PrivateKey\"?\s*[:=]\s*\"[^\"]{16,}\"", re.I), False),
    ("GitHub token",
     re.compile(r"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{30,}\b"), False),
    ("AWS access key id",
     re.compile(r"\bAKIA[0-9A-Z]{16}\b"), False),
    ("JWT",
     re.compile(r"\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{6,}\b"), True),
    ("Generic hardcoded secret",
     re.compile(r"(?:api[_-]?key|secret|access[_-]?token|client[_-]?secret)\s*[:=]\s*[\"'][A-Za-z0-9_\-.]{20,}[\"']", re.I), True),
]


def main():
    if os.environ.get("CLAUDE_DISABLE_SECRET_GUARD") == "1":
        _allow()

    raw = sys.stdin.read().strip()
    if not raw:
        _allow()

    data = json.loads(raw)
    tool_input = data.get("tool_input", {}) or {}
    file_path = tool_input.get("file_path") or tool_input.get("filePath") or ""

    # Content being introduced: Write.content, else Edit.new_string.
    content = tool_input.get("content")
    if content is None:
        content = tool_input.get("new_string") or tool_input.get("new_str") or ""
    if not content:
        _allow()

    base = os.path.basename(file_path).lower()
    norm = file_path.replace("\\", "/").lower()

    # Exempt files that legitimately hold local secrets (gitignored) or are noise/binaries.
    if base == ".env" or (base.startswith(".env.") and base != ".env.example"):
        _allow()
    if base.endswith(".local.json"):  # settings.local.json etc. (gitignored)
        _allow()
    for seg in ("/node_modules/", "/.playwright-artifacts/", "/bin/", "/obj/", "/dist/", "/.git/"):
        if seg in norm:
            _allow()
    if base.endswith((".png", ".jpg", ".jpeg", ".gif", ".ico", ".woff", ".woff2",
                      ".ttf", ".eot", ".pdf", ".zip", ".lock")):
        _allow()

    is_test_like = bool(
        re.search(r"\.(spec|test)\.[a-z]+$", base)
        or base.endswith(("tests.cs", "test.cs"))
        or "mock" in base
        or "/tests/" in norm or "/__tests__/" in norm or ".tests/" in norm
    )

    findings = []
    for name, rx, skip_in_test in PATTERNS:
        if skip_in_test and is_test_like:
            continue
        m = rx.search(content)
        if not m:
            continue
        if name.startswith("PostgreSQL") and m.lastindex:
            # Ignore empty passwords (the appsettings template ships with Password=).
            val = (m.group(1) or "").strip().strip("\"'")
            if not val:
                continue
        findings.append(name)

    if findings:
        uniq = list(dict.fromkeys(findings))
        _deny(
            "secret-guard blocked writing to '%s' — it looks like it contains a hardcoded secret:\n"
            % (file_path or "?")
            + "\n".join("  - " + f for f in uniq)
            + "\n\nProject rule (CLAUDE.md #6): secrets live in .env / dotnet user-secrets and are "
            "referenced via ${ENV_VAR} — never hardcoded in tracked files.\n"
            "If this is a placeholder/example or a deliberate exception, re-run with "
            "CLAUDE_DISABLE_SECRET_GUARD=1."
        )
    _allow()


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Fail open — a broken guard must never block a legitimate edit.
        sys.exit(0)
