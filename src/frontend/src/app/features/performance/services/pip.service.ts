import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  ICreatePipRequest,
  IEscalateRequest,
  IPip,
  IPipDraft,
  IPipSummary,
  IRecordCheckpointRequest,
  ISetOutcomeRequest,
} from '../models/pip.models';

/**
 * US-PRF-008: Service for Performance Improvement Plan (PIP) management. HR-led with
 * manager + employee touchpoints — one service covers HR (create/extend/close/
 * escalate, BR-1), manager-or-HR (record checkpoint, AC-3) and employee (acknowledge,
 * BR-4) operations; the backend authorizes each by session role/ownership.
 *
 * The route strings live HERE ONLY, so a backend contract change is a one-file fix.
 * All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped via
 * the tenantInterceptor (X-Tenant-Subdomain header). The backend resolves the acting
 * user + tenant from the session and enforces `Performance.Review.All`/manager scope +
 * PIP ownership + RLS (NFR-2). PIP data is excluded from general dashboards (BR-5).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see pip.models.ts.
 */
@Injectable({ providedIn: 'root' })
export class PipService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/performance/pips`;

  /** AC-1: the HR PIP list. Tolerates either a bare array or a `{ data }` page. */
  listPips(): Observable<IPipSummary[]> {
    return this.http
      .get<IPipSummary[] | { data: IPipSummary[] }>(this.baseUrl, {
        withCredentials: true,
      })
      .pipe(map((res) => (Array.isArray(res) ? res : (res?.data ?? []))));
  }

  /** Load the full PIP record (drives the detail/timeline screen + employee My PIP). */
  getPip(pipId: string): Observable<IPip> {
    return this.http.get<IPip>(`${this.baseUrl}/${pipId}`, {
      withCredentials: true,
    });
  }

  /**
   * AC-1: pre-fill payload for the creation form. `employeeId` is optional (omit for a
   * blank HR-initiated form); `reviewId` is sent only when arriving from a flagged
   * manager review so the server can suggest a reason.
   */
  getDraft(employeeId?: string, reviewId?: string): Observable<IPipDraft> {
    const params: Record<string, string> = {};
    if (employeeId) {
      params['employeeId'] = employeeId;
    }
    if (reviewId) {
      params['reviewId'] = reviewId;
    }
    return this.http.get<IPipDraft>(`${this.baseUrl}/draft`, {
      params,
      withCredentials: true,
    });
  }

  /**
   * AC-2: create + initiate the PIP (status → Active). Server re-validates min-30-days
   * (BR-3), ≥1 objective, and one-active-PIP-per-employee (BR-2), then notifies the
   * employee/manager/mentor and schedules checkpoint reminders.
   */
  createPip(request: ICreatePipRequest): Observable<IPip> {
    return this.http.post<IPip>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /**
   * AC-3: a manager OR HR records a checkpoint (traffic-light status + evidence notes
   * + optional attachment). Sent as multipart when a file is attached (field `file`,
   * FR-4), JSON otherwise. Returns the updated PIP.
   */
  recordCheckpoint(
    pipId: string,
    checkpointId: string,
    request: IRecordCheckpointRequest,
    file?: File | null,
  ): Observable<IPip> {
    const url = `${this.baseUrl}/${pipId}/checkpoints/${checkpointId}`;
    if (file) {
      const form = new FormData();
      form.append('status', request.status);
      form.append('notes', request.notes);
      form.append('file', file, file.name);
      return this.http.post<IPip>(url, form, { withCredentials: true });
    }
    return this.http.post<IPip>(url, request, { withCredentials: true });
  }

  /**
   * AC-4 (HR-only, BR-1): set the final outcome. SuccessfullyCompleted closes the PIP;
   * Extended sets a new end date (+ optional new objectives, FR-6); NotMet flips to
   * NotMet and unlocks the escalation step (AC-5).
   */
  setOutcome(pipId: string, request: ISetOutcomeRequest): Observable<IPip> {
    return this.http.post<IPip>(`${this.baseUrl}/${pipId}/outcome`, request, {
      withCredentials: true,
    });
  }

  /**
   * AC-5 (HR-only): confirm the configured escalation action once the outcome is
   * NotMet. Records an immutable audit entry and notifies stakeholders.
   */
  escalate(pipId: string, request: IEscalateRequest): Observable<IPip> {
    return this.http.post<IPip>(`${this.baseUrl}/${pipId}/escalate`, request, {
      withCredentials: true,
    });
  }

  /**
   * BR-4: the EMPLOYEE acknowledges PIP initiation (records a signature). No body.
   * Reached from the employee "My PIP" view under /my-review.
   */
  acknowledge(pipId: string): Observable<IPip> {
    return this.http.post<IPip>(
      `${this.baseUrl}/${pipId}/acknowledge`,
      {},
      { withCredentials: true },
    );
  }
}
