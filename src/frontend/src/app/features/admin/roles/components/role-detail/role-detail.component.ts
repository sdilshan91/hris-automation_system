import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { RolesService } from '../../services/roles.service';
import { IRole, IPermissionGroup } from '../../models/role.models';
import { PERMISSION_CATALOG } from '../../models/permission-catalog';

@Component({
  selector: 'app-role-detail',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
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
          <div class="h-6 bg-neutral-100 rounded w-1/3 mb-3"></div>
          <div class="h-4 bg-neutral-50 rounded w-2/3 mb-6"></div>
          <div class="space-y-3">
            @for (i of [1, 2, 3]; track i) {
              <div class="h-10 bg-neutral-50 rounded"></div>
            }
          </div>
        </div>
      }

      <!-- Error -->
      @if (errorMessage()) {
        <div class="card-notion text-center py-12">
          <p class="text-sm text-neutral-600">{{ errorMessage() }}</p>
          <a routerLink="/admin/roles" class="btn-secondary mt-4">
            Back to Roles
          </a>
        </div>
      }

      <!-- Role detail -->
      @if (role() && !isLoading()) {
        <div @fadeIn>
          <!-- Header -->
          <div class="card-notion mb-6">
            <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
              <div>
                <div class="flex items-center gap-2.5 mb-1">
                  <h1 class="text-xl font-semibold text-neutral-900">
                    {{ role()!.name }}
                  </h1>
                  @if (role()!.isBuiltIn) {
                    <span class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-neutral-100 text-neutral-600">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3 h-3">
                        <path fill-rule="evenodd" d="M8 1a3.5 3.5 0 0 0-3.5 3.5V7H4a2 2 0 0 0-2 2v4a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-.5V4.5A3.5 3.5 0 0 0 8 1Zm2 6V4.5a2 2 0 1 0-4 0V7h4Z" clip-rule="evenodd" />
                      </svg>
                      Built-in (read-only)
                    </span>
                  }
                </div>
                <p class="text-sm text-neutral-500">
                  {{ role()!.description || 'No description provided.' }}
                </p>
              </div>
              <div class="flex items-center gap-4 text-sm text-neutral-500">
                <span class="flex items-center gap-1.5">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4 text-neutral-400">
                    <path d="M8 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM12.735 14c.618 0 1.093-.561.872-1.139a6.002 6.002 0 0 0-11.215 0c-.22.578.255 1.139.872 1.139h9.47Z" />
                  </svg>
                  {{ role()!.userCount }} {{ role()!.userCount === 1 ? 'user' : 'users' }}
                </span>
                <span class="flex items-center gap-1.5">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4 text-neutral-400">
                    <path fill-rule="evenodd" d="M6.955 1.45A.5.5 0 0 1 7.452 1h1.096a.5.5 0 0 1 .497.45l.17 1.699c.484.12.94.312 1.356.562l1.321-.916a.5.5 0 0 1 .67.033l.774.775a.5.5 0 0 1 .034.67l-.916 1.32c.25.417.443.873.563 1.357l1.699.17a.5.5 0 0 1 .45.497v1.096a.5.5 0 0 1-.45.497l-1.7.17c-.12.484-.312.94-.562 1.356l.916 1.321a.5.5 0 0 1-.034.67l-.774.774a.5.5 0 0 1-.67.033l-1.32-.916c-.417.25-.874.443-1.357.563l-.17 1.699a.5.5 0 0 1-.497.45H7.452a.5.5 0 0 1-.497-.45l-.17-1.7a4.973 4.973 0 0 1-1.356-.562l-1.321.916a.5.5 0 0 1-.67-.033l-.774-.775a.5.5 0 0 1-.034-.67l.916-1.32a4.972 4.972 0 0 1-.563-1.357l-1.699-.17A.5.5 0 0 1 1 8.548V7.452a.5.5 0 0 1 .45-.497l1.7-.17c.12-.484.312-.94.562-1.356l-.916-1.321a.5.5 0 0 1 .034-.67l.774-.774a.5.5 0 0 1 .67-.033l1.32.916c.417-.25.874-.443 1.357-.563l.17-1.699ZM8 10.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z" clip-rule="evenodd" />
                  </svg>
                  {{ role()!.permissions.length }} permissions
                </span>
              </div>
            </div>
          </div>

          <!-- Permissions grouped by module -->
          <div class="space-y-3">
            <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider">
              Permissions
            </h2>
            @for (group of permissionGroups(); track group.module) {
              <div class="card-notion !p-0 overflow-hidden">
                <button
                  class="w-full flex items-center justify-between px-5 py-3.5 text-left hover:bg-neutral-50 transition-colors"
                  (click)="toggleGroup(group.module)"
                  [attr.aria-expanded]="isGroupExpanded(group.module)"
                  [attr.aria-controls]="'group-' + group.module"
                >
                  <div class="flex items-center gap-3">
                    <span class="text-sm font-medium text-neutral-900">
                      {{ group.label }}
                    </span>
                    <span class="text-xs text-neutral-400">
                      {{ getGroupGrantedCount(group) }}/{{ group.permissions.length }}
                    </span>
                  </div>
                  <svg
                    xmlns="http://www.w3.org/2000/svg"
                    viewBox="0 0 16 16"
                    fill="currentColor"
                    class="w-4 h-4 text-neutral-400 transition-transform duration-200"
                    [class.rotate-180]="isGroupExpanded(group.module)"
                  >
                    <path fill-rule="evenodd" d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd" />
                  </svg>
                </button>

                @if (isGroupExpanded(group.module)) {
                  <div
                    [id]="'group-' + group.module"
                    @expandCollapse
                    class="border-t border-neutral-100 px-5 py-3 space-y-2"
                  >
                    @for (perm of group.permissions; track perm.key) {
                      <div class="flex items-center gap-3 py-1.5">
                        <div
                          class="w-5 h-5 rounded flex items-center justify-center flex-shrink-0"
                          [class]="hasPermission(perm.key)
                            ? 'bg-brand-600 text-white'
                            : 'bg-neutral-100 text-neutral-300'"
                        >
                          @if (hasPermission(perm.key)) {
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5">
                              <path fill-rule="evenodd" d="M12.416 3.376a.75.75 0 0 1 .208 1.04l-5 7.5a.75.75 0 0 1-1.154.114l-3-3a.75.75 0 0 1 1.06-1.06l2.353 2.353 4.493-6.74a.75.75 0 0 1 1.04-.207Z" clip-rule="evenodd" />
                            </svg>
                          } @else {
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3 h-3">
                              <path d="M3.72 3.72a.75.75 0 0 1 1.06 0L8 6.94l3.22-3.22a.75.75 0 1 1 1.06 1.06L9.06 8l3.22 3.22a.75.75 0 1 1-1.06 1.06L8 9.06l-3.22 3.22a.75.75 0 0 1-1.06-1.06L6.94 8 3.72 4.78a.75.75 0 0 1 0-1.06Z" />
                            </svg>
                          }
                        </div>
                        <div class="min-w-0">
                          <span class="text-sm text-neutral-800">
                            {{ perm.label }}
                          </span>
                          <span class="text-xs text-neutral-400 ml-2 hidden sm:inline">
                            {{ perm.key }}
                          </span>
                        </div>
                      </div>
                    }
                  </div>
                }
              </div>
            }

            <!-- Modules with zero permissions for this role are hidden automatically -->
          </div>
        </div>
      }
    </div>
  `,
})
export class RoleDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly rolesService = inject(RolesService);

  readonly role = signal<IRole | null>(null);
  readonly isLoading = signal(true);
  readonly errorMessage = signal('');
  readonly expandedGroups = signal<Set<string>>(new Set());

  readonly permissionGroups = signal<IPermissionGroup[]>([]);

  ngOnInit(): void {
    const roleId = this.route.snapshot.paramMap.get('id');
    if (!roleId) {
      this.errorMessage.set('Role not found.');
      this.isLoading.set(false);
      return;
    }

    this.rolesService.getRole(roleId).subscribe({
      next: (role) => {
        this.role.set(role);
        // Filter catalog to only show groups that have at least one permission in this role
        const rolePerms = new Set(role.permissions);
        const groups = PERMISSION_CATALOG.filter((g) =>
          g.permissions.some((p) => rolePerms.has(p.key))
        );
        this.permissionGroups.set(groups.length > 0 ? groups : PERMISSION_CATALOG);

        // Auto-expand groups that have granted permissions
        const expanded = new Set<string>();
        for (const group of groups) {
          if (group.permissions.some((p) => rolePerms.has(p.key))) {
            expanded.add(group.module);
          }
        }
        this.expandedGroups.set(expanded);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to load role details.');
        this.isLoading.set(false);
      },
    });
  }

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

  hasPermission(key: string): boolean {
    return this.role()?.permissions.includes(key) ?? false;
  }

  getGroupGrantedCount(group: IPermissionGroup): number {
    const rolePerms = new Set(this.role()?.permissions ?? []);
    return group.permissions.filter((p) => rolePerms.has(p.key)).length;
  }
}
