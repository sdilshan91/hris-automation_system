/**
 * US-CHR-004: Department models matching the backend API contract.
 *
 * Note on manager fields: manager_employee_id references the Employee entity
 * (US-CHR-001) which is not yet implemented. The frontend renders manager info
 * as read-only / empty until that feature lands.
 * TODO(US-CHR-001): Replace managerName with a proper IEmployeeSummary once
 * the Employees feature is available.
 */

/** Department entity returned by the API */
export interface IDepartment {
  departmentId: string;
  tenantId: string;
  name: string;
  description: string | null;
  parentDepartmentId: string | null;
  parentDepartmentName: string | null;
  /** TODO(US-CHR-001): Will be populated once Employee entity exists */
  managerEmployeeId: string | null;
  /** TODO(US-CHR-001): Will be populated once Employee entity exists */
  managerName: string | null;
  isActive: boolean;
  employeeCount: number;
  createdAt: string;
  updatedAt: string;
}

/** Request payload for creating a department (FR-1) */
export interface ICreateDepartmentRequest {
  name: string;
  description?: string | null;
  parentDepartmentId?: string | null;
  /** TODO(US-CHR-001): Omitted until Employee entity exists */
  // managerEmployeeId?: string | null;
  isActive: boolean;
}

/** Request payload for updating a department (FR-1) */
export interface IUpdateDepartmentRequest {
  name: string;
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
