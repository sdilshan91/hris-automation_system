import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  ITeamCalendarResponse,
  ITeamCalendarEntry,
  ITeamCalendarFilters,
  ITeamCalendarErrorResponse,
} from '../models/team-calendar.models';

/**
 * US-LV-009: Service for the Team Leave Calendar (read-only).
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 * The data is scoped server-side to the caller's team/department and access
 * level (NFR-2/NFR-3): managers receive Approved + Pending for direct reports
 * with leave-type detail; employees receive only Approved department leaves
 * with the leave-type/status detail SUPPRESSED. The frontend renders whatever
 * the API returns and never requests hidden fields.
 *
 * `environment.apiBaseUrl` already includes `/api/v1`, so the resource base is
 * `${apiBaseUrl}/leaves/team-calendar`.
 *
 * Backend endpoint (assumed contract — backend agent building in parallel):
 *   GET /api/v1/leaves/team-calendar?from={date}&to={date}
 *       [&employeeId][&leaveTypeId][&status]
 *   → ITeamCalendarResponse { entries: [...], holidays: [...] }
 *
 * Tolerant decoding: the service also accepts a bare ITeamCalendarEntry[] body
 * (older/leaner backend shape) and normalizes it to the combined shape.
 */
@Injectable({ providedIn: 'root' })
export class TeamCalendarService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/leaves/team-calendar`;

  /**
   * Fetch the team leave calendar for a date range (FR-1).
   * Optional filters (employee, leave type, status) are passed as query params
   * (FR-6); status is only meaningful for the manager scope.
   */
  getTeamCalendar(
    from: string,
    to: string,
    filters?: ITeamCalendarFilters
  ): Observable<ITeamCalendarResponse> {
    let params = new HttpParams().set('from', from).set('to', to);
    if (filters?.employeeId) {
      params = params.set('employeeId', filters.employeeId);
    }
    if (filters?.leaveTypeId) {
      params = params.set('leaveTypeId', filters.leaveTypeId);
    }
    if (filters?.status) {
      params = params.set('status', filters.status);
    }

    return this.http
      .get<ITeamCalendarResponse | ITeamCalendarEntry[]>(this.baseUrl, {
        params,
        withCredentials: true,
      })
      .pipe(map((body) => TeamCalendarService.normalize(body)));
  }

  /**
   * Normalize the backend body into the combined { entries, holidays } shape.
   * Accepts either the combined object or a bare entries array.
   */
  static normalize(
    body: ITeamCalendarResponse | ITeamCalendarEntry[] | null | undefined
  ): ITeamCalendarResponse {
    if (Array.isArray(body)) {
      return { entries: body, holidays: [] };
    }
    if (body && typeof body === 'object') {
      return {
        entries: Array.isArray(body.entries) ? body.entries : [],
        holidays: Array.isArray(body.holidays) ? body.holidays : [],
      };
    }
    return { entries: [], holidays: [] };
  }

  /** Parse an error response into a typed team-calendar error. */
  static parseError(err: HttpErrorResponse): ITeamCalendarErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as ITeamCalendarErrorResponse;
    }
    return null;
  }
}
