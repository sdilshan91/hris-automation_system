import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IMyPayslipDetail,
  IMyPayslipListItem,
  IMyPayslipPage,
} from '../models/my-payslip.models';

/**
 * US-PAY-005: Service for the EMPLOYEE self-service payslip views — list the
 * authenticated employee's payslips (paginated, optional ?year= filter), fetch one
 * payslip's full earnings/deductions breakdown, and download the pre-generated PDF.
 *
 * Sibling to PayslipService (HR-facing run payslips, US-PAY-004); the my-payslips
 * route strings live HERE ONLY, so a backend contract change is a one-file fix. All
 * requests use withCredentials (httpOnly cookie auth) and are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header). The backend enforces `Payroll.Read.Self`
 * + tenant RLS, so a cross-employee/cross-tenant payslip id is invisible ⇒ 403/404
 * (AC-4, BR-5, NFR-4). The FE never sends employee/tenant info.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the `{ data }`
 * wrapper, so the JSON methods consume BARE payloads. The PDF download uses
 * `responseType: 'blob'` + `observe: 'response'` so the caller can read the
 * `Content-Disposition` filename and anchor-click the download; the JSON envelope does
 * not apply to it.
 */
@Injectable({ providedIn: 'root' })
export class MyPayslipService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/payroll/my-payslips`;

  /**
   * The authenticated employee's payslips from Finalized runs (FR-2/BR-1), most-recent
   * first (the backend sorts; the component re-sorts defensively). Paginated (§7);
   * pass `year` to filter (FR-6) and `page` to page through history. Tolerates either
   * the paginated shape or a bare array (normalized to a page) so a backend shape
   * choice doesn't break the list.
   */
  listMyPayslips(
    year?: number | null,
    page = 1,
    pageSize = 12,
  ): Observable<IMyPayslipPage> {
    let params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    if (year != null) {
      params = params.set('year', String(year));
    }
    return this.http
      .get<IMyPayslipPage | IMyPayslipListItem[]>(this.baseUrl, {
        params,
        withCredentials: true,
      })
      .pipe(map((res) => this.toPage(res, page, pageSize)));
  }

  /**
   * Full earnings/deductions breakdown for one of the employee's own payslips (FR-3).
   * A foreign payslip id is invisible to the tenant/employee filter ⇒ 403/404 (AC-4).
   */
  getMyPayslip(payslipId: string): Observable<IMyPayslipDetail> {
    return this.http.get<IMyPayslipDetail>(`${this.baseUrl}/${payslipId}`, {
      withCredentials: true,
    });
  }

  /**
   * Download the employee's pre-generated payslip PDF as a blob (FR-4, AC-3). Returns
   * the full response so the caller can read the `Content-Disposition` filename and
   * trigger an anchor-click download. On-demand generation is not supported in the
   * employee view (§10) — the PDF was rendered by US-PAY-004.
   */
  downloadMyPayslipPdf(payslipId: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.baseUrl}/${payslipId}/pdf`, {
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }

  /** Normalize a bare array or partial page into a full `IMyPayslipPage`. */
  private toPage(
    res: IMyPayslipPage | IMyPayslipListItem[] | null | undefined,
    page: number,
    pageSize: number,
  ): IMyPayslipPage {
    if (Array.isArray(res)) {
      return { items: res, totalCount: res.length, page, pageSize };
    }
    if (res && Array.isArray(res.items)) {
      return {
        items: res.items,
        totalCount: res.totalCount ?? res.items.length,
        page: res.page ?? page,
        pageSize: res.pageSize ?? pageSize,
      };
    }
    return { items: [], totalCount: 0, page, pageSize };
  }
}
