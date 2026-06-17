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
import { mapLifecycleError } from '../suspend-tenant-dialog/suspend-tenant-dialog.component';
import {
  ITenantLifecycleResult,
  LIFECYCLE_REASON_MIN,
  LIFECYCLE_REASON_MAX,
  GRACE_DAYS_MIN,
  GRACE_DAYS_MAX,
  GRACE_DAYS_DEFAULT,
} from '../../models/lifecycle.models';

/**
 * US-ADM-004 AC-3 / FR-4 / NFR-5: multi-step termination confirmation modal.
 *
 *   Step 1 — reason (10–500) + grace period (7–90, default 30).
 *   Step 2 — impact summary (tenant facts + permanent-deletion warning).
 *   Step 3 — typed-subdomain confirmation: the admin must type the tenant's
 *            subdomain EXACTLY; the final red Terminate button stays disabled
 *            until it matches. Paste/drop are blocked on that input (NFR-5) so it
 *            must be typed manually.
 *
 * On success it toasts and emits `terminated` so the host can refresh state.
 */
@Component({
  selector: 'app-terminate-tenant-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule],
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
  templateUrl: './terminate-tenant-dialog.component.html',
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
export class TerminateTenantDialogComponent {
  private readonly service = inject(TenantLifecycleService);
  private readonly toastr = inject(ToastrService);

  readonly tenantId = input.required<string>();
  readonly tenantName = input.required<string>();
  readonly subdomain = input.required<string>();
  /** Optional plan label shown in the impact summary. */
  readonly plan = input<string | null>(null);
  /** Optional employee count (from the monitoring detail) for the impact summary. */
  readonly employeeCount = input<number | null>(null);

  readonly terminated = output<ITenantLifecycleResult>();
  readonly cancelled = output<void>();

  readonly REASON_MIN = LIFECYCLE_REASON_MIN;
  readonly REASON_MAX = LIFECYCLE_REASON_MAX;
  readonly GRACE_MIN = GRACE_DAYS_MIN;
  readonly GRACE_MAX = GRACE_DAYS_MAX;

  /** Current wizard step (1..3). */
  readonly step = signal(1);

  // ngModel-backed form state.
  reason = '';
  graceDays: number = GRACE_DAYS_DEFAULT;
  confirmSubdomain = '';

  private readonly tick = signal(0);

  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);

  readonly reasonLength = computed(() => {
    this.tick();
    return this.reason.trim().length;
  });

  readonly reasonValid = computed(() => {
    const len = this.reasonLength();
    return len >= this.REASON_MIN && len <= this.REASON_MAX;
  });

  readonly graceValid = computed(() => {
    this.tick();
    const g = Number(this.graceDays);
    return Number.isInteger(g) && g >= this.GRACE_MIN && g <= this.GRACE_MAX;
  });

  /** Step 1 is complete when reason and grace period are both valid. */
  readonly step1Valid = computed(() => this.reasonValid() && this.graceValid());

  /** FR-4: the typed subdomain must match EXACTLY (case-sensitive). */
  readonly subdomainMatches = computed(() => {
    this.tick();
    return this.confirmSubdomain === this.subdomain();
  });

  /** Final Terminate is enabled only on step 3 with an exact subdomain match. */
  readonly canSubmit = computed(() => {
    return (
      !this.submitting() &&
      this.step() === 3 &&
      this.step1Valid() &&
      this.subdomainMatches()
    );
  });

  onInput(): void {
    this.tick.update((n) => n + 1);
  }

  next(): void {
    this.onInput();
    if (this.step() === 1 && !this.step1Valid()) {
      return;
    }
    if (this.step() < 3) {
      this.step.update((s) => s + 1);
    }
  }

  back(): void {
    if (this.step() > 1) {
      this.step.update((s) => s - 1);
    }
  }

  /**
   * NFR-5: block paste/drop on the typed-confirmation input so the subdomain
   * must be typed manually.
   */
  blockPaste(event: Event): void {
    event.preventDefault();
  }

  confirm(): void {
    this.onInput();
    if (!this.canSubmit()) {
      return;
    }

    this.submitting.set(true);
    this.submitError.set(null);

    this.service
      .terminate(this.tenantId(), {
        reason: this.reason.trim(),
        graceDays: Number(this.graceDays),
      })
      .subscribe({
        next: (res) => {
          this.toastr.success(
            `${this.tenantName()} is scheduled for termination.`,
          );
          this.submitting.set(false);
          this.terminated.emit(res);
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
