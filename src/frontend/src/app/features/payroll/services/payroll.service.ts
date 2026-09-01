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
  SalaryComponentListItemWire,
  SalaryComponentPageWire,
  SalaryComponentWire,
  SalaryStructureListItemWire,
  SalaryStructurePageWire,
  SalaryStructureWire,
  mapSalaryComponent,
  mapSalaryComponentListItem,
  mapSalaryStructure,
  mapSalaryStructureListItem,
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
   *
   * The contract's `GET /payroll/salary-components` is PAGED
   * (`PagedResultOfPayrollSalaryComponentListItemDto`, server default `pageSize` 25) and this call sends
   * no `page`/`pageSize`, so a tenant with more than 25 components silently sees only the first page —
   * flagged in the D1 report. `toArray` unwraps the page; the rows are `…ListItemDto`, which is a
   * LEANER shape than the full DTO (no `formulaExpression`) — see `mapSalaryComponentListItem`.
   */
  listComponents(): Observable<ISalaryComponent[]> {
    return this.http
      .get<
        | SalaryComponentPageWire
        | SalaryComponentListItemWire[]
        | { data: SalaryComponentListItemWire[] }
      >(this.componentsUrl, { withCredentials: true })
      .pipe(map((res) => this.toArray(res).map(mapSalaryComponentListItem)));
  }

  /** Create a salary component (AC-1). */
  createComponent(
    request: ISalaryComponentRequest,
  ): Observable<ISalaryComponent> {
    return this.http
      .post<SalaryComponentWire>(this.componentsUrl, request, {
        withCredentials: true,
      })
      .pipe(map(mapSalaryComponent));
  }

  /** Update a salary component (AC-2). Historical payslips are unaffected (server). */
  updateComponent(
    id: string,
    request: ISalaryComponentRequest,
  ): Observable<ISalaryComponent> {
    return this.http
      .put<SalaryComponentWire>(`${this.componentsUrl}/${id}`, request, {
        withCredentials: true,
      })
      .pipe(map(mapSalaryComponent));
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
   *
   * ⚠ NO CONTRACT PATH. `POST /payroll/salary-components/reorder` does not exist in
   * `contracts/openapi/hrm-v1.json` — this is a live 404 (GAP-010 / ISSUE-372). The only reorder route
   * on the API is `POST /payroll/salary-structures/{id}/components/reorder`, which reorders components
   * WITHIN one structure and takes `{ order: [{ salaryStructureComponentId, processingOrder }] }` — a
   * different resource and a different body from the `{ orderedIds }` sent here. There is therefore no
   * generated type to bind the response to; the request/response stay hand-written on purpose. Do not
   * "fix" the types here — the endpoint has to be built (or the drag-reorder control removed) first.
   */
  reorderComponents(orderedIds: string[]): Observable<ISalaryComponent[]> {
    const body: IReorderRequest = { orderedIds };
    return this.http
      .post<
        | SalaryComponentPageWire
        | SalaryComponentListItemWire[]
        | { data: SalaryComponentListItemWire[] }
      >(`${this.componentsUrl}/reorder`, body, { withCredentials: true })
      .pipe(map((res) => this.toArray(res).map(mapSalaryComponentListItem)));
  }

  /**
   * Evaluate a formula against sample values for the "Test" button (FR-4, §8).
   *
   * ⚠ NO CONTRACT PATH. `POST /payroll/salary-components/validate-formula` does not exist in
   * `contracts/openapi/hrm-v1.json` — zero paths, zero controller routes (GAP-010 / ISSUE-372). This is
   * a live 404, so there is no generated type for `IFormulaTestRequest`/`IFormulaTestResult` to bind to
   * and they stay hand-written on purpose. The claim in the old comment — that the backend runs the same
   * evaluator payroll uses — is unverified, because there is no backend endpoint at all.
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
      .get<
        | SalaryStructurePageWire
        | SalaryStructureListItemWire[]
        | { data: SalaryStructureListItemWire[] }
      >(this.structuresUrl, { withCredentials: true })
      .pipe(map((res) => this.toArray(res).map(mapSalaryStructureListItem)));
  }

  /**
   * Clone an existing structure into a new Draft one (FR-6).
   *
   * ⚠ The contract's body is `PayrollCloneSalaryStructureRequest { name, code }` and this posts `{}`, so
   * the API binds `Name = ""` / `Code = ""`. There is no validator registered for the request, so the
   * clone is persisted NAMELESS and CODELESS, and a second clone then 409s on the duplicate empty code.
   * Sending real values needs the caller to prompt for a name/code — a component change, outside this
   * task's lane. Flagged in the D1 report; the response IS bound (`PayrollSalaryStructureDto`).
   */
  cloneStructure(id: string): Observable<ISalaryStructure> {
    return this.http
      .post<SalaryStructureWire>(
        `${this.structuresUrl}/${id}/clone`,
        {},
        { withCredentials: true },
      )
      .pipe(map(mapSalaryStructure));
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
   * Accept a bare array, the `PagedResultOf…` `{ items }` envelope, or the legacy `{ data }` one;
   * default to []. Items first, then data, then [].
   *
   * `items` / `data` are `T[] | null` (not just optional) because the generated contract marks the
   * collections nullable — the hand-written envelope type here omitted the `| null` and the compiler
   * caught it the moment these calls were bound to the real wire types.
   */
  private toArray<T>(
    res:
      | T[]
      | { items?: T[] | null; data?: T[] | null }
      | null
      | undefined,
  ): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { items?: T[] | null }).items)) {
      return (res as { items: T[] }).items;
    }
    if (res && Array.isArray((res as { data?: T[] | null }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
  }
}
