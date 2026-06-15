/** US-PAY-001: Payroll feature barrel export. */
export * from './models/payroll.models';
export * from './services/payroll.service';
export { PAYROLL_ROUTES } from './payroll.routes';
export { SalaryStructuresComponent } from './components/salary-structures/salary-structures.component';
export { SalaryComponentsComponent } from './components/salary-components/salary-components.component';
export { ComponentFormComponent } from './components/component-form/component-form.component';

/** US-PAY-002: Assign salary structure to employee. */
export * from './models/employee-salary.models';
export * from './services/employee-salary.service';
export { EmployeeCompensationComponent } from './components/employee-compensation/employee-compensation.component';
export { BulkSalaryAssignmentComponent } from './components/bulk-salary-assignment/bulk-salary-assignment.component';
