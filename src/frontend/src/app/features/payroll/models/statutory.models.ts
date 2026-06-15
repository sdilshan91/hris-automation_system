/**
 * US-PAY-006: Statutory deductions configuration models — income tax slabs,
 * social-security (EPF/ETF) rules, and other statutory deductions, versioned by
 * fiscal year (AC-3).
 *
 * Backend endpoints (assumed REST contract — backend agent building in parallel;
 * the service layer is intentionally thin so a route mismatch is a one-file fix):
 *   GET    /api/v1/payroll/statutory-rules?fiscalYear=          - list (per FY)
 *   GET    /api/v1/payroll/statutory-rules/fiscal-years         - distinct FYs (FR-4)
 *   POST   /api/v1/payroll/statutory-rules                      - create rule
 *   PUT    /api/v1/payroll/statutory-rules/:id                  - update rule
 *   DELETE /api/v1/payroll/statutory-rules/:id                  - delete rule
 *   POST   /api/v1/payroll/statutory-rules/test-calculation     - sample gross → deductions (FR-5)
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/payroll/...`. All requests are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header) and use withCredentials for the
 * httpOnly cookie auth. The backend stamps tenant_id + audit fields and enforces
 * RLS (AC-4, FR-8) — the FE never sends them.
 *
 * ENUM CASING (US-PLT-003 — critical): every enum is a PascalCase string union
 * matching the C# member names (global JsonStringEnumConverter). The API returns
 * enums as STRINGS, never integers. The global ApiResponse unwrap interceptor
 * (US-PLT-001) already strips the envelope — services here consume BARE payloads.
 */

// ─── Enums ────────────────────────────────────────────────────

/**
 * Statutory rule type (§7 `rule_type`). Matches C# `StatutoryRuleType`. Drives
 * which tab/editor a rule belongs to.
 */
export type StatutoryRuleType =
  | 'IncomeTax'
  | 'EPF'
  | 'ETF'
  | 'ProfessionalTax'
  | 'Custom';

export const STATUTORY_RULE_TYPE_OPTIONS: StatutoryRuleType[] = [
  'IncomeTax',
  'EPF',
  'ETF',
  'ProfessionalTax',
  'Custom',
];

/** Human-readable labels (wire value is PascalCase). */
export const STATUTORY_RULE_TYPE_LABELS: Record<StatutoryRuleType, string> = {
  IncomeTax: 'Income Tax',
  EPF: 'Employee Provident Fund',
  ETF: 'Employees’ Trust Fund',
  ProfessionalTax: 'Professional Tax',
  Custom: 'Custom statutory item',
};

/**
 * Wage base a social-security / deduction rule applies on (§7 `applicable_on`).
 * Matches C# `ApplicableOn`.
 */
export type ApplicableOn = 'Basic' | 'Gross' | 'Custom';

export const APPLICABLE_ON_OPTIONS: ApplicableOn[] = ['Basic', 'Gross', 'Custom'];

export const APPLICABLE_ON_LABELS: Record<ApplicableOn, string> = {
  Basic: 'Basic salary',
  Gross: 'Gross earnings',
  Custom: 'Custom components',
};

// ─── Tax slab (income tax) ────────────────────────────────────

/** A single progressive income-tax slab (§7 tax_slab, FR-1/BR-3). */
export interface ITaxSlab {
  /** Server id; absent on a newly-added (unsaved) row. */
  id?: string;
  /** Lower bound of the slab (inclusive). */
  slabFrom: number;
  /** Upper bound; null = unlimited (the top slab, §7 nullable). */
  slabTo: number | null;
  /** Marginal rate applied to income within this slab (BR-3). */
  ratePercentage: number;
}

// ─── Social-security rule (EPF / ETF / etc.) ──────────────────

/** Social-security contribution config (§7 social_security_rule, FR-2). */
export interface ISocialSecurityRule {
  employeeRate: number;
  employerRate: number;
  /** Annual ceiling; null = no ceiling (§7 nullable). Monthly = annual / 12 (BR-8). */
  wageCeilingAnnual: number | null;
  applicableOn: ApplicableOn;
  /** Component ids when `applicableOn === 'Custom'` (§7). */
  applicableComponentIds?: string[];
}

// ─── Statutory rule (versioned container, §7 statutory_rule) ──

/**
 * A versioned statutory rule for the tenant (FR-3/FR-4). Either `slabs` (when
 * type === 'IncomeTax') OR `socialSecurity` (EPF/ETF/ProfessionalTax/Custom) is
 * populated, matching `ruleType`.
 */
export interface IStatutoryRule {
  id: string;
  ruleType: StatutoryRuleType;
  ruleName: string;
  countryCode: string;
  /** e.g. "2026-2027" (AC-3 versioning, §7). */
  fiscalYear: string;
  /** ISO date (yyyy-MM-dd). */
  effectiveFrom: string;
  /** ISO date or null (open-ended). */
  effectiveTo: string | null;
  isActive: boolean;
  slabs?: ITaxSlab[];
  socialSecurity?: ISocialSecurityRule | null;
  /** Server audit timestamp — drives the version-history timeline (FR-4). */
  updatedAt?: string;
}

/**
 * Create/update payload (FR-1/FR-2). Omits id and server-managed fields
 * (tenant_id, audit) — the backend assigns them. Slabs/socialSecurity are sent
 * per `ruleType`; the irrelevant one is omitted.
 */
export interface IStatutoryRuleRequest {
  ruleType: StatutoryRuleType;
  ruleName: string;
  countryCode: string;
  fiscalYear: string;
  effectiveFrom: string;
  slabs?: ITaxSlab[];
  socialSecurity?: ISocialSecurityRule | null;
}

// ─── Test calculation (FR-5) ──────────────────────────────────

/** Test-calc request: a sample monthly gross for a fiscal year (FR-5, §8). */
export interface ITestCalculationRequest {
  fiscalYear: string;
  monthlyGross: number;
  /** Optional monthly basic (EPF is applied on basic); defaults to gross server-side. */
  monthlyBasic?: number;
}

/** A single computed deduction line in the test-calc result. */
export interface ITestDeductionLine {
  ruleType: StatutoryRuleType;
  label: string;
  amount: number;
}

/** Test-calc response: computed statutory deductions for the sample gross (FR-5). */
export interface ITestCalculationResult {
  monthlyGross: number;
  incomeTax: number;
  employeeEpf: number;
  employerEpf: number;
  etf: number;
  otherDeductions: number;
  /** Total of the EMPLOYEE-borne deductions (tax + employee EPF + other). */
  totalDeductions: number;
  /** monthlyGross − totalDeductions. */
  netPay: number;
  /** Per-line breakdown for the panel; tolerant of an empty/absent list. */
  lines?: ITestDeductionLine[];
}

// ─── Client-side slab validation (FR-6) ───────────────────────

/** Reason a slab row fails contiguity validation (FR-6). */
export type SlabIssue = 'overlap' | 'gap' | 'invalid';

/**
 * Per-row validation flags computed client-side for real-time highlighting
 * (FR-6, §8). Index-aligned to the slab list.
 */
export interface ISlabValidation {
  /** Rows (by index) flagged with an issue → highlighted red in the editor. */
  issues: Map<number, SlabIssue>;
  /** True when the whole slab set is contiguous and saveable. */
  valid: boolean;
}
