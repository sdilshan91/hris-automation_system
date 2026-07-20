/**
 * US-ADM-006: Tenant Admin company-settings models.
 *
 * Consolidated in ONE file for easy realignment if the backend contract shifts
 * (the BE agent finalizes exact field names in parallel). All endpoints are
 * TENANT-scoped under `/api/v1/tenant/settings...`; tenant isolation is enforced
 * server-side via ITenantContext + EF global filters. Mirrors the user-management
 * (US-ADM-005) service shape: bare payloads (no ApiResponse unwrap) + the
 * tenantInterceptor's X-Tenant-Subdomain header.
 */

// ─── Sub-objects (each is the body of its own PUT endpoint) ──────────────

/**
 * AC-1 / FR-1: organization profile (org.* settings keys).
 *
 * The PUT /tenant/settings/org-profile endpoint is a FULL-REPLACE: every field
 * below is round-tripped, so the FE MUST load AND resend all of them — omitting
 * any wipes the stored value to its default (ISSUE-322 data-loss). These keys
 * match exactly what the GET aggregate (`ToOrgProfileDto`) returns.
 */
export interface IOrgProfile {
  name: string;
  legalName: string;
  registrationNumber: string;
  address: string;
  industry: string;
  companySize: string;
  /** 1–12; affects leave accrual / payroll cycles / reporting (BR-4). */
  fiscalYearStartMonth: number;
  /** 2-letter ISO country code (BE upper-cases it), e.g. 'LK'. */
  defaultCountryCode: string | null;
  /** Default probation length in days (BE default 90 when omitted). */
  probationPeriodDays: number;
  /**
   * DF-20 / ISSUE-044 (US-LV-010 FR-7): tenant self-cancellation window in days,
   * 0–90 (BE default 0). An employee may self-cancel an Approved leave only when
   * its start date is more than N days away (N=0 = any time strictly before start);
   * inside the window the BE rejects the cancel with a 400.
   */
  leaveCancellationWindowDays: number;
  /** ISSUE-159 footer text on payslips; blank clears it. */
  payslipFooterDisclaimer: string | null;
  /** ISSUE-229 sender email for payroll notifications; BE validates format. */
  payrollFromEmail: string | null;
}

/** AC-2 / FR-2 / FR-3: branding (branding.* settings keys). */
export interface IBranding {
  logoUrl: string | null;
  emailLogoUrl: string | null;
  faviconUrl: string | null;
  /** #RRGGBB primary brand colour. */
  primaryColor: string;
}

/** AC-3 / FR-4: localization (locale.* settings keys). */
export interface ILocalization {
  defaultLanguage: string;
  /** A date-format token, e.g. 'dd MMM yyyy' or 'MM/dd/yyyy'. */
  dateFormat: string;
  /** A number-format token, e.g. '1,234.56' or '1.234,56'. */
  numberFormat: string;
  timeZone: string;
  /** ISO-4217 currency code, e.g. 'USD'. */
  currency: string;
}

/** AC-4 / FR-5: password policy (security.password_policy). */
export interface IPasswordPolicy {
  minLength: number;
  requireUppercase: boolean;
  requireLowercase: boolean;
  requireDigit: boolean;
  requireSpecialCharacter: boolean;
  /** Number of previous passwords disallowed on change. */
  historyCount: number;
  /** Max password age in days (0 = never expires). */
  maxAgeDays: number;
}

/** FR-6: session policy (security.session_policy). */
export interface ISessionPolicy {
  idleTimeoutMinutes: number;
  absoluteTimeoutHours: number;
  maxConcurrentSessions: number;
}

/**
 * BR-3: plan-gating. The backend MAY return a `plan` block flagging settings
 * that are constrained by the subscription tier (enterprise-only). The UI
 * disables those options with an "Upgrade your plan" indicator. If the backend
 * omits this (no plan signal), the UI no-ops gracefully — nothing is gated.
 */
export interface IPlanInfo {
  /** e.g. 'starter' | 'professional' | 'enterprise'. */
  tier?: string;
  /**
   * Feature keys the current plan does NOT permit (enterprise-only). The UI
   * matches against well-known keys (e.g. 'branding.customColor').
   */
  lockedFeatures?: string[];
}

// ─── Aggregate (the shape of GET /tenant/settings) ───────────────────────

export interface ICompanySettings {
  org: IOrgProfile;
  branding: IBranding;
  localization: ILocalization;
  passwordPolicy: IPasswordPolicy;
  sessionPolicy: ISessionPolicy;
  /** Optional — present only when the backend supplies a plan signal (BR-3). */
  plan?: IPlanInfo;
}

// ─── Branding upload (multipart) ─────────────────────────────────────────

/** Which branding slot a file upload targets (matches the multipart `slot`). */
export type BrandingSlot = 'logo' | 'emailLogo' | 'favicon';

/** Response of POST /tenant/settings/branding/upload. */
export interface IBrandingUploadResult {
  url: string;
}

// ─── Derived colour theme (FR-3) ─────────────────────────────────────────

/**
 * A primary colour expanded into the complementary shades the UI applies via
 * CSS custom properties. Pure, client-derived from the chosen primary so the
 * live preview is instant; the backend only persists the primary hex.
 */
export interface IColorTheme {
  primary: string;
  dark: string;
  light: string;
  contrastText: string;
}

// ─── Dropdown option catalogues (client-side, FR-4) ──────────────────────

export interface IOption {
  value: string;
  label: string;
}

export const FISCAL_MONTHS: IOption[] = [
  { value: '1', label: 'January' },
  { value: '2', label: 'February' },
  { value: '3', label: 'March' },
  { value: '4', label: 'April' },
  { value: '5', label: 'May' },
  { value: '6', label: 'June' },
  { value: '7', label: 'July' },
  { value: '8', label: 'August' },
  { value: '9', label: 'September' },
  { value: '10', label: 'October' },
  { value: '11', label: 'November' },
  { value: '12', label: 'December' },
];

export const COMPANY_SIZES: IOption[] = [
  { value: '1-10', label: '1–10 employees' },
  { value: '11-50', label: '11–50 employees' },
  { value: '51-200', label: '51–200 employees' },
  { value: '201-500', label: '201–500 employees' },
  { value: '501-1000', label: '501–1000 employees' },
  { value: '1000+', label: '1000+ employees' },
];

export const LANGUAGES: IOption[] = [
  { value: 'en', label: 'English' },
  { value: 'es', label: 'Spanish (Español)' },
];

export const DATE_FORMATS: IOption[] = [
  { value: 'dd MMM yyyy', label: '11 May 2026' },
  { value: 'MM/dd/yyyy', label: '05/11/2026' },
  { value: 'dd/MM/yyyy', label: '11/05/2026' },
  { value: 'yyyy-MM-dd', label: '2026-05-11' },
];

export const NUMBER_FORMATS: IOption[] = [
  { value: '1,234.56', label: '1,234.56 (comma / dot)' },
  { value: '1.234,56', label: '1.234,56 (dot / comma)' },
  { value: '1 234.56', label: '1 234.56 (space / dot)' },
];

export const CURRENCIES: IOption[] = [
  { value: 'USD', label: 'USD — US Dollar' },
  { value: 'EUR', label: 'EUR — Euro' },
  { value: 'GBP', label: 'GBP — British Pound' },
  { value: 'LKR', label: 'LKR — Sri Lankan Rupee' },
  { value: 'INR', label: 'INR — Indian Rupee' },
  { value: 'AUD', label: 'AUD — Australian Dollar' },
];

export const TIME_ZONES: IOption[] = [
  { value: 'UTC', label: '(UTC) Coordinated Universal Time' },
  { value: 'America/New_York', label: '(UTC−05:00) Eastern Time' },
  { value: 'Europe/London', label: '(UTC+00:00) London' },
  { value: 'Asia/Colombo', label: '(UTC+05:30) Colombo' },
  { value: 'Asia/Kolkata', label: '(UTC+05:30) Kolkata' },
  { value: 'Asia/Singapore', label: '(UTC+08:00) Singapore' },
  { value: 'Australia/Sydney', label: '(UTC+10:00) Sydney' },
];

// ─── Pure helpers (unit-testable, FR-3 + previews) ───────────────────────

/** Normalise a hex string to lowercase #rrggbb, or null if invalid. */
export function normalizeHex(input: string): string | null {
  if (!input) {
    return null;
  }
  let hex = input.trim().toLowerCase();
  if (!hex.startsWith('#')) {
    hex = '#' + hex;
  }
  // Expand 3-digit shorthand (#abc → #aabbcc).
  if (/^#[0-9a-f]{3}$/.test(hex)) {
    hex = '#' + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3];
  }
  return /^#[0-9a-f]{6}$/.test(hex) ? hex : null;
}

function clamp(n: number): number {
  return Math.max(0, Math.min(255, Math.round(n)));
}

function toHexPart(n: number): string {
  return clamp(n).toString(16).padStart(2, '0');
}

/** Relative luminance (WCAG-style) of an #rrggbb colour, 0–1. */
function luminance(hex: string): number {
  const r = parseInt(hex.slice(1, 3), 16) / 255;
  const g = parseInt(hex.slice(3, 5), 16) / 255;
  const b = parseInt(hex.slice(5, 7), 16) / 255;
  const lin = (c: number) =>
    c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
}

/** Mix a colour toward black (factor>0) or white (factor<0) by `amount` 0–1. */
function shift(hex: string, amount: number, toward: 'black' | 'white'): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  const target = toward === 'black' ? 0 : 255;
  return (
    '#' +
    toHexPart(r + (target - r) * amount) +
    toHexPart(g + (target - g) * amount) +
    toHexPart(b + (target - b) * amount)
  );
}

/**
 * FR-3: derive complementary shades from a primary colour. Pure + deterministic
 * so it is unit-testable and the live preview is instant. Falls back to the
 * brand default if the input is not a valid hex.
 */
export function deriveColorTheme(primaryInput: string): IColorTheme {
  const primary = normalizeHex(primaryInput) ?? '#4f46e5';
  return {
    primary,
    dark: shift(primary, 0.25, 'black'),
    light: shift(primary, 0.85, 'white'),
    // Pick black/white text for adequate contrast on the primary colour.
    contrastText: luminance(primary) > 0.45 ? '#111827' : '#ffffff',
  };
}

/** Build a sample date string for a given format token (preview only, AC-3). */
export function formatDatePreview(token: string, sample = new Date(2026, 4, 11)): string {
  const yyyy = String(sample.getFullYear());
  const MM = String(sample.getMonth() + 1).padStart(2, '0');
  const dd = String(sample.getDate()).padStart(2, '0');
  const monthShort = [
    'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
    'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
  ][sample.getMonth()];

  switch (token) {
    case 'dd MMM yyyy':
      return `${dd} ${monthShort} ${yyyy}`;
    case 'MM/dd/yyyy':
      return `${MM}/${dd}/${yyyy}`;
    case 'dd/MM/yyyy':
      return `${dd}/${MM}/${yyyy}`;
    case 'yyyy-MM-dd':
      return `${yyyy}-${MM}-${dd}`;
    default:
      return `${dd} ${monthShort} ${yyyy}`;
  }
}
