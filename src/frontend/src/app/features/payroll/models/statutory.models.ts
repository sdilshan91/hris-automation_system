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

import type { Schema } from '@core/api';

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
  /**
   * GAP-010: the wire name is `taxSlabs`, not `slabs`. With the wrong name the API bound no slabs, so
   * **income-tax slabs could not be saved from the UI at all** — the rule saved with an empty band set.
   */
  taxSlabs?: ITaxSlab[];
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
  /**
   * GAP-010: the wire name is `taxSlabs`, not `slabs`. With the wrong name the API bound no slabs, so
   * **income-tax slabs could not be saved from the UI at all** — the rule saved with an empty band set.
   */
  taxSlabs?: ITaxSlab[];
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

// ─── Wire contract → view-model mappers (D1 payroll slice) ───────────────────
//
// `http.get<IStatutoryRule[]>(…)` was an unchecked assertion, not a check. These aliases bind the
// view-models above to the GENERATED contract so a backend rename is a compile error here rather than a
// silently-undefined tax band. Statutory rules ARE the tax engine's input, so the defaulting policy is
// stricter than elsewhere in payroll:
//
//  - `slabTo` and `wageCeilingAnnual` are genuinely nullable on the schema and MUST stay `null` when
//    absent. `null` means "unbounded"; `0` would mean "this band/ceiling is empty" — the top tax band
//    would collect nothing and an EPF ceiling of 0 would zero the contribution. Never `?? 0` on these.
//  - `slabFrom`, `ratePercentage`, `employeeRate`, `employerRate` are non-nullable C# decimals; their `?`
//    in the generated type is purely the Swashbuckle artifact described in `core/api/index.ts`, so they
//    are always present on the wire. They fall back to `0` only because the view-model types them as a
//    plain `number` (the editor binds them to numeric inputs and cannot render "unknown"). If one of
//    these ever IS absent the fallback understates a deduction — it does not overstate it.
//  - `isActive` falls back to `false` (an absent flag must not claim a rule is live).
//  - `ruleType` falls back to `'Custom'`, NOT `'IncomeTax'`. `'Custom'` is the catch-all bucket, and the
//    editor selects its tabs with `find(r => r.ruleType === 'IncomeTax' | 'EPF')` — defaulting to a real
//    type would let a rule whose type the server never sent hijack the income-tax or EPF editor.
//  - `applicableOn` falls back to `'Basic'`, which is the C# enum's zero-value member — i.e. the same
//    value the server itself would have defaulted to. There is no "unknown" member to prefer.

export type StatutoryRuleWire = Schema<'PayrollStatutoryRuleDto'>;
export type StatutoryRuleListItemWire = Schema<'PayrollStatutoryRuleListItemDto'>;
export type StatutoryRulePageWire = Schema<'PagedResultOfPayrollStatutoryRuleListItemDto'>;
export type TaxSlabWire = Schema<'PayrollTaxSlabDto'>;
export type SocialSecurityRuleWire = Schema<'PayrollSocialSecurityRuleDto'>;
export type StatutoryCalculationResultWire = Schema<'PayrollStatutoryCalculationResultDto'>;
export type StatutoryLineWire = Schema<'PayrollStatutoryLineDto'>;
export type CreateStatutoryRuleWire = Schema<'PayrollCreateStatutoryRuleRequest'>;
export type UpdateStatutoryRuleWire = Schema<'PayrollUpdateStatutoryRuleRequest'>;
export type TestCalculationRequestWire = Schema<'PayrollTestCalculationRequest'>;

/** One progressive tax band: wire → view-model. `slabTo: null` = the unbounded top band. */
export function mapTaxSlab(w: TaxSlabWire): ITaxSlab {
  return {
    // Left `undefined` when the server did not send one, matching "a new unsaved row has no id".
    id: w.id,
    slabFrom: w.slabFrom ?? 0,
    // MUST stay null — `0` would collapse the top (unlimited) band to an empty range.
    slabTo: w.slabTo ?? null,
    ratePercentage: w.ratePercentage ?? 0,
  };
  // NOTE: the wire also carries `orderIndex`; the view-model has no field for it and the editor treats
  // slab order as positional. See the D1 report (slab ordering is positional-only on the FE).
}

/** EPF/ETF contribution config: wire → view-model. `wageCeilingAnnual: null` = no ceiling. */
export function mapSocialSecurityRule(w: SocialSecurityRuleWire): ISocialSecurityRule {
  return {
    employeeRate: w.employeeRate ?? 0,
    employerRate: w.employerRate ?? 0,
    // MUST stay null — `0` would mean "ceiling of zero", i.e. no contribution at all (BR-8).
    wageCeilingAnnual: w.wageCeilingAnnual ?? null,
    // 'Basic' is the C# enum's zero-value member; there is no "unknown" member to prefer.
    applicableOn: w.applicableOn ?? 'Basic',
    applicableComponentIds: w.applicableComponentIds ?? undefined,
  };
}

/**
 * A FULL statutory rule (`GET /statutory-rules/{id}`, and the create/update/clone responses):
 * wire → view-model, including the tax bands and the social-security block.
 */
export function mapStatutoryRule(w: StatutoryRuleWire): IStatutoryRule {
  return {
    id: w.id ?? '',
    // 'Custom' (the catch-all) so an unknown rule cannot hijack the IncomeTax/EPF editors.
    ruleType: w.ruleType ?? 'Custom',
    ruleName: w.ruleName ?? '',
    countryCode: w.countryCode ?? '',
    fiscalYear: w.fiscalYear ?? '',
    effectiveFrom: w.effectiveFrom ?? '',
    effectiveTo: w.effectiveTo ?? null,
    // An absent flag must not claim the rule is live.
    isActive: w.isActive ?? false,
    // `undefined` (not `[]`) when the server sent no `taxSlabs` key at all: "this endpoint does not
    // carry bands" is a different claim from "this rule has zero bands".
    taxSlabs: w.taxSlabs ? w.taxSlabs.map(mapTaxSlab) : undefined,
    socialSecurity: w.socialSecurity ? mapSocialSecurityRule(w.socialSecurity) : null,
    updatedAt: w.updatedAt ?? undefined,
  };
  // NOTE: the wire also carries `isCumulative`, `exemptions`, `createdAt` and `ruleTypeName`, none of
  // which the view-model has. See the D1 report — `isCumulative` + `exemptions` are backend features
  // with no FE surface at all, not fields this mapper chose to drop.
}

/**
 * A statutory rule as returned by the LIST endpoint. `GET /payroll/statutory-rules` returns
 * `PagedResultOfPayrollStatutoryRuleListItemDto`, and a list item carries **no `taxSlabs`, no
 * `socialSecurity` and no `updatedAt`** — only a `slabCount`. They are therefore left `undefined`
 * here rather than being faked as `[]`/`null`-with-content. This is not a mapping choice that can be
 * improved: the data is not in the response. The editor hydrates from the list and consequently sees
 * no bands — flagged in the D1 report as a live defect requiring a `getRule(id)` fetch before edit.
 */
export function mapStatutoryRuleListItem(w: StatutoryRuleListItemWire): IStatutoryRule {
  return {
    id: w.id ?? '',
    ruleType: w.ruleType ?? 'Custom',
    ruleName: w.ruleName ?? '',
    countryCode: w.countryCode ?? '',
    fiscalYear: w.fiscalYear ?? '',
    effectiveFrom: w.effectiveFrom ?? '',
    effectiveTo: w.effectiveTo ?? null,
    isActive: w.isActive ?? false,
    socialSecurity: null,
  };
}

/** One line of the test-calc breakdown: wire → view-model. */
export function mapTestDeductionLine(w: StatutoryLineWire): ITestDeductionLine {
  return {
    ruleType: w.ruleType ?? 'Custom',
    label: w.label ?? '',
    amount: w.amount ?? 0,
  };
  // NOTE: the wire line also carries `basis` and `isEmployerContribution`; the view-model has neither,
  // and nothing renders `lines` today.
}

/**
 * Test-calculation result (FR-5): wire → view-model.
 *
 * TWO RENAMES (mapped, never renamed on the view-model — components bind to these names):
 *   `otherStatutory`         → `otherDeductions`
 *   `totalEmployeeDeductions` → `totalDeductions`
 *
 * TWO FIELDS WITH NO WIRE SOURCE — `PayrollStatutoryCalculationResultDto` carries neither
 * `monthlyGross` nor `netPay`, yet the panel renders "Net pay". Both are therefore supplied from the
 * REQUEST the caller just sent (which the FE owns — the admin typed the gross) rather than being
 * defaulted to `0`, which would print a confidently wrong money figure. `netPay` uses the definition
 * the view-model itself documents: gross − employee-borne deductions. Employer EPF/ETF are employer
 * contributions and correctly do not reduce net. Both are flagged in the D1 report: the honest fix is
 * for the backend to return them, not for the FE to keep deriving them.
 *
 * The wire's `professionalTax` has NO view-model field, so a PT deduction is invisible in the panel
 * while still being counted inside `totalEmployeeDeductions` — the displayed lines will not sum to the
 * displayed total. It is NOT folded into `otherDeductions`; that would be inventing arithmetic the
 * server did not do. Flagged separately.
 */
export function mapTestCalculationResult(
  w: StatutoryCalculationResultWire,
  requestMonthlyGross: number,
): ITestCalculationResult {
  const totalDeductions = w.totalEmployeeDeductions ?? 0;
  return {
    // Echoed from the request — no wire source (flagged).
    monthlyGross: requestMonthlyGross,
    incomeTax: w.incomeTax ?? 0,
    employeeEpf: w.employeeEpf ?? 0,
    employerEpf: w.employerEpf ?? 0,
    etf: w.etf ?? 0,
    // RENAME: wire `otherStatutory` → view-model `otherDeductions`.
    otherDeductions: w.otherStatutory ?? 0,
    // RENAME: wire `totalEmployeeDeductions` → view-model `totalDeductions`.
    totalDeductions,
    // Derived — no wire source (flagged).
    netPay: requestMonthlyGross - totalDeductions,
    lines: w.lines ? w.lines.map(mapTestDeductionLine) : undefined,
  };
}

// ─── View-model → wire REQUEST bodies ───────────────────────────────────────
//
// Typing the request bodies against the contract is what makes a missing/renamed request field a
// compile error instead of a 400 (or worse, a silently-ignored property). Fields the FE has no source
// for are OMITTED, never invented:
//   - `isActive`      — omitted so the server applies its own `= true` default. Sending `false` here
//                       would create every rule inactive; that is exactly the "a default is a decision"
//                       trap, on the money path.
//   - `isCumulative`  — no FE surface (see the D1 report). Omitted → server default `false`.
//   - `exemptions`    — no FE surface at all. Omitted → no exemptions.
//   - `effectiveTo`   — `IStatutoryRuleRequest` has no such field. Omitted → open-ended.
// `PayrollUpdateStatutoryRuleRequest` has no `ruleType` (the rule's type is immutable server-side), so
// the update mapper drops the one the form carries instead of sending a property the server ignores.

/** Serialize the create/update slab rows, stamping the positional `orderIndex` the contract expects. */
function toTaxSlabWires(
  slabs: ITaxSlab[] | undefined,
): CreateStatutoryRuleWire['taxSlabs'] {
  return slabs?.map((s, index) => ({
    // Positional order IS the band order in the editor; the server re-indexes by `orderIndex`, so
    // sending it explicitly removes the reliance on a stable sort of an all-zero index.
    orderIndex: index,
    slabFrom: s.slabFrom,
    slabTo: s.slabTo,
    ratePercentage: s.ratePercentage,
  }));
}

/** Serialize the social-security block for a create/update body. */
function toSocialSecurityWire(
  rule: ISocialSecurityRule | null | undefined,
): CreateStatutoryRuleWire['socialSecurity'] {
  return rule
    ? {
        employeeRate: rule.employeeRate,
        employerRate: rule.employerRate,
        wageCeilingAnnual: rule.wageCeilingAnnual,
        applicableOn: rule.applicableOn,
        applicableComponentIds: rule.applicableComponentIds,
      }
    : undefined;
}

/** `IStatutoryRuleRequest` → `PayrollCreateStatutoryRuleRequest` (FR-1/FR-2). */
export function toCreateStatutoryRuleWire(
  req: IStatutoryRuleRequest,
): CreateStatutoryRuleWire {
  return {
    ruleType: req.ruleType,
    ruleName: req.ruleName,
    countryCode: req.countryCode,
    fiscalYear: req.fiscalYear,
    effectiveFrom: req.effectiveFrom,
    taxSlabs: toTaxSlabWires(req.taxSlabs),
    socialSecurity: toSocialSecurityWire(req.socialSecurity),
  };
}

/** `IStatutoryRuleRequest` → `PayrollUpdateStatutoryRuleRequest` (no `ruleType`; it is immutable). */
export function toUpdateStatutoryRuleWire(
  req: IStatutoryRuleRequest,
): UpdateStatutoryRuleWire {
  return {
    ruleName: req.ruleName,
    countryCode: req.countryCode,
    fiscalYear: req.fiscalYear,
    effectiveFrom: req.effectiveFrom,
    taxSlabs: toTaxSlabWires(req.taxSlabs),
    socialSecurity: toSocialSecurityWire(req.socialSecurity),
  };
}

/**
 * `ITestCalculationRequest` → `PayrollTestCalculationRequest` (FR-5).
 *
 * DELIBERATELY OMITS `countryCode`, because `ITestCalculationRequest` has no such field and this
 * mapper must not invent one. That omission is a LIVE DEFECT, not a stylistic gap: with a null country
 * `StatutoryDeductionResolver.ResolveAsync` resolves NOTHING by design ("with no resolved tax country we
 * must resolve NOTHING — NEVER apply an arbitrary country's rules"), so the FR-5 preview returns all
 * zeros however the tenant's slabs are configured. Flagged in the D1 report; the fix needs the caller to
 * pass the rule's country, which is a component change and out of this task's lane.
 *
 * The remaining wire fields (`componentAmounts`, `declaredMonthlyExemptions`, `exemptEarnings`,
 * `priorTaxWithheldYtd`, `priorTaxableIncomeYtd`) are non-nullable server-side and default to 0 /
 * none, which is the plain monthly, non-cumulative preview the FE intends.
 */
export function toTestCalculationRequestWire(
  req: ITestCalculationRequest,
): TestCalculationRequestWire {
  return {
    fiscalYear: req.fiscalYear,
    monthlyGross: req.monthlyGross,
    monthlyBasic: req.monthlyBasic,
  };
}
