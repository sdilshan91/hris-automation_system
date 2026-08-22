---
name: audit-depth
description: How this user wants test-authenticity audits scoped — they pre-run mutation checks; focus on coverage-of-right-things + missing boundary arms
metadata:
  type: feedback
---

When this user requests a test audit, they often **pre-run their own mutation/tautology checks** and report the killed mutants. In that case, do NOT re-litigate whether assertions are tautological — accept their mutation evidence and spend the effort on the **harder questions**: (1) are the arms testing the RIGHT behavior (e.g. losslessness proven by a round-trip to the ORIGINAL value, not just "value changed"), and (2) what important arms are MISSING (boundaries, key-rotation-vs-missing-key distinctions, column-length/overflow, empty-vs-null).

**Why:** they explicitly said "the arms are not tautological ... what I want is the harder question." Re-proving non-tautology wastes the turn.

**How to apply:** lead with per-question evidence (quote the load-bearing assertion with file:line), then a ranked list of missing arms. Flag production-boot risks (startup back-fills that can throw) loudly. Related: [[green-theater-history]].
