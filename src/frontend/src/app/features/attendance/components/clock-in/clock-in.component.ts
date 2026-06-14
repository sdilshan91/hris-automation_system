import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import {
  IClockInRequest,
  IClockStatus,
  IGeolocationResult,
  IAttendanceLog,
  IClockOutRequest,
  IClockOutResult,
  ClockOutStatus,
  formatElapsed,
  formatWorkMinutes,
  buildStaticMapUrl,
} from '../../models/attendance.models';

/**
 * US-ATT-001: Employee self clock-in card (§8 — Notion-like large primary action).
 *
 * Behaviour:
 *  - Prominent "Clock In" button that, on success, transforms into a live elapsed
 *    work-time timer (§8) ticking once per second.
 *  - Shows the current shift name + expected start near the button for context (§8);
 *    placeholder text when shift data is not yet available (US-ATT-005 dependency).
 *  - Geolocation (browser API):
 *      AC-3 — tenant requires geo + permission denied  -> BLOCK with an explanatory message.
 *      AC-4 — geo optional + permission denied          -> proceed WITHOUT coordinates.
 *      On success with coordinates -> small static OSM map preview (no maps dependency).
 *  - AC-2 — backend "already clocked in" (409) -> inline error + reflect clocked-in state.
 *  - AC-5 — backend IP-allowlist rejection (403) -> non-intrusive inline error + help link.
 *
 * The backend is the source of truth for geo-fence (FR-3) and IP allowlist (FR-4); the FE
 * only branches on the tenant `requireGeolocation` flag for the denied-permission case.
 *
 * US-ATT-002 (Clock-Out, §8) extends this same card:
 *  - When clocked in, a warm-coloured "Clock Out" button sits below the live timer,
 *    replacing the green "Clock In" primary action (which only shows when clocked out).
 *  - On success (AC-1) the card fades in a summary: clock-in/out times (UTC -> local,
 *    NFR-5), total hours ("7h 45m"), overtime if any, and a Notion-style status pill
 *    (green Complete / amber Short Day / blue Overtime / red Anomaly).
 *  - AC-2: a "no open record" rejection shows the message inline and resets to clock-in.
 *  - AC-5: when the tenant policy requires geolocation, coordinates are captured (same
 *    seam as clock-in) and sent; otherwise omitted. The timer remains client-side (§8).
 */
@Component({
  selector: 'app-clock-in',
  standalone: true,
  imports: [CommonModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('swap', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.96)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'scale(1)' })),
      ]),
    ]),
  ],
  template: `
    <div class="clock-card" @fadeIn>
      <!-- Shift context (§8). Placeholder until US-ATT-005 supplies shift data. -->
      <div class="flex items-center gap-2 text-sm text-neutral-500">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
          class="w-4 h-4 text-neutral-400" aria-hidden="true">
          <path fill-rule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-13a.75.75 0 0 0-1.5 0v5c0 .414.336.75.75.75h4a.75.75 0 0 0 0-1.5h-3.25V5Z" clip-rule="evenodd"/>
        </svg>
        <span>{{ shiftLabel() }}</span>
      </div>

      <!-- Title -->
      <h2 class="mt-2 text-lg font-semibold text-neutral-900 tracking-tight">
        {{ cardTitle() }}
      </h2>

      @if (isLoading()) {
        <!-- Skeleton while status loads -->
        <div class="mt-5 skeleton-line w-full" aria-busy="true" aria-live="polite"></div>
      } @else if (summary(); as result) {
        <!-- US-ATT-002 AC-1: post clock-out summary card (fade-in, §8) -->
        <div class="mt-5 summary" @fadeIn>
          <div class="flex items-center justify-between gap-3">
            <p class="text-xs font-medium text-neutral-400 uppercase tracking-wide">Today's work summary</p>
            <!-- Notion-style status pill (§8) -->
            <span class="pill" [ngClass]="pillClass()" role="status">
              <span class="pill-dot" aria-hidden="true"></span>
              {{ statusLabel() }}
            </span>
          </div>

          <p class="total-hours">{{ totalHoursLabel() }}</p>

          @if (overtimeLabel(); as ot) {
            <p class="mt-1 text-sm font-medium text-blue-600">+{{ ot }} overtime</p>
          }
          @if (result.status === 'ANOMALY') {
            <p class="mt-2 anomaly-flag" role="alert">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                class="w-4 h-4 flex-shrink-0" aria-hidden="true">
                <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd"/>
              </svg>
              Flagged for review — please confirm with HR.
            </p>
          }

          <dl class="mt-4 grid grid-cols-2 gap-3">
            <div class="time-cell">
              <dt class="time-label">Clock in</dt>
              <dd class="time-value">{{ summaryClockIn() | date: 'shortTime' }}</dd>
            </div>
            <div class="time-cell">
              <dt class="time-label">Clock out</dt>
              <dd class="time-value">{{ summaryClockOut() | date: 'shortTime' }}</dd>
            </div>
          </dl>
        </div>
      } @else if (isClockedIn()) {
        <!-- Live elapsed-time timer (§8 transform) -->
        <div class="mt-5" @swap>
          <p class="text-xs font-medium text-neutral-400 uppercase tracking-wide">Elapsed work time</p>
          <p class="timer" role="timer" aria-live="off">{{ elapsed() }}</p>
          @if (clockedInAtLocal()) {
            <p class="mt-1 text-sm text-neutral-500">
              Clocked in at {{ clockedInAtLocal() | date: 'shortTime' }}
            </p>
          }
        </div>

        <!-- Map preview after a successful geo-captured clock-in (§8) -->
        @if (mapUrl(); as url) {
          <div class="mt-4 map-preview" @swap>
            <iframe
              [src]="url"
              title="Clock-in location preview"
              class="map-frame"
              loading="lazy"
              referrerpolicy="no-referrer-when-downgrade"></iframe>
            <p class="map-caption">Location captured at clock-in</p>
          </div>
        }

        <!-- US-ATT-002 §8: warm "Clock Out" button replacing the green primary action -->
        <button
          type="button"
          class="clock-out-btn"
          [disabled]="isSubmitting()"
          (click)="onClockOut()"
          aria-label="Clock out">
          @if (isSubmitting()) {
            <span class="btn-spinner"></span> Clocking out...
          } @else {
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
              class="w-5 h-5 mr-2" aria-hidden="true">
              <path fill-rule="evenodd" d="M3 4.25A2.25 2.25 0 0 1 5.25 2h5.5A2.25 2.25 0 0 1 13 4.25v2a.75.75 0 0 1-1.5 0v-2a.75.75 0 0 0-.75-.75h-5.5a.75.75 0 0 0-.75.75v11.5c0 .414.336.75.75.75h5.5a.75.75 0 0 0 .75-.75v-2a.75.75 0 0 1 1.5 0v2A2.25 2.25 0 0 1 10.75 18h-5.5A2.25 2.25 0 0 1 3 15.75V4.25Z" clip-rule="evenodd"/>
              <path fill-rule="evenodd" d="M19 10a.75.75 0 0 0-.75-.75H8.704l1.048-.943a.75.75 0 1 0-1.004-1.114l-2.5 2.25a.75.75 0 0 0 0 1.114l2.5 2.25a.75.75 0 1 0 1.004-1.114l-1.048-.943h9.546A.75.75 0 0 0 19 10Z" clip-rule="evenodd"/>
            </svg>
            Clock Out
          }
        </button>
      } @else {
        <!-- Primary Clock In action (§8 — full-width, >=48px touch target) -->
        <button
          type="button"
          class="clock-btn"
          [disabled]="isSubmitting()"
          (click)="onClockIn()"
          aria-label="Clock in">
          @if (isSubmitting()) {
            <span class="btn-spinner"></span> Clocking in...
          } @else {
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
              class="w-5 h-5 mr-2" aria-hidden="true">
              <path fill-rule="evenodd" d="M2 10a8 8 0 1 1 16 0 8 8 0 0 1-16 0Zm6.39-2.908a.75.75 0 0 1 .766.027l3.5 2.25a.75.75 0 0 1 0 1.262l-3.5 2.25A.75.75 0 0 1 8 12.25v-4.5a.75.75 0 0 1 .39-.658Z" clip-rule="evenodd"/>
            </svg>
            Clock In
          }
        </button>
      }

      <!-- Inline error (AC-2 duplicate, AC-3 geo-required denied, AC-5 IP block) -->
      @if (errorMessage(); as msg) {
        <div class="error-banner" role="alert" @swap>
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
            class="w-5 h-5 text-red-500 flex-shrink-0 mt-0.5" aria-hidden="true">
            <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-7 4a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm-1-9a.75.75 0 0 0-.75.75v3.5a.75.75 0 0 0 1.5 0v-3.5A.75.75 0 0 0 10 5Z" clip-rule="evenodd"/>
          </svg>
          <div class="flex-1">
            <p class="text-sm text-red-700">{{ msg }}</p>
            @if (showIpHelp()) {
              <a href="/help/attendance/network-locations" target="_blank" rel="noopener"
                class="help-link">Why is my network blocked?</a>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .clock-card {
      @apply w-full rounded-xl bg-white border border-neutral-100 shadow-sm p-5 sm:p-6;
    }
    .shift-placeholder { @apply text-neutral-400 italic; }

    .timer {
      @apply mt-1 text-4xl sm:text-5xl font-semibold text-neutral-900 tabular-nums tracking-tight;
      font-variant-numeric: tabular-nums;
    }

    .clock-btn {
      @apply mt-5 w-full inline-flex items-center justify-center rounded-xl bg-brand-600
        px-6 text-base font-semibold text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 active:scale-[0.99]
        disabled:opacity-50 disabled:cursor-not-allowed
        focus:outline-none focus:ring-2 focus:ring-brand-500/30;
      min-height: 52px; /* >=48px touch target (§8, NFR-5) */
    }
    /* US-ATT-002 §8: warm clock-out action (distinct from the green clock-in). */
    .clock-out-btn {
      @apply mt-5 w-full inline-flex items-center justify-center rounded-xl bg-red-600
        px-6 text-base font-semibold text-white shadow-sm transition-all duration-200
        hover:bg-red-700 active:scale-[0.99]
        disabled:opacity-50 disabled:cursor-not-allowed
        focus:outline-none focus:ring-2 focus:ring-red-500/30;
      min-height: 52px; /* >=48px touch target (§8, NFR-5) */
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* US-ATT-002 §8: post clock-out summary card. */
    .summary {
      @apply rounded-xl bg-neutral-50 border border-neutral-100 p-4 sm:p-5;
    }
    .total-hours {
      @apply mt-3 text-3xl sm:text-4xl font-semibold text-neutral-900 tabular-nums tracking-tight;
      font-variant-numeric: tabular-nums;
    }
    .anomaly-flag {
      @apply inline-flex items-center gap-1.5 text-sm font-medium text-red-600;
    }
    .time-cell { @apply rounded-lg bg-white border border-neutral-100 px-3 py-2; }
    .time-label { @apply text-xs font-medium text-neutral-400 uppercase tracking-wide; }
    .time-value { @apply mt-0.5 text-base font-semibold text-neutral-800 tabular-nums; }

    /* Notion-style inline status pills (§8). */
    .pill {
      @apply inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium;
    }
    .pill-dot { @apply w-1.5 h-1.5 rounded-full bg-current; }
    .pill-complete { @apply bg-green-50 text-green-700; }
    .pill-short    { @apply bg-amber-50 text-amber-700; }
    .pill-overtime { @apply bg-blue-50 text-blue-700; }
    .pill-anomaly  { @apply bg-red-50 text-red-700; }

    .map-preview { @apply rounded-lg overflow-hidden border border-neutral-100; }
    .map-frame { @apply w-full h-40 border-0 block; }
    .map-caption { @apply text-xs text-neutral-400 px-3 py-2 bg-neutral-50; }

    .error-banner {
      @apply mt-4 bg-red-50 border border-red-100 rounded-lg p-3 flex items-start gap-2.5;
    }
    .help-link {
      @apply inline-block mt-1 text-xs font-medium text-red-600 underline hover:text-red-700;
    }

    .skeleton-line {
      @apply rounded-xl bg-neutral-200;
      height: 52px;
      animation: shimmer 1.5s ease-in-out infinite;
    }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class ClockInComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly destroy$ = new Subject<void>();

  private timerHandle: ReturnType<typeof setInterval> | null = null;

  // ─── State signals ────────────────────────────────────────
  readonly isLoading = signal(true);
  readonly isSubmitting = signal(false);
  readonly isClockedIn = signal(false);

  /** Tenant policy (BR-2): when true a denied geolocation permission blocks clock-in (AC-3). */
  private readonly requireGeolocation = signal(false);

  /** UTC timestamp of the open clock-in record; drives the live elapsed timer. */
  private readonly clockedInAt = signal<string | null>(null);

  /** Shift context for the header (§8). */
  private readonly shiftName = signal<string | null>(null);
  private readonly shiftStart = signal<string | null>(null);

  /** Inline error message (AC-2, AC-3, AC-5). */
  readonly errorMessage = signal<string | null>(null);
  /** AC-5: show the "network locations" help link only for the IP-block error. */
  readonly showIpHelp = signal(false);

  /** Sanitized static map URL shown after a geo-captured clock-in (§8). Null otherwise. */
  readonly mapUrl = signal<SafeResourceUrl | null>(null);

  /** Live elapsed-time string, recomputed every tick (§8). */
  readonly elapsed = signal('00:00:00');

  /** US-ATT-002 AC-1: clock-out result; non-null swaps the card to the summary view. */
  readonly summary = signal<IClockOutResult | null>(null);

  // ─── Computed ─────────────────────────────────────────────

  /** Card heading reflects the three states: summary, clocked-in, ready (§8). */
  readonly cardTitle = computed(() => {
    if (this.summary()) {
      return 'Your day is wrapped up';
    }
    return this.isClockedIn() ? 'You are clocked in' : 'Ready to start your day?';
  });

  // ─── US-ATT-002 summary computeds ─────────────────────────

  /** Local Date of the summary clock-in for display (NFR-5: UTC -> local). */
  readonly summaryClockIn = computed(() => {
    const s = this.summary();
    return s ? new Date(s.clockIn) : null;
  });

  /** Local Date of the summary clock-out for display (NFR-5: UTC -> local). */
  readonly summaryClockOut = computed(() => {
    const s = this.summary();
    return s ? new Date(s.clockOut) : null;
  });

  /** Total worked hours formatted "7h 45m" (AC-1). */
  readonly totalHoursLabel = computed(() => {
    const s = this.summary();
    return s ? formatWorkMinutes(s.totalWorkMinutes) : '';
  });

  /** Overtime label (AC-3); empty when there is no overtime. */
  readonly overtimeLabel = computed(() => {
    const ot = this.summary()?.overtimeMinutes ?? 0;
    return ot > 0 ? formatWorkMinutes(ot) : '';
  });

  /** Status pill text (§8). */
  readonly statusLabel = computed(() => this.labelForStatus(this.summary()?.status));

  /** Status pill colour class (§8). */
  readonly pillClass = computed(() => {
    switch (this.summary()?.status) {
      case 'OVERTIME':
        return 'pill-overtime';
      case 'SHORT_DAY':
        return 'pill-short';
      case 'ANOMALY':
        return 'pill-anomaly';
      default:
        return 'pill-complete';
    }
  });

  /** Local Date of the open clock-in for display (FR-7). */
  readonly clockedInAtLocal = computed(() => {
    const ts = this.clockedInAt();
    return ts ? new Date(ts) : null;
  });

  /** Shift context label (§8); placeholder until US-ATT-005 supplies real data. */
  readonly shiftLabel = computed(() => {
    const name = this.shiftName();
    const start = this.shiftStart();
    if (name && start) {
      return `${name} · starts ${start}`;
    }
    if (name) {
      return name;
    }
    return 'No shift assigned';
  });

  // ─── Lifecycle ────────────────────────────────────────────

  ngOnInit(): void {
    this.loadStatus();
  }

  ngOnDestroy(): void {
    this.stopTimer();
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ─────────────────────────────────────────

  loadStatus(): void {
    this.isLoading.set(true);
    this.attendanceService
      .getStatus()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (status: IClockStatus) => {
          this.applyStatus(status);
          this.isLoading.set(false);
        },
        error: () => {
          // Non-blocking: default to a not-clocked-in card so the employee can still act.
          this.isLoading.set(false);
          this.toastr.error('Could not load your attendance status.');
        },
      });
  }

  private applyStatus(status: IClockStatus): void {
    this.requireGeolocation.set(status.requireGeolocation);
    this.shiftName.set(status.shiftName);
    this.shiftStart.set(status.shiftStart);
    if (status.isClockedIn && status.clockedInAt) {
      this.enterClockedInState(status.clockedInAt);
    }
  }

  // ─── Clock-in flow (AC-1, AC-3, AC-4) ─────────────────────

  async onClockIn(): Promise<void> {
    if (this.isSubmitting()) {
      return;
    }
    this.errorMessage.set(null);
    this.showIpHelp.set(false);
    this.isSubmitting.set(true);

    const geo = await this.captureGeolocation();

    // AC-3: geo is required by tenant policy but the browser permission was denied.
    if (this.requireGeolocation() && !geo.granted) {
      this.isSubmitting.set(false);
      this.errorMessage.set(
        'Location access is required to clock in for your organization. ' +
          'Please enable location permission in your browser and try again.',
      );
      return;
    }

    // AC-4: geo optional and denied -> proceed without coordinates.
    const request: IClockInRequest = {
      latitude: geo.coords?.latitude ?? null,
      longitude: geo.coords?.longitude ?? null,
      source: 'WEB',
    };

    this.sendClockIn(request, geo);
  }

  private sendClockIn(request: IClockInRequest, geo: IGeolocationResult): void {
    this.attendanceService
      .clockIn(request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (log: IAttendanceLog) => {
          this.isSubmitting.set(false);
          this.onClockedIn(log, geo);
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmitting.set(false);
          this.handleClockInError(err);
        },
      });
  }

  /** Success (AC-1): subtle toast (not a modal), transform to live timer, show map. */
  private onClockedIn(log: IAttendanceLog, geo: IGeolocationResult): void {
    this.toastr.success(
      `Clocked in at ${new Date(log.clockIn).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}.`,
      'Clock-in recorded',
    );
    this.enterClockedInState(log.clockIn);

    // §8: show a small map preview when coordinates were captured.
    if (geo.coords) {
      const raw = buildStaticMapUrl(geo.coords.latitude, geo.coords.longitude);
      this.mapUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(raw));
    }
  }

  /**
   * AC-2: duplicate clock-in (409) -> inline error + reflect clocked-in state.
   * AC-5: IP-allowlist rejection (403 `ip_not_allowed`) -> inline error + help link.
   * Geo-fence (FR-3) and all other errors -> inline error verbatim.
   */
  private handleClockInError(err: HttpErrorResponse): void {
    const parsed = AttendanceService.parseError(err);
    const message = parsed?.message ?? 'An unexpected error occurred.';
    this.errorMessage.set(message);

    if (parsed?.code === 'already_clocked_in' || err.status === 409) {
      // AC-2: reflect the already-clocked-in state in the UI.
      this.isClockedIn.set(true);
      if (!this.clockedInAt()) {
        // Best-effort: re-fetch the open record's real timestamp for an accurate timer.
        this.loadStatus();
      }
    }

    if (parsed?.code === 'ip_not_allowed') {
      this.showIpHelp.set(true);
    }
  }

  // ─── Clock-out flow (US-ATT-002 AC-1, AC-2, AC-5) ─────────

  async onClockOut(): Promise<void> {
    if (this.isSubmitting()) {
      return;
    }
    this.errorMessage.set(null);
    this.showIpHelp.set(false);
    this.isSubmitting.set(true);

    // AC-5: capture geolocation only when the tenant policy requires it; otherwise omit.
    let latitude: number | null = null;
    let longitude: number | null = null;
    if (this.requireGeolocation()) {
      const geo = await this.captureGeolocation();
      if (!geo.granted) {
        this.isSubmitting.set(false);
        this.errorMessage.set(
          'Location access is required to clock out for your organization. ' +
            'Please enable location permission in your browser and try again.',
        );
        return;
      }
      latitude = geo.coords?.latitude ?? null;
      longitude = geo.coords?.longitude ?? null;
    }

    const request: IClockOutRequest = { latitude, longitude };
    this.sendClockOut(request);
  }

  private sendClockOut(request: IClockOutRequest): void {
    this.attendanceService
      .clockOut(request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result: IClockOutResult) => {
          this.isSubmitting.set(false);
          this.onClockedOut(result);
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmitting.set(false);
          this.handleClockOutError(err);
        },
      });
  }

  /** Success (AC-1): stop the live timer, swap to the summary card, toast. */
  private onClockedOut(result: IClockOutResult): void {
    this.stopTimer();
    this.isClockedIn.set(false);
    this.mapUrl.set(null);
    this.summary.set(result);
    this.toastr.success(
      `${formatWorkMinutes(result.totalWorkMinutes)} recorded · ${this.labelForStatus(result.status)}.`,
      'Clock-out recorded',
    );
  }

  /**
   * AC-2: no open record (`no_active_clock_in` / HTTP 404) -> inline error + reset to clock-in state.
   * Any other error -> inline message verbatim, staying in the clocked-in state.
   */
  private handleClockOutError(err: HttpErrorResponse): void {
    const parsed = AttendanceService.parseError(err);
    this.errorMessage.set(parsed?.message ?? 'An unexpected error occurred.');

    if (parsed?.code === 'no_active_clock_in' || err.status === 404) {
      // AC-2: there is no open record server-side -> reset the card to clock-in.
      this.stopTimer();
      this.summary.set(null);
      this.isClockedIn.set(false);
      this.clockedInAt.set(null);
      this.mapUrl.set(null);
    }
  }

  /** Map a clock-out status to its pill label (§8). */
  private labelForStatus(status: ClockOutStatus | undefined): string {
    switch (status) {
      case 'OVERTIME':
        return 'Overtime';
      case 'SHORT_DAY':
        return 'Short Day';
      case 'ANOMALY':
        return 'Anomaly';
      default:
        return 'Complete';
    }
  }

  // ─── Clocked-in state + live timer ────────────────────────

  private enterClockedInState(clockInUtc: string): void {
    this.clockedInAt.set(clockInUtc);
    this.isClockedIn.set(true);
    this.startTimer();
  }

  private startTimer(): void {
    this.tickTimer();
    this.stopTimer();
    this.timerHandle = setInterval(() => this.tickTimer(), 1000);
  }

  private tickTimer(): void {
    const ts = this.clockedInAt();
    if (!ts) {
      return;
    }
    const start = new Date(ts).getTime();
    this.elapsed.set(formatElapsed(Date.now() - start));
  }

  private stopTimer(): void {
    if (this.timerHandle !== null) {
      clearInterval(this.timerHandle);
      this.timerHandle = null;
    }
  }

  // ─── Browser Geolocation (mockable seam for tests) ────────

  /**
   * Read the current position via the browser Geolocation API (AC-3, AC-4).
   * Resolves (never rejects) to an IGeolocationResult so the caller can branch
   * cleanly on `granted`/`denied`. NFR-3: the browser enforces HTTPS + consent.
   */
  private captureGeolocation(): Promise<IGeolocationResult> {
    return new Promise<IGeolocationResult>((resolve) => {
      const geolocation = navigator.geolocation;
      if (!geolocation) {
        resolve({ granted: false, denied: true, coords: null, error: 'Geolocation is not supported by this browser.' });
        return;
      }
      geolocation.getCurrentPosition(
        (position) => {
          resolve({
            granted: true,
            denied: false,
            coords: {
              latitude: position.coords.latitude,
              longitude: position.coords.longitude,
            },
            error: null,
          });
        },
        (error) => {
          resolve({
            granted: false,
            denied: true,
            coords: null,
            error: error?.message ?? 'Location permission denied.',
          });
        },
        { enableHighAccuracy: false, timeout: 10000, maximumAge: 60000 },
      );
    });
  }
}
