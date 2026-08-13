import { Injectable, inject } from '@angular/core';
import {
  HttpClient,
  HttpErrorResponse,
  HttpEvent,
  HttpRequest,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  IApplicableTemplate,
  IAssignChecklistRequest,
  IAssignedChecklist,
  IChecklistPreview,
  IModifyChecklistRequest,
} from '../models/onboarding-checklist.models';
import {
  ICompleteTaskRequest,
  ICompleteTaskResponse,
  IMyChecklist,
  IOnboardingProgress,
} from '../models/my-onboarding.models';

/**
 * US-ONB-002: Service for assigning onboarding checklists to new hires.
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (NFR-2).
 * Responses are the BARE `T` — the apiEnvelopeInterceptor unwraps the
 * ApiResponse<T> envelope, so this service never touches `.data` (matches
 * OnboardingTemplateService and every other FE feature service in this repo).
 *
 * CONTRACT — RECONCILED 2026-08-10 against contracts/openapi/hrm-v1.json (GAP-013). This block
 * previously said "(assumed — reconcile with backend)" and was never reconciled; that comment is
 * what made the drift look deliberate. Verified route list:
 *   GET    /onboarding/checklists/applicable-templates?employeeId=:id  -> IApplicableTemplate[]
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
    return this.http.get<IApplicableTemplate[]>(`${this.base}/applicable-templates`, {
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
    // GAP-013: the route is PUT /checklists/{id}; PATCH returned 405 for every modify.
    return this.http.put<IAssignedChecklist>(
      `${this.base}/${instanceId}`,
      request,
      { withCredentials: true },
    );
  }

  // ─── US-ONB-003: New-hire "me" checklist (employee self-service) ──
  // PINNED CONTRACT (backend builds these EXACTLY — do not drift):
  //   GET  /onboarding/checklists/me          -> IMyChecklist
  //   GET  /onboarding/checklists/me/progress -> IOnboardingProgress
  //   POST /onboarding/checklists/tasks/:id/complete  -> ICompleteTaskResponse

  /**
   * The logged-in employee's own onboarding checklist, tasks grouped by category
   * (FR-1 / AC-2). Tenant + identity are server-resolved from the session — no
   * employeeId is sent. Bare IMyChecklist (envelope unwrapped by the interceptor).
   */
  getMyChecklist(): Observable<IMyChecklist> {
    return this.http.get<IMyChecklist>(`${this.base}/me`, {
      withCredentials: true,
    });
  }

  /**
   * Lightweight progress summary for the dashboard widget (AC-1 / FR-4). The
   * widget hides itself when no checklist is assigned — backend returns 404 (or
   * empty) in that case, which the widget treats as "nothing to show".
   */
  getMyProgress(): Observable<IOnboardingProgress> {
    return this.http.get<IOnboardingProgress>(`${this.base}/me/progress`, {
      withCredentials: true,
    });
  }

  /**
   * Mark one of the employee's own tasks complete (AC-3 / FR-2). When the task
   * requires a document the file is sent as multipart form field "attachment"
   * with the optional comment; otherwise a plain JSON body is used. Returns the
   * updated task + recalculated progress so the ring can update without a
   * full re-fetch. BR-1/FR-7 (only Employee-role tasks) is enforced server-side;
   * the UI hides the action for other roles. Progress is reported via the
   * HttpEvent stream so the upload progress bar can render (AC-4).
   */
  completeTask(
    taskInstanceId: string,
    request: ICompleteTaskRequest,
  ): Observable<HttpEvent<ICompleteTaskResponse>> {
    const url = `${this.base}/tasks/${taskInstanceId}/complete`;

    if (request.file) {
      const formData = new FormData();
      formData.append('attachment', request.file, request.file.name);
      if (request.comment) {
        formData.append('comment', request.comment);
      }
      const req = new HttpRequest<FormData>('POST', url, formData, {
        reportProgress: true,
        withCredentials: true,
      });
      return this.http.request<ICompleteTaskResponse>(req);
    }

    const req = new HttpRequest<{ comment?: string | null }>(
      'POST',
      url,
      { comment: request.comment ?? null },
      { reportProgress: true, withCredentials: true },
    );
    return this.http.request<ICompleteTaskResponse>(req);
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
