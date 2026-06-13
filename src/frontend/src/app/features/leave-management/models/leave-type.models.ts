/**
 * US-LV-001: Leave Type models matching the backend API contract.
 *
 * Backend endpoint: /api/v1/tenant/leave-types
 * The backend agent is building this in parallel. Assumed contract:
 *   - leaveTypeId (uuid), tenantId (uuid), name (string, unique per tenant case-insensitive),
 *     code (string), color (hex string), description, annualEntitlement (number),
 *     accrualFrequency (string enum), carryForwardLimit (number), carryForwardExpiryMonths (number),
 *     probationEligible (boolean), documentsRequired (boolean), documentDayThreshold (number),
 *     encashable (boolean), maxEncashDays (number), halfDayAllowed (boolean),
 *     hourlyAllowed (boolean), gender (string enum), maxConsecutiveDays (number),
 *     negativeBalanceAllowed (boolean), negativeBalanceLimit (number),
 *     displayOrder (number), isActive (boolean),
 *     createdAt, updatedAt (ISO 8601 strings).
 */

/** Accrual frequency options per FR-2 */
export type AccrualFrequency = 'monthly' | 'quarterly' | 'yearly' | 'upfront';

/** Gender applicability options per FR-2 */
export type GenderApplicability = 'all' | 'male' | 'female';

/** Leave type entity returned by the API */
export interface ILeaveType {
  leaveTypeId: string;
  tenantId: string;
  name: string;
  code: string;
  color: string;
  description: string | null;
  annualEntitlement: number;
  accrualFrequency: AccrualFrequency;
  carryForwardLimit: number;
  carryForwardExpiryMonths: number;
  probationEligible: boolean;
  documentsRequired: boolean;
  documentDayThreshold: number | null;
  encashable: boolean;
  maxEncashDays: number | null;
  halfDayAllowed: boolean;
  hourlyAllowed: boolean;
  gender: GenderApplicability;
  maxConsecutiveDays: number | null;
  negativeBalanceAllowed: boolean;
  negativeBalanceLimit: number | null;
  displayOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

/** Request payload for creating a leave type (FR-1, FR-2) */
export interface ICreateLeaveTypeRequest {
  name: string;
  code: string;
  color: string;
  description?: string | null;
  annualEntitlement: number;
  accrualFrequency: AccrualFrequency;
  carryForwardLimit: number;
  carryForwardExpiryMonths: number;
  probationEligible: boolean;
  documentsRequired: boolean;
  documentDayThreshold?: number | null;
  encashable: boolean;
  maxEncashDays?: number | null;
  halfDayAllowed: boolean;
  hourlyAllowed: boolean;
  gender: GenderApplicability;
  maxConsecutiveDays?: number | null;
  negativeBalanceAllowed: boolean;
  negativeBalanceLimit?: number | null;
}

/** Request payload for updating a leave type (FR-1, FR-2) */
export interface IUpdateLeaveTypeRequest extends ICreateLeaveTypeRequest {}

/** Request payload for reordering leave types (FR-3) */
export interface IReorderLeaveTypesRequest {
  orderedIds: string[];
}

/** Error response shape from the backend for leave type operations */
export interface ILeaveTypeErrorResponse {
  message: string;
  code?: 'duplicate_name' | string;
}

/** Accrual frequency display labels */
export const ACCRUAL_FREQUENCY_OPTIONS: { value: AccrualFrequency; label: string }[] = [
  { value: 'monthly', label: 'Monthly' },
  { value: 'quarterly', label: 'Quarterly' },
  { value: 'yearly', label: 'Yearly' },
  { value: 'upfront', label: 'Upfront' },
];

/** Gender applicability display labels */
export const GENDER_OPTIONS: { value: GenderApplicability; label: string }[] = [
  { value: 'all', label: 'All Employees' },
  { value: 'male', label: 'Male Only' },
  { value: 'female', label: 'Female Only' },
];

/** Default color palette for leave type color picker */
export const LEAVE_TYPE_COLORS: string[] = [
  '#2563eb', // blue
  '#16a34a', // green
  '#dc2626', // red
  '#d97706', // amber
  '#7c3aed', // violet
  '#0891b2', // cyan
  '#db2777', // pink
  '#65a30d', // lime
  '#ea580c', // orange
  '#6366f1', // indigo
  '#0d9488', // teal
  '#a855f7', // purple
];

/**
 * Pure helper: compute contrasting text color for a given hex background.
 * Returns 'white' or 'black' based on luminance.
 */
export function getContrastTextColor(hex: string): string {
  const c = hex.replace('#', '');
  const r = parseInt(c.substring(0, 2), 16);
  const g = parseInt(c.substring(2, 4), 16);
  const b = parseInt(c.substring(4, 6), 16);
  // Relative luminance (ITU-R BT.709)
  const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  return luminance > 0.5 ? '#000000' : '#ffffff';
}
