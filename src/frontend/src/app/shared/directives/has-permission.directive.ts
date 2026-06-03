import {
  Directive,
  inject,
  input,
  effect,
  TemplateRef,
  ViewContainerRef,
} from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Structural directive that conditionally renders a template
 * based on whether the current user has the required permission(s).
 *
 * Usage:
 *   <button *appHasPermission="'Admin.Roles.Manage'">Edit Role</button>
 *   <div *appHasPermission="['Leave.View', 'Leave.Apply']">...</div>
 *
 * When an array is passed, the user needs ANY of the listed permissions.
 */
@Directive({
  selector: '[appHasPermission]',
  standalone: true,
})
export class HasPermissionDirective {
  private readonly authService = inject(AuthService);
  private readonly templateRef = inject(TemplateRef<unknown>);
  private readonly viewContainer = inject(ViewContainerRef);

  /** The permission key or array of keys to check */
  readonly appHasPermission = input.required<string | string[]>();

  private hasView = false;

  constructor() {
    effect(() => {
      const required = this.appHasPermission();
      const perms = Array.isArray(required) ? required : [required];
      // Read the permissions signal directly so the effect re-runs on changes
      const currentPerms = this.authService.permissions();
      const allowed = perms.some((p) => currentPerms.includes(p));

      if (allowed && !this.hasView) {
        this.viewContainer.createEmbeddedView(this.templateRef);
        this.hasView = true;
      } else if (!allowed && this.hasView) {
        this.viewContainer.clear();
        this.hasView = false;
      }
    });
  }
}
