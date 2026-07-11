/**
 * US-TRN-002: Benefits Plan Administration models.
 *
 * These interfaces mirror the pinned backend DTO contract EXACTLY (the backend
 * serializes camelCase JSON). Enums are string unions matching the C# enum
 * member names. `DateOnly?` fields are serialized as `yyyy-MM-dd` strings;
 * `DateTime?` fields as ISO-8601 strings.
 */

// --- Enums (string unions matching HRM.Domain/Enums) -------------

/** BenefitType { Health, Dental, Vision, Life, Retirement, Disability, Other } */
export type BenefitType =
  | 'Health'
  | 'Dental'
  | 'Vision'
  | 'Life'
  | 'Retirement'
  | 'Disability'
  | 'Other';

/** BenefitPlanStatus { Draft, Active, Inactive, Archived } */
export type BenefitPlanStatus = 'Draft' | 'Active' | 'Inactive' | 'Archived';

/** Selectable benefit types (create/edit form). */
export const BENEFIT_TYPES: readonly BenefitType[] = [
  'Health',
  'Dental',
  'Vision',
  'Life',
  'Retirement',
  'Disability',
  'Other',
];

/** All plan statuses (display + transition helpers). */
export const BENEFIT_PLAN_STATUSES: readonly BenefitPlanStatus[] = [
  'Draft',
  'Active',
  'Inactive',
  'Archived',
];

// --- DTOs (mirror HRM.Application/Features/Benefits/DTOs) ---------

/** BenefitPlanDto — a tenant's benefit offering. */
export interface IBenefitPlan {
  id: string;
  name: string;
  type: BenefitType;
  description: string | null;
  coverageDetails: string | null;
  /** decimal? — employer's share of the cost. */
  employerCost: number | null;
  /** decimal? — employee's share of the cost. */
  employeeCost: number | null;
  currency: string;
  /** DateOnly → 'yyyy-MM-dd' */
  effectiveFrom: string;
  /** DateOnly? → 'yyyy-MM-dd' (null = open-ended). */
  effectiveTo: string | null;
  /** DateOnly? → 'yyyy-MM-dd' — optional enrollment window open (US-TRN-003). */
  enrollmentOpensAt: string | null;
  /** DateOnly? → 'yyyy-MM-dd' — optional enrollment window close (US-TRN-003). */
  enrollmentClosesAt: string | null;
  status: BenefitPlanStatus;
  createdAt: string;
  updatedAt: string | null;
}

/** CreateBenefitPlanRequest — creates a Draft plan (null currency → tenant default). */
export interface ICreateBenefitPlan {
  name: string;
  type: BenefitType;
  description?: string | null;
  coverageDetails?: string | null;
  employerCost?: number | null;
  employeeCost?: number | null;
  currency?: string | null;
  effectiveFrom: string;
  effectiveTo?: string | null;
  enrollmentOpensAt?: string | null;
  enrollmentClosesAt?: string | null;
}

/** UpdateBenefitPlanRequest — same fields as create (metadata edit, not status). */
export type IUpdateBenefitPlan = ICreateBenefitPlan;

/** ChangeBenefitPlanStatusRequest — target status (server validates the transition). */
export interface IChangeBenefitPlanStatus {
  status: BenefitPlanStatus;
}

/** Error response shape from the benefits endpoints. */
export interface IBenefitErrorResponse {
  message: string;
  code?:
    | 'invalid_status_transition'
    | 'plan_has_enrollments'
    | 'invalid_rule'
    | 'not_eligible'
    | 'already_enrolled'
    | 'enrollment_window_closed'
    | 'plan_not_active'
    | string;
}

// =================================================================
// US-TRN-003: Benefit Eligibility & Enrollment
// =================================================================

// --- Enums (string unions matching HRM.Domain/Enums) -------------

/** EligibilityAttribute { EmploymentType, TenureDays, Department, JobGrade } */
export type EligibilityAttribute =
  | 'EmploymentType'
  | 'TenureDays'
  | 'Department'
  | 'JobGrade';

/** BenefitEnrollmentStatus { Active, Pending, Declined, Terminated } */
export type BenefitEnrollmentStatus =
  | 'Active'
  | 'Pending'
  | 'Declined'
  | 'Terminated';

/** CoverageLevel { EmployeeOnly, EmployeeSpouse, Family } */
export type CoverageLevel = 'EmployeeOnly' | 'EmployeeSpouse' | 'Family';

/** All eligibility attributes (rule editor dropdown). */
export const ELIGIBILITY_ATTRIBUTES: readonly EligibilityAttribute[] = [
  'EmploymentType',
  'TenureDays',
  'Department',
  'JobGrade',
];

/**
 * Comparison operators. The backend validates that the operator is legal for
 * the chosen attribute (EmploymentType → ==/!= only; TenureDays → all numeric;
 * Department/JobGrade → ==/!=/In). Kept as a flat list for the editor.
 */
export const ELIGIBILITY_OPERATORS: readonly string[] = [
  '==',
  '!=',
  '>',
  '>=',
  '<',
  '<=',
  'In',
];

/** All coverage levels (enrollment dropdown). */
export const COVERAGE_LEVELS: readonly CoverageLevel[] = [
  'EmployeeOnly',
  'EmployeeSpouse',
  'Family',
];

/** Human-readable labels for coverage levels. */
export const COVERAGE_LEVEL_LABELS: Record<CoverageLevel, string> = {
  EmployeeOnly: 'Employee only',
  EmployeeSpouse: 'Employee + spouse',
  Family: 'Family',
};

// --- DTOs (mirror HRM.Application/Features/Benefits/DTOs) ---------

/** EligibilityRuleDto — a single ANDed eligibility condition on a plan. */
export interface IEligibilityRule {
  id: string;
  benefitPlanId: string;
  attribute: EligibilityAttribute;
  /** One of ==,!=,>,>=,<,<=,In (legal set depends on the attribute). */
  operator: string;
  /** Scalar, or a comma-separated list when operator is 'In'. */
  value: string;
}

/** CreateEligibilityRuleRequest — adds a rule to a plan (Manage). */
export interface ICreateEligibilityRule {
  attribute: EligibilityAttribute;
  operator: string;
  value: string;
}

/** EligiblePlanDto — a plan the current employee qualifies for right now. */
export interface IEligiblePlan {
  planId: string;
  name: string;
  type: string;
  currency: string;
  /** decimal? — employee's share of the cost. */
  employeeCost: number | null;
  /** decimal? — employer's share of the cost. */
  employerCost: number | null;
  /** DateOnly → 'yyyy-MM-dd' */
  effectiveFrom: string;
  /** DateOnly? → 'yyyy-MM-dd' (null = open-ended). */
  effectiveTo: string | null;
  /** Whether the enrollment window is currently open. */
  enrollmentOpen: boolean;
}

/** EnrollRequest — enroll self (null employeeId) or another (Manage). */
export interface IEnrollRequest {
  planId: string;
  coverageLevel: CoverageLevel;
  /** null → the current user's employee; non-null → requires Manage. */
  employeeId?: string | null;
}

/** BenefitEnrollmentDto — an employee's enrollment in a plan. */
export interface IBenefitEnrollment {
  id: string;
  planId: string;
  planName: string;
  employeeId: string;
  employeeName: string;
  status: BenefitEnrollmentStatus;
  coverageLevel: CoverageLevel;
  /** DateOnly → 'yyyy-MM-dd' */
  effectiveDate: string;
  /** DateOnly? → 'yyyy-MM-dd' (null while active). */
  endDate: string | null;
  /** DateTime → ISO-8601 */
  electedAt: string;
}

/** TerminateEnrollmentRequest — end an active enrollment (default today). */
export interface ITerminateEnrollmentRequest {
  /** DateOnly? → 'yyyy-MM-dd'. Omit/null → backend defaults to today. */
  endDate?: string | null;
}
