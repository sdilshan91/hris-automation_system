import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnDestroy,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NotificationPreferenceService } from '../../services/notification-preference.service';
import {
  ICategoryPreference,
  NotificationChannel,
  buildTimezoneOptions,
  detectBrowserTimezone,
  toInputTime,
} from '../../models/notification-preference.models';

/** One pending channel change, debounced before it is flushed to the server. */
interface IPendingChange {
  category: string;
  channelInApp: boolean;
  channelEmail: boolean;
  /** The row state BEFORE this change, used to revert on a server rejection. */
  previous: ICategoryPreference;
}

/**
 * US-NTF-003: Profile > Notification Preferences.
 *
 * Smart container for the per-user preference matrix (AC-1), Quiet Hours card
 * (FR-9) and Reset-to-Defaults flow (FR-7). Auto-saves each toggle change
 * (debounced 500ms, AC/UI-notes) optimistically, reverting + surfacing the server
 * message on a 400/422 rejection (BR-3/AC-3). Mandatory rows lock both toggles
 * (AC-3/AC-4). Responsive: desktop table, mobile (<768px) card list.
 */
@Component({
  selector: 'app-notification-preferences',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatSlideToggleModule,
    MatTooltipModule,
    MatIconModule,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
    TranslateModule,
  ],
  templateUrl: './notification-preferences.component.html',
  styleUrl: './notification-preferences.component.css',
})
export class NotificationPreferencesComponent implements OnInit, OnDestroy {
  protected readonly svc = inject(NotificationPreferenceService);
  private readonly toastr = inject(ToastrService);
  private readonly translate = inject(TranslateService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  /** Debounce buffer for per-category auto-save (500ms, UI notes). */
  private readonly pending$ = new Subject<IPendingChange>();

  /** Quiet Hours collapsible open/closed state. */
  protected readonly quietHoursOpen = signal(false);
  /** Reset-to-defaults confirm overlay visibility. */
  protected readonly showResetConfirm = signal(false);

  /** IANA timezone options for the selector. */
  protected timezones: string[] = buildTimezoneOptions(null);

  /** Quiet Hours form: enable + start/end times + timezone (FR-9). */
  protected readonly quietForm = this.fb.nonNullable.group({
    enabled: [false],
    start: ['22:00'],
    end: ['07:00'],
    timezone: [detectBrowserTimezone(), Validators.required],
  });

  ngOnInit(): void {
    this.svc.load();

    // Flush debounced toggle changes to the server (one PUT per category).
    this.pending$
      .pipe(debounceTime(500), takeUntilDestroyed(this.destroyRef))
      .subscribe((change) => this.persistCategory(change));

    // When the matrix loads, hydrate the Quiet Hours form from server state.
    // We poll a microtask after each load via the service signal; simplest is to
    // read it lazily in the template, but the reactive form needs explicit sync.
  }

  ngOnDestroy(): void {
    this.pending$.complete();
  }

  // ─── Quiet hours sync ─────────────────────────────────────

  /** Pull the latest Quiet Hours into the reactive form (called from template effect). */
  protected hydrateQuietForm(): void {
    const qh = this.svc.quietHours();
    this.timezones = buildTimezoneOptions(qh.timezone);
    this.quietForm.setValue(
      {
        enabled: qh.enabled,
        start: toInputTime(qh.start) || '22:00',
        end: toInputTime(qh.end) || '07:00',
        timezone: qh.timezone ?? detectBrowserTimezone(),
      },
      { emitEvent: false },
    );
  }

  // ─── Toggle handling (AC-2/AC-3, BR-3) ────────────────────

  /**
   * Optimistically flip a channel for a category, then queue the debounced save.
   * Mandatory rows are guarded in the template (disabled), but we also no-op here
   * defensively. The visual state updates immediately; persistCategory reverts if
   * the server rejects.
   */
  protected onToggle(
    category: ICategoryPreference,
    channel: NotificationChannel,
    checked: boolean,
  ): void {
    if (category.isMandatory) {
      return;
    }
    // Snapshot the authoritative row BEFORE we mutate it, so a server rejection
    // reverts to the last good state (not the optimistic one).
    const previous =
      this.svc.categories().find((r) => r.category === category.category) ??
      category;
    const next: ICategoryPreference = { ...category, [channel]: checked };
    this.svc.categories.update((rows) =>
      rows.map((r) => (r.category === category.category ? next : r)),
    );
    this.pending$.next({
      category: next.category,
      channelInApp: next.channelInApp,
      channelEmail: next.channelEmail,
      previous,
    });
  }

  /** Send the buffered change; revert the cached row + toast on a 400/422. */
  private persistCategory(change: IPendingChange): void {
    const before = change.previous;
    this.svc
      .updateCategory(change.category, {
        channelInApp: change.channelInApp,
        channelEmail: change.channelEmail,
      })
      .subscribe({
        next: () =>
          this.toastr.success(
            this.translate.instant('notificationPreferences.saved'),
          ),
        error: (err: HttpErrorResponse) => {
          // Revert to the row's prior server-truthful state (re-load is overkill
          // for a single field). For 400/422 surface the server's reason (BR-3/AC-3).
          if (before) {
            this.svc.categories.update((rows) =>
              rows.map((r) =>
                r.category === change.category ? before : r,
              ),
            );
          }
          if (err.status === 400 || err.status === 422) {
            const msg =
              this.serverMessage(err) ??
              this.translate.instant('notificationPreferences.minChannelError');
            this.toastr.error(msg);
          }
          // Other statuses are surfaced by the global error interceptor.
        },
      });
  }

  // ─── Quiet hours save (FR-9) ──────────────────────────────

  protected saveQuietHours(): void {
    if (this.quietForm.invalid) {
      this.quietForm.markAllAsTouched();
      return;
    }
    const v = this.quietForm.getRawValue();
    this.svc
      .updateQuietHours({
        enabled: v.enabled,
        start: v.enabled ? v.start : null,
        end: v.enabled ? v.end : null,
        timezone: v.enabled ? v.timezone : null,
      })
      .subscribe({
        next: () =>
          this.toastr.success(
            this.translate.instant('notificationPreferences.quietHoursSaved'),
          ),
        error: (err: HttpErrorResponse) => {
          const msg =
            this.serverMessage(err) ??
            this.translate.instant('notificationPreferences.quietHoursError');
          if (err.status === 400 || err.status === 422) {
            this.toastr.error(msg);
          }
        },
      });
  }

  protected toggleQuietHours(): void {
    if (!this.quietHoursOpen()) {
      this.hydrateQuietForm();
    }
    this.quietHoursOpen.update((v) => !v);
  }

  // ─── Reset to defaults (FR-7) ─────────────────────────────

  protected openResetConfirm(): void {
    this.showResetConfirm.set(true);
  }

  protected cancelReset(): void {
    this.showResetConfirm.set(false);
  }

  protected confirmReset(): void {
    this.svc.resetToDefaults().subscribe({
      next: () => {
        this.hydrateQuietForm();
        this.showResetConfirm.set(false);
        this.toastr.success(
          this.translate.instant('notificationPreferences.resetDone'),
        );
      },
      error: () => {
        this.showResetConfirm.set(false);
        this.toastr.error(
          this.translate.instant('notificationPreferences.resetError'),
        );
      },
    });
  }

  // ─── A11y helpers (NFR-5) ─────────────────────────────────

  /** ARIA label announcing category + channel + current state. */
  protected toggleAria(
    category: ICategoryPreference,
    channel: NotificationChannel,
  ): string {
    const channelKey =
      channel === 'channelInApp'
        ? 'notificationPreferences.channels.inApp'
        : 'notificationPreferences.channels.email';
    const channelName = this.translate.instant(channelKey);
    const stateKey = category[channel]
      ? 'notificationPreferences.state.on'
      : 'notificationPreferences.state.off';
    const state = this.translate.instant(stateKey);
    if (category.isMandatory) {
      return this.translate.instant('notificationPreferences.toggleAriaLocked', {
        category: category.categoryName,
        channel: channelName,
      });
    }
    return this.translate.instant('notificationPreferences.toggleAria', {
      category: category.categoryName,
      channel: channelName,
      state,
    });
  }

  /** trackBy for the category list. */
  protected trackByCategory(_: number, c: ICategoryPreference): string {
    return c.category;
  }

  private serverMessage(err: HttpErrorResponse): string | null {
    const body = err.error as { message?: string } | string | null;
    if (typeof body === 'string') return body || null;
    return body?.message ?? null;
  }
}
