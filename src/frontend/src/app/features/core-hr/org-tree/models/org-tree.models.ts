import type { Schema } from '@core/api';

/**
 * US-CHR-006: Organization Tree / Hierarchy Visualization models.
 *
 * Data Requirements (Section 7):
 *   - node_id: uuid (department_id or employee_id)
 *   - node_type: "department" | "employee"
 *   - name: string
 *   - title: string (job title for employee nodes)
 *   - avatar_url: string (for employee nodes)
 *   - employee_count: number (for department nodes)
 *   - children_count: number
 *   - parent_id: uuid | null
 *   - is_expanded: boolean (client-side state only)
 *
 * API endpoint: GET /api/v1/org-tree?view=department|reporting&parentId=&depth=
 */

// ─── View type ───────────────────────────────────────────────

export type OrgTreeView = 'department' | 'reporting';

// ─── Node from API ───────────────────────────────────────────

/** A single node returned by the org-tree API. */
export interface IOrgTreeNode {
  nodeId: string;
  nodeType: 'department' | 'employee';
  name: string;
  title: string | null;
  avatarUrl: string | null;
  employeeCount: number;
  childrenCount: number;
  parentId: string | null;
  /**
   * DF-17: the API nests direct children inline down to the requested `depth`
   * (default 2). `childrenCount` is always the true direct-child count even when
   * `children` is truncated at the depth limit — so a node with `childrenCount > 0`
   * but no `children` was cut off by depth and must be lazy-fetched on expand.
   */
  children?: IOrgTreeNode[];
}

// ─── Client-side tree node (adds UI state) ───────────────────

/** Extended node with client-side expansion state and children. */
export interface IOrgTreeNodeState {
  node: IOrgTreeNode;
  children: IOrgTreeNodeState[];
  expanded: boolean;
  /** Whether children have been fetched from the API. */
  childrenLoaded: boolean;
  /** Loading state for lazy-fetched children. */
  loadingChildren: boolean;
  /** Depth level in the tree (0 = root). */
  level: number;
  /** Whether this node is highlighted by search. */
  highlighted: boolean;
}

// ─── API query params ────────────────────────────────────────

export interface IOrgTreeQueryParams {
  view: OrgTreeView;
  parentId?: string | null;
  depth?: number;
}

// ─── API response ────────────────────────────────────────────

/**
 * The org-tree GET endpoint returns an OBJECT payload (after the global ApiResponse
 * envelope is stripped): `{ nodes, view, reportingViewAvailable }` — NOT a bare array.
 * ISSUE-207: the service consumed this object as an array, so the tree builder threw a
 * TypeError and the page showed the empty state. The service now projects `.nodes` off
 * this result. `nodes` holds the ROOT nodes, each carrying its own nested `children`
 * array down to the requested `depth` (DF-17) — the tree is built directly from that
 * nesting via {@link buildTreeFromNested}, not re-derived from flat parentId links.
 */
export interface IOrgTreeResult {
  nodes: IOrgTreeNode[];
  view: OrgTreeView;
  reportingViewAvailable: boolean;
}

/** The nodes the service exposes after projecting `.nodes` off {@link IOrgTreeResult}. */
export type IOrgTreeResponse = IOrgTreeNode[];

// ─── Wire contract → view-model mappers (D-core-hr slice 2) ───────────────────
//
// The org-tree GET used a HAND-WRITTEN `IOrgTreeResult`/`IOrgTreeNode` guess. The service now types the
// response as the GENERATED `OrganizationTreeOrgTreeResult` and maps each node, so a backend rename is a
// compile error. Two wire fields need normalising at the seam:
//   - `nodeType` is `string | null` on the wire but the union `'department' | 'employee'` in the VM.
//   - `employeeCount` is `number | null` on the wire but `number` in the VM (a null count would otherwise
//     leak into the "N employees" label and the tree math).
// DF-17 truncation semantics are preserved: a wire node with NO `children` key maps to `children: undefined`
// (a depth-truncated node the page must lazy-fetch), while a delivered empty `[]` stays `[]`.

export type OrgTreeResultWire = Schema<'OrganizationTreeOrgTreeResult'>;
export type OrgTreeNodeWire = Schema<'OrganizationTreeOrgTreeNodeDto'>;

/** Map a single wire org-tree node (recursively, over any inline `children`) onto {@link IOrgTreeNode}. */
export function mapOrgTreeNode(w: OrgTreeNodeWire): IOrgTreeNode {
  return {
    nodeId: w.nodeId ?? '',
    nodeType: (w.nodeType ?? 'department') as IOrgTreeNode['nodeType'],
    name: w.name ?? '',
    title: w.title ?? null,
    avatarUrl: w.avatarUrl ?? null,
    employeeCount: w.employeeCount ?? 0,
    childrenCount: w.childrenCount ?? 0,
    parentId: w.parentId ?? null,
    children: w.children == null ? undefined : w.children.map(mapOrgTreeNode),
  };
}

/**
 * Project the ROOT node list off the wire result and map each (ISSUE-207: the payload is the object
 * `{ nodes, view, reportingViewAvailable }`, never a bare array). A null/short payload degrades to `[]`.
 */
export function mapOrgTreeResult(w: OrgTreeResultWire | null | undefined): IOrgTreeNode[] {
  return (w?.nodes ?? []).map(mapOrgTreeNode);
}

// ─── Search result ───────────────────────────────────────────

export interface IOrgTreeSearchResult {
  node: IOrgTreeNode;
  /** Path of ancestor node IDs from root to this node (for auto-expand). */
  ancestorPath: string[];
}

// ─── Detail panel data ───────────────────────────────────────

export interface IOrgNodeDetail {
  node: IOrgTreeNode;
  manager: IOrgTreeNode | null;
  directReports: IOrgTreeNode[];
  subDepartments: IOrgTreeNode[];
}

// ─── Helpers ─────────────────────────────────────────────────

/** Build an IOrgTreeNodeState from an API node at a given level. */
export function createNodeState(
  node: IOrgTreeNode,
  level: number,
  children: IOrgTreeNodeState[] = [],
  childrenLoaded = false
): IOrgTreeNodeState {
  return {
    node,
    children,
    expanded: false,
    childrenLoaded,
    loadingChildren: false,
    level,
    highlighted: false,
  };
}

/**
 * DF-17: Build a tree directly from the API's nested `children` payload.
 *
 * The org-tree endpoint delivers root nodes each carrying their own `children`
 * recursively down to the requested `depth`, so we map that nesting straight into
 * client tree state — no per-expand round-trip is needed within the delivered depth.
 *
 * `childrenLoaded` is true when children were delivered inline OR the node is a leaf
 * (`childrenCount === 0`). A node with `childrenCount > 0` but no delivered `children`
 * was truncated at the depth limit and stays `childrenLoaded === false` so the page can
 * lazy-fetch that subtree on expand.
 */
export function buildTreeFromNested(
  nodes: IOrgTreeNode[],
  baseLevel: number = 0
): IOrgTreeNodeState[] {
  return nodes.map((n) => {
    const nested = n.children ?? [];
    const childStates = buildTreeFromNested(nested, baseLevel + 1);
    const childrenLoaded = nested.length > 0 || n.childrenCount === 0;
    return createNodeState(n, baseLevel, childStates, childrenLoaded);
  });
}

/**
 * Build a tree from a flat node array using parentId references.
 * Assumes nodes are returned for contiguous levels (e.g., depth 0-1).
 *
 * Retained for callers that still receive a flat payload; the primary org-tree load
 * path now consumes the API's nested `children` via {@link buildTreeFromNested}.
 */
export function buildTreeFromFlat(
  nodes: IOrgTreeNode[],
  baseLevel: number = 0
): IOrgTreeNodeState[] {
  const childrenMap = new Map<string | null, IOrgTreeNode[]>();

  for (const n of nodes) {
    const pid = n.parentId;
    if (!childrenMap.has(pid)) {
      childrenMap.set(pid, []);
    }
    childrenMap.get(pid)!.push(n);
  }

  // Determine roots: nodes whose parentId is null or whose parent is not in the set
  const nodeIdSet = new Set(nodes.map((n) => n.nodeId));

  const buildLevel = (
    parentId: string | null,
    level: number
  ): IOrgTreeNodeState[] => {
    const children = childrenMap.get(parentId) ?? [];
    return children.map((n) => {
      const subChildren = buildLevel(n.nodeId, level + 1);
      return createNodeState(
        n,
        level,
        subChildren,
        subChildren.length > 0 || n.childrenCount === 0
      );
    });
  };

  // Find root nodes: parentId is null or parent not in the node set
  const roots = nodes.filter(
    (n) => n.parentId === null || !nodeIdSet.has(n.parentId)
  );
  const rootParentIds = new Set(roots.map((n) => n.parentId));

  const result: IOrgTreeNodeState[] = [];
  for (const pid of rootParentIds) {
    result.push(...buildLevel(pid, baseLevel));
  }
  return result;
}

/**
 * Find a node in the tree by nodeId (depth-first search).
 */
export function findNodeInTree(
  roots: IOrgTreeNodeState[],
  nodeId: string
): IOrgTreeNodeState | null {
  for (const root of roots) {
    if (root.node.nodeId === nodeId) return root;
    const found = findNodeInTree(root.children, nodeId);
    if (found) return found;
  }
  return null;
}

/**
 * Collect all node IDs along the path from root to target.
 * Returns the path (inclusive of target) or empty array if not found.
 */
export function findPathToNode(
  roots: IOrgTreeNodeState[],
  targetId: string,
  currentPath: string[] = []
): string[] {
  for (const root of roots) {
    const pathWithCurrent = [...currentPath, root.node.nodeId];
    if (root.node.nodeId === targetId) return pathWithCurrent;
    const found = findPathToNode(root.children, targetId, pathWithCurrent);
    if (found.length > 0) return found;
  }
  return [];
}
