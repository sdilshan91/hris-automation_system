/**
 * GAP-S1 — step 2 of 2 of the contract gate: fail when the committed TypeScript models no longer match
 * the committed OpenAPI document.
 *
 * Run AFTER `npm run api:types` has regenerated the models: this only inspects the working tree.
 *
 * Why not a bare `git diff --exit-code`: `git diff` does not report UNTRACKED files, so on any checkout
 * where the generated file is missing or newly added, the regeneration would produce a brand-new file and
 * the gate would exit 0 having verified nothing. A guard that can pass vacuously is the same failure mode
 * this whole pipeline exists to eliminate (see the RLS-vs-EF-filter lesson, finding S-2), so this uses
 * `git status --porcelain`, which reports modified AND untracked paths.
 *
 * Node rather than shell so it behaves identically under Linux CI and a Windows dev's `npm run`.
 */

import { execFileSync } from 'node:child_process';

const GENERATED_DIR = 'src/app/core/api/generated';

let status;
try {
  status = execFileSync('git', ['status', '--porcelain', '--', GENERATED_DIR], {
    encoding: 'utf8',
  }).trim();
} catch (error) {
  console.error(`check-api-types: could not run git (${error.message}).`);
  process.exit(1);
}

if (status === '') {
  console.log(`check-api-types: OK — ${GENERATED_DIR} matches contracts/openapi/hrm-v1.json.`);
  process.exit(0);
}

console.error(`
GAP-S1 CONTRACT GATE: the generated TypeScript models are STALE.

  Regenerating from contracts/openapi/hrm-v1.json changed ${GENERATED_DIR}, which means the committed
  models describe a different contract than the API does. This is the drift that broke 9 of 13 modules —
  an Angular layer coded against a shape the backend cannot emit, with specs mocking the invented shape.

  git status reports:
${status
  .split('\n')
  .map((line) => `    ${line}`)
  .join('\n')}

  Fix: commit the regenerated models.
    npm run api:types    (from src/frontend)
  If the backend contract itself changed, regenerate that first:
    scripts/gen-openapi.sh    (from the repo root)
`);
process.exit(1);
