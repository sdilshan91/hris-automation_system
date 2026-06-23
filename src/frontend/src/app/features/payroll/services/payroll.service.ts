import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  ISalaryComponent,
  ISalaryComponentRequest,
  ISalaryStructure,
  IReorderRequest,
  IFormulaTestRequest,
  IFormulaTestResult,
  IComponentInUseError,
} from '../models/payroll.models';

/**
 * US-PAY-001: Service for tenant-scoped salary structures + salary components.
 *
 * Thin and isolated by design — the route strings live here ONLY, so a backend
 * contract change is a one-file fix. All requests use withCredentials (httpOnly
 * cookie auth) and are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain
 * header). The backend stamps tenant_id + audit fields and enforces RLS (AC-6).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see payroll.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class PayrollService {
  private readonly http = inject(HttpClient);
  private readonly componentsUrl = `${environment.apiBaseUrl}/payroll/salary-components`;
  private readonly structuresUrl = `${environment.apiBaseUrl}/payroll/salary-structures`;

  // ─── Salary components ─────────────────────────────────────

  /**
   * All salary components for the tenant, sorted by processing order (AC-1).
   * Tolerates either a bare array or a `{ data }`-style page so a backend
   * pagination choice (NFR-3) doesn't break the inline table.
   */
  listComponents(): Observable<ISalaryComponent[]> {
    return this.http
      .get<ISalaryComponent[] | { data: ISalaryComponent[] }>(
        this.componentsUrl,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
  }

  /** Create a salary component (AC-1). */
  createComponent(
    request: ISalaryComponentRequest,
  ): Observable<ISalaryComponent> {
    return this.http.post<ISalaryComponent>(this.componentsUrl, request, {
      withCredentials: true,
    });
  }

  /** Update a salary component (AC-2). Historical payslips are unaffected (server). */
  updateComponent(
    id: string,
    request: ISalaryComponentRequest,
  ): Observable<ISalaryComponent> {
    return this.http.put<ISalaryComponent>(
      `${this.componentsUrl}/${id}`,
      request,
      { withCredentials: true },
    );
  }

  /**
   * Delete a salary component (AC-5). If the component is in use by active
   * employees the backend returns 409 — `parseInUseError` extracts the affected
   * count so the caller can show the blocking message.
   */
  deleteComponent(id: string): Observable<void> {
    return this.http.delete<void>(`${this.componentsUrl}/${id}`, {
      withCredentials: true,
    });
  }

  /**
   * Persist a new processing order (AC-4) as an ordered list of component ids.
   * Returns the re-sequenced components.
   */
  reorderComponents(orderedIds: string[]): Observable<ISalaryComponent[]> {
    const body: IReorderRequest = { orderedIds };
    return this.http
      .post<ISalaryComponent[] | { data: ISalaryComponent[] }>(
        `${this.componentsUrl}/reorder`,
        body,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
  }

  /**
   * Evaluate a formula against sample values for the "Test" button (FR-4, §8).
   * The backend uses the same safe evaluator that runs payroll, so what the user
   * tests is what payroll will compute (BR-6 syntax + circular-ref validation).
   */
  testFormula(
    request: IFormulaTestRequest,
  ): Observable<IFormulaTestResult> {
    return this.http.post<IFormulaTestResult>(
      `${this.componentsUrl}/validate-formula`,
      request,
      { withCredentials: true },
    );
  }

  // ─── Salary structures ─────────────────────────────────────

  /** All salary structures for the tenant (AC-3). Tolerates a `{ data }` envelope. */
  listStructures(): Observable<ISalaryStructure[]> {
    return this.http
      .get<ISalaryStructure[] | { data: ISalaryStructure[] }>(
        this.structuresUrl,
        { withCredentials: true },
      )
      .pipe(map((res) => this.toArray(res)));
  }

  /** Clone an existing structure into a new Draft one (FR-6). */
  cloneStructure(id: string): Observable<ISalaryStructure> {
    return this.http.post<ISalaryStructure>(
      `${this.structuresUrl}/${id}/clone`,
      {},
      { withCredentials: true },
    );
  }

  // ─── Helpers ───────────────────────────────────────────────

  /**
   * AC-5: pull the affected-employee count out of a delete 409. Returns null when
   * the error isn't the in-use case, so the caller can fall back to a toast.
   */
  parseInUseError(err: unknown): IComponentInUseError | null {
    if (!(err instanceof HttpErrorResponse)) {
      return null;
    }
    const body = err.error as Partial<IComponentInUseError> | undefined;
    const isInUse =
      err.status === 409 || body?.code === 'component_in_use';
    if (!isInUse) {
      return null;
    }
    return {
      code: 'component_in_use',
      affectedEmployeeCount:
        typeof body?.affectedEmployeeCount === 'number'
          ? body.affectedEmployeeCount
          : 0,
      message: body?.message,
    };
  }

  /**
   * Accept a bare array, the new `{ items }` page envelope, or the legacy
   * `{ data }` one; default to []. Items first, then data, then [].
   */
  private toArray<T>(
    res: T[] | { items?: T[]; data?: T[] } | null | undefined,
  ): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { items?: T[] }).items)) {
      return (res as { items: T[] }).items;
    }
    if (res && Array.isArray((res as { data?: T[] }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
  }
}
