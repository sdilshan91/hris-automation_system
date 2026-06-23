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
 * Codes to the System Admin backend contract (AdminTenantsController), rooted at
 * `/api/v1/system/tenants` — the `/v1/system` namespace (same root as plans,
 * lifecycle, impersonation, data-export). We append to `environment.apiBaseUrl`
 * (`…/api/v1`) verbatim.
 *
 * Endpoints (must match AdminTenantsController exactly):
 *   POST /api/v1/system/tenants                              provision a tenant
 *   GET  /api/v1/system/tenants                              list tenants (AC-4)
 *   GET  /api/v1/system/tenants/subdomain-availability?...   debounced availability (AC-2)
 *   GET  /api/v1/system/tenants/plans                        active plans for the picker
 *
 * Envelope: the global apiEnvelopeInterceptor (US-PLT-001) strips the
 * `ApiResponse<T>` wrapper, so these methods consume BARE payloads — matching
 * the sibling feature services. All requests use withCredentials (httpOnly
 * cookie auth).
 */
@Injectable({ providedIn: 'root' })
export class TenantProvisioningService {
  private readonly http = inject(HttpClient);

  /** `/api/v1/system/tenants` — the system-admin tenant namespace. */
  private readonly tenantsUrl = `${environment.apiBaseUrl}/system/tenants`;

  /** AC-1/FR-1: provision a new tenant. */
  provisionTenant(
    request: IProvisionTenantRequest,
  ): Observable<IProvisionTenantResponse> {
    return this.http.post<IProvisionTenantResponse>(
      this.tenantsUrl,
      request,
      { withCredentials: true },
    );
  }

  /** AC-4: list all tenants for the System Admin tenant list. */
  getTenants(): Observable<ITenantSummary[]> {
    return this.http.get<ITenantSummary[]>(this.tenantsUrl, {
      withCredentials: true,
    });
  }

  /** AC-2/FR-2: debounced subdomain availability check (taken/reserved/invalid). */
  checkSubdomainAvailability(
    subdomain: string,
  ): Observable<ISubdomainAvailability> {
    const params = new HttpParams().set('subdomain', subdomain);
    return this.http.get<ISubdomainAvailability>(
      `${this.tenantsUrl}/subdomain-availability`,
      { params, withCredentials: true },
    );
  }

  /** Active subscription plans for the card-based picker. */
  getSubscriptionPlans(): Observable<ISubscriptionPlan[]> {
    return this.http.get<ISubscriptionPlan[]>(
      `${this.tenantsUrl}/plans`,
      { withCredentials: true },
    );
  }
}
