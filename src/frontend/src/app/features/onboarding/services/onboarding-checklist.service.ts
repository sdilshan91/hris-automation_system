import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IApplicableTemplate,
  IAssignChecklistRequest,
  IAssignedChecklist,
  IChecklistPreview,
  IModifyChecklistRequest,
} from '../models/onboarding-checklist.models';

/**
 * US-ONB-002: Service for assigning onboarding checklists to new hires.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (NFR-2).
 * Responses are the BARE `T` — the apiEnvelopeInterceptor unwraps the
 * ApiResponse<T> envelope, so this service never touches `.data` (matches
 * OnboardingTemplateService and every other FE feature service in this repo).
 *
 * CONTRACT (assumed — reconcile with backend; mapping kept in ONE place here):
 *   GET    /onboarding/checklists/applicable?employeeId=:id  -> IApplicableTemplate[]
 *   GET    /onboarding/checklists/preview?employeeId=:e&templateId=:t -> IChecklistPreview
 *   GET    /onboarding/checklists/employee/:employeeId       -> IAssignedChecklist | null
 *   POST   /onboarding/checklists                            body IAssign... -> IAssignedChecklist
 *   PATCH  /onboarding/checklists/:instanceId                body IModify... -> IAssignedChecklist
 */
@Injectable({ providedIn: 'root' })
export class OnboardingChecklistService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/onboarding/checklists`;

  // ─── Read ─────────────────────────────────────────────────

  /**
   * Templates the employee is eligible for — server-filtered by department /
   * job title + universal templates, active only (AC-1 / FR-1 / BR-1).
   */
  getApplicableTemplates(employeeId: string): Observable<IApplicableTemplate[]> {
    return this.http.get<IApplicableTemplate[]>(`${this.base}/applicable`, {
      params: { employeeId },
      withCredentials: true,
    });
  }

  /**
   * Preview the task instances a template would create for this employee, with
   * server-calculated due dates (FR-2 / BR-4). Shown before confirming (UI/UX §8).
   */
  preview(
    employeeId: string,
    templateId: string,
  ): Observable<IChecklistPreview> {
    return this.http.get<IChecklistPreview>(`${this.base}/preview`, {
      params: { employeeId, templateId },
      withCredentials: true,
    });
  }

  /**
   * The employee's current active checklist, if any (drives the AC-3
   * replace/merge prompt). Backend returns null/204 when none exists.
   */
  getByEmployee(employeeId: string): Observable<IAssignedChecklist | null> {
    return this.http.get<IAssignedChecklist | null>(
      `${this.base}/employee/${employeeId}`,
      { withCredentials: true },
    );
  }

  // ─── Write (tenant_id is server-set — FR-7) ───────────────

  /**
   * Assign the (possibly inline-edited) task set to the employee (AC-2). When the
   * employee already has an active checklist, `mode` ('replace'|'merge') must be
   * set (AC-3). Response carries notifiedCount for the success toast (AC-2/AC-5).
   */
  assign(request: IAssignChecklistRequest): Observable<IAssignedChecklist> {
    return this.http.post<IAssignedChecklist>(this.base, request, {
      withCredentials: true,
    });
  }

  /**
   * Modify an existing checklist instance — add/remove/re-date tasks (AC-4 /
   * FR-5 / FR-6). Mandatory template tasks cannot be removed (BR-3, server-
   * enforced; the 400 message is surfaced verbatim).
   */
  modify(
    instanceId: string,
    request: IModifyChecklistRequest,
  ): Observable<IAssignedChecklist> {
    return this.http.patch<IAssignedChecklist>(
      `${this.base}/${instanceId}`,
      request,
      { withCredentials: true },
    );
  }

  // ─── Helpers ─────────────────────────────────────────────

  /** Parse an error body into a human-readable message; caller shows verbatim. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return (body as { message: string }).message;
    }
    return 'An unexpected error occurred.';
  }
}
