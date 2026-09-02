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
- `src/backend/HRM.Tests/Unit/EncryptingFileStorageTests.cs`

**Why:** any inventory built with `grep -rn` under-counts, and the `.semgrep/tenant-isolation.yml`
scan very likely never analyses these files at all. `AuditAnonymizationService.cs` holds two
`IgnoreQueryFilters()` sites — one a cross-tenant **write** over `audit_logs` — so the security
linter's most sensitive rule has a hole in exactly the kind of file it exists to watch.

**How to apply:** when you need a complete inventory of a code pattern in this repo, do **not**
trust `grep -r` alone. Cross-check with a Python/`find`-based walk that opens files with
`errors='replace'`, or first run
`find src/backend -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | xargs file | grep -v text`
to list the blind spots. Detected 2026-09-02 while auditing the `IgnoreQueryFilters()` surface;
verify the list is still the same three before relying on it.

Related: [[reference-gap-tracking-sources]].
