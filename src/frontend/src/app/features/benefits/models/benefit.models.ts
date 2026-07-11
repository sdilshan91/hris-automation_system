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
  code?: 'invalid_status_transition' | 'plan_has_enrollments' | string;
}
