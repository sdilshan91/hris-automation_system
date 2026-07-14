import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { ImpersonationService } from '../../services/impersonation.service';
import { AuthService } from '../../../../../core/auth/auth.service';
import {
  IImpersonationTarget,
  IStartImpersonationResponse,
  IMPERSONATION_REASON_MIN,
  IMPERSONATION_REASON_MAX,
} from '../../models/impersonation.models';
import { TrappedDialogDirective } from '../../../../../shared/directives';

/**
 * US-ADM-003: confirmation modal to start an impersonation session.
 *
 * AC-1/BR-4: a target-user picker (loaded from GET targets, showing email +
 * roles) and a mandatory reason textarea with a live char counter (min 10 / max
 * 500); the confirm button stays disabled until both are valid.
 *
 * On confirm it POSTs the session, hands the returned token to AuthService
 * (single-SPA activation — see AuthService.activateImpersonation), toasts, then
 * emits `started` so the host can navigate into the app. A 409 (session_active)
 * surfaces a clear inline error (BR-3).
 */
@Component({
  selector: 'app-impersonate-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, TrappedDialogDirective],
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
  templateUrl: './impersonate-dialog.component.html',
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
        border-color: #818cf8;
        box-shadow: 0 0 0 2px #e0e7ff;
      }
    `,
  ],
})
export class ImpersonateDialogComponent implements OnInit {
  private readonly service = inject(ImpersonationService);
  private readonly auth = inject(AuthService);
  private readonly toastr = inject(ToastrService);

  /** Target tenant id (for the targets fetch + the start payload). */
  readonly tenantId = input.required<string>();
  /** Tenant display name (for the copy). */
  readonly tenantName = input.required<string>();

  /** Emitted with the start response once the session is activated. */
  readonly started = output<IStartImpersonationResponse>();
  /** Emitted when the admin dismisses the modal. */
  readonly cancelled = output<void>();

  readonly REASON_MIN = IMPERSONATION_REASON_MIN;
  readonly REASON_MAX = IMPERSONATION_REASON_MAX;

  readonly targets = signal<IImpersonationTarget[]>([]);
  readonly targetsLoading = signal(true);
  readonly targetsError = signal<string | null>(null);

  // ngModel-backed form state.
  selectedUserId = '';
  reason = '';

  /** Bumped on every keystroke / selection so the computed gates re-evaluate. */
  private readonly tick = signal(0);

  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);

  /** Live reason length for the counter. */
  readonly reasonLength = computed(() => {
    this.tick();
    return this.reason.trim().length;
  });

  /** AC-1/BR-4: reason must be 10–500 trimmed chars. */
  readonly reasonValid = computed(() => {
    const len = this.reasonLength();
    return len >= this.REASON_MIN && len <= this.REASON_MAX;
  });

  /** Submit is enabled only with a chosen target AND a valid reason. */
  readonly canSubmit = computed(() => {
    this.tick();
    return (
      !this.submitting() &&
      this.selectedUserId.length > 0 &&
      this.reasonValid()
    );
  });

  /** Resolve the chosen target for the confirmation copy. */
  readonly selectedTarget = computed<IImpersonationTarget | null>(() => {
    this.tick();
    return (
      this.targets().find((t) => t.userId === this.selectedUserId) ?? null
    );
  });

  ngOnInit(): void {
    this.loadTargets();
  }

  loadTargets(): void {
    this.targetsLoading.set(true);
    this.targetsError.set(null);
    this.service.getTargets(this.tenantId()).subscribe({
      next: (targets) => {
        this.targets.set(targets);
        this.targetsLoading.set(false);
      },
      error: () => {
        this.targetsError.set('Failed to load tenant users.');
        this.targetsLoading.set(false);
      },
    });
  }

  /** Re-evaluate the reactive gates on input. */
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

    this.service
      .start({
        targetUserId: this.selectedUserId,
        targetTenantId: this.tenantId(),
        reason: this.reason.trim(),
      })
      .subscribe({
        next: (res) => {
          // AC-1: activate the impersonation token in this SPA so the
          // impersonation context (and the global banner) become active.
          this.auth.activateImpersonation(res.token);
          this.toastr.success(
            res.isReadOnly
              ? 'Read-only impersonation session started.'
              : 'Impersonation session started.',
          );
          this.submitting.set(false);
          this.started.emit(res);
        },
        error: (err) => {
          this.submitting.set(false);
          this.submitError.set(this.mapError(err));
        },
      });
  }

  cancel(): void {
    this.cancelled.emit();
  }

  /** Map the backend error codes to a clear, user-facing message. */
  private mapError(err: unknown): string {
    const e = err as { status?: number; error?: { code?: string; message?: string } };
    const code = e?.error?.code;
    switch (code) {
      case 'session_active':
        return 'You already have an active impersonation session. End it before starting another.';
      case 'reason_invalid':
        return 'The reason must be between 10 and 500 characters.';
      case 'tenant_terminated':
        return 'This tenant is terminated and cannot be impersonated.';
      case 'target_is_system':
        return 'System users cannot be impersonated.';
      case 'tenant_not_found':
        return 'Tenant not found.';
      case 'user_not_found':
      case 'membership_not_found':
        return 'The selected user is no longer an active member of this tenant.';
      default:
        if (e?.status === 409) {
          return 'You already have an active impersonation session. End it before starting another.';
        }
        return e?.error?.message || 'Failed to start the impersonation session.';
    }
  }
}
