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
import { ToastrService } from 'ngx-toastr';
import { RolesService } from '../../services/roles.service';
import { IRole, IUserWithRoles } from '../../models/role.models';

@Component({
  selector: 'app-user-role-assignment',
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
    trigger('chipEnter', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.9)' }),
        animate('150ms ease-out', style({ opacity: 1, transform: 'scale(1)' })),
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
          <div class="h-10 bg-neutral-50 rounded mb-4"></div>
          <div class="flex gap-2">
            <div class="h-8 bg-neutral-50 rounded-full w-24"></div>
            <div class="h-8 bg-neutral-50 rounded-full w-32"></div>
          </div>
        </div>
      }

      @if (user() && !isLoading()) {
        <div @fadeIn>
          <!-- User header -->
          <div class="card-notion mb-6">
            <div class="flex items-center gap-4">
              <div class="w-12 h-12 rounded-full bg-brand-100 text-brand-700 flex items-center justify-center text-base font-semibold flex-shrink-0">
                {{ getUserInitials(user()!) }}
              </div>
              <div class="min-w-0">
                <h1 class="text-lg font-semibold text-neutral-900 truncate">
                  {{ user()!.displayName }}
                </h1>
                <p class="text-sm text-neutral-500 truncate">
                  {{ user()!.email }}
                </p>
              </div>
            </div>
          </div>

          <!-- Current roles (chips) -->
          <div class="card-notion mb-6">
            <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider mb-3">
              Assigned Roles
            </h2>

            @if (selectedRoleIds().size === 0) {
              <p class="text-sm text-neutral-400 italic">No roles assigned.</p>
            } @else {
              <div class="flex flex-wrap gap-2">
                @for (roleId of selectedRoleIdsArray(); track roleId) {
                  <span
                    @chipEnter
                    class="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-sm font-medium transition-colors"
                    [class]="getRoleById(roleId)?.isBuiltIn
                      ? 'bg-neutral-100 text-neutral-700'
                      : 'bg-brand-50 text-brand-700'"
                  >
                    @if (getRoleById(roleId)?.isBuiltIn) {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3 h-3">
                        <path fill-rule="evenodd" d="M8 1a3.5 3.5 0 0 0-3.5 3.5V7H4a2 2 0 0 0-2 2v4a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-.5V4.5A3.5 3.5 0 0 0 8 1Zm2 6V4.5a2 2 0 1 0-4 0V7h4Z" clip-rule="evenodd" />
                      </svg>
                    }
                    {{ getRoleById(roleId)?.name || 'Unknown' }}
                    <button
                      type="button"
                      class="ml-0.5 w-4 h-4 rounded-full flex items-center justify-center hover:bg-black/10 transition-colors"
                      (click)="removeRole(roleId)"
                      [attr.aria-label]="'Remove role ' + (getRoleById(roleId)?.name || '')"
                    >
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3 h-3">
                        <path d="M5.28 4.22a.75.75 0 0 0-1.06 1.06L6.94 8l-2.72 2.72a.75.75 0 1 0 1.06 1.06L8 9.06l2.72 2.72a.75.75 0 1 0 1.06-1.06L9.06 8l2.72-2.72a.75.75 0 0 0-1.06-1.06L8 6.94 5.28 4.22Z" />
                      </svg>
                    </button>
                  </span>
                }
              </div>
            }
          </div>

          <!-- Available roles multi-select -->
          <div class="card-notion mb-6">
            <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider mb-3">
              Available Roles
            </h2>

            <!-- Search -->
            <div class="relative mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4 text-neutral-400 absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none">
                <path fill-rule="evenodd" d="M9.965 11.026a5 5 0 1 1 1.06-1.06l2.755 2.754a.75.75 0 1 1-1.06 1.06l-2.755-2.754ZM10.5 7a3.5 3.5 0 1 1-7 0 3.5 3.5 0 0 1 7 0Z" clip-rule="evenodd" />
              </svg>
              <input
                type="text"
                class="input-notion !pl-9"
                placeholder="Search roles..."
                [value]="searchQuery()"
                (input)="onSearchInput($event)"
              />
            </div>

            <div class="space-y-1 max-h-72 overflow-y-auto">
              @for (role of filteredRoles(); track role.roleId) {
                <label
                  class="flex items-center gap-3 px-3 py-2.5 rounded-lg cursor-pointer hover:bg-neutral-50 transition-colors"
                >
                  <input
                    type="checkbox"
                    class="w-4 h-4 rounded border-neutral-300 text-brand-600 focus:ring-brand-500 cursor-pointer"
                    [checked]="selectedRoleIds().has(role.roleId)"
                    (change)="toggleRole(role.roleId, $event)"
                    style="accent-color: var(--brand-primary)"
                  />
                  <div class="flex-1 min-w-0">
                    <div class="flex items-center gap-2">
                      <span class="text-sm font-medium text-neutral-800">{{ role.name }}</span>
                      @if (role.isBuiltIn) {
                        <span class="text-[10px] px-1.5 py-0.5 rounded bg-neutral-100 text-neutral-500">
                          Built-in
                        </span>
                      }
                    </div>
                    @if (role.description) {
                      <div class="text-xs text-neutral-400 truncate mt-0.5">
                        {{ role.description }}
                      </div>
                    }
                  </div>
                  <span class="text-xs text-neutral-400 flex-shrink-0">
                    {{ role.permissions.length }} perms
                  </span>
                </label>
              }

              @if (filteredRoles().length === 0) {
                <p class="text-sm text-neutral-400 text-center py-4">
                  No roles match your search.
                </p>
              }
            </div>
          </div>

          <!-- Save button -->
          <div class="flex justify-end gap-3">
            <a routerLink="/admin/roles" class="btn-secondary">
              Cancel
            </a>
            <button
              class="btn-primary"
              (click)="saveAssignments()"
              [disabled]="isSaving() || !hasChanges()"
            >
              @if (isSaving()) {
                <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                Saving...
              } @else {
                Save Role Assignments
              }
            </button>
          </div>
        </div>
      }

      <!-- Error -->
      @if (errorMessage() && !isLoading()) {
        <div class="card-notion text-center py-12">
          <p class="text-sm text-neutral-600">{{ errorMessage() }}</p>
          <a routerLink="/admin/roles" class="btn-secondary mt-4">
            Back to Roles
          </a>
        </div>
      }
    </div>
  `,
})
export class UserRoleAssignmentComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly rolesService = inject(RolesService);
  private readonly toastr = inject(ToastrService);

  readonly user = signal<IUserWithRoles | null>(null);
  readonly allRoles = signal<IRole[]>([]);
  readonly selectedRoleIds = signal<Set<string>>(new Set());
  readonly originalRoleIds = signal<Set<string>>(new Set());
  readonly searchQuery = signal('');
  readonly isLoading = signal(true);
  readonly isSaving = signal(false);
  readonly errorMessage = signal('');

  readonly filteredRoles = signal<IRole[]>([]);
  readonly selectedRoleIdsArray = signal<string[]>([]);

  private roleMap = new Map<string, IRole>();

  ngOnInit(): void {
    const userTenantId = this.route.snapshot.paramMap.get('userId');
    if (!userTenantId) {
      this.errorMessage.set('User not found.');
      this.isLoading.set(false);
      return;
    }

    // Load user and all roles in parallel
    this.rolesService.getUserWithRoles(userTenantId).subscribe({
      next: (user) => {
        this.user.set(user);
        const roleIds = new Set(user.roles.map((r) => r.roleId));
        this.selectedRoleIds.set(roleIds);
        this.originalRoleIds.set(new Set(roleIds));
        this.selectedRoleIdsArray.set([...roleIds]);
        this.loadAllRoles();
      },
      error: () => {
        this.errorMessage.set('Failed to load user details.');
        this.isLoading.set(false);
      },
    });
  }

  private loadAllRoles(): void {
    this.rolesService.getRoles().subscribe({
      next: (roles) => {
        this.allRoles.set(roles);
        this.roleMap = new Map(roles.map((r) => [r.roleId, r]));
        this.updateFilteredRoles();
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to load roles.');
        this.isLoading.set(false);
      },
    });
  }

  getRoleById(roleId: string): IRole | undefined {
    return this.roleMap.get(roleId);
  }

  getUserInitials(user: IUserWithRoles): string {
    const parts = user.displayName.split(' ');
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return user.displayName.substring(0, 2).toUpperCase();
  }

  onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchQuery.set(value);
    this.updateFilteredRoles();
  }

  toggleRole(roleId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedRoleIds.update((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(roleId);
      } else {
        next.delete(roleId);
      }
      return next;
    });
    this.selectedRoleIdsArray.set([...this.selectedRoleIds()]);
  }

  removeRole(roleId: string): void {
    this.selectedRoleIds.update((current) => {
      const next = new Set(current);
      next.delete(roleId);
      return next;
    });
    this.selectedRoleIdsArray.set([...this.selectedRoleIds()]);
  }

  hasChanges(): boolean {
    const current = this.selectedRoleIds();
    const original = this.originalRoleIds();
    if (current.size !== original.size) return true;
    for (const id of current) {
      if (!original.has(id)) return true;
    }
    return false;
  }

  saveAssignments(): void {
    const user = this.user();
    if (!user) return;

    this.isSaving.set(true);
    const roleIds = [...this.selectedRoleIds()];

    this.rolesService
      .assignRoles(user.userTenantId, { roleIds })
      .subscribe({
        next: (updatedUser) => {
          this.user.set(updatedUser);
          const newIds = new Set(updatedUser.roles.map((r) => r.roleId));
          this.selectedRoleIds.set(newIds);
          this.originalRoleIds.set(new Set(newIds));
          this.selectedRoleIdsArray.set([...newIds]);
          this.isSaving.set(false);
          this.toastr.success(
            `Roles updated for ${user.displayName}. Changes take effect on next token refresh.`
          );
        },
        error: () => {
          this.isSaving.set(false);
        },
      });
  }

  private updateFilteredRoles(): void {
    const query = this.searchQuery().toLowerCase();
    let roles = this.allRoles();
    if (query) {
      roles = roles.filter(
        (r) =>
          r.name.toLowerCase().includes(query) ||
          r.description.toLowerCase().includes(query)
      );
    }
    this.filteredRoles.set(roles);
  }
}
