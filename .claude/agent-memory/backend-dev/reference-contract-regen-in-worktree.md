---
name: reference-contract-regen-in-worktree
description: Regenerating the OpenAPI contract + FE types from a git worktree — gen-openapi.sh works, npm run api:types does not (no node_modules)
metadata:
  type: reference
---

Any controller-action or DTO change makes `contracts/openapi/hrm-v1.json` stale and CI's GAP-S1
`--check` gate fails. Two steps, and only the first works out of the box in a fresh worktree:

1. `bash scripts/gen-openapi.sh` — works anywhere. Builds HRM.Api and emits the doc from the
   **assembly** (no Kestrel, no DB), so it needs no running stack. It prints the path-count so you
   can sanity-check it actually rewrote the file.
2. `cd src/frontend && npm run api:types` — **fails in a worktree** with `sh: 1: openapi-typescript:
   not found`, because `git worktree add` does not copy `node_modules` and nobody runs `npm ci`
   there. Do NOT `npm ci` just for this (minutes, and a huge untracked tree). Invoke the main
   checkout's binary directly against the worktree's paths:

   `/mnt/d/WORK/hris-automation_system/src/frontend/node_modules/.bin/openapi-typescript ../../contracts/openapi/hrm-v1.json -o src/app/core/api/generated/api-types.ts`

   run with cwd = the **worktree's** `src/frontend`. It only reads the main checkout's node_modules;
   it writes only inside the worktree.

**Why:** the generated `api-types.ts` lives under `src/frontend/`, which is normally out of the
backend lane — but it is machine-generated from the backend's own Swagger doc, so regenerating it is
part of the backend change, not frontend work. Say so in the report rather than skipping it and
leaving CI red.

**How to apply:** any story that adds/changes a `[Route]`, action signature, or a DTO shape. Verify
with `git diff --stat contracts/` plus a grep for the new schema name in both generated files.

Related: [[reference-architecture-tests-project]].
