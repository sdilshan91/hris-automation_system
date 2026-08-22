---
name: leave-apply-form-spec-gotcha
description: In LeaveApplicationComponent.submit(), the document-required hard block runs before the insufficient-balance/LOP path — balance tests must not use a document-required leave type
metadata:
  type: feedback
---

In `LeaveApplicationComponent.submit()` (leave-management apply form), the ordering is:
form-validity → **document-required hard block (AC-3)** → insufficient-balance / LOP path
(US-LV-011 AC-1) → send.

**Why:** the existing `leave-application.component.spec.ts` fixture defines `lt-2` as a
document-required Sick Leave type (`documentsRequired:true, documentDayThreshold:2`). A test
that drives an *insufficient-balance* scenario on `lt-2` with > threshold days and no
attachment hits the **document block first** and returns early — so the LOP prompt never
opens and the test mis-fails with "Expected null not to be null".

**How to apply:** when writing balance/LOP/projection tests on this form, use `lt-1`
(Annual, no document requirement) and set a low balance via `component.balances.set([...])`,
OR attach a file. Do NOT reach for `lt-2` just because it already has a low remaining balance
in the fixture. Same applies if the submit() ordering is ever refactored — keep the doc block
and balance/LOP block independent so tests can target each in isolation.
