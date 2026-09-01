import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IStartImpersonationRequest,
  IStartImpersonationResponse,
  IEndImpersonationResponse,
  IImpersonationTarget,
  StartImpersonationWire,
  EndImpersonationWire,
  ImpersonationTargetWire,
  mapStartImpersonation,
  mapEndImpersonation,
  mapImpersonationTarget,
} from '../models/impersonation.models';

/**
 * US-ADM-003: System Admin impersonation service.
 *
 * Codes to the backend contract rooted at `/api/v1/system/impersonation`. Unlike
 * the US-ADM-002 monitoring service (which strips `/v1` for the `/api/admin`
 * namespace), impersonation lives UNDER the `/v1/system` root — so we use
 * `environment.apiBaseUrl` (`…/api/v1`) verbatim and append `/system/impersonation`.
 *
 * Endpoints:
 *   POST /system/impersonation                  start a session (AC-1)
 *   POST /system/impersonation/{id}/end         end a session (AC-3)
 *   GET  /system/impersonation/targets?tenantId active members, system excluded
 *
 * Envelope: the global apiEnvelopeInterceptor strips `ApiResponse<T>`, so these
 * methods consume BARE payloads. All requests use withCredentials (httpOnly
 * cookie auth), matching the sibling admin services.
 */
@Injectable({ providedIn: 'root' })
export class ImpersonationService {
  private readonly http = inject(HttpClient);

  /** `/api/v1/system/impersonation` — impersonation is rooted at the `/v1/system` namespace. */
  private readonly baseUrl = `${environment.apiBaseUrl}/system/impersonation`;

  /** AC-1/FR-1: start an impersonation session for a target user in a tenant. */
  start(
    request: IStartImpersonationRequest,
  ): Observable<IStartImpersonationResponse> {
    return this.http
      .post<StartImpersonationWire>(this.baseUrl, request, { withCredentials: true })
      .pipe(map(mapStartImpersonation));
  }

  /** AC-3: end the active impersonation session. */
  end(sessionId: string): Observable<IEndImpersonationResponse> {
    return this.http
      .post<EndImpersonationWire>(`${this.baseUrl}/${sessionId}/end`, null, {
        withCredentials: true,
      })
      .pipe(map(mapEndImpersonation));
  }

  /** Active, impersonatable members of a tenant (BR-2: system users excluded). */
  getTargets(tenantId: string): Observable<IImpersonationTarget[]> {
    const params = new HttpParams().set('tenantId', tenantId);
    return this.http
      .get<ImpersonationTargetWire[]>(`${this.baseUrl}/targets`, {
        params,
        withCredentials: true,
      })
      .pipe(map((rows) => (rows ?? []).map(mapImpersonationTarget)));
  }
}
