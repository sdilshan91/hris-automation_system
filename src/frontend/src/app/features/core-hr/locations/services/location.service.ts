import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  ILocation,
  ICreateLocationRequest,
  IUpdateLocationRequest,
  LocationWire,
  mapLocation,
} from '../models/location.models';

/**
 * US-CHR-007: Service for location CRUD operations.
 *
 * All requests include withCredentials for httpOnly cookie auth and are
 * tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (assumed contract — backend agent building in parallel):
 *   GET    /api/v1/tenant/locations              - list all locations for current tenant
 *   GET    /api/v1/tenant/locations/:id          - single location
 *   POST   /api/v1/tenant/locations              - create location
 *   PUT    /api/v1/tenant/locations/:id          - update location
 *   POST   /api/v1/tenant/locations/:id/deactivate - soft-deactivate (FR-5)
 */
@Injectable({ providedIn: 'root' })
export class LocationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/locations`;

  // --- Read --------------------------------------------------

  /** Get all locations for the current tenant (FR-1, FR-7) */
  getLocations(activeOnly?: boolean): Observable<ILocation[]> {
    let params = new HttpParams();
    if (activeOnly !== undefined) {
      params = params.set('activeOnly', activeOnly.toString());
    }
    // IReadOnlyList<LocationsLocationDto> (bare array post-envelope); map each row through the mapper.
    return this.http
      .get<LocationWire[]>(this.baseUrl, { params, withCredentials: true })
      .pipe(map((rows) => rows.map(mapLocation)));
  }

  /** Get a single location by ID */
  getLocation(id: string): Observable<ILocation> {
    return this.http
      .get<LocationWire>(`${this.baseUrl}/${id}`, { withCredentials: true })
      .pipe(map(mapLocation));
  }

  // --- Write -------------------------------------------------

  /** Create a new location (FR-1, FR-2) */
  createLocation(request: ICreateLocationRequest): Observable<ILocation> {
    return this.http
      .post<LocationWire>(this.baseUrl, request, { withCredentials: true })
      .pipe(map(mapLocation));
  }

  /** Update an existing location (FR-1) */
  updateLocation(
    id: string,
    request: IUpdateLocationRequest
  ): Observable<ILocation> {
    return this.http
      .put<LocationWire>(`${this.baseUrl}/${id}`, request, {
        withCredentials: true,
      })
      .pipe(map(mapLocation));
  }

  /** Deactivate (soft-delete) a location (FR-5, FR-6) */
  deactivateLocation(id: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/${id}/deactivate`,
      null,
      { withCredentials: true }
    );
  }
}
