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

/** US-PAY-005: Employee self-service — view + download own payslips. */
export * from './models/my-payslip.models';
export * from './services/my-payslip.service';
export { MyPayslipsComponent } from './components/my-payslips/my-payslips.component';
export { MY_PAYSLIPS_ROUTES } from './my-payslips.routes';

/** US-PAY-006: Statutory deductions configuration (tax / PF / other). */
export * from './models/statutory.models';
export * from './services/statutory.service';
export { validateSlabs } from './services/slab-validation';
export { StatutoryConfigurationComponent } from './components/statutory-configuration/statutory-configuration.component';

/** US-PAY-007: Payroll adjustments (bonus / deduction / reimbursement / correction). */
export * from './models/adjustment.models';
export * from './services/adjustment.service';
export { PayrollAdjustmentsComponent } from './components/payroll-adjustments/payroll-adjustments.component';
export { AdjustmentFormComponent } from './components/adjustment-form/adjustment-form.component';
export { AdjustmentBulkUploadComponent } from './components/adjustment-bulk-upload/adjustment-bulk-upload.component';

/** US-PAY-008: Payroll approval workflow (submit / approve / reject / return / finalize). */
export * from './models/approval.models';
export * from './services/payroll-approval.service';
export { PendingApprovalsComponent } from './components/pending-approvals/pending-approvals.component';

/** US-PAY-009: Payroll reports & analytics (reports page + analytics dashboard). */
export * from './models/payroll-report.models';
export * from './services/payroll-report.service';
export { PayrollReportsComponent } from './components/payroll-reports/payroll-reports.component';
export { PayrollAnalyticsComponent } from './components/payroll-analytics/payroll-analytics.component';
