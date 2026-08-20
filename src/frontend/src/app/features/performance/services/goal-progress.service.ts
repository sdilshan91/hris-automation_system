import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  GoalTimelineWire,
  IAddGoalUpdateRequest,
  IEmployeeGoalProgress,
  IGoalComment,
  IGoalUpdate,
  IMyGoals,
  ITeamGoalProgressRow,
  MyGoalProgressWire,
  TeamGoalRowWire,
  latestGoalComment,
  latestGoalUpdate,
  mapEmployeeGoalProgress,
  mapGoalTimeline,
  mapMyGoals,
  mapTeamGoalRow,
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
    // The wire is a FLAT goal list (PerformanceMyGoalProgressDto[]), not an envelope.
    return this.http
      .get<
        MyGoalProgressWire[] | { data: MyGoalProgressWire[] }
      >(`${this.baseUrl}/my-goals`, { withCredentials: true })
      .pipe(map((res) => mapMyGoals(this.toArray(res))));
  }

  /**
   * AC-3/FR-3: the append-only update history for one goal (chronological timeline +
   * per-update manager comment thread). The wire is a single `PerformanceGoalTimelineDto`
   * — the mapper unpacks its `updates[]` and regroups the timeline-level comments.
   */
  getGoalUpdates(goalId: string): Observable<IGoalUpdate[]> {
    return this.http
      .get<GoalTimelineWire>(`${this.baseUrl}/goals/${goalId}/timeline`, {
        withCredentials: true,
      })
      .pipe(map(mapGoalTimeline));
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
    // The POST returns the whole timeline; `latestGoalUpdate` picks the appended row.
    if (files && files.length > 0) {
      const form = new FormData();
      form.append('progressPercent', String(request.progressPercent));
      form.append('status', request.status);
      form.append('notes', request.notes);
      for (const file of files) {
        form.append('files', file, file.name);
      }
      return this.http
        .post<GoalTimelineWire>(url, form, { withCredentials: true })
        .pipe(map((w) => latestGoalUpdate(w, request)));
    }
    return this.http
      .post<GoalTimelineWire>(url, request, { withCredentials: true })
      .pipe(map((w) => latestGoalUpdate(w, request)));
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
    // The POST returns the whole timeline; `latestGoalComment` picks the new comment.
    return this.http
      .post<GoalTimelineWire>(
        `${this.baseUrl}/goals/${goalId}/comments`,
        { progressUpdateId: updateId, body: comment },
        { withCredentials: true },
      )
      .pipe(map((w) => latestGoalComment(w, updateId, comment)));
  }

  /**
   * AC-4: the manager Team Goal Progress table (direct reports with overall
   * completion %, # goals at risk, last update). Tolerates a bare array or `{ data }`.
   */
  getTeamProgress(): Observable<ITeamGoalProgressRow[]> {
    return this.http
      .get<
        TeamGoalRowWire[] | { data: TeamGoalRowWire[] }
      >(`${this.baseUrl}/team-goals`, { withCredentials: true })
      .pipe(map((res) => this.toArray(res).map(mapTeamGoalRow)));
  }

  /** AC-4 drill-down: one direct report's goals + progress (for the manager). */
  getEmployeeProgress(employeeId: string): Observable<IEmployeeGoalProgress> {
    // The wire is a FLAT goal list; the employee id is threaded from the caller.
    return this.http
      .get<
        MyGoalProgressWire[] | { data: MyGoalProgressWire[] }
      >(`${this.baseUrl}/team-goals/employees/${employeeId}`, {
        withCredentials: true,
      })
      .pipe(map((res) => mapEmployeeGoalProgress(this.toArray(res), employeeId)));
  }

  /** Accept either a bare array or a `{ data }` page; default to []. */
  private toArray<T>(res: T[] | { data: T[] } | null | undefined): T[] {
    if (Array.isArray(res)) {
      return res;
    }
    if (res && Array.isArray((res as { data: T[] }).data)) {
      return (res as { data: T[] }).data;
    }
    return [];
  }
}
