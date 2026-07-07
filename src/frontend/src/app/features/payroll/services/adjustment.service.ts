import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IAdjustment,
  IAdjustmentFilters,
  IAdjustmentRequest,
  IBulkAdjustmentResult,
} from '../models/adjustment.models';

/**
 * US-PAY-007: Service for payroll adjustments — list/filter, create, cancel
 * (FR-6), bulk CSV upload (FR-2; commits immediately, no server dry-run),
 * supporting-document upload/download (AC-3), and the CLIENT-SIDE bulk CSV template.
 *
 * Sibling to PayrollRunService / PayslipService; the adjustment route strings live
 * here ONLY, so a backend contract change is a one-file fix (the BE had not pinned
 * routes in the vault — see adjustment.models.ts). All requests use withCredentials
 * (httpOnly cookie auth) and are tenant-scoped via the tenantInterceptor
 * (X-Tenant-Subdomain header). The backend stamps tenant_id and enforces RLS
 * (FR-8, AC-5).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the JSON methods consume BARE payloads (and `listAdjustments`
 * reads `items` from the `PayrollAdjustmentPageDto` page, tolerating a bare array or a
 * legacy `{ data }` page too). Enums arrive as PascalCase
 * STRINGS (US-PLT-003) — see adjustment.models.ts.
 *
 * Binary methods use `responseType: 'blob'` + `observe: 'response'` so the caller
 * can read the `Content-Disposition` filename and anchor-click a download; the JSON
 * envelope does not apply to them.
 */
@Injectable({ providedIn: 'root' })
export class AdjustmentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/payroll/adjustments`;

  /**
   * List adjustments, optionally filtered by Status, Type, Period (`YYYY-MM`), and
   * Employee (§8 table). Empty/null filters are omitted so the backend returns
   * everything. Reads `items` from the backend `PayrollAdjustmentPageDto` envelope,
   * and also tolerates a bare array or a legacy `{ data }` page.
   */
  listAdjustments(filters: IAdjustmentFilters = {}): Observable<IAdjustment[]> {
    let params = new HttpParams();
    if (filters.status) {
      params = params.set('status', filters.status);
    }
    if (filters.type) {
      params = params.set('type', filters.type);
    }
    if (filters.period) {
      params = params.set('period', filters.period);
    }
    if (filters.employeeId) {
      params = params.set('employeeId', filters.employeeId);
    }
    return this.http
      .get<
        | IAdjustment[]
        | { items: IAdjustment[] }
        | { data: IAdjustment[] }
      >(this.baseUrl, {
        params,
        withCredentials: true,
      })
      .pipe(map((res) => this.toArray(res)));
  }

  /** Create a single adjustment (FR-1). The supporting document, if any, is uploaded after via `uploadDocument`. */
  createAdjustment(request: IAdjustmentRequest): Observable<IAdjustment> {
    return this.http.post<IAdjustment>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /** Cancel a Pending adjustment (FR-6). The backend rejects Applied/Cancelled records. */
  cancelAdjustment(id: string): Observable<IAdjustment> {
    return this.http.post<IAdjustment>(
      `${this.baseUrl}/${id}/cancel`,
      {},
      { withCredentials: true },
    );
  }

  /**
   * Upload a bulk-adjustment CSV for the given period (FR-2). The backend COMMITS
   * immediately — there is no preview/dry-run mode — and returns the per-row
   * outcome (`BulkAdjustmentResultDto`). The period is carried by the `payMonth` /
   * `payYear` form fields (one period per upload), NOT by the CSV. The FE parses the
   * CSV client-side for the preview table before calling this.
   */
  bulkUpload(
    file: File,
    payMonth: number,
    payYear: number,
  ): Observable<IBulkAdjustmentResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('payMonth', String(payMonth));
    form.append('payYear', String(payYear));
    return this.http.post<IBulkAdjustmentResult>(`${this.baseUrl}/bulk`, form, {
      withCredentials: true,
    });
  }

  /**
   * Upload a supporting document for an adjustment (AC-3, NFR-5). The file is
   * validated client-side (PDF/JPG/PNG, <=5MB) before this call; the backend
   * re-validates and stores it at `{tenantId}/payroll/adjustments/{id}/`.
   */
  uploadDocument(id: string, file: File): Observable<IAdjustment> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<IAdjustment>(
      `${this.baseUrl}/${id}/document`,
      form,
      { withCredentials: true },
    );
  }

  /**
   * Download an adjustment's supporting document as a blob (AC-3). Returns the full
   * response so the caller can read the `Content-Disposition` filename and
   * anchor-click the download; bypasses the JSON envelope.
   */
  downloadDocument(id: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.baseUrl}/${id}/document`, {
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }

  /**
   * The bulk-adjustment CSV template, generated CLIENT-SIDE (there is no backend
   * `/template` endpoint). The header row matches the columns the backend parser
   * expects; the period is supplied per-upload, so it is NOT a CSV column. The
   * component builds the Blob and triggers the download.
   */
  bulkTemplateCsv(): string {
    return [
      'employee_no,adjustment_type,amount,description,is_taxable',
      'EMP001,Bonus,10000,Q2 performance bonus,true',
    ].join('\n');
  }

  /**
   * Accept the backend `PayrollAdjustmentPageDto` envelope (`{ items, totalCount, … }`,
   * after the global ApiResponse unwrap), or a bare array / legacy `{ data }` page;
   * default to []. `items` is the real shape the List endpoint returns.
   */
  private toArray<T>(
    res: T[] | { items: T[] } | { data: T[] } | null | undefined,
  ): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { items: T[] }).items)) {
      return (res as { items: T[] }).items;
    }
    if (res && Array.isArray((res as { data: T[] }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
  }
}
