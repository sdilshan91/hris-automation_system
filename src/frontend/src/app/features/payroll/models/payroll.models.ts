/**
 * US-PAY-001: Payroll salary-structure & salary-component models matching the
 * backend API contract.
 *
 * Backend endpoints (assumed contract — backend agent building in parallel; the
 * service layer is intentionally thin so a route mismatch is a one-file fix):
 *   GET    /api/v1/payroll/salary-components                    - list
 *   POST   /api/v1/payroll/salary-components                    - create
 *   PUT    /api/v1/payroll/salary-components/:id                - update
 *   DELETE /api/v1/payroll/salary-components/:id                - delete (409 if in use)
 *   POST   /api/v1/payroll/salary-components/reorder            - bulk processing-order
 *   POST   /api/v1/payroll/salary-components/validate-formula   - formula "Test"
 *   GET    /api/v1/payroll/salary-structures                    - list
 *   POST   /api/v1/payroll/salary-structures/:id/clone          - clone (FR-6)
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/payroll/...`. All requests are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header) and use withCredentials for the
 * httpOnly cookie auth. The backend stamps tenant_id and audit fields server-side
 * (FR-8, §7) — the FE never sends them.
 *
 * ENUM CASING (US-PLT-003 — critical): every enum is a PascalCase string union
 * matching the C# member names (global JsonStringEnumConverter). The API returns
 * enums as STRINGS, never integers. The global ApiResponse unwrap interceptor
 * (US-PLT-001) already strips the envelope — services here consume BARE payloads.
 */

import type { Schema } from '@core/api';

// ─── Enums ────────────────────────────────────────────────────

/** Salary-component type (FR-1, §7). Matches C# `SalaryComponentType`. */
export type SalaryComponentType =
  | 'Earning'
  | 'Deduction'
  | 'Statutory'
  | 'Reimbursement';

export const COMPONENT_TYPE_OPTIONS: SalaryComponentType[] = [
  'Earning',
  'Deduction',
  'Statutory',
  'Reimbursement',
];

/** Tailwind badge classes per component type (§8). Single source of truth. */
export const COMPONENT_TYPE_BADGE: Record<SalaryComponentType, string> = {
  Earning: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Deduction: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Statutory: 'bg-indigo-50 text-indigo-700 ring-indigo-600/20',
  Reimbursement: 'bg-amber-50 text-amber-700 ring-amber-600/20',
};

/** Calculation method (FR-1, §7). Matches C# `CalculationMethod`. */
export type CalculationMethod =
  | 'Fixed'
  | 'PercentageOfBasic'
  | 'PercentageOfGross'
  | 'Formula';

export const CALCULATION_METHOD_OPTIONS: CalculationMethod[] = [
  'Fixed',
  'PercentageOfBasic',
  'PercentageOfGross',
  'Formula',
];

/** Human-readable labels for calculation methods (wire value is PascalCase). */
export const CALCULATION_METHOD_LABELS: Record<CalculationMethod, string> = {
  Fixed: 'Fixed amount',
  PercentageOfBasic: '% of Basic',
  PercentageOfGross: '% of Gross',
  Formula: 'Formula',
};

// ─── Salary component ─────────────────────────────────────────

/** A tenant-scoped salary component (FR-1, §7). */
export interface ISalaryComponent {
  id: string;
  name: string;
  /** Unique per tenant (BR-2). */
  code: string;
  type: SalaryComponentType;
  calculationMethod: CalculationMethod;
  /** Fixed amount OR percentage value depending on `calculationMethod`. */
  defaultValue: number | null;
  /** Safe expression, used only when calculationMethod === 'Formula' (FR-4). */
  formulaExpression: string | null;
  isTaxable: boolean;
  isStatutory: boolean;
  isActive: boolean;
  processingOrder: number;
}

/**
 * Create/update payload (FR-1). Deliberately omits id, processingOrder, and all
 * server-managed fields (tenant_id, audit) — the backend assigns them. On create
 * the backend appends to the end of the processing order; reorders go through the
 * dedicated reorder endpoint.
 */
export interface ISalaryComponentRequest {
  name: string;
  code: string;
  type: SalaryComponentType;
  calculationMethod: CalculationMethod;
  defaultValue: number | null;
  formulaExpression: string | null;
  isTaxable: boolean;
  isStatutory: boolean;
}

/** Reorder payload (AC-4): the new processing order as an ordered list of ids. */
export interface IReorderRequest {
  orderedIds: string[];
}

/** Formula "Test" request/response (FR-4, §8). */
export interface IFormulaTestRequest {
  expression: string;
  /** Sample variable values, e.g. { basic: 1000, gross: 1500 }. */
  sampleValues: Record<string, number>;
}

export interface IFormulaTestResult {
  valid: boolean;
  /** Evaluated numeric result when valid. */
  result?: number;
  /** Human-readable syntax / circular-reference error when invalid (BR-6). */
  error?: string;
}

// ─── Salary structure ─────────────────────────────────────────

/** A tenant-scoped salary structure (FR-2, §7). */
export interface ISalaryStructure {
  id: string;
  name: string;
  code: string;
  description: string | null;
  /** ISO date (yyyy-MM-dd). */
  effectiveFrom: string;
  isDefault: boolean;
  isActive: boolean;
  /** Number of linked components — drives the card subtitle. Optional on the wire. */
  componentCount?: number;
}

// ─── Error shapes ─────────────────────────────────────────────

/**
 * AC-5: deleting a component that is in use by active employees returns 409 with
 * a body carrying the affected count. The service normalizes this so the UI can
 * show the count without parsing the raw error.
 */
export interface IComponentInUseError {
  code: 'component_in_use';
  affectedEmployeeCount: number;
  message?: string;
}

// ─── Wire contract → view-model mappers (D1 payroll slice) ───────────────────
//
// `http.get<ISalaryComponent[]>(…)` was an unchecked ASSERTION, not a check: TypeScript believed the
// annotation and the server sent something else. These aliases bind the view-models above to the
// GENERATED contract (`contracts/openapi/hrm-v1.json` → `core/api/generated/api-types.ts`), so a backend
// rename becomes a compile error here instead of an `undefined` cell in the salary-components table.
//
// DEFAULTING POLICY (payroll is money — a default is a decision):
//  - Flags (`isActive`, `isDefault`, `isTaxable`, `isStatutory`) default to FALSE. An absent `isActive`
//    must not present a component as live and assignable; the LEAST-CLAIMING value wins.
//  - `defaultValue` / `description` default to NULL, not 0 / '' — both are `nullable` in the SCHEMA
//    itself and the UI renders "no value" differently from a zero amount ("—" vs "0.00").
//  - `processingOrder` defaults to 0: it is a non-nullable int the server always computes, and the UI
//    draws no distinction between "order 0" and "no order".
//  - ENUMS (`type`, `calculationMethod`) are NOT coerced to a member — see `rawEnum` below.
//
// NOTE on the two list endpoints: both are PAGED (`PagedResultOf…`, server default pageSize 25) and the
// list rows are the LEANER `…ListItemDto`, not the full `…Dto`. That difference is load-bearing — see
// `mapSalaryComponentListItem`.

export type SalaryComponentWire = Schema<'PayrollSalaryComponentDto'>;
export type SalaryComponentListItemWire = Schema<'PayrollSalaryComponentListItemDto'>;
export type SalaryComponentPageWire =
  Schema<'PagedResultOfPayrollSalaryComponentListItemDto'>;
export type SalaryStructureWire = Schema<'PayrollSalaryStructureDto'>;
export type SalaryStructureListItemWire = Schema<'PayrollSalaryStructureListItemDto'>;
export type SalaryStructurePageWire =
  Schema<'PagedResultOfPayrollSalaryStructureListItemDto'>;

/**
 * Pass a wire enum through WITHOUT substituting a member.
 *
 * Both C# enum properties are non-nullable and always serialised as strings
 * (JsonStringEnumConverter), so `undefined` here means the contract was violated, not that the component
 * is in some real "unknown" state. Every generated property is `?` purely because Swashbuckle does not
 * emit `required` for non-nullable reference types — see the header of `core/api/index.ts`.
 *
 * We deliberately do NOT pick a fallback member. `type` decides whether a line ADDS to or SUBTRACTS from
 * pay, and `calculationMethod` decides whether a number is an amount or a percentage; defaulting either
 * would be a wrong claim about money on screen (the admin slice's `'terminated'` mistake, in a payroll
 * context). The raw value is passed through instead: `{{ c.type }}` renders it verbatim and
 * `COMPONENT_TYPE_BADGE[…]` / `CALCULATION_METHOD_LABELS[…]` degrade to an unstyled, unlabelled cell —
 * visibly missing rather than confidently wrong.
 *
 * Widening `ISalaryComponent['type']` to admit an explicit "unknown" member is the better fix, but it
 * would force a change to `component-form.component.ts`'s reactive-form control types, which is outside
 * this task's lane. Flagged as a DECISION in the D1 report.
 */
function rawEnum<T extends string>(wire: T | undefined): T {
  return (wire ?? '') as T;
}

/**
 * Map the FULL salary-component DTO (create / update / get-by-id responses).
 * This is the only shape that carries `formulaExpression`.
 */
export function mapSalaryComponent(w: SalaryComponentWire): ISalaryComponent {
  return {
    id: w.id ?? '',
    name: w.name ?? '',
    code: w.code ?? '',
    type: rawEnum(w.type),
    calculationMethod: rawEnum(w.calculationMethod),
    // `nullable` in the schema: null means "no default configured", which the list renders as "—".
    defaultValue: w.defaultValue ?? null,
    formulaExpression: w.formulaExpression ?? null,
    isTaxable: w.isTaxable ?? false,
    isStatutory: w.isStatutory ?? false,
    // Fail CLOSED: an absent flag must not present a component as live and assignable.
    isActive: w.isActive ?? false,
    processingOrder: w.processingOrder ?? 0,
  };
}

/**
 * Map a LIST row (`GET /payroll/salary-components`).
 *
 * ⚠ `PayrollSalaryComponentListItemDto` has NO `formulaExpression` — the field simply does not exist on
 * the list wire. It is mapped to `null` because that is the only honest value available here, NOT because
 * the component has no formula. Consequences, both flagged in the D1 report (see BUG "formula wipe"):
 *   - the list's Value column always renders "—" for a Formula component;
 *   - `component-form` patches its form from THIS object and posts the result back, so saving an edit
 *     writes `formulaExpression: null` over a real formula.
 * The fix is for the edit flow to fetch `GET /payroll/salary-components/{id}` (which returns the full
 * `PayrollSalaryComponentDto`) — a component change, outside this task's lane.
 */
export function mapSalaryComponentListItem(
  w: SalaryComponentListItemWire,
): ISalaryComponent {
  return {
    id: w.id ?? '',
    name: w.name ?? '',
    code: w.code ?? '',
    type: rawEnum(w.type),
    calculationMethod: rawEnum(w.calculationMethod),
    defaultValue: w.defaultValue ?? null,
    // NO WIRE SOURCE on the list DTO — see the warning above. Never treat this as "has no formula".
    formulaExpression: null,
    isTaxable: w.isTaxable ?? false,
    isStatutory: w.isStatutory ?? false,
    isActive: w.isActive ?? false,
    processingOrder: w.processingOrder ?? 0,
  };
}

/** Map the FULL salary-structure DTO (create / clone / get-by-id responses). */
export function mapSalaryStructure(w: SalaryStructureWire): ISalaryStructure {
  return {
    id: w.id ?? '',
    name: w.name ?? '',
    code: w.code ?? '',
    description: w.description ?? null,
    effectiveFrom: w.effectiveFrom ?? '',
    // Fail CLOSED on both: an absent flag must not mark a structure default or assignable.
    isDefault: w.isDefault ?? false,
    isActive: w.isActive ?? false,
    // Derived, not defaulted: when `components` is present its length IS the count; when the wire omits
    // the array the count is genuinely UNKNOWN, so the optional field stays absent rather than claiming 0.
    componentCount: w.components ? w.components.length : undefined,
  };
}

/**
 * Map a LIST row (`GET /payroll/salary-structures`).
 *
 * ⚠ `PayrollSalaryStructureListItemDto` has NO `description` — so the structure card's
 * `@if (s.description)` block can never render from the list. Mapped to `null` (the honest value) and
 * flagged in the D1 report as a dead UI control.
 */
export function mapSalaryStructureListItem(
  w: SalaryStructureListItemWire,
): ISalaryStructure {
  return {
    id: w.id ?? '',
    name: w.name ?? '',
    code: w.code ?? '',
    // NO WIRE SOURCE on the list DTO — see the warning above.
    description: null,
    effectiveFrom: w.effectiveFrom ?? '',
    isDefault: w.isDefault ?? false,
    isActive: w.isActive ?? false,
    componentCount: w.componentCount ?? undefined,
  };
}
