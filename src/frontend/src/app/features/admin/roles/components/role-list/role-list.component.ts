import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../../core/auth/auth.service';
import { RolesService } from '../../services/roles.service';
import { IRole } from '../../models/role.models';
import { HasPermissionDirective } from '../../../../../shared/directives/has-permission.directive';

@Component({
  selector: 'app-role-list',
  standalone: true,
  imports: [CommonModule, RouterLink, HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate(
          '250ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Roles
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Manage roles and permissions for your workspace.
          </p>
        </div>
        <div *appHasPermission="'Admin.Roles.Manage'">
          <a
            routerLink="create"
            class="btn-primary"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5">
              <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
            </svg>
            Create Role
          </a>
        </div>
      </div>

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          @for (i of [1, 2, 3, 4, 5, 6]; track i) {
            <div class="card-notion animate-pulse">
              <div class="h-5 bg-neutral-100 rounded w-2/3 mb-3"></div>
              <div class="h-4 bg-neutral-50 rounded w-full mb-4"></div>
              <div class="flex gap-3">
                <div class="h-4 bg-neutral-50 rounded w-1/3"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/3"></div>
              </div>
            </div>
          }
        </div>
      }

      <!-- Error state -->
      @if (errorMessage()) {
        <div class="card-notion text-center py-12">
          <div class="w-12 h-12 rounded-full bg-red-50 flex items-center justify-center mx-auto mb-4">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-6 h-6 text-red-500">
              <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
            </svg>
          </div>
          <p class="text-sm text-neutral-600">{{ errorMessage() }}</p>
          <button class="btn-secondary mt-4" (click)="loadRoles()">
            Try Again
          </button>
        </div>
      }

      <!-- Role cards grid -->
      @if (!isLoading() && !errorMessage()) {
        <!-- Built-in roles section -->
        @if (builtInRoles().length > 0) {
          <div class="mb-8">
            <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider mb-3">
              Built-in Roles
            </h2>
            <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              @for (role of builtInRoles(); track role.roleId) {
                <div
                  class="card-notion group cursor-pointer hover:shadow-notion-md transition-shadow duration-200"
                  @fadeSlideIn
                  (click)="viewRole(role)"
                  (keydown.enter)="viewRole(role)"
                  tabindex="0"
                  [attr.aria-label]="'View role: ' + role.name"
                  role="button"
                >
                  <div class="flex items-start justify-between mb-2">
                    <div class="flex items-center gap-2">
                      <h3 class="text-base font-semibold text-neutral-900">
                        {{ role.name }}
                      </h3>
                      <!-- Lock icon + Built-in badge -->
                      <span class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-neutral-100 text-neutral-600">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3 h-3">
                          <path fill-rule="evenodd" d="M8 1a3.5 3.5 0 0 0-3.5 3.5V7H4a2 2 0 0 0-2 2v4a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-.5V4.5A3.5 3.5 0 0 0 8 1Zm2 6V4.5a2 2 0 1 0-4 0V7h4Z" clip-rule="evenodd" />
                        </svg>
                        Built-in
                      </span>
                    </div>
                  </div>

                  <p class="text-sm text-neutral-500 mb-4 line-clamp-2">
                    {{ role.description || 'No description' }}
                  </p>

                  <div class="flex items-center gap-4 text-xs text-neutral-400">
                    <span class="flex items-center gap-1">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5">
                        <path d="M8 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM12.735 14c.618 0 1.093-.561.872-1.139a6.002 6.002 0 0 0-11.215 0c-.22.578.255 1.139.872 1.139h9.47Z" />
                      </svg>
                      {{ role.userCount }} {{ role.userCount === 1 ? 'user' : 'users' }}
                    </span>
                    <span class="flex items-center gap-1">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5">
                        <path fill-rule="evenodd" d="M6.955 1.45A.5.5 0 0 1 7.452 1h1.096a.5.5 0 0 1 .497.45l.17 1.699c.484.12.94.312 1.356.562l1.321-.916a.5.5 0 0 1 .67.033l.774.775a.5.5 0 0 1 .034.67l-.916 1.32c.25.417.443.873.563 1.357l1.699.17a.5.5 0 0 1 .45.497v1.096a.5.5 0 0 1-.45.497l-1.7.17c-.12.484-.312.94-.562 1.356l.916 1.321a.5.5 0 0 1-.034.67l-.774.774a.5.5 0 0 1-.67.033l-1.32-.916c-.417.25-.874.443-1.357.563l-.17 1.699a.5.5 0 0 1-.497.45H7.452a.5.5 0 0 1-.497-.45l-.17-1.7a4.973 4.973 0 0 1-1.356-.562l-1.321.916a.5.5 0 0 1-.67-.033l-.774-.775a.5.5 0 0 1-.034-.67l.916-1.32a4.972 4.972 0 0 1-.563-1.357l-1.699-.17A.5.5 0 0 1 1 8.548V7.452a.5.5 0 0 1 .45-.497l1.7-.17c.12-.484.312-.94.562-1.356l-.916-1.321a.5.5 0 0 1 .034-.67l.774-.774a.5.5 0 0 1 .67-.033l1.32.916c.417-.25.874-.443 1.357-.563l.17-1.699ZM8 10.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z" clip-rule="evenodd" />
                      </svg>
                      {{ role.permissions.length }} permissions
                    </span>
                  </div>
                </div>
              }
            </div>
          </div>
        }

        <!-- Custom roles section -->
        <div>
          <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider mb-3">
            Custom Roles
          </h2>
          @if (customRoles().length === 0) {
            <div class="card-notion text-center py-10">
              <div class="w-12 h-12 rounded-full bg-neutral-50 flex items-center justify-center mx-auto mb-3">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-6 h-6 text-neutral-400">
                  <path d="M10 1a6 6 0 0 0-3.815 10.631C7.237 12.5 8 13.443 8 14.456v.644a.75.75 0 0 0 .75.75h2.5a.75.75 0 0 0 .75-.75v-.644c0-1.013.762-1.957 1.815-2.825A6 6 0 0 0 10 1ZM8.863 17.414a.75.75 0 0 0-.226 1.483 9.066 9.066 0 0 0 2.726 0 .75.75 0 0 0-.226-1.483 7.563 7.563 0 0 1-2.274 0Z" />
                </svg>
              </div>
              <p class="text-sm text-neutral-600 mb-1">No custom roles yet</p>
              <p class="text-xs text-neutral-400">
                Create a custom role to define specific permissions for your team.
              </p>
            </div>
          } @else {
            <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              @for (role of customRoles(); track role.roleId) {
                <div
                  class="card-notion group cursor-pointer hover:shadow-notion-md transition-shadow duration-200"
                  @fadeSlideIn
                  (click)="editRole(role)"
                  (keydown.enter)="editRole(role)"
                  tabindex="0"
                  [attr.aria-label]="'Edit role: ' + role.name"
                  role="button"
                >
                  <div class="flex items-start justify-between mb-2">
                    <h3 class="text-base font-semibold text-neutral-900">
                      {{ role.name }}
                    </h3>
                    <div class="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity" *appHasPermission="'Admin.Roles.Manage'">
                      <button
                        class="w-7 h-7 rounded-md flex items-center justify-center text-neutral-400 hover:text-red-500 hover:bg-red-50 transition-colors"
                        (click)="confirmDelete(role, $event)"
                        [attr.aria-label]="'Delete role: ' + role.name"
                        title="Delete role"
                      >
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4">
                          <path fill-rule="evenodd" d="M5 3.25V4H2.75a.75.75 0 0 0 0 1.5h.3l.815 8.15A1.5 1.5 0 0 0 5.357 15h5.285a1.5 1.5 0 0 0 1.493-1.35l.815-8.15h.3a.75.75 0 0 0 0-1.5H11V3.25A2.25 2.25 0 0 0 8.75 1h-1.5A2.25 2.25 0 0 0 5 3.25Zm2.25-.75a.75.75 0 0 0-.75.75V4h3V3.25a.75.75 0 0 0-.75-.75h-1.5ZM6.05 6a.75.75 0 0 1 .787.713l.275 5.5a.75.75 0 0 1-1.498.075l-.275-5.5A.75.75 0 0 1 6.05 6Zm3.9 0a.75.75 0 0 1 .712.787l-.275 5.5a.75.75 0 0 1-1.498-.075l.275-5.5a.75.75 0 0 1 .786-.711Z" clip-rule="evenodd" />
                        </svg>
                      </button>
                    </div>
                  </div>

                  <p class="text-sm text-neutral-500 mb-4 line-clamp-2">
                    {{ role.description || 'No description' }}
                  </p>

                  <div class="flex items-center gap-4 text-xs text-neutral-400">
                    <span class="flex items-center gap-1">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5">
                        <path d="M8 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM12.735 14c.618 0 1.093-.561.872-1.139a6.002 6.002 0 0 0-11.215 0c-.22.578.255 1.139.872 1.139h9.47Z" />
                      </svg>
                      {{ role.userCount }} {{ role.userCount === 1 ? 'user' : 'users' }}
                    </span>
                    <span class="flex items-center gap-1">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5">
                        <path fill-rule="evenodd" d="M6.955 1.45A.5.5 0 0 1 7.452 1h1.096a.5.5 0 0 1 .497.45l.17 1.699c.484.12.94.312 1.356.562l1.321-.916a.5.5 0 0 1 .67.033l.774.775a.5.5 0 0 1 .034.67l-.916 1.32c.25.417.443.873.563 1.357l1.699.17a.5.5 0 0 1 .45.497v1.096a.5.5 0 0 1-.45.497l-1.7.17c-.12.484-.312.94-.562 1.356l.916 1.321a.5.5 0 0 1-.034.67l-.774.774a.5.5 0 0 1-.67.033l-1.32-.916c-.417.25-.874.443-1.357.563l-.17 1.699a.5.5 0 0 1-.497.45H7.452a.5.5 0 0 1-.497-.45l-.17-1.7a4.973 4.973 0 0 1-1.356-.562l-1.321.916a.5.5 0 0 1-.67-.033l-.774-.775a.5.5 0 0 1-.034-.67l.916-1.32a4.972 4.972 0 0 1-.563-1.357l-1.699-.17A.5.5 0 0 1 1 8.548V7.452a.5.5 0 0 1 .45-.497l1.7-.17c.12-.484.312-.94.562-1.356l-.916-1.321a.5.5 0 0 1 .034-.67l.774-.774a.5.5 0 0 1 .67-.033l1.32.916c.417-.25.874-.443 1.357-.563l.17-1.699ZM8 10.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z" clip-rule="evenodd" />
                      </svg>
                      {{ role.permissions.length }} permissions
                    </span>
                  </div>
                </div>
              }
            </div>
          }
        </div>
      }

      <!-- Delete confirmation dialog -->
      @if (roleToDelete()) {
        <div
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/20 backdrop-blur-sm px-4"
          (click)="cancelDelete()"
          (keydown.escape)="cancelDelete()"
          role="dialog"
          aria-modal="true"
          aria-labelledby="delete-dialog-title"
        >
          <div
            class="w-full max-w-md rounded-xl bg-white shadow-notion-lg p-6"
            (click)="$event.stopPropagation()"
          >
            <h3 id="delete-dialog-title" class="text-lg font-semibold text-neutral-900 mb-2">
              Delete Role
            </h3>
            <p class="text-sm text-neutral-600 mb-1">
              Are you sure you want to delete <strong>{{ roleToDelete()!.name }}</strong>?
            </p>
            @if (roleToDelete()!.userCount > 0) {
              <p class="text-sm text-amber-600 bg-amber-50 rounded-lg px-3 py-2 mt-3">
                This role is currently assigned to {{ roleToDelete()!.userCount }}
                {{ roleToDelete()!.userCount === 1 ? 'user' : 'users' }}.
                They will lose the permissions granted by this role.
              </p>
            }
            <div class="flex justify-end gap-3 mt-6">
              <button
                class="btn-secondary"
                (click)="cancelDelete()"
              >
                Cancel
              </button>
              <button
                class="inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2.5 text-sm font-medium text-white shadow-sm transition-all duration-200 hover:bg-red-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-red-600 disabled:opacity-50 disabled:cursor-not-allowed"
                (click)="deleteRole()"
                [disabled]="isDeleting()"
              >
                @if (isDeleting()) {
                  <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                  Deleting...
                } @else {
                  Delete Role
                }
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .line-clamp-2 {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
  `],
})
export class RoleListComponent implements OnInit {
  private readonly rolesService = inject(RolesService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly roles = signal<IRole[]>([]);
  readonly isLoading = signal(true);
  readonly errorMessage = signal('');
  readonly roleToDelete = signal<IRole | null>(null);
  readonly isDeleting = signal(false);

  readonly builtInRoles = signal<IRole[]>([]);
  readonly customRoles = signal<IRole[]>([]);

  ngOnInit(): void {
    this.loadRoles();
  }

  loadRoles(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.rolesService.getRoles().subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.builtInRoles.set(roles.filter((r) => r.isBuiltIn));
        this.customRoles.set(roles.filter((r) => !r.isBuiltIn));
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set(
          'Failed to load roles. Please try again.'
        );
        this.isLoading.set(false);
      },
    });
  }

  viewRole(role: IRole): void {
    this.router.navigate(['/admin/roles', role.roleId]);
  }

  editRole(role: IRole): void {
    if (this.authService.hasPermission('Admin.Roles.Manage')) {
      this.router.navigate(['/admin/roles', role.roleId, 'edit']);
    } else {
      this.router.navigate(['/admin/roles', role.roleId]);
    }
  }

  confirmDelete(role: IRole, event: Event): void {
    event.stopPropagation();
    this.roleToDelete.set(role);
  }

  cancelDelete(): void {
    this.roleToDelete.set(null);
  }

  deleteRole(): void {
    const role = this.roleToDelete();
    if (!role) return;

    this.isDeleting.set(true);
    this.rolesService.deleteRole(role.roleId).subscribe({
      next: () => {
        this.toastr.success(`Role "${role.name}" deleted successfully.`);
        this.roleToDelete.set(null);
        this.isDeleting.set(false);
        this.loadRoles();
      },
      error: () => {
        this.isDeleting.set(false);
      },
    });
  }
}
