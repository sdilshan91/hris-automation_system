#!/usr/bin/env python3
"""
plan-audit scanner — the DETERMINISTIC half of the /plan-audit skill.

Parses this repo's plan/status/ledger documents into a normalized item index,
computes per-doc and program-level completion metrics, cross-checks PR claims
against merged git history, and emits deterministic drift/duplicate findings as
JSON on stdout. The /plan-audit skill feeds this JSON to a reconciler sub-agent
that handles freeform table docs + narrative and writes test-cases/PLAN-AUDIT.md.

REPORT-ONLY: this script reads files + `git log`; it writes nothing.

Usage:
  python .claude/skills/plan-audit/scan.py [--root DIR] [--living-only]
                                           [--module CHR] [--pretty]

Design notes:
  - The same checkbox glyph means different things on the two boards, so each
    doc is parsed under a named status vocabulary (see REGISTRY / VOCAB).
  - Stable IDs (US-/TC-/BUG-/ISSUE-/ENH-) are the cross-doc join key.
  - "Living" docs are an explicit allowlist; every other plan-ish doc is scanned
    but tagged lifecycle=historical (lower priority, contradictions informational)
    and, if it matches a plan glob but isn't registered, flagged UNREGISTERED-DOC.
"""
import sys
import os
import re
import json
import glob
import argparse
import subprocess

# --- Normalized status enum -------------------------------------------------
DONE, IN_PROGRESS, TODO, BLOCKED, SKIPPED, UNKNOWN = (
    "DONE", "IN_PROGRESS", "TODO", "BLOCKED", "SKIPPED", "UNKNOWN")
# Precedence for picking a canonical status when docs disagree (higher wins).
PRECEDENCE = {DONE: 5, IN_PROGRESS: 4, BLOCKED: 3, TODO: 2, SKIPPED: 1, UNKNOWN: 0}

# --- Status vocabularies (per-doc checkbox alphabets / status words) ---------
VOCAB = {
    "board-impl": {  # user-stories/STATUS.md
        "x": DONE, "~": IN_PROGRESS, " ": TODO, "s": SKIPPED, "-": SKIPPED,
    },
    "board-test": {  # test-cases/TEST-STATUS.md
        "x": DONE, "!": DONE, "~": IN_PROGRESS, " ": TODO, "b": BLOCKED, "s": SKIPPED,
    },
    "finding": {  # TEST-FINDINGS.md / BUG-STATUS.md status words
        "RESOLVED": DONE, "VERIFIED": DONE, "FIXED": DONE, "CLOSED": DONE,
        "WIP": IN_PROGRESS, "OPEN": TODO, "WONTFIX": SKIPPED, "DUP": SKIPPED,
    },
    "tc": {  # TC-*.md frontmatter status:
        "pass": DONE, "automated": IN_PROGRESS, "draft": TODO,
        "fail": TODO, "blocked": BLOCKED, "skipped": SKIPPED,
    },
}

# --- Living-doc registry (authoritative, actively maintained) ---------------
# path (relative, forward slash) -> (parser, vocab)
REGISTRY = {
    "user-stories/STATUS.md":        ("checkbox", "board-impl"),
    "test-cases/TEST-STATUS.md":     ("checkbox", "board-test"),
    "test-cases/TEST-FINDINGS.md":   ("ledger",   "finding"),
    "test-cases/BUG-STATUS.md":      ("table",    "finding"),
}

# Globs of plan-ish docs to auto-discover (anything here not in REGISTRY is
# scanned as lifecycle=historical and flagged UNREGISTERED-DOC if plan-like).
DISCOVER_GLOBS = [
    "test-cases/*PLAN*.md", "test-cases/*STATUS*.md", "test-cases/*TRIAGE*.md",
    "test-cases/*DECISIONS*.md", "test-cases/TRACEABILITY-MATRIX.md",
    "user-stories/INDEX.md", "docs/*plan*.md",
]
TC_GLOB = "test-cases/**/TC-*.md"

# --- ID / PR extraction -----------------------------------------------------
ID_RX = re.compile(
    r"\b(US-[A-Z]{2,5}-\d+|TC-[A-Z]{2,5}-\d+(?:-\d+)?|ISO-\d+|"
    r"BUG-\d+|ISSUE-\d+|ENH-\d+)\b")
PR_RX = re.compile(r"(?:PR\s*)?#(\d{1,5})\b")
CHECKBOX_RX = re.compile(r"^\s*[-*]\s*\[(.)\]\s*(.*)$")
LEDGER_HEAD_RX = re.compile(r"^#{2,4}\s+((?:BUG|ISSUE|ENH)-\d+)\b")
STATUS_LINE_RX = re.compile(r"Status:?\*{0,2}\s*[:\-]?\s*(.*)", re.I)
FINDING_WORD_RX = re.compile(
    r"\b(RESOLVED|VERIFIED|FIXED|CLOSED|WONTFIX|WIP|OPEN|DUP)\b", re.I)
DATE_IN_NAME_RX = re.compile(r"\d{4}-\d{2}-\d{2}")


def _norm(path):
    return path.replace("\\", "/")


def _rel(path, root):
    return _norm(os.path.relpath(path, root))


def merged_prs(root):
    """Set of PR numbers merged into the current branch (merge + squash subjects)."""
    prs = set()
    try:
        out = subprocess.run(
            ["git", "-C", root, "log", "--format=%s", "-n", "4000"],
            capture_output=True, text=True, timeout=30)
        for line in out.stdout.splitlines():
            m = re.search(r"Merge pull request #(\d+)", line)
            if m:
                prs.add(int(m.group(1)))
                continue
            m = re.search(r"\(#(\d+)\)\s*$", line)  # squash-merge subject
            if m:
                prs.add(int(m.group(1)))
    except Exception:
        pass  # git unavailable -> no cross-check, drift for PRs simply not asserted
    return prs


def parse_checkbox(text, vocab):
    items = []
    for i, line in enumerate(text.splitlines(), 1):
        m = CHECKBOX_RX.match(line)
        if not m:
            continue
        glyph, body = m.group(1), m.group(2)
        norm = VOCAB[vocab].get(glyph, VOCAB[vocab].get(glyph.lower(), UNKNOWN))
        ids = ID_RX.findall(body)
        if not ids:
            continue
        prs = [int(p) for p in PR_RX.findall(body)]
        primary = ids[0]  # the line's subject; trailing ids are cross-refs (findings)
        items.append(dict(id=primary, refs=ids[1:], norm=norm, raw=glyph,
                          prs=prs, line=i))
    return items


def parse_ledger(text, vocab):
    items = []
    lines = text.splitlines()
    for i, line in enumerate(lines):
        h = LEDGER_HEAD_RX.match(line)
        if not h:
            continue
        fid = h.group(1)
        norm, raw, prs = UNKNOWN, "", []
        for look in lines[i:i + 6]:  # status line sits just under the heading
            if "Status" in look:
                w = FINDING_WORD_RX.search(look)
                if w:
                    raw = w.group(1).upper()
                    norm = VOCAB[vocab].get(raw, UNKNOWN)
                prs = [int(p) for p in PR_RX.findall(look)]
                break
        items.append(dict(id=fid, refs=[], norm=norm, raw=raw, prs=prs, line=i + 1))
    return items


def parse_table(text, vocab):
    """Best-effort: pipe-table rows carrying an ID + a recognizable status word."""
    items = []
    for i, line in enumerate(text.splitlines(), 1):
        if "|" not in line:
            continue
        ids = ID_RX.findall(line)
        if not ids:
            continue
        w = FINDING_WORD_RX.search(line)
        raw = w.group(1).upper() if w else ""
        norm = VOCAB[vocab].get(raw, UNKNOWN)
        prs = [int(p) for p in PR_RX.findall(line)]
        items.append(dict(id=ids[0], refs=ids[1:], norm=norm, raw=raw, prs=prs, line=i))
    return items


def parse_generic(text, _vocab):
    """Freeform doc: extract IDs + PRs per line, status UNKNOWN (agent resolves)."""
    items = []
    for i, line in enumerate(text.splitlines(), 1):
        ids = ID_RX.findall(line)
        if not ids:
            continue
        prs = [int(p) for p in PR_RX.findall(line)]
        items.append(dict(id=ids[0], refs=ids[1:], norm=UNKNOWN, raw="",
                          prs=prs, line=i))
    return items


def parse_tc(text, vocab):
    if not text.startswith("---"):
        return []
    fm = text.split("---", 2)
    if len(fm) < 3:
        return []
    meta = {}
    for line in fm[1].splitlines():
        if ":" in line:
            k, _, v = line.partition(":")
            meta[k.strip()] = v.strip()
    tid = meta.get("id")
    if not tid:
        return []
    norm = VOCAB[vocab].get(meta.get("status", "").lower(), UNKNOWN)
    return [dict(id=tid, refs=[], norm=norm, raw=meta.get("status", ""),
                 prs=[], line=1, user_story=meta.get("user_story"))]


PARSERS = {"checkbox": parse_checkbox, "ledger": parse_ledger,
           "table": parse_table, "tc": parse_tc, "generic": parse_generic}


def is_plan_like(rel):
    return bool(re.search(r"(PLAN|STATUS|TRIAGE|DECISIONS|MATRIX)", rel, re.I))


def scan(root, living_only=False, module=None):
    root = os.path.abspath(root)
    docs, all_items, unregistered = [], [], []

    # 1. Registered living docs
    targets = {}
    for rel, (parser, vocab) in REGISTRY.items():
        p = os.path.join(root, rel)
        if os.path.exists(p):
            targets[_norm(rel)] = (parser, vocab, "living")

    # 2. Auto-discovered plan docs (historical unless registered)
    if not living_only:
        for g in DISCOVER_GLOBS:
            for p in glob.glob(os.path.join(root, g), recursive=True):
                rel = _rel(p, root)
                if rel in targets:
                    continue
                lifecycle = "historical" if DATE_IN_NAME_RX.search(rel) else "living-candidate"
                targets[rel] = ("generic", "finding", lifecycle)
                if is_plan_like(rel):
                    unregistered.append(rel)
        # 3. TC frontmatter (ground-truth per test)
        for p in glob.glob(os.path.join(root, TC_GLOB), recursive=True):
            targets[_rel(p, root)] = ("tc", "tc", "living")

    for rel, (parser, vocab, lifecycle) in sorted(targets.items()):
        try:
            with open(os.path.join(root, rel), encoding="utf-8") as fh:
                text = fh.read()
        except OSError:
            continue
        items = PARSERS[parser](text, vocab)
        if module:
            items = [it for it in items if module.upper() in it["id"].upper()]
        for it in items:
            it["doc"], it["lifecycle"] = rel, lifecycle
            all_items.append(it)
        countable = [it for it in items if it["norm"] != UNKNOWN]
        done = [it for it in countable if it["norm"] == DONE]
        docs.append(dict(
            doc=rel, parser=parser, vocab=vocab, lifecycle=lifecycle,
            items=len(items), countable=len(countable), done=len(done),
            pct=round(100 * len(done) / len(countable), 1) if countable else None))

    merged = merged_prs(root)
    drift = compute_drift(all_items, merged)
    rollup = compute_rollup(all_items)
    return dict(
        root=root, merged_pr_count=len(merged),
        docs=docs, unregistered_docs=sorted(set(unregistered)),
        rollup=rollup, drift=drift,
        item_count=len(all_items))


def compute_rollup(items):
    """Dedup by ID; canonical status = highest-precedence across LIVING docs."""
    by_id = {}
    for it in items:
        by_id.setdefault(it["id"], []).append(it)
    kinds = {}
    for _id, occ in by_id.items():
        kind = re.match(r"[A-Z]+", _id).group(0) if re.match(r"[A-Z]+", _id) else "OTHER"
        if _id.startswith("US"):
            kind = "US"
        elif _id.startswith("TC") or _id.startswith("ISO"):
            kind = "TC"
        living = [o for o in occ if o["lifecycle"] == "living"] or occ
        canon = max(living, key=lambda o: PRECEDENCE[o["norm"]])["norm"]
        b = kinds.setdefault(kind, {"total": 0, DONE: 0, IN_PROGRESS: 0,
                                    TODO: 0, BLOCKED: 0, SKIPPED: 0, UNKNOWN: 0})
        b["total"] += 1
        b[canon] += 1
    for k, b in kinds.items():
        denom = b["total"] - b[SKIPPED] - b[UNKNOWN]
        b["pct_done"] = round(100 * b[DONE] / denom, 1) if denom else None
    return kinds


def compute_drift(items, merged):
    by_id = {}
    for it in items:
        by_id.setdefault(it["id"], []).append(it)
    drift = []
    for _id, occ in sorted(by_id.items()):
        docs_seen = sorted(set(o["doc"] for o in occ))
        living = [o for o in occ if o["lifecycle"] == "living"]
        statuses = set(o["norm"] for o in living if o["norm"] != UNKNOWN)

        # DUPLICATE-TRACKING: same item tracked in >2 distinct docs
        if len(docs_seen) > 2:
            drift.append(dict(type="DUPLICATE-TRACKING", id=_id,
                              docs=docs_seen, detail=f"tracked in {len(docs_seen)} docs"))

        # STATUS-CONFLICT: living docs disagree done-vs-not
        if DONE in statuses and (TODO in statuses or BLOCKED in statuses):
            drift.append(dict(type="STATUS-CONFLICT", id=_id, docs=docs_seen,
                              detail="DONE in one living doc, open/blocked in another",
                              where=[f"{o['doc']}:{o['line']}={o['norm']}" for o in living]))

        # STALE-CHECKBOX: a finding is DONE (RESOLVED w/ merged PR) but a board
        # still lists it open.
        resolved_prs = [p for o in occ if o["norm"] == DONE for p in o["prs"]]
        merged_here = [p for p in resolved_prs if p in merged]
        board_open = [o for o in living if o["norm"] in (TODO, BLOCKED)
                      and o["doc"].endswith("STATUS.md")]
        if merged_here and board_open:
            drift.append(dict(type="STALE-CHECKBOX", id=_id,
                              detail=f"resolved via merged PR #{merged_here[0]} but still open on a board",
                              where=[f"{o['doc']}:{o['line']}" for o in board_open]))

        # UNVERIFIED-CLAIM: marked DONE citing a PR that is NOT merged
        for o in occ:
            if o["norm"] == DONE and o["prs"]:
                if not any(p in merged for p in o["prs"]):
                    drift.append(dict(type="UNVERIFIED-CLAIM", id=_id,
                                      detail=f"{o['doc']}:{o['line']} claims DONE via PR "
                                             f"#{o['prs'][0]} which is not merged on this branch"))
    return drift


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=".")
    ap.add_argument("--living-only", action="store_true")
    ap.add_argument("--module", default=None)
    ap.add_argument("--pretty", action="store_true")
    a = ap.parse_args()
    result = scan(a.root, living_only=a.living_only, module=a.module)
    print(json.dumps(result, indent=2 if a.pretty else None))


if __name__ == "__main__":
    main()
