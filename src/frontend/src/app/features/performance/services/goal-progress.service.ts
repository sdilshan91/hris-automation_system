import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IAddGoalUpdateRequest,
  IEmployeeGoalProgress,
  IGoalComment,
  IGoalUpdate,
  IMyGoals,
  ITeamGoalProgressRow,
} from '../models/goal-progress.models';

/**
 * US-PRF-009: Service for goal tracking with progress updates. Covers BOTH personas:
 * employee "My Goals" (load, add update, history — `Performance.Read.Self`) and the
 * manager/HR "Team Goal Progress" (team table, drill-down, comment on an update —
 * `Performance.Review.Team`/`.All`). The backend authorizes each by session role +
 * ownership; the FE sends no tenant/employee id for the self views.
 *
 * The route strings live HERE ONLY, so a backend contract change is a one-file fix.
 * All requests use withCredentials (httpOnly cookie auth) and are tenant-scoped via
 * the tenantInterceptor (X-Tenant-Subdomain header) + RLS (NFR-2).
 *
 * Envelope: the global ApiResponse unwrap interceptor (US-PLT-001) strips the
 * `{ data }` wrapper, so these methods consume BARE payloads. Enums arrive as
 * PascalCase STRINGS (US-PLT-003) — see goal-progress.models.ts. Progress history is
 * append-only server-side (NFR-3); this service exposes no edit/delete.
 */
@Injectable({ providedIn: 'root' })
export class GoalProgressService {
  private readonly http = inject(HttpClient);
  // BUG-243: the backend serves these under /tenant/performance directly (no
  // 'goal-progress' segment) — see GoalProgressController [Route].
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/performance`;

  /** AC-1: the employee's "My Goals" screen (cycle window, overall %, goal cards). */
  getMyGoals(): Observable<IMyGoals> {
    return this.http.get<IMyGoals>(`${this.baseUrl}/my-goals`, {
      withCredentials: true,
    });
  }

  /**
   * AC-3/FR-3: the append-only update history for one goal (chronological timeline +
   * per-update manager comment thread). Tolerates a bare array or a `{ data }` page.
   */
  getGoalUpdates(goalId: string): Observable<IGoalUpdate[]> {
    return this.http
      .get<
        IGoalUpdate[] | { data: IGoalUpdate[] }
      >(`${this.baseUrl}/goals/${goalId}/timeline`, { withCredentials: true })
      .pipe(map((res) => (Array.isArray(res) ? res : (res?.data ?? []))));
  }

  /**
   * AC-2/FR-2: post a progress update. Sent as multipart when files are attached
   * (repeated field `files`, ≤3), JSON otherwise. Returns the appended, server-
   * timestamped update; the server notifies the manager (FR-5) and HR if Blocked (BR-3).
   */
  addGoalUpdate(
    goalId: string,
    request: IAddGoalUpdateRequest,
    files?: readonly File[] | null,
  ): Observable<IGoalUpdate> {
    const url = `${this.baseUrl}/goals/${goalId}/progress`;
    if (files && files.length > 0) {
      const form = new FormData();
      form.append('progressPercent', String(request.progressPercent));
      form.append('status', request.status);
      form.append('notes', request.notes);
      for (const file of files) {
        form.append('files', file, file.name);
      }
      return this.http.post<IGoalUpdate>(url, form, { withCredentials: true });
    }
    return this.http.post<IGoalUpdate>(url, request, { withCredentials: true });
  }

  /**
   * FR-8: a manager/HR posts a comment on an employee's progress update. BUG-243:
   * the backend keys the comment thread by GOAL (`goals/{goalId}/comments`) and takes
   * the update id in the body as `progressUpdateId` — the caller must pass the goal id.
   */
  addComment(
    goalId: string,
    updateId: string,
    comment: string,
  ): Observable<IGoalComment> {
    return this.http.post<IGoalComment>(
      `${this.baseUrl}/goals/${goalId}/comments`,
      { progressUpdateId: updateId, body: comment },
      { withCredentials: true },
    );
  }

  /**
   * AC-4: the manager Team Goal Progress table (direct reports with overall
   * completion %, # goals at risk, last update). Tolerates a bare array or `{ data }`.
   */
  getTeamProgress(): Observable<ITeamGoalProgressRow[]> {
    return this.http
      .get<
        ITeamGoalProgressRow[] | { data: ITeamGoalProgressRow[] }
      >(`${this.baseUrl}/team-goals`, { withCredentials: true })
      .pipe(map((res) => (Array.isArray(res) ? res : (res?.data ?? []))));
  }

  /** AC-4 drill-down: one direct report's goals + progress (for the manager). */
  getEmployeeProgress(employeeId: string): Observable<IEmployeeGoalProgress> {
    return this.http.get<IEmployeeGoalProgress>(
      `${this.baseUrl}/team-goals/employees/${employeeId}`,
      { withCredentials: true },
    );
  }
}
