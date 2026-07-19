import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';
import {
  IOrgTreeNode,
  IOrgTreeQueryParams,
  IOrgTreeResult,
  IOrgTreeSearchResult,
} from '../models/org-tree.models';

/**
 * US-CHR-006: Service for Organization Tree API calls.
 *
 * All requests are tenant-scoped via the tenantInterceptor (X-Tenant-Subdomain header).
 *
 * Backend endpoints (verified contract):
 *   GET /api/v1/tenant/org-tree?view=department|reporting&parentId=&depth=
 *   Returns (after the ApiResponse envelope is stripped) an OBJECT:
 *     { nodes: IOrgTreeNode[], view, reportingViewAvailable }  — NOT a bare array.
 *     ISSUE-207: we project `.nodes` off it; consuming the object as an array made
 *     the tree builder throw and the page render "No departments found".
 *
 *   GET /api/v1/tenant/org-tree/search?q=&view=department|reporting
 *   Returns: IOrgTreeSearchResult[] (a list payload → bare array after envelope-stripping).
 */
@Injectable({ providedIn: 'root' })
export class OrgTreeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/tenant/org-tree`;

  /**
   * Fetch org tree nodes for the given view and optional subtree root.
   * When parentId is null, returns the top-level roots.
   * depth controls how many levels deep to fetch (default 2).
   */
  getOrgTree(params: IOrgTreeQueryParams): Observable<IOrgTreeNode[]> {
    let httpParams = new HttpParams().set('view', params.view);

    if (params.parentId) {
      httpParams = httpParams.set('parentId', params.parentId);
    }
    if (params.depth != null) {
      httpParams = httpParams.set('depth', params.depth.toString());
    }

    // ISSUE-207: the endpoint returns { nodes, view, reportingViewAvailable } — project the
    // root node list off it (a null/short payload degrades to []), never the raw object.
    // DF-17: each root keeps its nested `children` intact so the page can build the whole
    // delivered subtree without a per-expand round-trip.
    return this.http
      .get<IOrgTreeResult>(this.baseUrl, { params: httpParams, withCredentials: true })
      .pipe(map((result) => result?.nodes ?? []));
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
