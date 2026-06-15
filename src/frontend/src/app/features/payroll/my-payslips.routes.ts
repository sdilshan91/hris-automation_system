import { Routes } from '@angular/router';

/**
 * US-PAY-005: Employee self-service "My Payslips" route.
 *
 * Lazy-loaded under the 'my-payslips' path in app.routes.ts. This is the EMPLOYEE
 * persona view — distinct from the HR-facing '/payroll' config/run screens (US-PAY-001..004)
 * which are guarded by ['Tenant Admin', 'HR Officer'] and require Payroll.View. The
 * parent '/my-payslips' route uses a permissive role guard so any authenticated
 * employee can reach their own payslips; the backend enforces the registered
 * `Payroll.View.Own` permission (the story names `Payroll.Read.Self`, but that string
 * isn't in the catalog — `Payroll.View.Own` is the registered self permission and the
 * actual gate). An employee only ever sees their OWN data — AC-4, BR-5.
 */
export const MY_PAYSLIPS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/my-payslips/my-payslips.component').then(
        (m) => m.MyPayslipsComponent,
      ),
  },
];
