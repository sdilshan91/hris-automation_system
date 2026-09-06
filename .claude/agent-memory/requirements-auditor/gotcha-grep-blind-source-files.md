---
name: gotcha-grep-blind-source-files
description: Three .cs files contain raw NUL bytes so grep/rg treat them as binary and skip them silently — any grep-based audit or semgrep scan of this repo has a blind spot
metadata:
  type: project
---

Three C# source files in `src/backend` contain **literal NUL bytes**, which makes `file(1)` report
them as `data`. `grep -r` / `rg` skip them **silently** (no "Binary file matches" line, just no
output and exit 1):

- `src/backend/HRM.Infrastructure/Services/AuditAnonymizationService.cs` — **production code**.
  Cause: `const string sentinel = "\x00REDACTED\x00";` at ~line 129 embeds real NUL bytes in the
  literal instead of using the escape `"\0REDACTED\0"`. The file is otherwise valid UTF-8.
- `src/backend/HRM.Tests/Unit/AesGcmFieldEncryptorTests.cs`
~~- `src/backend/HRM.Tests/Unit/EncryptingFileStorageTests.cs`~~ — **REMOVED 2026-09-06: this file has
  NEVER contained a NUL byte**, in any commit (`b63b77b0`, the only commit touching it, has a clean blob).
  It was a false positive that this note then cached, and `BUG-448` still carries it.

**Why:** any inventory built with `grep -rn` under-counts, and the `.semgrep/tenant-isolation.yml`
scan very likely never analyses these files at all. `AuditAnonymizationService.cs` holds two
`IgnoreQueryFilters()` sites — one a cross-tenant **write** over `audit_logs` — so the security
linter's most sensitive rule has a hole in exactly the kind of file it exists to watch.

**How to apply:** when you need a complete inventory of a code pattern in this repo, do **not**
trust `grep -r` alone. Cross-check with a Python/`find`-based walk that opens files with
`errors='replace'`, or first run
`find src/backend -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | xargs file | grep -v text`
to list the blind spots. Detected 2026-09-02 while auditing the `IgnoreQueryFilters()` surface;
verify the list before relying on it — and verify it BYTE-EXACTLY (`tr -d '\000' | cmp`), not with grep,
which is the very tool that is blind here.

⚠ **Exclude `.claude/worktrees/*` when counting.** Those are full repo copies, so an unfiltered walk
returns 6 paths for 2 unique files; partial de-duplication is how a "5" was reported on 2026-09-06. The
verified answer that day was **exactly 2 files**.

## The opposite error: `.claude/worktrees/*` makes grep OVER-count

Confirmed 2026-09-06. `.claude/worktrees/p14/` and `.claude/worktrees/plan94/` hold **full copies of
the repo**, including `src/` and every `docs/QA/` ledger. An unfiltered `grep -rn FOO .` therefore
reports a finding or code pattern 2–3× and can make a worktree-only edit look like it landed in the
main tree. Always scope to `src/` or `docs/`, or exclude `.claude/worktrees`.

This also matters for **ledger state**: a batch/queue edit may exist only on a worktree's branch. On
2026-09-06 the "94 findings scheduled" batch lived solely in
`.claude/worktrees/plan94/docs/QA/plans/GAP-CLOSURE-QUEUE.md` (branch `docs/schedule-the-94`) while
the canonical `docs/QA/plans/GAP-CLOSURE-QUEUE.md` contained **none** of it. Check which tree a
claim about ledger state actually refers to before treating it as current.

Related: [[reference-gap-tracking-sources]], [[project-ledger-staleness-rate]].
