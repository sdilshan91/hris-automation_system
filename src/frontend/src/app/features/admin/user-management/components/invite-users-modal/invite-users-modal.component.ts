import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { UserManagementService } from '../../services/user-management.service';
import {
  IAssignableRole,
  IInviteResult,
  ICsvInviteRow,
} from '../../models/user-management.models';

/**
 * US-ADM-005 AC-2: Invite modal.
 * - Tag-style email entry (type + Enter / comma to add multiple).
 * - Role multi-select (checkbox list).
 * - CSV upload dropzone parsed client-side into {email, roleNames} rows.
 * - Per-email / per-row results rendered after submit, plan-limit surfaced.
 */
@Component({
  selector: 'app-invite-users-modal',
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
    trigger('slideUp', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(12px)' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
  ],
  template: `
    <div
      class="fixed inset-0 z-50 flex items-end sm:items-center justify-center bg-black/30 backdrop-blur-sm sm:p-4"
      @fade
      (click)="onBackdrop()"
      (keydown.escape)="close.emit()"
      role="dialog"
      aria-modal="true"
      aria-labelledby="invite-title"
    >
      <div
        class="w-full sm:max-w-lg max-h-[92vh] overflow-y-auto rounded-t-2xl sm:rounded-2xl bg-white shadow-xl"
        @slideUp
        (click)="$event.stopPropagation()"
      >
        <!-- Header -->
        <div class="flex items-start justify-between px-6 pt-6 pb-4 border-b border-neutral-100">
          <div>
            <h2 id="invite-title" class="text-lg font-semibold text-neutral-900 tracking-tight">
              {{ 'userManagement.invite.title' | translate }}
            </h2>
            <p class="mt-1 text-sm text-neutral-500">
              {{ 'userManagement.invite.subtitle' | translate }}
            </p>
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

        <!-- Results view (after submit) -->
        @if (results(); as res) {
          <div class="px-6 py-5" data-testid="invite-results">
            @if (planLimitError()) {
              <div class="mb-4 rounded-lg bg-red-50 border border-red-100 px-4 py-3 text-sm text-red-700" data-testid="plan-limit-error">
                {{ 'userManagement.invite.planLimit' | translate }}
              </div>
            }
            <p class="text-sm text-neutral-600 mb-3">
              {{ 'userManagement.invite.resultsSummary' | translate: { invited: invitedCount(), errors: errorCount() } }}
            </p>
            <ul class="space-y-2 mb-5">
              @for (r of res; track r.email) {
                <li
                  class="flex items-center justify-between rounded-lg border px-3 py-2 text-sm"
                  [class.border-emerald-100]="r.status === 'invited'"
                  [class.bg-emerald-50]="r.status === 'invited'"
                  [class.border-red-100]="r.status === 'error'"
                  [class.bg-red-50]="r.status === 'error'"
                  [attr.data-testid]="'invite-result-row'"
                >
                  <span class="font-medium text-neutral-800 truncate">{{ r.email }}</span>
                  @if (r.status === 'invited') {
                    <span class="text-emerald-600 text-xs font-medium">
                      {{ 'userManagement.invite.statusInvited' | translate }}
                    </span>
                  } @else {
                    <span class="text-red-600 text-xs font-medium truncate ml-3" data-testid="invite-row-error">
                      {{ r.error || ('userManagement.invite.statusError' | translate) }}
                    </span>
                  }
                </li>
              }
            </ul>
            <div class="flex justify-end gap-3">
              <button type="button" class="btn-secondary" (click)="reset()">
                {{ 'userManagement.invite.inviteMore' | translate }}
              </button>
              <button type="button" class="btn-primary" (click)="done.emit()">
                {{ 'common.done' | translate }}
              </button>
            </div>
          </div>
        } @else {
          <div class="px-6 py-5">
            <!-- Mode tabs -->
            <div class="inline-flex rounded-lg bg-neutral-100 p-1 mb-5" role="tablist">
              <button
                type="button"
                role="tab"
                class="px-3 py-1.5 text-sm rounded-md transition-colors"
                [class.bg-white]="mode() === 'email'"
                [class.shadow-sm]="mode() === 'email'"
                [class.text-neutral-900]="mode() === 'email'"
                [class.text-neutral-500]="mode() !== 'email'"
                [attr.aria-selected]="mode() === 'email'"
                (click)="mode.set('email')"
              >
                {{ 'userManagement.invite.tabEmail' | translate }}
              </button>
              <button
                type="button"
                role="tab"
                class="px-3 py-1.5 text-sm rounded-md transition-colors"
                [class.bg-white]="mode() === 'csv'"
                [class.shadow-sm]="mode() === 'csv'"
                [class.text-neutral-900]="mode() === 'csv'"
                [class.text-neutral-500]="mode() !== 'csv'"
                [attr.aria-selected]="mode() === 'csv'"
                (click)="mode.set('csv')"
              >
                {{ 'userManagement.invite.tabCsv' | translate }}
              </button>
            </div>

            <!-- EMAIL MODE -->
            @if (mode() === 'email') {
              <label class="block text-sm font-medium text-neutral-700 mb-1.5">
                {{ 'userManagement.invite.emailsLabel' | translate }}
              </label>
              <div
                class="flex flex-wrap gap-1.5 rounded-lg border border-neutral-200 px-2 py-1.5 focus-within:ring-2 focus-within:ring-brand-500 focus-within:border-brand-500 transition"
              >
                @for (email of emails(); track email) {
                  <span
                    class="inline-flex items-center gap-1 rounded-md bg-brand-50 text-brand-700 text-xs font-medium px-2 py-1"
                    data-testid="email-tag"
                  >
                    {{ email }}
                    <button
                      type="button"
                      class="hover:text-brand-900"
                      [attr.aria-label]="('userManagement.invite.removeEmail' | translate) + ' ' + email"
                      (click)="removeEmail(email)"
                    >
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3 h-3">
                        <path d="M5.28 4.22a.75.75 0 0 0-1.06 1.06L6.94 8l-2.72 2.72a.75.75 0 1 0 1.06 1.06L8 9.06l2.72 2.72a.75.75 0 1 0 1.06-1.06L9.06 8l2.72-2.72a.75.75 0 0 0-1.06-1.06L8 6.94 5.28 4.22Z" />
                      </svg>
                    </button>
                  </span>
                }
                <input
                  type="email"
                  class="flex-1 min-w-[8rem] border-0 outline-none text-sm py-1 px-1 placeholder:text-neutral-400"
                  [placeholder]="'userManagement.invite.emailPlaceholder' | translate"
                  [value]="emailDraft()"
                  data-testid="email-input"
                  (input)="emailDraft.set($any($event.target).value)"
                  (keydown.enter)="$event.preventDefault(); addEmailFromDraft()"
                  (keyup)="onEmailKeyup($event)"
                  (blur)="addEmailFromDraft()"
                />
              </div>
              @if (emailError()) {
                <p class="mt-1 text-xs text-red-600" data-testid="email-error">
                  {{ emailError() }}
                </p>
              }
            }

            <!-- CSV MODE -->
            @if (mode() === 'csv') {
              <label class="block text-sm font-medium text-neutral-700 mb-1.5">
                {{ 'userManagement.invite.csvLabel' | translate }}
              </label>
              <label
                class="flex flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed border-neutral-200 px-4 py-8 text-center cursor-pointer hover:border-brand-400 hover:bg-brand-50/40 transition"
                data-testid="csv-dropzone"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-8 h-8 text-neutral-400">
                  <path d="M9.25 13.25a.75.75 0 0 0 1.5 0V4.66l1.95 2.1a.75.75 0 1 0 1.1-1.02l-3.25-3.5a.75.75 0 0 0-1.1 0L6.2 5.74a.75.75 0 1 0 1.1 1.02l1.95-2.1v8.59Z" />
                  <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z" />
                </svg>
                <span class="text-sm text-neutral-600">
                  {{ 'userManagement.invite.csvDrop' | translate }}
                </span>
                <span class="text-xs text-neutral-400">
                  {{ 'userManagement.invite.csvHint' | translate }}
                </span>
                <input
                  type="file"
                  accept=".csv,text/csv"
                  class="sr-only"
                  data-testid="csv-input"
                  (change)="onCsvSelected($event)"
                />
              </label>
              @if (csvRows().length > 0) {
                <div class="mt-3 rounded-lg border border-neutral-100 divide-y divide-neutral-50 max-h-40 overflow-y-auto" data-testid="csv-preview">
                  @for (row of csvRows(); track row.email) {
                    <div class="flex items-center justify-between px-3 py-1.5 text-xs">
                      <span class="text-neutral-700 truncate">{{ row.email }}</span>
                      <span class="text-neutral-400 ml-2 truncate">{{ row.roleNames.join(', ') || '—' }}</span>
                    </div>
                  }
                </div>
                <p class="mt-1.5 text-xs text-neutral-500">
                  {{ 'userManagement.invite.csvParsed' | translate: { count: csvRows().length } }}
                </p>
              }
              @if (csvError()) {
                <p class="mt-1 text-xs text-red-600" data-testid="csv-error">{{ csvError() }}</p>
              }
            }

            <!-- ROLES (email mode only; CSV carries role names per row) -->
            @if (mode() === 'email') {
              <div class="mt-5">
                <label class="block text-sm font-medium text-neutral-700 mb-1.5">
                  {{ 'userManagement.invite.rolesLabel' | translate }}
                </label>
                <div class="rounded-lg border border-neutral-200 divide-y divide-neutral-50 max-h-48 overflow-y-auto">
                  @for (role of roles(); track role.id) {
                    <label class="flex items-start gap-3 px-3 py-2.5 cursor-pointer hover:bg-neutral-50 transition-colors">
                      <input
                        type="checkbox"
                        class="mt-0.5 h-4 w-4 rounded border-neutral-300 text-brand-600 focus:ring-brand-500"
                        [attr.data-testid]="'role-checkbox-' + role.id"
                        [checked]="selectedRoleIds().includes(role.id)"
                        (change)="toggleRole(role.id)"
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
                @if (roleError()) {
                  <p class="mt-1 text-xs text-red-600" data-testid="role-error">{{ roleError() }}</p>
                }
              </div>
            }

            <!-- Footer -->
            <div class="flex justify-end gap-3 mt-6">
              <button type="button" class="btn-secondary" (click)="close.emit()">
                {{ 'common.cancel' | translate }}
              </button>
              <button
                type="button"
                class="btn-primary"
                data-testid="invite-submit"
                [disabled]="submitting() || !canSubmit()"
                (click)="submit()"
              >
                @if (submitting()) {
                  <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
                  </svg>
                }
                {{ 'userManagement.invite.send' | translate }}
              </button>
            </div>
          </div>
        }
      </div>
    </div>
  `,
})
export class InviteUsersModalComponent {
  private readonly service = inject(UserManagementService);
  private readonly toastr = inject(ToastrService);

  /** Assignable roles supplied by the parent (already loaded for filters). */
  readonly roles = input<IAssignableRole[]>([]);

  readonly close = output<void>();
  /** Emitted when the admin finishes (so the parent can refresh the list). */
  readonly done = output<void>();

  readonly mode = signal<'email' | 'csv'>('email');

  // Email-mode state
  readonly emails = signal<string[]>([]);
  readonly emailDraft = signal('');
  readonly emailError = signal('');
  readonly selectedRoleIds = signal<string[]>([]);
  readonly roleError = signal('');

  // CSV-mode state
  readonly csvRows = signal<ICsvInviteRow[]>([]);
  readonly csvError = signal('');

  readonly submitting = signal(false);
  readonly results = signal<IInviteResult[] | null>(null);

  readonly invitedCount = computed(
    () => this.results()?.filter((r) => r.status === 'invited').length ?? 0
  );
  readonly errorCount = computed(
    () => this.results()?.filter((r) => r.status === 'error').length ?? 0
  );
  readonly planLimitError = computed(() =>
    (this.results() ?? []).some(
      (r) => r.status === 'error' && /user limit reached/i.test(r.error ?? '')
    )
  );

  readonly canSubmit = computed(() => {
    if (this.mode() === 'csv') {
      return this.csvRows().length > 0;
    }
    return this.emails().length > 0 && this.selectedRoleIds().length > 0;
  });

  private static readonly EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  // ─── Email tag entry ─────────────────────────────────────

  addEmailFromDraft(): void {
    const raw = this.emailDraft().trim().replace(/,$/, '');
    if (!raw) {
      this.emailDraft.set('');
      return;
    }
    if (!InviteUsersModalComponent.EMAIL_RE.test(raw)) {
      this.emailError.set('Enter a valid email address.');
      return;
    }
    if (this.emails().includes(raw)) {
      this.emailDraft.set('');
      return;
    }
    this.emails.update((list) => [...list, raw]);
    this.emailDraft.set('');
    this.emailError.set('');
  }

  /** Comma also commits the current draft email (in addition to Enter/blur). */
  onEmailKeyup(event: KeyboardEvent): void {
    if (event.key === ',') {
      this.emailDraft.update((d) => d.replace(/,/g, ''));
      this.addEmailFromDraft();
    }
  }

  removeEmail(email: string): void {
    this.emails.update((list) => list.filter((e) => e !== email));
  }

  toggleRole(roleId: string): void {
    this.selectedRoleIds.update((ids) =>
      ids.includes(roleId)
        ? ids.filter((id) => id !== roleId)
        : [...ids, roleId]
    );
    if (this.selectedRoleIds().length > 0) {
      this.roleError.set('');
    }
  }

  // ─── CSV upload ──────────────────────────────────────────

  onCsvSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }
    this.csvError.set('');
    const reader = new FileReader();
    reader.onload = () => {
      const rows = UserManagementService.parseCsv(String(reader.result ?? ''));
      if (rows.length === 0) {
        this.csvError.set('No valid rows found in the CSV.');
      }
      this.csvRows.set(rows);
    };
    reader.onerror = () => this.csvError.set('Could not read the file.');
    reader.readAsText(file);
    // Allow re-selecting the same file.
    input.value = '';
  }

  // ─── Submit ──────────────────────────────────────────────

  submit(): void {
    // Flush any pending draft email first.
    if (this.mode() === 'email') {
      this.addEmailFromDraft();
      if (this.emails().length === 0) {
        this.emailError.set('Add at least one email address.');
        return;
      }
      if (this.selectedRoleIds().length === 0) {
        this.roleError.set('Select at least one role.');
        return;
      }
    } else if (this.csvRows().length === 0) {
      this.csvError.set('Upload a CSV with at least one row.');
      return;
    }

    this.submitting.set(true);
    const request$ =
      this.mode() === 'email'
        ? this.service.inviteUsers({
            emails: this.emails(),
            roleIds: this.selectedRoleIds(),
          })
        : this.service.inviteFromCsv(this.csvRows());

    request$.subscribe({
      next: (res) => {
        this.results.set(res);
        this.submitting.set(false);
        const invited = res.filter((r) => r.status === 'invited').length;
        if (invited > 0) {
          this.toastr.success(`${invited} invitation(s) sent.`);
        }
      },
      error: (err) => {
        this.submitting.set(false);
        const msg = err?.error?.message ?? err?.error?.error ?? '';
        if (err?.status === 409 || /user limit reached/i.test(msg)) {
          // Surface the plan-limit rejection as a results entry per target.
          const targets =
            this.mode() === 'email'
              ? this.emails()
              : this.csvRows().map((r) => r.email);
          this.results.set(
            targets.map((email) => ({
              email,
              status: 'error' as const,
              error: 'User limit reached for your plan.',
            }))
          );
        } else {
          this.toastr.error('Failed to send invitations. Please try again.');
        }
      },
    });
  }

  reset(): void {
    this.results.set(null);
    this.emails.set([]);
    this.emailDraft.set('');
    this.selectedRoleIds.set([]);
    this.csvRows.set([]);
    this.emailError.set('');
    this.roleError.set('');
    this.csvError.set('');
  }

  onBackdrop(): void {
    this.close.emit();
  }
}
