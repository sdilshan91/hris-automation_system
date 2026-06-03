import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  AbstractControl,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { RolesService } from '../../services/roles.service';
import { IRole, IPermissionGroup } from '../../models/role.models';
import { PERMISSION_CATALOG } from '../../models/permission-catalog';

@Component({
  selector: 'app-role-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('expandCollapse', [
      transition(':enter', [
        style({ height: '0', opacity: 0, overflow: 'hidden' }),
        animate('200ms ease-out', style({ height: '*', opacity: 1 })),
      ]),
      transition(':leave', [
        style({ height: '*', opacity: 1, overflow: 'hidden' }),
        animate('200ms ease-in', style({ height: '0', opacity: 0 })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Back link -->
      <a
        routerLink="/admin/roles"
        class="inline-flex items-center gap-1.5 text-sm text-neutral-500 hover:text-neutral-700 transition-colors mb-6"
      >
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4">
          <path fill-rule="evenodd" d="M9.78 4.22a.75.75 0 0 1 0 1.06L7.06 8l2.72 2.72a.75.75 0 1 1-1.06 1.06L5.47 8.53a.75.75 0 0 1 0-1.06l3.25-3.25a.75.75 0 0 1 1.06 0Z" clip-rule="evenodd" />
        </svg>
        Back to Roles
      </a>

      <!-- Loading -->
      @if (isLoading()) {
        <div class="card-notion animate-pulse">
          <div class="h-6 bg-neutral-100 rounded w-1/3 mb-4"></div>
          <div class="h-10 bg-neutral-50 rounded mb-3"></div>
          <div class="h-10 bg-neutral-50 rounded mb-3"></div>
          <div class="h-32 bg-neutral-50 rounded"></div>
        </div>
      }

      @if (!isLoading()) {
        <form [formGroup]="roleForm" (ngSubmit)="onSubmit()">
          <!-- Header -->
          <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
            <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
              {{ isEditMode() ? 'Edit Role' : 'Create Role' }}
            </h1>
            <div class="flex items-center gap-3">
              <a routerLink="/admin/roles" class="btn-secondary">
                Cancel
              </a>
              <button
                type="submit"
                class="btn-primary"
                [disabled]="isSaving() || roleForm.invalid"
              >
                @if (isSaving()) {
                  <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                  Saving...
                } @else {
                  {{ isEditMode() ? 'Save Changes' : 'Create Role' }}
                }
              </button>
            </div>
          </div>

          <!-- Role info card -->
          <div class="card-notion mb-6">
            <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider mb-4">
              Role Details
            </h2>
            <div class="space-y-4">
              <!-- Name field -->
              <div>
                <label for="roleName" class="label-notion">Role Name</label>
                <input
                  id="roleName"
                  type="text"
                  formControlName="name"
                  class="input-notion"
                  placeholder="e.g. HR Coordinator"
                  autocomplete="off"
                />
                @if (roleForm.get('name')?.touched && roleForm.get('name')?.errors) {
                  <p class="text-xs text-red-500 mt-1">
                    @if (roleForm.get('name')?.errors?.['required']) {
                      Role name is required.
                    } @else if (roleForm.get('name')?.errors?.['minlength']) {
                      Role name must be at least 2 characters.
                    } @else if (roleForm.get('name')?.errors?.['maxlength']) {
                      Role name must be at most 100 characters.
                    }
                  </p>
                }
              </div>

              <!-- Description field -->
              <div>
                <label for="roleDescription" class="label-notion">Description</label>
                <textarea
                  id="roleDescription"
                  formControlName="description"
                  class="input-notion resize-none"
                  rows="3"
                  placeholder="Describe what this role is for..."
                ></textarea>
                @if (roleForm.get('description')?.touched && roleForm.get('description')?.errors) {
                  <p class="text-xs text-red-500 mt-1">
                    @if (roleForm.get('description')?.errors?.['maxlength']) {
                      Description must be at most 500 characters.
                    }
                  </p>
                }
              </div>
            </div>
          </div>

          <!-- Permissions tree -->
          <div class="mb-6">
            <div class="flex items-center justify-between mb-3">
              <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider">
                Permissions
              </h2>
              <span class="text-xs text-neutral-400">
                {{ selectedPermissionCount() }} selected
              </span>
            </div>

            <div class="space-y-3">
              @for (group of permissionCatalog; track group.module) {
                <div class="card-notion !p-0 overflow-hidden">
                  <!-- Module header -->
                  <div class="flex items-center justify-between px-5 py-3.5 hover:bg-neutral-50 transition-colors">
                    <button
                      type="button"
                      class="flex-1 flex items-center gap-3 text-left"
                      (click)="toggleGroup(group.module)"
                      [attr.aria-expanded]="isGroupExpanded(group.module)"
                    >
                      <svg
                        xmlns="http://www.w3.org/2000/svg"
                        viewBox="0 0 16 16"
                        fill="currentColor"
                        class="w-4 h-4 text-neutral-400 transition-transform duration-200"
                        [class.rotate-180]="isGroupExpanded(group.module)"
                      >
                        <path fill-rule="evenodd" d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd" />
                      </svg>
                      <span class="text-sm font-medium text-neutral-900">
                        {{ group.label }}
                      </span>
                      <span class="text-xs text-neutral-400">
                        {{ getGroupSelectedCount(group) }}/{{ group.permissions.length }}
                      </span>
                    </button>

                    <!-- Select all / Deselect all for module -->
                    <label class="flex items-center gap-2 cursor-pointer px-2">
                      <span class="text-xs text-neutral-400">All</span>
                      <input
                        type="checkbox"
                        class="permission-checkbox"
                        [checked]="isGroupFullySelected(group)"
                        [indeterminate]="isGroupPartiallySelected(group)"
                        (change)="toggleGroupPermissions(group, $event)"
                      />
                    </label>
                  </div>

                  <!-- Permission items -->
                  @if (isGroupExpanded(group.module)) {
                    <div
                      @expandCollapse
                      class="border-t border-neutral-100 px-5 py-3 space-y-1"
                    >
                      @for (perm of group.permissions; track perm.key) {
                        <label
                          class="flex items-start gap-3 py-2 px-2 rounded-lg cursor-pointer hover:bg-neutral-50 transition-colors"
                        >
                          <input
                            type="checkbox"
                            class="permission-checkbox mt-0.5"
                            [checked]="isPermissionSelected(perm.key)"
                            (change)="togglePermission(perm.key, $event)"
                          />
                          <div class="min-w-0 flex-1">
                            <div class="text-sm text-neutral-800">
                              {{ perm.label }}
                            </div>
                            <div class="text-xs text-neutral-400 mt-0.5">
                              {{ perm.description }}
                            </div>
                          </div>
                          <span class="text-[10px] text-neutral-300 font-mono hidden sm:block flex-shrink-0 mt-0.5">
                            {{ perm.key }}
                          </span>
                        </label>
                      }
                    </div>
                  }
                </div>
              }
            </div>

            @if (roleForm.get('permissions')?.touched && roleForm.get('permissions')?.errors) {
              <p class="text-xs text-red-500 mt-2">
                At least one permission must be selected.
              </p>
            }
          </div>
        </form>
      }
    </div>
  `,
  styles: [`
    .permission-checkbox {
      @apply w-4 h-4 rounded border-neutral-300 text-brand-600
        focus:ring-brand-500 focus:ring-2 cursor-pointer;
      accent-color: var(--brand-primary);
    }
  `],
})
export class RoleFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly rolesService = inject(RolesService);
  private readonly toastr = inject(ToastrService);

  readonly permissionCatalog = PERMISSION_CATALOG;

  readonly isEditMode = signal(false);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly expandedGroups = signal<Set<string>>(new Set());
  readonly selectedPermissions = signal<Set<string>>(new Set());

  readonly selectedPermissionCount = computed(() => this.selectedPermissions().size);

  private roleId: string | null = null;

  roleForm!: FormGroup;

  ngOnInit(): void {
    this.roleForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
      description: ['', [Validators.maxLength(500)]],
      permissions: [[] as string[], [this.permissionsValidator]],
    });

    this.roleId = this.route.snapshot.paramMap.get('id');
    if (this.roleId) {
      this.isEditMode.set(true);
      this.loadRole(this.roleId);
    }
  }

  private loadRole(roleId: string): void {
    this.isLoading.set(true);
    this.rolesService.getRole(roleId).subscribe({
      next: (role: IRole) => {
        this.roleForm.patchValue({
          name: role.name,
          description: role.description,
        });
        const perms = new Set(role.permissions);
        this.selectedPermissions.set(perms);
        this.roleForm.get('permissions')?.setValue([...perms]);

        // Auto-expand groups with selected permissions
        const expanded = new Set<string>();
        for (const group of this.permissionCatalog) {
          if (group.permissions.some((p) => perms.has(p.key))) {
            expanded.add(group.module);
          }
        }
        this.expandedGroups.set(expanded);
        this.isLoading.set(false);
      },
      error: () => {
        this.toastr.error('Failed to load role.');
        this.router.navigate(['/admin/roles']);
      },
    });
  }

  onSubmit(): void {
    // Mark all as touched so validation errors show
    this.roleForm.markAllAsTouched();
    if (this.roleForm.invalid) return;

    this.isSaving.set(true);
    const { name, description } = this.roleForm.value;
    const permissions = [...this.selectedPermissions()];

    if (this.isEditMode() && this.roleId) {
      this.rolesService
        .updateRole(this.roleId, { name, description, permissions })
        .subscribe({
          next: () => {
            this.toastr.success(`Role "${name}" updated successfully.`);
            this.router.navigate(['/admin/roles']);
          },
          error: () => {
            this.isSaving.set(false);
          },
        });
    } else {
      this.rolesService
        .createRole({ name, description, permissions })
        .subscribe({
          next: () => {
            this.toastr.success(`Role "${name}" created successfully.`);
            this.router.navigate(['/admin/roles']);
          },
          error: () => {
            this.isSaving.set(false);
          },
        });
    }
  }

  // ─── Permission Tree Helpers ─────────────────────────────

  toggleGroup(module: string): void {
    this.expandedGroups.update((current) => {
      const next = new Set(current);
      if (next.has(module)) {
        next.delete(module);
      } else {
        next.add(module);
      }
      return next;
    });
  }

  isGroupExpanded(module: string): boolean {
    return this.expandedGroups().has(module);
  }

  isPermissionSelected(key: string): boolean {
    return this.selectedPermissions().has(key);
  }

  togglePermission(key: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedPermissions.update((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(key);
      } else {
        next.delete(key);
      }
      return next;
    });
    this.syncPermissionsFormControl();
  }

  isGroupFullySelected(group: IPermissionGroup): boolean {
    return group.permissions.every((p) => this.selectedPermissions().has(p.key));
  }

  isGroupPartiallySelected(group: IPermissionGroup): boolean {
    const selected = group.permissions.filter((p) =>
      this.selectedPermissions().has(p.key)
    );
    return selected.length > 0 && selected.length < group.permissions.length;
  }

  getGroupSelectedCount(group: IPermissionGroup): number {
    return group.permissions.filter((p) =>
      this.selectedPermissions().has(p.key)
    ).length;
  }

  toggleGroupPermissions(group: IPermissionGroup, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedPermissions.update((current) => {
      const next = new Set(current);
      for (const perm of group.permissions) {
        if (checked) {
          next.add(perm.key);
        } else {
          next.delete(perm.key);
        }
      }
      return next;
    });
    this.syncPermissionsFormControl();
  }

  private syncPermissionsFormControl(): void {
    const perms = [...this.selectedPermissions()];
    this.roleForm.get('permissions')?.setValue(perms);
    this.roleForm.get('permissions')?.markAsTouched();
  }

  /** Custom validator: at least one permission required */
  private permissionsValidator = (control: AbstractControl): ValidationErrors | null => {
    const value = control.value;
    if (!value || !Array.isArray(value) || value.length === 0) {
      return { required: true };
    }
    return null;
  };
}
