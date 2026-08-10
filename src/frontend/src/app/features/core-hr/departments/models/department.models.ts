import type { Schema } from '@core/api';

/**
 * US-CHR-004: Department models matching the backend API contract.
 *
 * Note on manager fields: manager_employee_id references the Employee entity
 * (US-CHR-001) which is not yet implemented. The frontend renders manager info
 * as read-only / empty until that feature lands.
 * TODO(US-CHR-001): Replace managerName with a proper IEmployeeSummary once
 * the Employees feature is available.
 */

/**
 * Department as the API actually returns it.
 *
 * GAP-014: was hand-written with `departmentId`, plus a `tenantId`, `managerName` and `employeeCount` the
 * API does not send at all — so every deactivate call put `undefined` in the URL, the list rendered
 * "undefined employees", and the manager line was permanently blank. The specs stayed green because they
 * mocked the invented shape. Now derived from the generated contract, so a rename is a compile error.
 *
 * `employeeCount` / `managerName` are NOT re-added as optional fields: a phantom field that is always
 * undefined is what produced the broken UI in the first place. The affected UI is removed here and the
 * backend gap is tracked as ISSUE-364 (both sibling DTOs — JobTitle and Location — already return a count,
 * so departments is the inconsistent one). Deactivation itself is safe regardless: DepartmentService
 * enforces both the active-children and active-employee guards server-side, so the warning was advisory.
 */
export type IDepartment = Schema<'DepartmentsDepartmentDto'> & {
  // Narrowed: the API always sends these, and the list cannot render a row without them.
  id: string;
  name: string;
  code: string;
};

/** Request payload for creating a department (FR-1) */
export interface ICreateDepartmentRequest {
  name: string;
  code: string;
  description?: string | null;
  parentDepartmentId?: string | null;
  /** TODO(US-CHR-001): Omitted until Employee entity exists */
  // managerEmployeeId?: string | null;
  isActive: boolean;
}

/** Request payload for updating a department (FR-1) */
export interface IUpdateDepartmentRequest {
  name: string;
  code: string;
  description?: string | null;
  parentDepartmentId?: string | null;
  /** TODO(US-CHR-001): Omitted until Employee entity exists */
  // managerEmployeeId?: string | null;
  isActive: boolean;
}

/** Hierarchical tree node for department tree view (FR-8) */
export interface IDepartmentTreeNode {
  department: IDepartment;
  children: IDepartmentTreeNode[];
  expanded: boolean;
  level: number;
}

/** Error response shape from the backend for department operations */
export interface IDepartmentErrorResponse {
  message: string;
  code?: 'duplicate_name' | 'circular_reference' | 'has_active_employees' | string;
  employeeCount?: number;
}
