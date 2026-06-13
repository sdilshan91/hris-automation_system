import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  ICustomFieldDefinition,
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
   * Response includes plan limits for the UI progress bar.
   */
  getCustomFields(entityType = 'employee'): Observable<ICustomFieldListResponse> {
    return this.http.get<ICustomFieldListResponse>(this.baseUrl, {
      params: { entityType },
      withCredentials: true,
    });
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
