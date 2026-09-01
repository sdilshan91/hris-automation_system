---
name: wire-migration-parallel-split
description: How to parallelise a large single-file wire-type migration — fan out research, apply edits serially; sub-agents write fragments, never the shared file
metadata:
  type: feedback
---

For a large migration concentrated in ONE service + ONE models file (attendance: 41 call sites, 31 response
types, 1785-line models file), split by DOMAIN CONCERN across parallel sub-agents — but have them write only
to their own scratch fragment file and return an exact-match SERVICE EDIT TABLE. The lead applies all edits
to the shared files serially.

**Why:** the expensive work is per-type field-by-field comparison against the contract plus the default
decision — that parallelises perfectly. The application is mechanical. Letting six agents write the same two
files violates the no-concurrent-writes rule and produces merge churn; `isolation: worktree` for six agents
on two files is worse.

**How to apply:**
- Give every agent one shared brief file plus a pre-rendered dump of the module's DTO schemas
  (`python` over `contracts/openapi/hrm-v1.json` → properties, types, nullability) so six agents do not each
  re-parse a 479-path document.
- Assign shared child types explicitly ("SHIFTS owns `AttendanceRotationDto`") or you get duplicate
  declarations. With that in place a `grep '^export ' frag-*.ts | uniq -c` collision check came back clean.
- Require the edit table to quote the CURRENT source verbatim, including any pre-existing
  `.pipe(map(res => res?.items ?? []))` — those sites are the ones a naive replacement breaks.
- Apply with a python script doing `assert s.count(old)==1` per edit. It catches a stale line number
  immediately instead of silently editing the wrong site.
- Establish the green baseline (both tsc projects + the module's tests) BEFORE touching anything, so a
  failure later is unambiguously yours.
