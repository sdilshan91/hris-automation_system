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
   * DF-9: the backend contract for `options` is a JSON-encoded STRING on the wire in BOTH directions —
   * `CustomFieldDefinitionDto.Options` and `Create/UpdateCustomFieldCommand.Options` are `string?` (the raw
   * jsonb text, e.g. `'["S","M"]'`). The FE model exposes `options` as `string[] | null` for ergonomic
   * rendering (`@for (opt of cf.options)`), so this service normalizes at the wire boundary: parse the JSON
   * string → array when reading, stringify the array → JSON string when writing. Without this a
   * dropdown/multi_select field's options were iterated as the string's individual characters (garbage
   * options in the employee wizard, the profile edit, and the admin list), and admin create/update posted a
   * JSON array where the backend binds a `string?` (400 / rejected). Non-option field types have `options: null`.
   */
  private static parseOptions(raw: unknown): string[] | null {
    if (Array.isArray(raw)) return raw as string[]; // defensive: already normalized
    if (typeof raw === 'string' && raw.trim() !== '') {
      try {
        const parsed = JSON.parse(raw);
        return Array.isArray(parsed) ? (parsed as string[]) : null;
      } catch {
        return null;
      }
    }
    return null;
  }

  private static normalizeDefinition(def: ICustomFieldDefinition): ICustomFieldDefinition {
    return { ...def, options: CustomFieldService.parseOptions(def.options) };
  }

  private static serializeOptions(options: string[] | null | undefined): string | null {
    return options && options.length > 0 ? JSON.stringify(options) : null;
  }

  /** Build the wire payload for a create/update, stringifying `options` to the backend's `string?` contract. */
  private static toWirePayload<T extends { options: string[] | null }>(request: T): Omit<T, 'options'> & { options: string | null } {
    return { ...request, options: CustomFieldService.serializeOptions(request.options) };
  }

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
      definitions: (group.fields ?? []).map((d) => CustomFieldService.normalizeDefinition(d)),
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
    return this.http
      .get<ICustomFieldDefinition[]>(`${this.baseUrl}/active`, {
        params: { entityType },
        withCredentials: true,
      })
      .pipe(map((defs) => (defs ?? []).map((d) => CustomFieldService.normalizeDefinition(d))));
  }

  /**
   * Create a new custom field definition.
   * Backend returns 409/403 with plan_limit_exceeded code when limit is reached (AC-4).
   */
  createCustomField(request: ICreateCustomFieldRequest): Observable<ICustomFieldDefinition> {
    return this.http
      .post<ICustomFieldDefinition>(this.baseUrl, CustomFieldService.toWirePayload(request), {
        withCredentials: true,
      })
      .pipe(map((def) => CustomFieldService.normalizeDefinition(def)));
  }

  /**
   * Update an existing custom field definition (name, required, options, order).
   * Field type and key are immutable after creation (BR-5).
   */
  updateCustomField(
    id: string,
    request: IUpdateCustomFieldRequest
  ): Observable<ICustomFieldDefinition> {
    return this.http
      .put<ICustomFieldDefinition>(`${this.baseUrl}/${id}`, CustomFieldService.toWirePayload(request), {
        withCredentials: true,
      })
      .pipe(map((def) => CustomFieldService.normalizeDefinition(def)));
  }

  /** Deactivate a custom field (hide from forms, preserve data) (AC-5). */
  deactivateCustomField(id: string): Observable<ICustomFieldDefinition> {
    return this.http
      .post<ICustomFieldDefinition>(`${this.baseUrl}/${id}/deactivate`, {}, { withCredentials: true })
      .pipe(map((def) => CustomFieldService.normalizeDefinition(def)));
  }

  /** Reactivate a previously deactivated custom field (AC-5). */
  activateCustomField(id: string): Observable<ICustomFieldDefinition> {
    return this.http
      .post<ICustomFieldDefinition>(`${this.baseUrl}/${id}/activate`, {}, { withCredentials: true })
      .pipe(map((def) => CustomFieldService.normalizeDefinition(def)));
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
