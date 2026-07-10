import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ICourse,
  ICreateCourse,
  IUpdateCourse,
  IChangeCourseStatus,
  IEnrollRequest,
  ICompleteEnrollment,
  IEnrollment,
} from '../models/training.models';

/**
 * US-TRN-001: Service for the training catalog + enrollment lifecycle.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend contract (`api/v1/tenant/training`):
 *   GET    /courses                                  - list courses (visibility by permission)
 *   GET    /courses/:id                              - single course
 *   POST   /courses                                  - create Draft course (Manage)
 *   PUT    /courses/:id                              - update course metadata (Manage)
 *   POST   /courses/:id/status                       - change status (Manage)
 *   POST   /courses/:id/enrollments                  - enroll (self or, with Manage, another)
 *   POST   /enrollments/:id/cancel                   - cancel (own or Manage) → FIFO promotion
 *   POST   /enrollments/:id/complete                 - mark completed + cert/score (Manage)
 *   GET    /me/enrollments                           - current user's enrollments
 *   GET    /employees/:employeeId/training-history   - an employee's enrollment history
 */
@Injectable({ providedIn: 'root' })
export class TrainingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/training`;

  // --- Courses (read) ----------------------------------------

  /** List courses for the current tenant (AC-3 visibility applied server-side). */
  getCourses(): Observable<ICourse[]> {
    return this.http.get<ICourse[]>(`${this.baseUrl}/courses`, {
      withCredentials: true,
    });
  }

  /** Get a single course by ID. */
  getCourse(courseId: string): Observable<ICourse> {
    return this.http.get<ICourse>(`${this.baseUrl}/courses/${courseId}`, {
      withCredentials: true,
    });
  }

  // --- Courses (write, Manage) -------------------------------

  /** Create a new Draft course (AC-1). */
  createCourse(request: ICreateCourse): Observable<ICourse> {
    return this.http.post<ICourse>(`${this.baseUrl}/courses`, request, {
      withCredentials: true,
    });
  }

  /** Update an existing course's metadata. */
  updateCourse(courseId: string, request: IUpdateCourse): Observable<ICourse> {
    return this.http.put<ICourse>(
      `${this.baseUrl}/courses/${courseId}`,
      request,
      { withCredentials: true }
    );
  }

  /** Change a course's status (Draft→Open, Open→Closed/Cancelled/Completed, …) (AC-2). */
  changeCourseStatus(
    courseId: string,
    request: IChangeCourseStatus
  ): Observable<ICourse> {
    return this.http.post<ICourse>(
      `${this.baseUrl}/courses/${courseId}/status`,
      request,
      { withCredentials: true }
    );
  }

  // --- Enrollments -------------------------------------------

  /**
   * Enroll on a course (AC-4/AC-5). Null employeeId self-enrolls; a non-null
   * employeeId enrolls another employee (requires Manage).
   */
  enroll(courseId: string, request: IEnrollRequest): Observable<IEnrollment> {
    return this.http.post<IEnrollment>(
      `${this.baseUrl}/courses/${courseId}/enrollments`,
      request,
      { withCredentials: true }
    );
  }

  /** Cancel an enrollment (AC-7); frees a seat and promotes the FIFO head. */
  cancelEnrollment(enrollmentId: string): Observable<IEnrollment> {
    return this.http.post<IEnrollment>(
      `${this.baseUrl}/enrollments/${enrollmentId}/cancel`,
      null,
      { withCredentials: true }
    );
  }

  /** Mark an enrollment completed with optional certificate/score (AC-8, Manage). */
  completeEnrollment(
    enrollmentId: string,
    request: ICompleteEnrollment
  ): Observable<IEnrollment> {
    return this.http.post<IEnrollment>(
      `${this.baseUrl}/enrollments/${enrollmentId}/complete`,
      request,
      { withCredentials: true }
    );
  }

  // --- History -----------------------------------------------

  /** The current user's employee's enrollments (AC-9 self). */
  getMyEnrollments(): Observable<IEnrollment[]> {
    return this.http.get<IEnrollment[]>(`${this.baseUrl}/me/enrollments`, {
      withCredentials: true,
    });
  }

  /** An employee's training history (AC-9; Manage/View.All for others). */
  getTrainingHistory(employeeId: string): Observable<IEnrollment[]> {
    return this.http.get<IEnrollment[]>(
      `${this.baseUrl}/employees/${employeeId}/training-history`,
      { withCredentials: true }
    );
  }
}
