import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IVacancy,
  IVacancyRequest,
  IVacancyListParams,
  IVacancyPage,
  IVacancyStatusRequest,
  ILookupOption,
  IVacancyErrorResponse,
  VacancyStatus,
} from '../models/vacancy.models';

/**
 * US-REC-001: Service for recruiter-facing vacancy CRUD + lifecycle actions.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header). The
 * backend stamps tenant_id, reference number, slug, and audit fields server-side.
 *
 * CONTRACT (assumed — backend agent building in parallel; ADAPT if it differs):
 *   GET  /recruitment/vacancies?status=&departmentId=&search=&page=&pageSize=
 *        -> IVacancyPage { data, total, page, pageSize }
 *   GET  /recruitment/vacancies/:id            -> IVacancy
 *   POST /recruitment/vacancies                body IVacancyRequest -> IVacancy (Draft)
 *   PUT  /recruitment/vacancies/:id            body IVacancyRequest -> IVacancy
 *   POST /recruitment/vacancies/:id/publish    -> IVacancy (status Open)
 *   POST /recruitment/vacancies/:id/close      -> IVacancy (status Closed)
 *   POST /recruitment/vacancies/:id/status     body { status } -> IVacancy
 *
 * Master-data lookups for the form dropdowns reuse the existing Core HR endpoints
 * and are normalized to ILookupOption HERE so the mapping is in one place.
 */
@Injectable({ providedIn: 'root' })
export class VacancyService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/recruitment/vacancies`;

  // ─── Vacancies: read ─────────────────────────────────────

  /** Paged vacancy list with optional status / department / search filters (§8, NFR-1). */
  listVacancies(params: IVacancyListParams = {}): Observable<IVacancyPage> {
    let httpParams = new HttpParams();
    if (params.status) {
      httpParams = httpParams.set('status', params.status);
    }
    if (params.departmentId) {
      httpParams = httpParams.set('departmentId', params.departmentId);
    }
    if (params.search) {
      httpParams = httpParams.set('search', params.search);
    }
    if (params.page != null) {
      httpParams = httpParams.set('page', params.page.toString());
    }
    if (params.pageSize != null) {
      httpParams = httpParams.set('pageSize', params.pageSize.toString());
    }
    return this.http
      .get<IVacancyPage>(this.baseUrl, {
        withCredentials: true,
        params: httpParams,
      })
      .pipe(map((res) => this.normalizePage(res)));
  }

  /** Single vacancy by id — feeds the edit form (AC-3). */
  getVacancy(id: string): Observable<IVacancy> {
    return this.http.get<IVacancy>(`${this.baseUrl}/${id}`, {
      withCredentials: true,
    });
  }

  // ─── Vacancies: write ────────────────────────────────────

  /** Create a vacancy in Draft status (AC-1). */
  createVacancy(request: IVacancyRequest): Observable<IVacancy> {
    return this.http.post<IVacancy>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /** Update an existing vacancy (AC-3); audit log written server-side (FR-7). */
  updateVacancy(id: string, request: IVacancyRequest): Observable<IVacancy> {
    return this.http.put<IVacancy>(`${this.baseUrl}/${id}`, request, {
      withCredentials: true,
    });
  }

  /** Publish: Draft/On Hold -> Open (AC-2). Backend validates BR-2 required fields. */
  publishVacancy(id: string): Observable<IVacancy> {
    return this.http.post<IVacancy>(`${this.baseUrl}/${id}/publish`, {}, {
      withCredentials: true,
    });
  }

  /** Close: Open/On Hold -> Closed (AC-5); applicants are not deleted (BR-3). */
  closeVacancy(id: string): Observable<IVacancy> {
    return this.http.post<IVacancy>(`${this.baseUrl}/${id}/close`, {}, {
      withCredentials: true,
    });
  }

  /** Generic status change — backs the inline status dropdown on the list (§8). */
  changeStatus(id: string, status: VacancyStatus): Observable<IVacancy> {
    const body: IVacancyStatusRequest = { status };
    return this.http.post<IVacancy>(`${this.baseUrl}/${id}/status`, body, {
      withCredentials: true,
    });
  }

  // ─── Master-data lookups (form dropdowns) ────────────────
  // Normalized to ILookupOption in one place; if a master-data DTO shape differs,
  // adjust ONLY the mapping closures below.

  /** Departments for the form dropdown (FR-1). */
  getDepartments(): Observable<ILookupOption[]> {
    return this.http
      .get<Array<{ departmentId: string; name: string }>>(
        `${environment.apiBaseUrl}/tenant/departments`,
        { withCredentials: true },
      )
      .pipe(
        map((rows) =>
          (rows ?? []).map((d) => ({ id: d.departmentId, label: d.name })),
        ),
      );
  }

  /** Job titles for the form dropdown (FR-1). */
  getJobTitles(): Observable<ILookupOption[]> {
    return this.http
      .get<Array<{ jobTitleId: string; titleName: string }>>(
        `${environment.apiBaseUrl}/tenant/job-titles`,
        { withCredentials: true },
      )
      .pipe(
        map((rows) =>
          (rows ?? []).map((j) => ({ id: j.jobTitleId, label: j.titleName })),
        ),
      );
  }

  /** Locations for the form dropdown (FR-1). */
  getLocations(): Observable<ILookupOption[]> {
    return this.http
      .get<Array<{ id: string; name: string }>>(
        `${environment.apiBaseUrl}/tenant/locations`,
        { withCredentials: true },
      )
      .pipe(
        map((rows) => (rows ?? []).map((l) => ({ id: l.id, label: l.name }))),
      );
  }

  /**
   * Searchable hiring-manager (employee) lookup (§8). Sends the typed query so the
   * backend can scope/limit results; falls back to a plain list when empty.
   */
  searchEmployees(search?: string): Observable<ILookupOption[]> {
    let params = new HttpParams();
    if (search) {
      params = params.set('search', search);
    }
    return this.http
      .get<Array<{
        employeeId: string;
        firstName: string;
        lastName: string;
        jobTitleName?: string | null;
      }>>(`${environment.apiBaseUrl}/tenant/employees`, {
        withCredentials: true,
        params,
      })
      .pipe(
        map((rows) =>
          (rows ?? []).map((e) => ({
            id: e.employeeId,
            label: `${e.firstName} ${e.lastName}`.trim(),
            sublabel: e.jobTitleName ?? null,
          })),
        ),
      );
  }

  // ─── Helpers ─────────────────────────────────────────────

  /** Tolerate a bare array OR a page envelope from the list endpoint. */
  private normalizePage(res: IVacancyPage | IVacancy[]): IVacancyPage {
    if (Array.isArray(res)) {
      return { data: res, total: res.length, page: 1, pageSize: res.length };
    }
    return {
      data: res?.data ?? [],
      total: res?.total ?? 0,
      page: res?.page ?? 1,
      pageSize: res?.pageSize ?? 0,
    };
  }

  /** Parse an error body into the typed shape; the caller shows `message` verbatim. */
  static parseError(err: HttpErrorResponse): IVacancyErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IVacancyErrorResponse;
    }
    return null;
  }

  /** Convenience: extract a human-readable message from an error response. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    return VacancyService.parseError(err)?.message ?? 'An unexpected error occurred.';
  }
}
