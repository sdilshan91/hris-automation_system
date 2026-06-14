import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IHoliday,
  ICreateHolidayRequest,
  IUpdateHolidayRequest,
  IHolidayImportResult,
  IHolidayErrorResponse,
} from '../models/holiday.models';

/**
 * US-LV-007: Service for holiday calendar CRUD + CSV import.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 * Holidays are tenant-isolated server-side (NFR-2).
 *
 * `environment.apiBaseUrl` already includes `/api/v1`, so the resource base
 * is `${apiBaseUrl}/holidays`.
 *
 * Backend endpoints (assumed contract — backend agent building in parallel):
 *   GET    /api/v1/holidays?year={year}              - list holidays for a year
 *   GET    /api/v1/holidays?from={date}&to={date}    - list within a date range (FR-6)
 *   POST   /api/v1/holidays                          - create (AC-1)
 *   PUT    /api/v1/holidays/{id}                      - update
 *   POST   /api/v1/holidays/{id}/deactivate          - soft-deactivate (BR-4)
 *   POST   /api/v1/holidays/{id}/reactivate           - reactivate
 *   POST   /api/v1/holidays/import                    - bulk CSV import (AC-3, FR-4)
 */
@Injectable({ providedIn: 'root' })
export class HolidayService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/holidays`;

  // --- Read --------------------------------------------------

  /**
   * Get all holidays for a given calendar year (AC-4).
   * Optionally filter to a single location server-side.
   */
  getHolidaysForYear(year: number, locationId?: string | null): Observable<IHoliday[]> {
    let params = new HttpParams().set('year', year.toString());
    if (locationId) {
      params = params.set('locationId', locationId);
    }
    return this.http.get<IHoliday[]>(this.baseUrl, {
      params,
      withCredentials: true,
    });
  }

  /** Get holidays within a date range (FR-6 — used by leave-day calculation). */
  getHolidaysInRange(from: string, to: string): Observable<IHoliday[]> {
    const params = new HttpParams().set('from', from).set('to', to);
    return this.http.get<IHoliday[]>(this.baseUrl, {
      params,
      withCredentials: true,
    });
  }

  // --- Write -------------------------------------------------

  /** Create a new holiday (AC-1, FR-1, FR-2). */
  createHoliday(request: ICreateHolidayRequest): Observable<IHoliday> {
    return this.http.post<IHoliday>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /** Update an existing holiday (FR-1). */
  updateHoliday(id: string, request: IUpdateHolidayRequest): Observable<IHoliday> {
    return this.http.put<IHoliday>(`${this.baseUrl}/${id}`, request, {
      withCredentials: true,
    });
  }

  /** Deactivate a holiday (BR-4 — deletion blocked in finalized payroll periods). */
  deactivateHoliday(id: string): Observable<IHoliday> {
    return this.http.post<IHoliday>(
      `${this.baseUrl}/${id}/deactivate`,
      {},
      { withCredentials: true }
    );
  }

  /** Reactivate a previously deactivated holiday. */
  reactivateHoliday(id: string): Observable<IHoliday> {
    return this.http.post<IHoliday>(
      `${this.baseUrl}/${id}/reactivate`,
      {},
      { withCredentials: true }
    );
  }

  /**
   * Bulk-import holidays from a CSV file (AC-3, FR-4).
   * Sent as multipart/form-data under the `file` field.
   */
  importHolidays(file: File): Observable<IHolidayImportResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<IHolidayImportResult>(
      `${this.baseUrl}/import`,
      formData,
      { withCredentials: true }
    );
  }

  /** Parse an error response into a typed holiday error. */
  static parseError(err: HttpErrorResponse): IHolidayErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IHolidayErrorResponse;
    }
    return null;
  }
}
