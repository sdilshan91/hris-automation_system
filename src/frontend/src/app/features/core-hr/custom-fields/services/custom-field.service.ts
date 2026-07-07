import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  ICustomFieldDefinition,
  ICustomFieldListResult,
  ICustomFieldListResponse,
  ICreateCustomFieldRequest,
  IUpdateCustomFieldRequest,
  IReorderCustomFieldsRequest,
  ICustomFieldErrorResponse,
} from '../models/custom-field.models';

/**
 * US-CHR-012: Service for custom field definition CRUD + reorder.
 *
 * Backend endpoints (assumed contract — backend agent building in parallel):
 *   GET    /api/v1/tenant/custom-fields?entityType=employee  - list definitions + plan limits
 *   POST   /api/v1/tenant/custom-fields                      - create definition
 *   PUT    /api/v1/tenant/custom-fields/:id                  - update definition
 *   POST   /api/v1/tenant/custom-fields/:id/deactivate       - deactivate (toggle off)
 *   POST   /api/v1/tenant/custom-fields/:id/activate         - activate (toggle on)
 *   POST   /api/v1/tenant/custom-fields/reorder              - reorder display_order
 */
@Injectable({ providedIn: 'root' })
export class CustomFieldService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/custom-fields`;

  /**
   * List custom field definitions for the current tenant, scoped to entity type.
   *
   * The backend returns an ARRAY of `CustomFieldDefinitionListResult` grouped by entity
   * type (`{ entityType, fields[], totalCount, maxAllowed }`). We pick the group for the
   * requested `entityType` (Phase 1 is employee-only) and map it to the FE-facing
   * `{ definitions, planLimits }` shape the UI consumes (definitions come from `fields`,
   * and the plan-limit progress bar from `totalCount` / `maxAllowed`).
   */
  getCustomFields(entityType = 'employee'): Observable<ICustomFieldListResponse> {
    return this.http
      .get<ICustomFieldListResult[]>(this.baseUrl, {
        params: { entityType },
        withCredentials: true,
      })
      .pipe(map((results) => this.toListResponse(results, entityType)));
  }

  /**
   * Map the backend grouped-array result to the FE-facing list response. Falls back to
   * an empty group (no fields, unlimited plan) when the entity type isn't present.
   */
  private toListResponse(
    results: ICustomFieldListResult[] | null | undefined,
    entityType: string,
  ): ICustomFieldListResponse {
    const group =
      results?.find((r) => r.entityType === entityType) ?? results?.[0];
    if (!group) {
      return { definitions: [], planLimits: { currentCount: 0, maxAllowed: null } };
    }
    return {
      definitions: group.fields ?? [],
      planLimits: {
        currentCount: group.totalCount,
        maxAllowed: group.maxAllowed,
      },
    };
  }

  /**
   * List only active custom field definitions (for rendering on forms).
   * Returns definitions sorted by display_order.
   */
  getActiveCustomFields(entityType = 'employee'): Observable<ICustomFieldDefinition[]> {
    return this.http.get<ICustomFieldDefinition[]>(`${this.baseUrl}/active`, {
      params: { entityType },
      withCredentials: true,
    });
  }

  /**
   * Create a new custom field definition.
   * Backend returns 409/403 with plan_limit_exceeded code when limit is reached (AC-4).
   */
  createCustomField(request: ICreateCustomFieldRequest): Observable<ICustomFieldDefinition> {
    return this.http.post<ICustomFieldDefinition>(this.baseUrl, request, {
      withCredentials: true,
    });
  }

  /**
   * Update an existing custom field definition (name, required, options, order).
   * Field type and key are immutable after creation (BR-5).
   */
  updateCustomField(
    id: string,
    request: IUpdateCustomFieldRequest
  ): Observable<ICustomFieldDefinition> {
    return this.http.put<ICustomFieldDefinition>(`${this.baseUrl}/${id}`, request, {
      withCredentials: true,
    });
  }

  /** Deactivate a custom field (hide from forms, preserve data) (AC-5). */
  deactivateCustomField(id: string): Observable<ICustomFieldDefinition> {
    return this.http.post<ICustomFieldDefinition>(
      `${this.baseUrl}/${id}/deactivate`,
      {},
      { withCredentials: true }
    );
  }

  /** Reactivate a previously deactivated custom field (AC-5). */
  activateCustomField(id: string): Observable<ICustomFieldDefinition> {
    return this.http.post<ICustomFieldDefinition>(
      `${this.baseUrl}/${id}/activate`,
      {},
      { withCredentials: true }
    );
  }

  /** Reorder custom field display order (FR-8). */
  reorderCustomFields(request: IReorderCustomFieldsRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/reorder`, request, {
      withCredentials: true,
    });
  }

  /** Parse an error response into a typed custom field error. */
  static parseError(err: HttpErrorResponse): ICustomFieldErrorResponse | null {
    const body = err.error;
    if (body && typeof body === 'object' && 'message' in body) {
      return body as ICustomFieldErrorResponse;
    }
    return null;
  }
}
