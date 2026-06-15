import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../environments/environment';
import {
  IConversionPrefill,
  IConvertEmployeeRequest,
  IConvertResult,
  IConversionErrorResponse,
} from '../models/conversion.models';

/**
 * US-REC-010: Service for converting an accepted applicant into a Core HR employee.
 *
 * Two calls back the flow: fetch the pre-filled employee data mapped from the
 * application + accepted offer (FR-2/FR-3), then post the HR Officer's reviewed
 * values to create the employee atomically (AC-2/AC-4/NFR-3).
 *
 * All requests use `withCredentials` for the httpOnly cookie and are tenant-scoped
 * via the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 /
 * NFR-2). Responses are the BARE `T` — the apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the envelope, so this service never touches `.data`.
 *
 * The backend DTO ↔ FE shape mapping lives in ONE place here (`mapPrefill` /
 * `mapResult`) so a field/path mismatch is a one-line fix.
 *
 * CONTRACT (PROPOSED — reconcile with the backend agent; `apiBaseUrl` already
 * includes `/api/v1`):
 *   GET  /recruitment/applicants/:applicantId/conversion-prefill -> IConversionPrefill
 *   POST /recruitment/applicants/:applicantId/convert body IConvertEmployeeRequest
 *        -> IConvertResult
 */
@Injectable({ providedIn: 'root' })
export class ConversionService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/recruitment/applicants`;

  /**
   * Fetch the pre-filled conversion form data for an applicant (FR-2/FR-3/AC-1).
   * The backend maps name/email/phone from the application and job title /
   * department / manager / salary / start date from the accepted offer, and
   * suggests an auto-generated employee number (FR-4).
   */
  getPrefill(applicantId: string): Observable<IConversionPrefill> {
    return this.http
      .get<unknown>(`${this.base}/${applicantId}/conversion-prefill`, {
        withCredentials: true,
      })
      .pipe(map((res) => this.mapPrefill(res)));
  }

  /**
   * Convert the applicant into an employee (AC-2/AC-4). Atomic server-side: creates
   * the employee, links the applicant, increments the vacancy filled count, and
   * optionally creates a user account (BR-7). Duplicate/plan-limit rejections come
   * back as HTTP errors with a typed `code` (BR-2/BR-3).
   */
  convert(
    applicantId: string,
    request: IConvertEmployeeRequest,
  ): Observable<IConvertResult> {
    // Map the FE field names to the backend ConvertApplicantRequest shape.
    const body = {
      jobTitleId: request.jobTitleId,
      departmentId: request.departmentId,
      // Core HR's FE EmploymentType union is hyphenated ('Full-Time') but the API enum is
      // PascalCase ('FullTime') — normalize here until US-PLT-003 reconciles core-hr enum casing.
      employmentType: request.employmentType.replace('-', ''),
      dateOfJoining: request.dateOfJoining,
      reportsToEmployeeId: request.reportingManagerId,
      locationId: request.workLocationId,
      employeeNo: request.employeeNumber,
    };
    return this.http
      .post<unknown>(`${this.base}/${applicantId}/convert`, body, {
        withCredentials: true,
      })
      .pipe(map((res) => this.mapResult(res)));
  }

  // ─── Mapping (ONE place — reconcile with backend here) ───

  private mapPrefill(res: unknown): IConversionPrefill {
    const src = (res ?? {}) as Partial<IConversionPrefill> & Record<string, unknown>;
    return {
      applicantId: src.applicantId ?? '',
      alreadyConverted: src.alreadyConverted ?? false,
      convertedToEmployeeId: src.convertedToEmployeeId ?? null,
      firstName: src.firstName ?? '',
      lastName: src.lastName ?? '',
      email: src.email ?? '',
      phone: src.phone ?? null,
      jobTitleId: src.jobTitleId ?? null,
      jobTitleName: src.jobTitleName ?? null,
      departmentId: src.departmentId ?? null,
      departmentName: src.departmentName ?? null,
      // Backend names: reportingManagerEmployeeId / startDate.
      reportingManagerId: src.reportingManagerId ?? (src['reportingManagerEmployeeId'] as string | null) ?? null,
      reportingManagerName: src.reportingManagerName ?? null,
      dateOfJoining: src.dateOfJoining ?? (src['startDate'] as string | null) ?? null,
      probationMonths: src.probationMonths ?? null,
      salaryAmount: src.salaryAmount ?? null,
      currency: src.currency ?? null,
      suggestedEmployeeNumber:
        src.suggestedEmployeeNumber ?? (src['employeeNo'] as string | null) ?? null,
      vacancyId: src.vacancyId ?? null,
      vacancyTitle: src.vacancyTitle ?? null,
      vacancyFilledCount: src.vacancyFilledCount ?? null,
      vacancyHeadcount: src.vacancyHeadcount ?? null,
    };
  }

  private mapResult(res: unknown): IConvertResult {
    const src = (res ?? {}) as Partial<IConvertResult> & Record<string, unknown>;
    return {
      employeeId: src.employeeId ?? '',
      employeeNumber: src.employeeNumber ?? (src['employeeNo'] as string) ?? '',
      firstName: src.firstName ?? '',
      lastName: src.lastName ?? '',
      userAccountCreated: src.userAccountCreated ?? false,
      vacancyFilledCount: src.vacancyFilledCount ?? null,
      vacancyHeadcount: src.vacancyHeadcount ?? null,
      vacancyClosed: src.vacancyClosed ?? false,
    };
  }

  // ─── Helpers ─────────────────────────────────────────────

  /** Parse an error body into the typed shape; the caller shows `message` verbatim. */
  static parseError(err: HttpErrorResponse): IConversionErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as IConversionErrorResponse;
    }
    return null;
  }

  /** Convenience: extract a human-readable message from an error response. */
  static parseErrorMessage(err: HttpErrorResponse): string {
    return (
      ConversionService.parseError(err)?.message ??
      'An unexpected error occurred.'
    );
  }
}
