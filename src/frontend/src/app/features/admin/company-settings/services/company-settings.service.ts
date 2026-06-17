import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  ICompanySettings,
  IOrgProfile,
  ILocalization,
  IPasswordPolicy,
  ISessionPolicy,
  IBrandingUploadResult,
  BrandingSlot,
} from '../models/company-settings.models';

/**
 * US-ADM-006: Tenant Admin company-settings service.
 *
 * TENANT-scoped endpoints under `/api/v1/tenant/settings...` (NOT the
 * system-admin console). Base derived from `environment.apiBaseUrl` verbatim
 * (which already ends in `/v1`) + `/tenant/settings`, matching how US-ADM-005
 * built its `/api/v1/users` base. Tenant isolation is enforced server-side via
 * ITenantContext + EF global filters (AC-5); here we just carry the auth cookie
 * + the X-Tenant-Subdomain header (added by the tenantInterceptor) on every call.
 *
 * Backend contract (finalized in parallel — keep models in one file):
 *   GET  /api/v1/tenant/settings                     — full aggregate
 *   PUT  /api/v1/tenant/settings/org-profile         — IOrgProfile
 *   PUT  /api/v1/tenant/settings/localization        — ILocalization
 *   PUT  /api/v1/tenant/settings/password-policy     — IPasswordPolicy
 *   PUT  /api/v1/tenant/settings/session-policy      — ISessionPolicy
 *   PUT  /api/v1/tenant/settings/primary-color       — { primaryColor }
 *   POST /api/v1/tenant/settings/branding/upload     — multipart { file, slot }
 */
@Injectable({ providedIn: 'root' })
export class CompanySettingsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/settings`;

  /** Full settings aggregate for the current tenant (AC-1..AC-4). */
  getSettings(): Observable<ICompanySettings> {
    return this.http.get<ICompanySettings>(this.baseUrl, {
      withCredentials: true,
    });
  }

  /** AC-1 / FR-1: persist the organization profile sub-object. */
  updateOrgProfile(org: IOrgProfile): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/org-profile`, org, {
      withCredentials: true,
    });
  }

  /** AC-3 / FR-4: persist the localization sub-object. */
  updateLocalization(localization: ILocalization): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/localization`, localization, {
      withCredentials: true,
    });
  }

  /** AC-4 / FR-5: persist the password policy sub-object. */
  updatePasswordPolicy(policy: IPasswordPolicy): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/password-policy`, policy, {
      withCredentials: true,
    });
  }

  /** FR-6: persist the session policy sub-object. */
  updateSessionPolicy(policy: ISessionPolicy): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/session-policy`, policy, {
      withCredentials: true,
    });
  }

  /** FR-3: persist only the primary brand colour (hex). */
  updatePrimaryColor(primaryColor: string): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl}/primary-color`,
      { primaryColor },
      { withCredentials: true }
    );
  }

  /**
   * AC-2 / FR-2: upload a branding file to a slot. Multipart so the backend can
   * validate magic bytes + size (NFR-2) and reject (400) with a clear message;
   * we surface the server error. Returns the stored tenant-scoped URL.
   */
  uploadBranding(
    file: File,
    slot: BrandingSlot
  ): Observable<IBrandingUploadResult> {
    const form = new FormData();
    form.append('file', file);
    form.append('slot', slot);
    return this.http.post<IBrandingUploadResult>(
      `${this.baseUrl}/branding/upload`,
      form,
      { withCredentials: true }
    );
  }
}
