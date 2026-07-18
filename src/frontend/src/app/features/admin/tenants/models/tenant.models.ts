/**
 * US-ADM-001: System Admin tenant provisioning models.
 *
 * These DTOs match the System Admin Console backend contract, which lives under
 * `/api/admin/...` (the platform/system context), NOT the tenant-scoped
 * `/api/v1/...` namespace used by the rest of the app.
 */

/** Reserved subdomains (AC-2) — kept client-side for instant feedback before
 * the debounced availability call confirms against the server. The backend is
 * the source of truth; this list is a UX nicety only. */
export const RESERVED_SUBDOMAINS: readonly string[] = [
  'www', 'api', 'admin', 'app', 'mail', 'status', 'docs', 'help', 'support',
  'static', 'cdn', 'dev', 'stage', 'prod', 'test', 'qa',
];

/** Subdomain rule (FR-1, AC-5): lowercase alphanumeric + hyphens, 3-50 chars,
 * cannot start/end with a hyphen. */
export const SUBDOMAIN_PATTERN = /^[a-z0-9](?:[a-z0-9-]{1,48}[a-z0-9])$/;

/** Static region list (Phase-1: captured but single-region — see §10). */
export interface IRegionOption {
  code: string;
  label: string;
}

export const REGION_OPTIONS: readonly IRegionOption[] = [
  { code: 'us-east', label: 'US East' },
  { code: 'us-west', label: 'US West' },
  { code: 'eu-west', label: 'EU West' },
  { code: 'eu-central', label: 'EU Central' },
  { code: 'ap-south', label: 'Asia Pacific (South)' },
  { code: 'ap-southeast', label: 'Asia Pacific (Southeast)' },
];

/** An active subscription plan for the card-based picker (GET /subscription-plans). */
export interface ISubscriptionPlan {
  id: string;
  name: string;
  code: string;
  priceMonthly: number;
  trialDays: number;
  /** Employee cap for the plan; null/0 = unlimited (e.g. Enterprise). */
  maxEmployees: number | null;
}

/** Request body for POST /api/admin/tenants. */
export interface IProvisionTenantRequest {
  name: string;
  subdomain: string;
  subscriptionPlanId: string;
  ownerEmail: string;
  region: string;
  trialDays?: number;
}

/** Success response from POST /api/admin/tenants. */
export interface IProvisionTenantResponse {
  tenantId: string;
  subdomain: string;
  status: string;
  createdAt: string;
}

/** Result of GET /api/admin/tenants/subdomain-available. */
export interface ISubdomainAvailability {
  available: boolean;
  reason?: string;
}

/** A row in the tenant list (GET /api/admin/tenants) (AC-4). */
export interface ITenantSummary {
  tenantId: string;
  name: string;
  subdomain: string;
  status: string;
  plan: string;
  createdAt: string;
}
