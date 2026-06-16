import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IProvisionTenantRequest,
  IProvisionTenantResponse,
  ISubdomainAvailability,
  ISubscriptionPlan,
  ITenantSummary,
} from '../models/tenant.models';

/**
 * US-ADM-001: System Admin Console tenant provisioning service.
 *
 * Codes to the System Admin backend contract, which is rooted at `/api/admin`
 * (the platform/system context) — NOT the tenant-scoped `/api/v1` namespace the
 * rest of the app uses. We derive the `/api` root by stripping the trailing
 * `/v1` from `environment.apiBaseUrl` so a single env var still drives both.
 *
 * Endpoints:
 *   POST /api/admin/tenants                               provision a tenant
 *   GET  /api/admin/tenants                               list tenants (AC-4)
 *   GET  /api/admin/tenants/subdomain-available?subdomain debounced availability (AC-2)
 *   GET  /api/admin/subscription-plans                    active plans for the picker
 *
 * Envelope: the global apiEnvelopeInterceptor (US-PLT-001) strips the
 * `ApiResponse<T>` wrapper, so these methods consume BARE payloads — matching
 * the sibling feature services. All requests use withCredentials (httpOnly
 * cookie auth).
 */
@Injectable({ providedIn: 'root' })
export class TenantProvisioningService {
  private readonly http = inject(HttpClient);

  /** `/api` root (strip the tenant `/v1` suffix — admin console is system-scoped). */
  private readonly adminUrl = `${environment.apiBaseUrl.replace(/\/v1$/, '')}/admin`;

  /** AC-1/FR-1: provision a new tenant. */
  provisionTenant(
    request: IProvisionTenantRequest,
  ): Observable<IProvisionTenantResponse> {
    return this.http.post<IProvisionTenantResponse>(
      `${this.adminUrl}/tenants`,
      request,
      { withCredentials: true },
    );
  }

  /** AC-4: list all tenants for the System Admin tenant list. */
  getTenants(): Observable<ITenantSummary[]> {
    return this.http.get<ITenantSummary[]>(`${this.adminUrl}/tenants`, {
      withCredentials: true,
    });
  }

  /** AC-2/FR-2: debounced subdomain availability check (taken/reserved/invalid). */
  checkSubdomainAvailability(
    subdomain: string,
  ): Observable<ISubdomainAvailability> {
    const params = new HttpParams().set('subdomain', subdomain);
    return this.http.get<ISubdomainAvailability>(
      `${this.adminUrl}/tenants/subdomain-available`,
      { params, withCredentials: true },
    );
  }

  /** Active subscription plans for the card-based picker. */
  getSubscriptionPlans(): Observable<ISubscriptionPlan[]> {
    return this.http.get<ISubscriptionPlan[]>(
      `${this.adminUrl}/subscription-plans`,
      { withCredentials: true },
    );
  }
}
