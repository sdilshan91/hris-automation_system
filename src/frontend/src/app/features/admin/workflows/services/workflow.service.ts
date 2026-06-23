import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IWorkflowSummary,
  IWorkflowDefinition,
  ICreateWorkflowRequest,
  IUpdateWorkflowRequest,
  IApproverRoleOption,
  IApproverUserOption,
} from '../models/workflow.models';

/**
 * US-ADM-007: Tenant Admin approval-workflow definition service.
 *
 * TENANT-scoped endpoints under `/api/v1/tenant/workflows...` (NOT the
 * system-admin console). Base derived from `environment.apiBaseUrl` verbatim
 * (already ends in `/v1`) + `/tenant/workflows`, matching how US-ADM-006
 * (`/tenant/settings`) and US-ADM-005 (`/users`) built their bases. Tenant
 * isolation is enforced server-side via ITenantContext + EF global filters
 * (FR-7); here we just carry the auth cookie + X-Tenant-Subdomain header (added
 * by the tenantInterceptor) on every call. Bare payloads — no ApiResponse unwrap.
 *
 * Backend contract (finalized in parallel — keep models in one file):
 *   GET    /api/v1/tenant/workflows                 — grouped list summaries (AC-1)
 *   GET    /api/v1/tenant/workflows/:id             — full definition (editor)
 *   POST   /api/v1/tenant/workflows                 — create (AC-2 / plan-limit AC-4)
 *   PUT    /api/v1/tenant/workflows/:id             — update → new version (AC-3)
 *   POST   /api/v1/tenant/workflows/:id/archive     — archive (FR-6)
 *   POST   /api/v1/tenant/workflows/:id/restore     — restore (FR-6)
 *   DELETE /api/v1/tenant/workflows/:id             — delete (BR-6)
 *
 * Approver pickers REUSE existing tenant sources rather than inventing new ones:
 *   GET    /api/v1/tenant/roles                     — role options (US-AUTH-006)
 *   GET    /api/v1/users?pageSize=…                 — user options (US-ADM-005)
 */
@Injectable({ providedIn: 'root' })
export class WorkflowService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/workflows`;
  private readonly rolesUrl = `${environment.apiBaseUrl}/tenant/roles`;
  private readonly usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  // ─── Workflow definitions ──────────────────────────────────

  /** Grouped-in-UI list of workflow summaries for the current tenant (AC-1). */
  getWorkflows(): Observable<IWorkflowSummary[]> {
    return this.http.get<IWorkflowSummary[]>(this.baseUrl, {
      withCredentials: true,
    });
  }

  /** Full workflow definition (with ordered steps) for the editor (AC-2/3). */
  getWorkflow(id: string): Observable<IWorkflowDefinition> {
    return this.http.get<IWorkflowDefinition>(`${this.baseUrl}/${id}`, {
      withCredentials: true,
    });
  }

  /**
   * Create a new workflow definition (AC-2). The server enforces the plan's
   * `max_workflows` limit and returns 400/409 with the EXACT message the UI
   * surfaces verbatim (AC-4); we let that error propagate to the component.
   */
  createWorkflow(
    request: ICreateWorkflowRequest
  ): Observable<IWorkflowDefinition> {
    return this.http.post<IWorkflowDefinition>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /** Update a workflow → server creates a NEW version (AC-3, FR-3). */
  updateWorkflow(
    id: string,
    request: IUpdateWorkflowRequest
  ): Observable<IWorkflowDefinition> {
    return this.http.put<IWorkflowDefinition>(
      `${this.baseUrl}/${id}`,
      request,
      { withCredentials: true }
    );
  }

  /** Archive a workflow — marks inactive; restorable (FR-6). */
  archiveWorkflow(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/archive`, null, {
      withCredentials: true,
    });
  }

  /** Restore a previously archived workflow (FR-6). */
  restoreWorkflow(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/restore`, null, {
      withCredentials: true,
    });
  }

  /** Delete a workflow — allowed only with no in-flight instances (BR-6). */
  deleteWorkflow(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`, {
      withCredentials: true,
    });
  }

  // ─── Approver picker sources (REUSED endpoints, US-ADM-005/AUTH-006) ──

  /** Role options for the approver/escalation picker (reuses tenant roles). */
  getApproverRoles(): Observable<IApproverRoleOption[]> {
    return this.http
      .get<IApproverRoleOption[]>(this.rolesUrl, { withCredentials: true })
      .pipe(map((roles) => roles.map((r) => ({ id: r.id, name: r.name }))));
  }

  /**
   * User options for the approver / delegation-backup picker (reuses the
   * US-ADM-005 user list). We request a large page of active users and project
   * to the minimal option shape the pickers need.
   */
  getApproverUsers(): Observable<IApproverUserOption[]> {
    return this.http
      .get<IUserListEnvelope>(this.usersUrl, {
        params: { page: '1', pageSize: '200', status: 'active' },
        withCredentials: true,
      })
      .pipe(
        map((res) =>
          (res.items ?? []).map((u) => ({
            id: u.userTenantId,
            displayName: u.displayName,
            email: u.email,
          }))
        )
      );
  }
}

/** Minimal projection of the US-ADM-005 user-list response we consume here. */
interface IUserListEnvelope {
  items: { userTenantId: string; displayName: string; email: string }[];
}
