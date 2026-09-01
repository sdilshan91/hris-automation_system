import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IStatutoryRule,
  IStatutoryRuleRequest,
  ITestCalculationRequest,
  ITestCalculationResult,
  StatutoryCalculationResultWire,
  StatutoryRuleListItemWire,
  StatutoryRuleWire,
  mapStatutoryRule,
  mapStatutoryRuleListItem,
  mapTestCalculationResult,
  toCreateStatutoryRuleWire,
  toTestCalculationRequestWire,
  toUpdateStatutoryRuleWire,
} from '../models/statutory.models';

/**
 * US-PAY-006: Service for tenant-scoped statutory configuration — income-tax
 * slabs, social-security (EPF/ETF) rules, other deductions, versioned by fiscal
 * year (AC-3), plus the test-calculation endpoint (FR-5).
 *
 * Sibling to PayrollService / PayrollRunService; the route strings live here
 * ONLY, so a backend contract change is a one-file fix. All requests use
 * withCredentials (httpOnly cookie auth) and are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header). The backend stamps tenant_id +
 * audit fields and enforces RLS (AC-4, FR-8).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see statutory.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class StatutoryService {
  private readonly http = inject(HttpClient);
  private readonly rulesUrl = `${environment.apiBaseUrl}/payroll/statutory-rules`;

  /**
   * Statutory rules for a fiscal year (FR-4 versioning). When `fiscalYear` is
   * omitted the backend returns the active/current FY's rules. Tolerates either a
   * bare array or a `{ data }`-style page.
   */
  listRules(fiscalYear?: string): Observable<IStatutoryRule[]> {
    const url = fiscalYear
      ? `${this.rulesUrl}?fiscalYear=${encodeURIComponent(fiscalYear)}`
      : this.rulesUrl;
    return this.http
      .get<
        | StatutoryRuleListItemWire[]
        | { items?: StatutoryRuleListItemWire[]; data?: StatutoryRuleListItemWire[] }
      >(url, { withCredentials: true })
      .pipe(map((res) => this.toArray(res).map(mapStatutoryRuleListItem)));
  }

  /**
   * Distinct fiscal years that have statutory rules (FR-4), newest-first, for the
   * fiscal-year selector. Tolerates a `{ data }` envelope; defaults to [].
   */
  listFiscalYears(): Observable<string[]> {
    return this.http
      .get<string[] | { data: string[] }>(`${this.rulesUrl}/fiscal-years`, {
        withCredentials: true,
      })
      .pipe(map((res) => this.toArray(res)));
  }

  /** Create a statutory rule (FR-1/FR-2). */
  createRule(request: IStatutoryRuleRequest): Observable<IStatutoryRule> {
    return this.http
      .post<StatutoryRuleWire>(
        this.rulesUrl,
        toCreateStatutoryRuleWire(request),
        { withCredentials: true },
      )
      .pipe(map(mapStatutoryRule));
  }

  /**
   * Update a statutory rule. Per BR-7 the backend rejects edits to rules whose
   * period has finalized payroll runs (corrections go through US-PAY-007).
   */
  updateRule(
    id: string,
    request: IStatutoryRuleRequest,
  ): Observable<IStatutoryRule> {
    return this.http
      .put<StatutoryRuleWire>(
        `${this.rulesUrl}/${id}`,
        toUpdateStatutoryRuleWire(request),
        { withCredentials: true },
      )
      .pipe(map(mapStatutoryRule));
  }

  /** Delete a statutory rule. */
  deleteRule(id: string): Observable<void> {
    return this.http.delete<void>(`${this.rulesUrl}/${id}`, {
      withCredentials: true,
    });
  }

  /**
   * Test calculation (FR-5, §8): send a sample monthly gross for a fiscal year,
   * get back the computed statutory deductions. The backend uses the SAME
   * calculation engine payroll runs use (NFR-2/NFR-5), so what the admin tests is
   * what payroll will deduct.
   */
  testCalculation(
    request: ITestCalculationRequest,
  ): Observable<ITestCalculationResult> {
    return this.http
      .post<StatutoryCalculationResultWire>(
        `${this.rulesUrl}/test-calculation`,
        toTestCalculationRequestWire(request),
        { withCredentials: true },
      )
      .pipe(
        // `monthlyGross` and `netPay` have no wire source; the mapper supplies them from the request
        // the caller just sent rather than printing 0. See mapTestCalculationResult's contract note.
        map((res) => mapTestCalculationResult(res, request.monthlyGross)),
      );
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
