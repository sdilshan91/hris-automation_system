import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { UserManagementService } from '../../services/user-management.service';
import {
  IAssignableRole,
  IUserSummary,
} from '../../models/user-management.models';

/**
 * US-ADM-005 AC-3: Edit-roles side panel.
 * Checkbox list of assignable roles with descriptions; save sends the complete
 * new role-id set (PUT /users/roles) and toasts on success.
 */
@Component({
  selector: 'app-edit-roles-panel',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fade', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('150ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
    trigger('slideIn', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('220ms cubic-bezier(0.16,1,0.3,1)', style({ transform: 'translateX(0)' })),
      ]),
    ]),
  ],
  template: `
    <div
      class="fixed inset-0 z-50 flex justify-end bg-black/30 backdrop-blur-sm"
      @fade
      (click)="close.emit()"
      (keydown.escape)="close.emit()"
      role="dialog"
      aria-modal="true"
      aria-labelledby="edit-roles-title"
    >
      <div
        class="w-full sm:max-w-md h-full bg-white shadow-xl flex flex-col"
        @slideIn
        (click)="$event.stopPropagation()"
      >
        <!-- Header -->
        <div class="flex items-start justify-between px-6 pt-6 pb-4 border-b border-neutral-100">
          <div class="min-w-0">
            <h2 id="edit-roles-title" class="text-lg font-semibold text-neutral-900 tracking-tight">
              {{ 'userManagement.editRoles.title' | translate }}
            </h2>
            <p class="mt-1 text-sm text-neutral-500 truncate">{{ user().displayName }}</p>
          </div>
          <button
            type="button"
            class="w-8 h-8 rounded-lg flex items-center justify-center text-neutral-400 hover:bg-neutral-100 transition-colors"
            [attr.aria-label]="'common.close' | translate"
            (click)="close.emit()"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5">
              <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
            </svg>
          </button>
        </div>

        <!-- Role checkboxes -->
        <div class="flex-1 overflow-y-auto px-6 py-4">
          <div class="rounded-lg border border-neutral-200 divide-y divide-neutral-50">
            @for (role of roles(); track role.id) {
              <label class="flex items-start gap-3 px-3 py-3 cursor-pointer hover:bg-neutral-50 transition-colors">
                <input
                  type="checkbox"
                  class="mt-0.5 h-4 w-4 rounded border-neutral-300 text-brand-600 focus:ring-brand-500"
                  [attr.data-testid]="'edit-role-' + role.id"
                  [checked]="selected().includes(role.id)"
                  (change)="toggle(role.id)"
                />
                <span class="min-w-0">
                  <span class="block text-sm font-medium text-neutral-800">{{ role.name }}</span>
                  @if (role.description) {
                    <span class="block text-xs text-neutral-500">{{ role.description }}</span>
                  }
                </span>
              </label>
            } @empty {
              <p class="px-3 py-3 text-sm text-neutral-400">
                {{ 'userManagement.invite.noRoles' | translate }}
              </p>
            }
          </div>
        </div>

        <!-- Footer -->
        <div class="flex justify-end gap-3 px-6 py-4 border-t border-neutral-100">
          <button type="button" class="btn-secondary" (click)="close.emit()">
            {{ 'common.cancel' | translate }}
          </button>
          <button
            type="button"
            class="btn-primary"
            data-testid="edit-roles-save"
            [disabled]="saving()"
            (click)="save()"
          >
            @if (saving()) {
              <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
              </svg>
            }
            {{ 'common.save' | translate }}
          </button>
        </div>
      </div>
    </div>
  `,
})
export class EditRolesPanelComponent {
  private readonly service = inject(UserManagementService);
  private readonly toastr = inject(ToastrService);

  readonly user = input.required<IUserSummary>();
  readonly roles = input<IAssignableRole[]>([]);

  readonly close = output<void>();
  /** Emitted after a successful save (parent refreshes the row). */
  readonly saved = output<void>();

  readonly selected = signal<string[]>([]);
  readonly saving = signal(false);

  constructor() {
    // Seed the checkbox state from the user's current roles.
    effect(() => {
      const current = this.user();
      this.selected.set(current.roles.map((r) => r.id));
    });
  }

  toggle(roleId: string): void {
    this.selected.update((ids) =>
      ids.includes(roleId)
        ? ids.filter((id) => id !== roleId)
        : [...ids, roleId]
    );
  }

  save(): void {
    this.saving.set(true);
    this.service
      .editRoles({
        userTenantId: this.user().userTenantId,
        roleIds: this.selected(),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.toastr.success('Roles updated.');
          this.saved.emit();
        },
        error: () => {
          this.saving.set(false);
          this.toastr.error('Failed to update roles. Please try again.');
        },
      });
  }
}
