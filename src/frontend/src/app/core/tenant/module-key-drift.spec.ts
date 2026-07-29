import { CANONICAL_MODULE_KEYS } from './module.guard';
import { CANONICAL_MODULES } from '../../features/admin/plans/models/plan.models';

/**
 * ISSUE-353 — drift guard between the two FRONTEND copies of the canonical module vocabulary.
 *
 * The keys are duplicated on purpose: `core/` must not import from a feature (`features/admin/plans`),
 * so the guard carries its own copy. That is the right layering call, but it leaves nothing asserting
 * the two agree — which is the exact shape of the bug this whole story exists to fix (ISSUE-335: two
 * module vocabularies, nothing linking them, invisible because nothing read the column).
 *
 * Why a mismatch is worse than it looks: `isModuleEntitled` FAILS OPEN on any token it does not
 * recognize. So if the plan editor can grant a module key the guard has never heard of, a tenant
 * holding that key trips the unknown-token branch and the frontend silently stops enforcing
 * entitlement ENTIRELY — while the backend keeps enforcing it. The UI and the API then disagree about
 * what is enabled, silently, and the symptom looks like "it works".
 *
 * This closes two of the three copies. The third — the backend's `PlanModules.All` — cannot be reached
 * from a frontend spec; ISSUE-353 tracks the contract fixture that would close it.
 */
describe('canonical module key drift (ISSUE-353)', () => {
  it('module.guard keys exactly match the plan editor CANONICAL_MODULES keys', () => {
    const guardKeys = [...CANONICAL_MODULE_KEYS].sort();
    const editorKeys = CANONICAL_MODULES.map((m) => m.key).sort();

    // Order-insensitive but membership-exact, in BOTH directions: an extra key in either copy fails.
    // A one-directional `toContain` sweep would pass while one list quietly grew.
    expect(guardKeys).toEqual(editorKeys);
  });

  it('CoreHR is present in both and is the always-on module', () => {
    // CoreHR is the specific key whose absence turns a fail-closed gate into a total outage — it covers
    // employees, departments and the dashboard. Pinned explicitly so it can never be dropped quietly.
    expect(CANONICAL_MODULE_KEYS).toContain('CoreHR');
    expect(CANONICAL_MODULES.find((m) => m.key === 'CoreHR')?.alwaysOn).toBeTrue();
  });

  it('neither copy contains a legacy permission-prefix token (ISSUE-335 regression)', () => {
    // The pre-normalization seed wrote permission prefixes into enabled_modules. If any of these ever
    // reappear in a canonical list, the vocabularies have re-merged and the original bug is back.
    const legacyOnlyTokens = ['Audit', 'CustomField', 'Department', 'Employee', 'Roles', 'Tenant', 'Reports'];

    for (const token of legacyOnlyTokens) {
      expect(CANONICAL_MODULE_KEYS).not.toContain(token);
      expect(CANONICAL_MODULES.map((m) => m.key)).not.toContain(token);
    }
  });
});
