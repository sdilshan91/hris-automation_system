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

/** The API returns a flat array of nodes for the requested subtree. */
export type IOrgTreeResponse = IOrgTreeNode[];

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
 * Build a tree from a flat node array using parentId references.
 * Assumes nodes are returned for contiguous levels (e.g., depth 0-1).
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
