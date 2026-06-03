import { Routes } from '@angular/router';
import { permissionGuard } from '../../../core/auth/auth.guard';

export const ROLES_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./components/role-list/role-list.component').then(
        (m) => m.RoleListComponent
      ),
  },
  {
    path: 'create',
    loadComponent: () =>
      import('./components/role-form/role-form.component').then(
        (m) => m.RoleFormComponent
      ),
    canActivate: [permissionGuard(['Admin.Roles.Manage'])],
  },
  {
    path: 'users/:userId',
    loadComponent: () =>
      import(
        './components/user-role-assignment/user-role-assignment.component'
      ).then((m) => m.UserRoleAssignmentComponent),
    canActivate: [permissionGuard(['Admin.Roles.Manage'])],
  },
  {
    path: ':id',
    loadComponent: () =>
      import('./components/role-detail/role-detail.component').then(
        (m) => m.RoleDetailComponent
      ),
  },
  {
    path: ':id/edit',
    loadComponent: () =>
      import('./components/role-form/role-form.component').then(
        (m) => m.RoleFormComponent
      ),
    canActivate: [permissionGuard(['Admin.Roles.Manage'])],
  },
];
