#!/usr/bin/env python3
"""
SubagentStop ADVISORY: nudge when a *writing* agent finished substantive work but
recorded nothing to shared memory (docs/vault/ or .claude/agent-memory/).

Why this exists:
  CLAUDE.md's "Agent contract" asks every agent to write non-obvious decisions and
  domain rules to docs/vault/. That is a *discipline contract*, and discipline
  contracts leak. Measured over the 120 subagent runs on disk at the time of
  writing, the four writing agents recorded memory on only 11 of 27 substantive
  runs -- 59% of runs that changed real files left no trace behind.

  This hook turns the contract into a signal, the same way test-integrity-guard
  turns "never weaken a test" into a guard. It is the deliberate in-repo
  alternative to bolting on an external auto-capture memory daemon: no new
  service, no vector DB, no extra LLM spend, and every artefact it encourages
  stays as reviewable markdown in git.

Why ADVISORY, not blocking (cf. antipattern-advisor):
  - SubagentStop *can* force a subagent to continue (decision: "block"), but doing
    that unconditionally would wedge the unattended /implement-all and /test-all
    loops -- the exact failure mode we rejected elsewhere. Advisory can't wedge.
  - Not every substantive run genuinely learns something worth persisting. A hard
    block would manufacture vault noise, which is worse than a thin vault.
  - Opt in to blocking with CLAUDE_VAULT_ENFORCE=1 once you trust the signal.

Scope (SCOPED_AGENTS): only agents whose contract actually permits vault writes.
  Read-only auditors (requirements-auditor, integration-enforcer, principal-advisor,
  test-authenticator, browser-debugger, Explore) are deliberately excluded -- their
  own contracts say they never write files, so nudging them would be a bug.
  test-runner is excluded too: its lane is the docs/QA/ ledgers, not the vault.

How it identifies the agent:
  Claude Code stores each subagent's transcript at
      <session-dir>/subagents/agent-<id>.jsonl
  with a sidecar
      <session-dir>/subagents/agent-<id>.meta.json   -> {"agentType": ...}
  The SubagentStop payload's transcript_path points at that .jsonl.

Output: a `systemMessage` (visible, non-blocking) plus a durable append to
  .claude/hooks/vault-compliance.log -- so an overnight loop leaves a reviewable
  trail rather than a notification nobody was awake to see.

Env:
  CLAUDE_DISABLE_VAULT_ADVISOR=1  silence entirely
  CLAUDE_VAULT_ENFORCE=1          escalate from advisory to blocking
  CLAUDE_VAULT_MIN_WRITES=N       substantive-write threshold (default 3)
  CLAUDE_VAULT_HOOK_DEBUG=1       log the raw payload + why it stayed silent

Fails open: a broken advisor must never disrupt a legitimate agent run.
"""
import sys
import os
import json
import datetime

# Agents with a genuine vault-write mandate. Everything else is read-only or
# ledger-scoped by its own contract -- see the module docstring.
SCOPED_AGENTS = {
    "backend-dev",
    "frontend-dev",
    "qa-engineer",
    "business-analyst",
}

# A write here counts as "you learned something about this codebase".
SUBSTANTIVE_PREFIXES = ("src/", "docs/BA/", "docs/QA/")

# A write here counts as "you wrote it down".
# The two stores are NOT interchangeable, and treating them as one is why the shared vault
# starved. CLAUDE.md: "if it's worth sharing, it goes in the vault; if it's just one agent's
# working memory, the built-in store is fine." Measured 2026-08-22: every "compliant" run in a
# 10-transcript replay satisfied this hook via the PRIVATE store, and docs/vault/ fell from 70
# commits in June to 5 in August. A contract that accepts the cheaper path teaches the cheaper path.
VAULT_MARKER = "docs/vault/"
PRIVATE_MARKER = ".claude/agent-memory/"
MEMORY_MARKERS = (VAULT_MARKER, PRIVATE_MARKER)

WRITE_TOOLS = {"Write", "Edit", "NotebookEdit", "MultiEdit"}

LOG_RELPATH = os.path.join(".claude", "hooks", "vault-compliance.log")


def _quiet():
    sys.exit(0)


def _norm(path):
    return (path or "").replace("\\", "/")


def _rel(path, cwd):
    """Project-relative, forward-slashed. Absolute paths outside cwd stay absolute."""
    p = _norm(path)
    c = _norm(cwd).rstrip("/")
    if c and p.startswith(c + "/"):
        return p[len(c) + 1:]
    return p


def _debug(msg, cwd):
    if os.environ.get("CLAUDE_VAULT_HOOK_DEBUG") != "1":
        return
    try:
        with open(os.path.join(cwd, LOG_RELPATH), "a", encoding="utf-8") as fh:
            fh.write("[debug %s] %s\n" % (datetime.datetime.now().isoformat(timespec="seconds"), msg))
    except OSError:
        pass


def _agent_type(transcript_path):
    """Resolve agentType from the .meta.json sidecar next to the subagent transcript."""
    p = _norm(transcript_path)
    if not p.endswith(".jsonl") or "/subagents/" not in p:
        return None
    meta = p[: -len(".jsonl")] + ".meta.json"
    try:
        with open(meta, encoding="utf-8") as fh:
            return (json.load(fh) or {}).get("agentType")
    except (OSError, ValueError):
        return None


def _scan_writes(transcript_path, cwd):
    """-> (substantive, vault_writes, private_writes) from the subagent's own transcript."""
    substantive, vault, private = [], [], []
    try:
        fh = open(transcript_path, encoding="utf-8", errors="replace")
    except OSError:
        return substantive, vault, private
    with fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                rec = json.loads(line)
            except ValueError:
                continue
            msg = rec.get("message")
            if not isinstance(msg, dict):
                continue
            content = msg.get("content")
            if not isinstance(content, list):
                continue
            for block in content:
                if not isinstance(block, dict) or block.get("type") != "tool_use":
                    continue
                if block.get("name") not in WRITE_TOOLS:
                    continue
                inp = block.get("input") or {}
                fp = _rel(inp.get("file_path") or inp.get("filePath"), cwd)
                if not fp:
                    continue
                if VAULT_MARKER in fp:
                    vault.append(fp)
                elif PRIVATE_MARKER in fp:
                    private.append(fp)
                elif fp.startswith(SUBSTANTIVE_PREFIXES):
                    substantive.append(fp)
    return substantive, vault, private


def _log(cwd, agent, n_writes, sample, kind):
    stamp = datetime.datetime.now().isoformat(timespec="seconds")
    try:
        with open(os.path.join(cwd, LOG_RELPATH), "a", encoding="utf-8") as fh:
            fh.write("%s  %-16s %-14s %3d substantive write(s)  e.g. %s\n"
                     % (stamp, agent, kind, n_writes, ", ".join(sample)))
    except OSError:
        pass


def main():
    if os.environ.get("CLAUDE_DISABLE_VAULT_ADVISOR") == "1":
        _quiet()

    raw = sys.stdin.read().strip()
    if not raw:
        _quiet()
    try:
        data = json.loads(raw)
    except ValueError:
        _quiet()

    cwd = data.get("cwd") or os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()
    _debug("payload keys=%s" % sorted(data.keys()), cwd)

    transcript = data.get("transcript_path") or data.get("transcriptPath")
    if not transcript or not os.path.exists(transcript):
        _debug("no usable transcript_path (%r)" % transcript, cwd)
        _quiet()

    agent = _agent_type(transcript)
    if agent not in SCOPED_AGENTS:
        _debug("agentType %r not in scope" % agent, cwd)
        _quiet()

    substantive, vault, private = _scan_writes(transcript, cwd)
    if vault:
        # Wrote to the SHARED store -- fully compliant, say nothing.
        _debug("%s recorded to the vault: %s" % (agent, vault[:3]), cwd)
        _quiet()

    try:
        threshold = int(os.environ.get("CLAUDE_VAULT_MIN_WRITES", "3"))
    except ValueError:
        threshold = 3
    if len(substantive) < threshold:
        _debug("%s below threshold (%d < %d)" % (agent, len(substantive), threshold), cwd)
        _quiet()

    sample = sorted(set(substantive))[:3]
    kind = "private-only" if private else "no-memory"
    _log(cwd, agent, len(substantive), sample, kind)

    if private:
        # The run DID record something -- but only to its own private store. This is the case that
        # quietly starved docs/vault/: a private note satisfies the letter of the contract while
        # nothing reaches the store other agents and humans can actually read.
        note = (
            "vault-compliance: @%s changed %d file(s) (e.g. %s) and recorded %d note(s) to its "
            "PRIVATE store (.claude/agent-memory/%s/) -- but nothing to the SHARED vault.\n"
            "That is fine for your own operational notes. But if this run learned a DOMAIN RULE, "
            "an EDGE CASE, or made a NON-OBVIOUS DECISION, it belongs in docs/vault/modules/ or "
            "docs/vault/decisions/ where the other agents and a human will actually find it -- a "
            "private note is invisible to everyone but you. Advisory only; logged to %s."
            % (agent, len(substantive), ", ".join(sample), len(private), agent, LOG_RELPATH)
        )
    else:
        note = (
            "vault-compliance: @%s changed %d file(s) (e.g. %s) but wrote nothing to "
            "docs/vault/ or .claude/agent-memory/.\n"
            "CLAUDE.md's agent contract asks for a note when a run produced a non-obvious "
            "decision or domain rule -- shared knowledge belongs in docs/vault/modules/ or "
            "docs/vault/decisions/; one agent's own operational notes belong in "
            ".claude/agent-memory/%s/. If this run genuinely learned nothing worth keeping, "
            "ignore this. Advisory only; logged to %s. Silence with "
            "CLAUDE_DISABLE_VAULT_ADVISOR=1."
            % (agent, len(substantive), ", ".join(sample), agent, LOG_RELPATH)
        )

    if os.environ.get("CLAUDE_VAULT_ENFORCE") == "1":
        # Opt-in: hand the subagent back control so it can write the note itself.
        print(json.dumps({"decision": "block", "reason": note}))
    else:
        print(json.dumps({"systemMessage": note}))
    sys.exit(0)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        # Fail open -- never disrupt a legitimate agent run.
        sys.exit(0)
