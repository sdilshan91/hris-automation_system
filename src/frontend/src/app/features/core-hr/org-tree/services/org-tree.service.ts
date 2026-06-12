import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import {
  IOrgTreeQueryParams,
  IOrgTreeResponse,
  IOrgTreeSearchResult,
} from '../models/org-tree.models';

/**
 * US-CHR-006: Service for Organization Tree API calls.
 *
 * All requests are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoint (assumed contract — backend building in parallel):
 *   GET /api/v1/org-tree?view=department|reporting&parentId=&depth=
 *   Returns: IOrgTreeNode[] (flat array of nodes for the requested subtree)
 *
 *   GET /api/v1/org-tree/search?q=&view=department|reporting
 *   Returns: IOrgTreeSearchResult[] (matching nodes with ancestor paths)
 */
@Injectable({ providedIn: 'root' })
export class OrgTreeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/org-tree`;

  /**
   * Fetch org tree nodes for the given view and optional subtree root.
   * When parentId is null, returns the top-level roots.
   * depth controls how many levels deep to fetch (default 2).
   */
  getOrgTree(params: IOrgTreeQueryParams): Observable<IOrgTreeResponse> {
    let httpParams = new HttpParams().set('view', params.view);

    if (params.parentId) {
      httpParams = httpParams.set('parentId', params.parentId);
    }
    if (params.depth != null) {
      httpParams = httpParams.set('depth', params.depth.toString());
    }

    return this.http.get<IOrgTreeResponse>(this.baseUrl, {
      params: httpParams,
      withCredentials: true,
    });
  }

  /**
   * Search for a node by name in the org tree.
   * Returns matching nodes with their ancestor paths for auto-expand.
   *
   * Endpoint assumption: GET /api/v1/org-tree/search?q=&view=
   * If the backend does not implement this endpoint yet, the component
   * falls back to client-side filtering of already-loaded nodes.
   */
  searchNodes(
    query: string,
    view: IOrgTreeQueryParams['view']
  ): Observable<IOrgTreeSearchResult[]> {
    const httpParams = new HttpParams()
      .set('q', query)
      .set('view', view);

    return this.http.get<IOrgTreeSearchResult[]>(`${this.baseUrl}/search`, {
      params: httpParams,
      withCredentials: true,
    });
  }
}
