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
import type { Schema } from '@core/api';

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

// ─── Wire contract → view-model mappers (D1 admin slice) ─────────────────────
//
// THIS MIGRATION FOUND A LIVE DEFECT. `ITenantSummary` declares `tenantId`; the API sends **`id`**
// (`TenantListItemDto.Id`). `http.get<ITenantSummary[]>(…)` asserted the shape rather than checking it, so
// `tenantId` was `undefined` on every row — and the tenant list uses it as the `@for` TRACK KEY
// (`track t.tenantId`). Every row therefore tracked by the same undefined value.
//
// A track key is exactly the wrong field to leave undefined: Angular uses it to decide which DOM node
// belongs to which row, so the failure is not a blank cell but wrong or duplicated rows on re-render.

export type ProvisionTenantWire = Schema<'TenantsProvisionTenantResultDto'>;
export type TenantListItemWire = Schema<'TenantsTenantListItemDto'>;
export type SubdomainAvailabilityWire = Schema<'TenantsSubdomainAvailabilityDto'>;
export type SubscriptionPlanWire = Schema<'TenantsSubscriptionPlanDto'>;

export function mapProvisionTenant(w: ProvisionTenantWire): IProvisionTenantResponse {
  return {
    tenantId: w.tenantId ?? '',
    subdomain: w.subdomain ?? '',
    status: w.status ?? '',
    createdAt: w.createdAt ?? '',
  };
}

/** `id` → `tenantId`: the rename that was silently failing. */
export function mapTenantSummary(w: TenantListItemWire): ITenantSummary {
  return {
    tenantId: w.id ?? '',
    name: w.name ?? '',
    subdomain: w.subdomain ?? '',
    status: w.status ?? '',
    plan: w.plan ?? '',
    createdAt: w.createdAt ?? '',
  };
}

export function mapSubdomainAvailability(w: SubdomainAvailabilityWire): ISubdomainAvailability {
  return {
    // Fail CLOSED: an absent flag must not read as "this subdomain is free".
    available: w.available ?? false,
    reason: w.reason ?? undefined,
  };
}

export function mapSubscriptionPlan(w: SubscriptionPlanWire): ISubscriptionPlan {
  return {
    id: w.id ?? '',
    name: w.name ?? '',
    code: w.code ?? '',
    priceMonthly: w.priceMonthly ?? 0,
    trialDays: w.trialDays ?? 0,
    maxEmployees: w.maxEmployees ?? null,
  };
}
