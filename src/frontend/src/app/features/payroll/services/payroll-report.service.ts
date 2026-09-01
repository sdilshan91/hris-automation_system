import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable, forkJoin } from 'rxjs';
import { map } from 'rxjs/operators';

import { environment } from '../../../../environments/environment';
import {
  BankAdvicePreviewWire,
  IBankAdvicePreview,
  IDashboardAnalytics,
  IPayrollAnalyticsResult,
  IReportFilters,
  IReportResult,
  IReportTypeMeta,
  PayrollAnalyticsResultWire,
  PayrollChartType,
  PayrollReportType,
  ReportExportFormat,
  ReportResultWire,
  ReportTypeMetaWire,
  mapBankAdvicePreview,
  mapPayrollAnalyticsResult,
  mapReportResult,
  mapReportTypeMeta,
  splitPeriod,
} from '../models/payroll-report.models';

/**
 * US-PAY-009: Payroll Reports & Analytics service.
 *
 * Sibling to PayrollRunService / PayslipService; the report route strings live here
 * ONLY, so a backend contract change is a one-file fix. The REST surface (base
 * `${apiBaseUrl}/payroll`, where apiBaseUrl already ends in `/api/v1`; all endpoints
 * gate on `Payroll.Export`):
 *  - GET /payroll/reports                                            → descriptor[]
 *  - GET /payroll/reports/{reportType}?payMonth=&payYear=&departmentId=  → JSON result
 *  - GET /payroll/reports/{reportType}/export?format=&payMonth=&…    → blob (FR-2)
 *  - GET /payroll/analytics/{chartType}?payMonth=&payYear=&departmentId= → chart result
 *  - GET /payroll/reports/bank-advice/preview?payMonth=&payYear=&…   → masked preview
 *  - GET /payroll/reports/BankAdvice/export?format=&payMonth=&…      → full file (BR-2)
 *
 * Query params are SEPARATE: `payMonth` (int 1-12), `payYear` (int), and optional
 * `departmentId` (guid). The UI carries a `period` (YYYY-MM) string which this service
 * splits via {@link splitPeriod}; an unparseable period omits both params and the
 * backend defaults to the most-recent finalized run.
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so the JSON methods consume BARE payloads. Binary exports use
 * `responseType: 'blob'` + `observe: 'response'` — the envelope interceptor passes
 * blobs through untouched, and the caller reads the `Content-Disposition` filename.
 */
@Injectable({ providedIn: 'root' })
export class PayrollReportService {
  private readonly http = inject(HttpClient);
  private readonly payrollUrl = `${environment.apiBaseUrl}/payroll`;
  private readonly reportsUrl = `${this.payrollUrl}/reports`;
  private readonly analyticsUrl = `${this.payrollUrl}/analytics`;

  /** The available report types for the Notion-style sidebar (§8, FR-1). */
  listReportTypes(): Observable<IReportTypeMeta[]> {
    return this.http
      .get<ReportTypeMetaWire[] | { data: ReportTypeMetaWire[] }>(this.reportsUrl, {
        withCredentials: true,
      })
      .pipe(map((res) => this.toArray(res).map(mapReportTypeMeta)));
  }

  /**
   * Generate (fetch) a filtered report as JSON for the preview table + chart (AC-1).
   * Period is split into `payMonth` + `payYear`; `departmentId` is passed when set.
   */
  getReport(
    reportType: PayrollReportType,
    filters: IReportFilters,
  ): Observable<IReportResult> {
    return this.http
      .get<ReportResultWire>(`${this.reportsUrl}/${reportType}`, {
        params: this.filterParams(filters),
        withCredentials: true,
      })
      .pipe(map(mapReportResult));
  }

  /**
   * Export a report to a downloadable blob in the requested format (FR-2, AC-4).
   * Returns the full response so the caller reads the `Content-Disposition`
   * filename. Bypasses the JSON envelope (responseType: 'blob').
   */
  exportReport(
    reportType: PayrollReportType,
    filters: IReportFilters,
    format: ReportExportFormat,
  ): Observable<HttpResponse<Blob>> {
    const params = this.filterParams(filters).set('format', format);
    return this.http.get(`${this.reportsUrl}/${reportType}/export`, {
      params,
      withCredentials: true,
      responseType: 'blob',
      observe: 'response',
    });
  }

  /** One analytics chart payload (`/payroll/analytics/{chartType}`, FR-5). */
  getAnalytics(
    chartType: PayrollChartType,
    filters: IReportFilters,
  ): Observable<IPayrollAnalyticsResult> {
    return this.http
      .get<PayrollAnalyticsResultWire>(`${this.analyticsUrl}/${chartType}`, {
        params: this.filterParams(filters),
        withCredentials: true,
      })
      .pipe(map(mapPayrollAnalyticsResult));
  }

  /**
   * The analytics dashboard view model — there is NO single dashboard endpoint, so
   * fan out the three chart calls and assemble them (FR-5). The trend/department/
   * statutory results are exposed under the combined {@link IDashboardAnalytics}.
   */
  getDashboardAnalytics(filters: IReportFilters): Observable<IDashboardAnalytics> {
    return forkJoin({
      trend: this.getAnalytics('MonthlyTrend', filters),
      departmentCosts: this.getAnalytics('DepartmentCostDistribution', filters),
      statutory: this.getAnalytics('StatutoryBreakdown', filters),
    });
  }

  /**
   * Bank-advice preview with MASKED account numbers (BR-2, AC-2). The backend masks
   * to the last 4 digits; full numbers are only in the downloaded file.
   */
  getBankAdvicePreview(filters: IReportFilters): Observable<IBankAdvicePreview> {
    return this.http
      .get<BankAdvicePreviewWire>(`${this.reportsUrl}/bank-advice/preview`, {
        params: this.filterParams(filters),
        withCredentials: true,
      })
      .pipe(map(mapBankAdvicePreview));
  }

  /**
   * Download the FULL bank-advice file (un-masked account numbers, BR-2/AC-2). This
   * is the standard report export for the `BankAdvice` report type. Returns the full
   * response for the `Content-Disposition` filename. CSV by default.
   */
  downloadBankAdvice(
    filters: IReportFilters,
    format: ReportExportFormat = 'csv',
  ): Observable<HttpResponse<Blob>> {
    return this.exportReport('BankAdvice', filters, format);
  }

  /**
   * US-RPT-003 (AC-4 / FR-6): the bank-advice preview with FULL (un-masked) account
   * numbers for the on-screen "Reveal" toggle. This is the audited sensitive path
   * (NFR-3) — the backend records a `PayrollReport.ViewSensitive` audit entry and only
   * authorises callers with the `Payroll.ViewSensitive` permission, so the UI gates
   * the toggle behind the same permission before ever calling it.
   *
   * Coded against `GET /payroll/reports/bank-advice/full` returning the SAME
   * {@link IBankAdvicePreview} shape as the masked preview but with `accountNumber`
   * un-masked.
   */
  getBankAdviceFull(filters: IReportFilters): Observable<IBankAdvicePreview> {
    return this.http
      .get<BankAdvicePreviewWire>(`${this.reportsUrl}/bank-advice/full`, {
        params: this.filterParams(filters),
        withCredentials: true,
      })
      .pipe(map(mapBankAdvicePreview));
  }

  /**
   * Build the shared query params: split `period` (YYYY-MM) into `payMonth` +
   * `payYear`, and add `departmentId` when a department is selected. An unparseable
   * period omits both period params (the BE then defaults to the latest finalized run).
   */
  private filterParams(filters: IReportFilters): HttpParams {
    let params = new HttpParams();
    const split = splitPeriod(filters.period);
    if (split) {
      params = params
        .set('payMonth', `${split.payMonth}`)
        .set('payYear', `${split.payYear}`);
    }
    if (filters.departmentId) {
      params = params.set('departmentId', filters.departmentId);
    }
    // US-RPT-003 FR-4: select a specific finalized run (e.g. a supplementary run).
    if (filters.payrollRunId) {
      params = params.set('payrollRunId', filters.payrollRunId);
    }
    return params;
  }

  /** Accept either a bare array or a `{ data }` page; default to []. */
  private toArray<T>(res: T[] | { data: T[] } | null | undefined): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { data: T[] }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
  }
}
