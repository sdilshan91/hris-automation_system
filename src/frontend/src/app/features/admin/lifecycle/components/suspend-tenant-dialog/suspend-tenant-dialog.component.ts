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
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { TenantLifecycleService } from '../../services/tenant-lifecycle.service';
import {
  ITenantLifecycleResult,
  LIFECYCLE_REASON_MIN,
  LIFECYCLE_REASON_MAX,
} from '../../models/lifecycle.models';
import { TrappedDialogDirective } from '../../../../../shared/directives';

/**
 * US-ADM-004 AC-1: confirmation modal to suspend a tenant.
 *
 * A reason textarea with a live char counter (min 10 / max 500), clear warning
 * text that suspension immediately blocks all tenant users, a red Suspend button,
 * and a TWO-STEP confirmation — the admin must additionally tick the
 * acknowledgement checkbox before the destructive POST is enabled.
 *
 * On success it toasts and emits `suspended` so the host can refresh state. A 409
 * invalid_transition surfaces a clear inline error.
 */
@Component({
  selector: 'app-suspend-tenant-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule, TrappedDialogDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('150ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
    trigger('pop', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.96)' }),
        animate('160ms ease-out', style({ opacity: 1, transform: 'scale(1)' })),
      ]),
    ]),
  ],
  templateUrl: './suspend-tenant-dialog.component.html',
  styles: [
    `
      :host {
        display: block;
      }
      .field {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.5rem 0.75rem;
        font-size: 0.875rem;
        color: #171717;
      }
      .field:focus {
        outline: none;
        border-color: #f87171;
        box-shadow: 0 0 0 2px #fee2e2;
      }
    `,
  ],
})
export class SuspendTenantDialogComponent {
  private readonly service = inject(TenantLifecycleService);
  private readonly toastr = inject(ToastrService);

  readonly tenantId = input.required<string>();
  readonly tenantName = input.required<string>();

  /** Emitted with the lifecycle result once the tenant is suspended. */
  readonly suspended = output<ITenantLifecycleResult>();
  /** Emitted when the admin dismisses the modal. */
  readonly cancelled = output<void>();

  readonly REASON_MIN = LIFECYCLE_REASON_MIN;
  readonly REASON_MAX = LIFECYCLE_REASON_MAX;

  // ngModel-backed form state.
  reason = '';
  acknowledged = false;

  /** Bumped on every keystroke / toggle so the computed gates re-evaluate. */
  private readonly tick = signal(0);

  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);

  readonly reasonLength = computed(() => {
    this.tick();
    return this.reason.trim().length;
  });

  /** AC-1: reason must be 10–500 trimmed chars. */
  readonly reasonValid = computed(() => {
    const len = this.reasonLength();
    return len >= this.REASON_MIN && len <= this.REASON_MAX;
  });

  /** Two-step: valid reason AND the acknowledgement checkbox is ticked. */
  readonly canSubmit = computed(() => {
    this.tick();
    return !this.submitting() && this.reasonValid() && this.acknowledged;
  });

  onInput(): void {
    this.tick.update((n) => n + 1);
  }

  confirm(): void {
    this.onInput();
    if (!this.canSubmit()) {
      return;
    }

    this.submitting.set(true);
    this.submitError.set(null);

    this.service.suspend(this.tenantId(), { reason: this.reason.trim() }).subscribe({
      next: (res) => {
        this.toastr.success(`${this.tenantName()} has been suspended.`);
        this.submitting.set(false);
        this.suspended.emit(res);
      },
      error: (err) => {
        this.submitting.set(false);
        this.submitError.set(mapLifecycleError(err));
      },
    });
  }

  cancel(): void {
    this.cancelled.emit();
  }
}

/**
 * Map the shared lifecycle backend error codes to a clear, user-facing message.
 * Exported so the terminate/reactivate/restore dialogs reuse it.
 */
export function mapLifecycleError(err: unknown): string {
  const e = err as {
    status?: number;
    error?: { code?: string; message?: string };
  };
  const code = e?.error?.code;
  switch (code) {
    case 'reason_invalid':
      return 'The reason must be between 10 and 500 characters.';
    case 'grace_days_invalid':
      return 'The grace period must be between 7 and 90 days.';
    case 'system_tenant_protected':
      return 'The system tenant cannot be suspended or terminated.';
    case 'invalid_transition':
      return 'This action is not allowed from the tenant\'s current status.';
    case 'tenant_not_found':
      return 'Tenant not found.';
    default:
      if (e?.status === 409) {
        return 'This action is not allowed from the tenant\'s current status.';
      }
      if (e?.status === 403) {
        return 'The system tenant cannot be suspended or terminated.';
      }
      return e?.error?.message || 'The action could not be completed.';
  }
}
