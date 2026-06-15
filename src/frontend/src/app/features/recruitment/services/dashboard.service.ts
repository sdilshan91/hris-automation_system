import { Injectable, inject } from '@angular/core';
import {
  HttpClient,
  HttpErrorResponse,
  HttpParams,
  HttpResponse,
} from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';

import { environment } from '../../../../environments/environment';
import {
  IDashboardQuery,
  IRecruitmentDashboard,
  IDashboardFilterOption,
  IFunnelStage,
  ISourceEffectiveness,
  ITimeToHirePoint,
  IVacancyStatusCount,
  IActivityEvent,
  IDashboardKpis,
  funnelConversion,
  sourceConversion,
  DashboardExportFormat,
} from '../models/dashboard.models';

/**
 * US-REC-009: the recruiter/HR recruitment-dashboard service. Single place for the
 * dashboard contract + DTO↔FE mapping (FR-1..FR-9).
 *
 * All requests use `withCredentials` (httpOnly cookie auth) and are tenant-scoped via
 * the tenantInterceptor (X-Tenant-Subdomain) + backend RLS (AC-5/NFR-2). The backend
 * aggregates tenant data; the FE only maps + charts it.
 *
 * `apiBaseUrl` already includes `/api/v1`, so the base resource is
 * `${apiBaseUrl}/recruitment/dashboard`.
 *
 * Endpoints (reconciled with the backend agent):
 *   GET /recruitment/dashboard?from=&to=&departmentId=&vacancyId=
 *       -> IRecruitmentDashboard            (tolerates a `{ data }` envelope)
 *   GET /recruitment/dashboard/export?from=&to=&format=csv|xlsx|pdf&departmentId=&vacancyId=
 *       -> file download (blob + Content-Disposition)   (FR-8)
 *   GET /recruitment/dashboard/filters
 *       -> { departments: IDashboardFilterOption[], vacancies: IDashboardFilterOption[] }  (FR-7)
 */
@Injectable({ providedIn: 'root' })
export class RecruitmentDashboardService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/recruitment/dashboard`;

  /**
   * US-REC-009 (FR-1..FR-9, AC-1/AC-2): load the full dashboard for the date range +
   * optional department/vacancy drill-down. Normalizes the DTO so the components can
   * render without defensive checks: arrays default to [], funnel/source conversion
   * rates are derived when the backend omits them (BR-3).
   */
  getDashboard(query: IDashboardQuery): Observable<IRecruitmentDashboard> {
    return this.http
      .get<IRecruitmentDashboard | { data: IRecruitmentDashboard }>(this.baseUrl, {
        withCredentials: true,
        params: this.queryParams(query),
      })
      .pipe(map((res) => this.mapDashboard(this.unwrap(res), query)));
  }

  /**
   * US-REC-009 (FR-7): the available department + vacancy filter options for the
   * drill-down selects. Tolerates a bare `{ departments, vacancies }` or a `{ data }`
   * envelope; defaults each list to [].
   */
  getFilterOptions(): Observable<{
    departments: IDashboardFilterOption[];
    vacancies: IDashboardFilterOption[];
  }> {
    return this.http
      .get<
        | { departments?: IDashboardFilterOption[]; vacancies?: IDashboardFilterOption[] }
        | { data: { departments?: IDashboardFilterOption[]; vacancies?: IDashboardFilterOption[] } }
      >(`${this.baseUrl}/filters`, { withCredentials: true })
      .pipe(
        map((res) => {
          const body = this.unwrap(res);
          return {
            departments: body?.departments ?? [],
            vacancies: body?.vacancies ?? [],
          };
        }),
        // Filter options are optional (FR-7); the backend may not expose the endpoint
        // yet — fail soft to empty lists rather than breaking the dashboard.
        catchError(() => of({ departments: [], vacancies: [] })),
      );
  }

  /**
   * US-REC-009 (FR-8): export the dashboard data. xlsx (ClosedXML) + pdf (QuestPDF)
   * are server-generated and CSV is the Phase-1 button; the FE streams the blob and
   * reads the filename from Content-Disposition. Honors the active range + filters.
   *   GET /recruitment/dashboard/export?from=&to=&format=&departmentId=&vacancyId=
   */
  exportDashboard(
    query: IDashboardQuery,
    format: DashboardExportFormat,
  ): Observable<HttpResponse<Blob>> {
    const params = this.queryParams(query).set('format', format);
    return this.http.get(`${this.baseUrl}/export`, {
      withCredentials: true,
      params,
      responseType: 'blob',
      observe: 'response',
    });
  }

  // ─── Mapping (kept in ONE place) ───────────────────────────────

  /** Build the shared dashboard HttpParams (range + optional filters). */
  private queryParams(query: IDashboardQuery): HttpParams {
    let params = new HttpParams().set('from', query.from).set('to', query.to);
    if (query.departmentId) {
      params = params.set('departmentId', query.departmentId);
    }
    if (query.vacancyId) {
      params = params.set('vacancyId', query.vacancyId);
    }
    return params;
  }

  /** Unwrap a `{ data: T }` envelope if present, else pass the body through. */
  private unwrap<T>(res: T | { data: T }): T {
    if (res && typeof res === 'object' && 'data' in (res as { data?: T })) {
      return (res as { data: T }).data;
    }
    return res as T;
  }

  /**
   * Normalize the dashboard DTO: default arrays, derive missing funnel/source
   * conversion rates (BR-3), and echo the applied range when the backend omits it.
   */
  private mapDashboard(
    raw: unknown,
    query: IDashboardQuery,
  ): IRecruitmentDashboard {
    // The backend RecruitmentDashboardDto uses slightly different field names than the FE
    // model; translate BE -> FE here (the one reconciliation point). Tolerates both names.
    const dto = (raw ?? {}) as Record<string, unknown>;
    const beKpis = (dto['kpis'] ?? {}) as Record<string, unknown>;
    const num = (v: unknown): number => (typeof v === 'number' ? v : 0);

    const kpis: IDashboardKpis = {
      openVacancies: num(beKpis['openVacancies']),
      totalApplicants: num(beKpis['totalApplicants']),
      hires: num(beKpis['hires']),
      avgTimeToHireDays: num(beKpis['avgTimeToHireDays'] ?? beKpis['averageTimeToHireDays']),
      offerAcceptanceRate: num(beKpis['offerAcceptanceRate']),
      offersPending: num(beKpis['offersPending']),
    };

    const funnelSrc = (dto['funnel'] ?? []) as Array<Record<string, unknown>>;
    const funnel: IFunnelStage[] = funnelSrc.map((s, i, arr) => ({
      stage: s['stage'] as IFunnelStage['stage'],
      count: num(s['count']),
      conversionRate:
        s['conversionRate'] != null
          ? Math.round(s['conversionRate'] as number)
          : funnelConversion(
              arr.map((x) => ({ count: num(x['count']) })) as IFunnelStage[],
              i,
            ),
    }));

    const sourcesSrc = (dto['sources'] ?? []) as Array<Record<string, unknown>>;
    const sources: ISourceEffectiveness[] = sourcesSrc.map((s) => {
      const applicants = num(s['applicants'] ?? s['applicantCount']);
      const hires = num(s['hires'] ?? s['hireCount']);
      const source = s['source'] as ISourceEffectiveness['source'];
      const conversionRate = (s['conversionRate'] as number | null) ?? null;
      return {
        source,
        applicants,
        hires,
        conversionRate: sourceConversion({ source, applicants, hires, conversionRate }),
      };
    });

    const trendSrc = (dto['timeToHireTrend'] ?? []) as Array<Record<string, unknown>>;
    const timeToHireTrend: ITimeToHirePoint[] = trendSrc.map((p) => ({
      label: (p['label'] ?? p['period'] ?? '') as string,
      avgDays: num(p['avgDays'] ?? p['averageDays']),
    }));

    const vsSrc = (dto['vacancyStatus'] ?? dto['vacancyStatusSummary'] ?? []) as Array<
      Record<string, unknown>
    >;
    const vacancyStatus: IVacancyStatusCount[] = vsSrc.map((v) => ({
      status: v['status'] as IVacancyStatusCount['status'],
      count: num(v['count']),
    }));

    const actSrc = (dto['recentActivity'] ?? []) as Array<Record<string, unknown>>;
    const activityType: Record<string, IActivityEvent['type']> = {
      Applied: 'ApplicantApplied',
      ApplicantApplied: 'ApplicantApplied',
      StageChanged: 'StageChanged',
      InterviewScheduled: 'InterviewScheduled',
      OfferSent: 'OfferSent',
      OfferAccepted: 'OfferAccepted',
      Hired: 'Hired',
    };
    const recentActivity: IActivityEvent[] = actSrc.map((a, i) => ({
      id: (a['id'] ?? `${(a['occurredAt'] as string) ?? ''}-${i}`) as string,
      type: activityType[a['type'] as string] ?? 'StageChanged',
      description: (a['description'] ?? '') as string,
      occurredAt: (a['occurredAt'] ?? '') as string,
      applicantId: (a['applicantId'] ?? null) as string | null,
      vacancyId: (a['vacancyId'] ?? null) as string | null,
    }));

    const range = (dto['range'] ?? {
      from: (dto['from'] as string) ?? query.from,
      to: (dto['to'] as string) ?? query.to,
    }) as IRecruitmentDashboard['range'];

    return { range, kpis, funnel, sources, timeToHireTrend, vacancyStatus, recentActivity };
  }

  // ─── Helpers ───────────────────────────────────────────────────

  /** Parse the filename from a Content-Disposition header (export download). */
  static filenameFromResponse(res: HttpResponse<Blob>, fallback: string): string {
    const cd = res.headers.get('content-disposition') ?? '';
    const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(cd);
    if (utf8?.[1]) {
      return decodeURIComponent(utf8[1].trim());
    }
    const plain = /filename="?([^";]+)"?/i.exec(cd);
    if (plain?.[1]) {
      return plain[1].trim();
    }
    return fallback;
  }

  /** Trigger a browser download for a blob with the given filename. */
  static downloadBlob(blob: Blob, filename: string): void {
    if (typeof document === 'undefined' || typeof URL === 'undefined' || !URL.createObjectURL) {
      return;
    }
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  /** Parse an error body into a human-readable message; caller shows verbatim. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return (body as { message: string }).message;
    }
    return 'An unexpected error occurred.';
  }
}
