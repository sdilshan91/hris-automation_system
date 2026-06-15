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
