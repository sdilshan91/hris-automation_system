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
import { TenantLifecycleService } from '../../services/tenant-lifecycle.service';
import { mapLifecycleError } from '../suspend-tenant-dialog/suspend-tenant-dialog.component';
import { ITenantLifecycleResult } from '../../models/lifecycle.models';

/** Which (no-body) lifecycle transition this confirm dialog performs. */
export type LifecycleConfirmAction = 'reactivate' | 'restore';

/**
 * US-ADM-004 AC-5 / AC-6: simple confirmation modal for the non-destructive,
 * no-body lifecycle transitions — reactivate (suspended → active) and restore
 * (terminating → suspended/active). Both use a GREEN action button.
 *
 * On confirm it POSTs the matching endpoint, toasts, and emits `confirmed` so the
 * host can refresh state.
 */
@Component({
  selector: 'app-lifecycle-confirm-dialog',
  standalone: true,
  imports: [CommonModule, TranslateModule],
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
  templateUrl: './lifecycle-confirm-dialog.component.html',
})
export class LifecycleConfirmDialogComponent {
  private readonly service = inject(TenantLifecycleService);
  private readonly toastr = inject(ToastrService);

  readonly tenantId = input.required<string>();
  readonly tenantName = input.required<string>();
  readonly action = input.required<LifecycleConfirmAction>();

  readonly confirmed = output<ITenantLifecycleResult>();
  readonly cancelled = output<void>();

  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);

  /** i18n key prefix for the active action's copy. */
  readonly keyPrefix = computed(() =>
    this.action() === 'reactivate'
      ? 'LIFECYCLE.REACTIVATE'
      : 'LIFECYCLE.RESTORE',
  );

  confirm(): void {
    if (this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.submitError.set(null);

    const op =
      this.action() === 'reactivate'
        ? this.service.reactivate(this.tenantId())
        : this.service.restore(this.tenantId());

    op.subscribe({
      next: (res) => {
        const verb =
          this.action() === 'reactivate' ? 'reactivated' : 'restored';
        this.toastr.success(`${this.tenantName()} has been ${verb}.`);
        this.submitting.set(false);
        this.confirmed.emit(res);
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
