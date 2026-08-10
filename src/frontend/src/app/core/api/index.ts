/**
 * GAP-S1 — the single source of truth for the FE↔BE contract.
 *
 * WHY THIS EXISTS
 * The 2026-08-08 gap analysis found the contract had drifted in 9 of 13 modules, and that this one defect
 * class accounts for ~70% of every PARTIAL requirement in the product. The cause was never carelessness — it
 * was that the contract had TWO hand-written descriptions (C# DTOs and Angular interfaces) with nothing
 * comparing them. Two services still carry the comment `CONTRACT (assumed — reconcile with backend)`. It was
 * fixed pointwise twice (`document.service.ts`, `custom-field.service.ts`) and never generalised. The one
 * clean performance surface, US-PRF-004, is clean precisely because it got an explicit reconciliation pass.
 *
 * The types re-exported here are GENERATED from the API's own OpenAPI document, so the compiler — not a code
 * review — is what notices when a backend DTO changes shape.
 *
 * THE PIPELINE (two steps, both gated in CI)
 *   1. `scripts/gen-openapi.sh`      C# assembly  → contracts/openapi/hrm-v1.json   (committed)
 *   2. `npm run api:types`           that document → core/api/generated/api-types.ts (committed)
 * CI runs step 1 with `--check` and re-runs step 2 then diffs, so a DTO rename that is not regenerated fails
 * the build at the point of change instead of silently reaching a runtime 400 months later.
 *
 * NEVER hand-edit `generated/api-types.ts` — it is overwritten on every regeneration.
 *
 * HOW TO USE IT
 *   import { Schema } from '@core/api';
 *   type Employee = Schema<'EmployeesEmployeeDto'>;
 *
 * Schema names are Swashbuckle's ids, which carry the feature-namespace prefix (`EmployeesEmployeeDto`, not
 * `EmployeeDto`). Autocomplete on `Schema<'…'>` lists every available name. Most endpoints wrap their payload
 * in the envelope, so the type you usually want is the inner `data`:
 *   type EmployeeResponse = Schema<'ApiResponseOfEmployeesEmployeeDto'>;
 *
 * MIGRATING A HAND-WRITTEN MODEL (the G2 work this unblocks)
 * Replace the hand-written interface with a `Schema<'…'>` alias and let `tsc` point at every call site that
 * disagreed. The failures ARE the contract bugs — GAP-009 through GAP-016 are all instances. Do it a module
 * at a time; a rename surfaced this way is a compile error, which is the entire point.
 *
 * Note on optionality: every generated property is optional (`?`) because Swashbuckle does not emit
 * `required` for non-nullable C# reference types under the current configuration. So these types catch WRONG
 * and MISSPELLED fields — the whole of the observed drift — but do not yet prove a field is always present.
 * Tightening that is a follow-up on the backend schema config, not a reason to hand-edit these types.
 */

import type { components, paths, operations } from './generated/api-types';

/** Every schema in the API contract, keyed by its OpenAPI schema id. */
export type Schemas = components['schemas'];

/** A single DTO by name: `Schema<'EmployeesEmployeeDto'>`. */
export type Schema<K extends keyof Schemas> = Schemas[K];

/** The full generated surface, for the rare case a route or operation type is needed directly. */
export type { components, paths, operations };
