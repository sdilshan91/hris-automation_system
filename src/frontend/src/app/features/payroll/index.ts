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

/** US-PAY-003: Run monthly payroll. */
export * from './models/payroll-run.models';
export * from './services/payroll-run.service';
export { PayrollRunsComponent } from './components/payroll-runs/payroll-runs.component';
export { NewPayrollRunComponent } from './components/new-payroll-run/new-payroll-run.component';
export { PayrollRunDetailComponent } from './components/payroll-run-detail/payroll-run-detail.component';

/** US-PAY-004: Generate individual payslips. */
export * from './models/payslip.models';
export * from './services/payslip.service';
export { PayslipListComponent } from './components/payslip-list/payslip-list.component';
