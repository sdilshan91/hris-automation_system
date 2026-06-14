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
  formatElapsed,
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
        {{ isClockedIn() ? 'You are clocked in' : 'Ready to start your day?' }}
      </h2>

      @if (isLoading()) {
        <!-- Skeleton while status loads -->
        <div class="mt-5 skeleton-line w-full" aria-busy="true" aria-live="polite"></div>
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
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

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

  // ─── Computed ─────────────────────────────────────────────

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
