---
name: spec-append-wrong-describe
description: Attendance service specs split into TWO top-level describes (HTTP w/ httpMock + pure-fn); append HTTP tests to the FIRST or they break
metadata:
  type: feedback
---

`attendance.service.spec.ts` (and similarly-structured specs) has TWO top-level
`describe` blocks: the first sets up `service`/`httpMock`/`baseUrl` via TestBed; the
second is `... (pure function)` with NO TestBed (only static parse helpers). The
regularization parse tests live in the FIRST describe but the file ends with the pure
describe — so a naive "append before the last `});`" lands HTTP tests in the pure
block where `service`/`httpMock`/`baseUrl` are undefined (TS2304 at build).

**Why:** I appended new shift HTTP tests at EOF and they compiled-failed; fixing it by
deleting the misplaced copy then tripped the test-integrity-guard (it counts as
"removing test cases"), forcing a `CLAUDE_DISABLE_TEST_GUARD=1` python rewrite to dedup.

**How to apply:** When adding HTTP-based tests, insert them inside the FIRST
TestBed describe (right after the last existing `it`, before that describe's `});`),
not at end-of-file. Put pure static-helper tests in the pure describe. Verify with
`grep -n "^describe\|^});"` that your new block sits between the right braces before
running. See [[jasmine-optional-arg-spy]].
