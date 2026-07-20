import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { trigger, transition, style, animate } from '@angular/animations';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ToastrService } from 'ngx-toastr';
import { PayslipService } from '../../services/payslip.service';
import {
  IPayslip,
  IPayslipGenerationStatus,
  PdfStatus,
  PDF_STATUS_BADGE,
  PDF_STATUS_LABELS,
} from '../../models/payslip.models';
import { TrappedDialogDirective } from '../../../../shared/directives';

/**
 * US-PAY-004 (§8): Payslip list within a payroll run-detail view.
 *
 * Embedded as a child of PayrollRunDetailComponent via `[runId]` + `[canGenerate]`
 * (same self-contained child pattern as employee-compensation). Responsibilities:
 *
 * - A searchable/filterable Notion-style table (AC-3): Employee Name | Employee No |
 *   Department | Net Salary | PDF Status badge | Actions (View / Download). Search
 *   matches name/number/department; a status filter narrows by PDF status.
 * - "Generate Payslips" / "Regenerate Payslips" (AC-1/AC-5) — enqueues the backend
 *   job, then shows a generation status bar ("4,850 / 5,000 generated") fed by
 *   `streamGenerationStatus` (POLLING; the single SignalR swap point). The
 *   subscription is torn down on destroy and when the stream completes.
 * - Per-employee preview: an in-browser PDF viewer in a modal (an <iframe> bound to
 *   a blob URL from the download endpoint) before download (§8).
 * - Per-employee PDF download (anchor click, BR-5 filename) and a desktop-only
 *   "Download All" ZIP (AC-3); the bulk button is hidden on mobile (§8).
 *
 * Binary downloads stub `.click()` in specs (known pattern). The preview blob URL is
 * created by us from our own PDF, so binding it to the iframe via
 * `bypassSecurityTrustResourceUrl` is safe (no user content; NFR-5 — PDFs carry no
 * executable content).
 */
@Component({
  selector: 'app-payslip-list',
  standalone: true,
  imports: [CommonModule, FormsModule, TrappedDialogDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('180ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 })),
      ]),
    ]),
  ],
  template: `
    <section class="mt-2" @fadeIn>
      <!-- Header: title + generate / download-all actions -->
      <div
        class="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
      >
        <h2 class="text-sm font-semibold text-neutral-900">Payslips</h2>
        <div class="flex flex-wrap items-center gap-2">
          <!-- Download All ZIP — desktop only (§8) -->
          <button
            type="button"
            class="hidden items-center gap-1.5 rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm font-medium text-neutral-700 shadow-sm transition hover:bg-neutral-50 disabled:opacity-50 sm:inline-flex"
            [disabled]="!hasGenerated() || zipDownloading()"
            (click)="downloadAll()"
          >
            @if (zipDownloading()) {
              <span class="btn-spinner"></span>
            }
            Download all (ZIP)
          </button>

          <!-- Generate / Regenerate (AC-1 / AC-5) -->
          @if (canGenerate()) {
            <button
              type="button"
              class="inline-flex items-center gap-1.5 rounded-lg px-3 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              [disabled]="generating()"
              (click)="generate()"
            >
              @if (generating()) {
                <span class="btn-spinner btn-spinner--light"></span>
              }
              {{ hasGenerated() ? 'Regenerate payslips' : 'Generate payslips' }}
            </button>
          }
        </div>
      </div>

      <!-- Mobile note: bulk download unavailable (§8) -->
      <p class="mb-3 text-xs text-neutral-400 sm:hidden">
        Bulk ZIP download is available on desktop. You can still view and
        download individual payslips here.
      </p>

      <!-- Generation status bar (§8: "4,850 / 5,000 generated") -->
      @if (genStatus(); as g) {
        @if (g.isGenerating || generating()) {
          <div
            @fadeIn
            class="mb-4 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
          >
            <div class="mb-2 flex items-center justify-between">
              <p class="text-sm font-medium text-neutral-900">
                {{ g.generatedCount | number }} /
                {{ g.totalCount | number }} generated
              </p>
              <span class="text-sm font-medium text-neutral-500"
                >{{ genPercent() }}%</span
              >
            </div>
            <div
              class="h-2.5 w-full overflow-hidden rounded-full bg-neutral-100"
              role="progressbar"
              [attr.aria-valuenow]="genPercent()"
              aria-valuemin="0"
              aria-valuemax="100"
            >
              <div
                class="h-full rounded-full transition-[width] duration-500 ease-out"
                [style.width.%]="genPercent()"
                [style.background-color]="'var(--brand-primary)'"
              ></div>
            </div>
            @if (g.failedCount > 0) {
              <p class="mt-2 text-xs text-rose-600">
                {{ g.failedCount | number }} failed to generate.
              </p>
            }
          </div>
        }
      }

      <!-- Search + status filter -->
      <div class="mb-3 flex flex-col gap-2 sm:flex-row sm:items-center">
        <div class="relative flex-1">
          <input
            type="search"
            [ngModel]="search()"
            (ngModelChange)="search.set($event)"
            placeholder="Search by name, number, or department"
            aria-label="Search payslips"
            class="w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900 shadow-sm transition placeholder:text-neutral-400 focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-900/5"
          />
        </div>
        <div class="flex items-center gap-1.5" role="group" aria-label="Filter by PDF status">
          @for (f of statusFilters; track f.value) {
            <button
              type="button"
              class="rounded-full px-3 py-1.5 text-xs font-medium ring-1 ring-inset transition"
              [class]="
                statusFilter() === f.value
                  ? 'bg-neutral-900 text-white ring-neutral-900'
                  : 'bg-white text-neutral-600 ring-neutral-200 hover:bg-neutral-50'
              "
              [attr.aria-pressed]="statusFilter() === f.value"
              (click)="statusFilter.set(f.value)"
            >
              {{ f.label }}
            </button>
          }
        </div>
      </div>

      <!-- Loading -->
      @if (loading()) {
        <div class="space-y-2">
          @for (i of [1, 2, 3, 4]; track i) {
            <div class="h-12 animate-pulse rounded-lg bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="loadPayslips()">
            Retry
          </button>
        </div>
      } @else if (filtered().length === 0) {
        <div
          class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-4 py-10 text-center text-sm text-neutral-500"
        >
          @if (payslips().length === 0) {
            No payslips yet. Generate payslips to create per-employee documents.
          } @else {
            No payslips match your search.
          }
        </div>
      } @else {
        <!-- Notion-style table (desktop) -->
        <div
          class="hidden overflow-hidden rounded-xl border border-neutral-200 bg-white shadow-sm sm:block"
        >
          <table class="w-full text-sm">
            <thead>
              <tr class="border-b border-neutral-100 text-left text-xs text-neutral-500">
                <th class="px-4 py-3 font-medium">Employee</th>
                <th class="px-4 py-3 font-medium">Employee no</th>
                <th class="px-4 py-3 font-medium">Department</th>
                <th class="px-4 py-3 text-right font-medium">Net salary</th>
                <th class="px-4 py-3 font-medium">PDF status</th>
                <th class="px-4 py-3 text-right font-medium">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (p of filtered(); track p.id) {
                <tr class="border-b border-neutral-50 transition hover:bg-neutral-50/60">
                  <td class="px-4 py-3 font-medium text-neutral-900">
                    {{ p.employeeName }}
                  </td>
                  <td class="px-4 py-3 text-neutral-600">{{ p.employeeNo }}</td>
                  <td class="px-4 py-3 text-neutral-600">
                    {{ p.department || '—' }}
                  </td>
                  <td class="px-4 py-3 text-right tabular-nums text-neutral-900">
                    {{ p.netSalary | number: '1.2-2' }}
                  </td>
                  <td class="px-4 py-3">
                    <span
                      class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                      [class]="statusBadge[p.pdfStatus]"
                    >
                      {{ statusLabels[p.pdfStatus] }}
                    </span>
                  </td>
                  <td class="px-4 py-3">
                    <div class="flex items-center justify-end gap-1.5">
                      <button
                        type="button"
                        class="rounded-md px-2 py-1 text-xs font-medium text-neutral-700 transition hover:bg-neutral-100 disabled:opacity-40"
                        [disabled]="p.pdfStatus !== 'Generated'"
                        (click)="preview(p)"
                      >
                        View
                      </button>
                      <button
                        type="button"
                        class="rounded-md px-2 py-1 text-xs font-medium text-neutral-700 transition hover:bg-neutral-100 disabled:opacity-40"
                        [disabled]="
                          p.pdfStatus !== 'Generated' || downloadingId() === p.id
                        "
                        (click)="download(p)"
                      >
                        Download
                      </button>
                      @if (p.pdfStatus === 'Failed' && canGenerate()) {
                        <button
                          type="button"
                          class="rounded-md px-2 py-1 text-xs font-medium text-rose-700 transition hover:bg-rose-50 disabled:opacity-40"
                          [disabled]="retryingId() === p.id || generating()"
                          (click)="retry(p)"
                        >
                          Retry
                        </button>
                      }
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Stacked cards (mobile) -->
        <ul class="space-y-2 sm:hidden">
          @for (p of filtered(); track p.id) {
            <li class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0">
                  <p class="truncate font-medium text-neutral-900">
                    {{ p.employeeName }}
                  </p>
                  <p class="text-xs text-neutral-500">
                    {{ p.employeeNo }} · {{ p.department || '—' }}
                  </p>
                </div>
                <span
                  class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                  [class]="statusBadge[p.pdfStatus]"
                >
                  {{ statusLabels[p.pdfStatus] }}
                </span>
              </div>
              <div class="mt-3 flex items-center justify-between">
                <span class="text-sm font-semibold tabular-nums text-neutral-900">
                  {{ p.netSalary | number: '1.2-2' }}
                </span>
                <div class="flex items-center gap-1.5">
                  <button
                    type="button"
                    class="rounded-md px-2 py-1 text-xs font-medium text-neutral-700 transition hover:bg-neutral-100 disabled:opacity-40"
                    [disabled]="p.pdfStatus !== 'Generated'"
                    (click)="preview(p)"
                  >
                    View
                  </button>
                  <button
                    type="button"
                    class="rounded-md px-2 py-1 text-xs font-medium text-neutral-700 transition hover:bg-neutral-100 disabled:opacity-40"
                    [disabled]="
                      p.pdfStatus !== 'Generated' || downloadingId() === p.id
                    "
                    (click)="download(p)"
                  >
                    Download
                  </button>
                  @if (p.pdfStatus === 'Failed' && canGenerate()) {
                    <button
                      type="button"
                      class="rounded-md px-2 py-1 text-xs font-medium text-rose-700 transition hover:bg-rose-50 disabled:opacity-40"
                      [disabled]="retryingId() === p.id || generating()"
                      (click)="retry(p)"
                    >
                      Retry
                    </button>
                  }
                </div>
              </div>
            </li>
          }
        </ul>
      }

      <!-- PDF preview modal (§8) -->
      @if (previewSlip(); as ps) {
        <div
          class="fixed inset-0 z-50 flex items-center justify-center p-4"
          role="dialog"
          aria-modal="true"
          aria-label="Payslip preview"
          appTrappedDialog
          (dismiss)="closePreview()"
        >
          <div
            @backdrop
            class="absolute inset-0 bg-neutral-900/40"
            (click)="closePreview()"
          ></div>
          <div
            @fadeIn
            class="relative z-10 flex h-[85vh] w-full max-w-3xl flex-col overflow-hidden rounded-xl bg-white shadow-xl"
          >
            <div
              class="flex items-center justify-between border-b border-neutral-100 px-4 py-3"
            >
              <div class="min-w-0">
                <p class="truncate text-sm font-semibold text-neutral-900">
                  {{ ps.employeeName }}
                </p>
                <p class="text-xs text-neutral-500">{{ ps.employeeNo }}</p>
              </div>
              <div class="flex items-center gap-2">
                <button
                  type="button"
                  class="rounded-lg px-3 py-1.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                  [style.background-color]="'var(--brand-primary)'"
                  [disabled]="downloadingId() === ps.id"
                  (click)="download(ps)"
                >
                  Download
                </button>
                <button
                  type="button"
                  class="rounded-lg p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
                  aria-label="Close preview"
                  (click)="closePreview()"
                >
                  ✕
                </button>
              </div>
            </div>
            <div class="flex-1 bg-neutral-100">
              @if (previewLoading()) {
                <div class="flex h-full items-center justify-center">
                  <span class="btn-spinner"></span>
                </div>
              } @else if (previewUrl()) {
                <iframe
                  [src]="previewUrl()"
                  class="h-full w-full"
                  title="Payslip PDF preview"
                ></iframe>
              } @else {
                <div
                  class="flex h-full items-center justify-center px-4 text-center text-sm text-neutral-500"
                >
                  Could not load the payslip preview. Try downloading it
                  instead.
                </div>
              }
            </div>
          </div>
        </div>
      }
    </section>
  `,
  styles: [
    `
      .btn-spinner {
        display: inline-block;
        width: 0.85rem;
        height: 0.85rem;
        border: 2px solid rgba(0, 0, 0, 0.15);
        border-top-color: rgba(0, 0, 0, 0.55);
        border-radius: 9999px;
        animation: btn-spin 0.6s linear infinite;
      }
      .btn-spinner--light {
        border-color: rgba(255, 255, 255, 0.4);
        border-top-color: #fff;
      }
      @keyframes btn-spin {
        to {
          transform: rotate(360deg);
        }
      }
    `,
  ],
})
export class PayslipListComponent implements OnInit, OnDestroy {
  private readonly payslipService = inject(PayslipService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  /** The run whose payslips we list. */
  readonly runId = input.required<string>();
  /** Generation allowed only on a non-finalized run (BR-1); set by the parent. */
  readonly canGenerate = input<boolean>(true);

  readonly statusBadge = PDF_STATUS_BADGE;
  readonly statusLabels = PDF_STATUS_LABELS;
  readonly statusFilters: { value: PdfStatus | 'All'; label: string }[] = [
    { value: 'All', label: 'All' },
    { value: 'Generated', label: 'Generated' },
    { value: 'Pending', label: 'Pending' },
    { value: 'Failed', label: 'Failed' },
  ];

  readonly payslips = signal<IPayslip[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly search = signal('');
  readonly statusFilter = signal<PdfStatus | 'All'>('All');

  readonly generating = signal(false);
  readonly genStatus = signal<IPayslipGenerationStatus | null>(null);

  readonly downloadingId = signal<string | null>(null);
  readonly zipDownloading = signal(false);
  /** The slip id whose failed PDF is currently being re-queued (DF-31, FR-8). */
  readonly retryingId = signal<string | null>(null);

  readonly previewSlip = signal<IPayslip | null>(null);
  readonly previewLoading = signal(false);
  readonly previewUrl = signal<SafeResourceUrl | null>(null);
  /** Raw object URL kept so we can revoke it when the modal closes. */
  private previewObjectUrl: string | null = null;

  // ─── Derived state ─────────────────────────────────────────

  readonly hasGenerated = computed(() =>
    this.payslips().some((p) => p.pdfStatus === 'Generated'),
  );

  readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const status = this.statusFilter();
    return this.payslips().filter((p) => {
      if (status !== 'All' && p.pdfStatus !== status) {
        return false;
      }
      if (!q) {
        return true;
      }
      return (
        p.employeeName.toLowerCase().includes(q) ||
        p.employeeNo.toLowerCase().includes(q) ||
        (p.department ?? '').toLowerCase().includes(q)
      );
    });
  });

  readonly genPercent = computed(() => {
    const g = this.genStatus();
    if (!g || g.totalCount <= 0) {
      return 0;
    }
    const done = g.generatedCount + g.failedCount;
    return Math.min(100, Math.round((done / g.totalCount) * 100));
  });

  ngOnInit(): void {
    this.loadPayslips();
  }

  ngOnDestroy(): void {
    this.revokePreviewUrl();
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadPayslips(): void {
    this.loading.set(true);
    this.error.set(null);
    this.payslipService
      .listPayslips(this.runId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (rows) => {
          this.payslips.set(rows);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load payslips for this run.');
          this.loading.set(false);
        },
      });
  }

  // ─── Generation (AC-1 / AC-5) ──────────────────────────────

  generate(): void {
    if (this.generating()) {
      return;
    }
    this.generating.set(true);
    const runId = this.runId();
    const request$ = this.hasGenerated()
      ? this.payslipService.regeneratePayslips(runId)
      : this.payslipService.generatePayslips(runId);

    request$.pipe(takeUntil(this.destroy$)).subscribe({
      next: (status) => {
        this.genStatus.set(status);
        this.streamStatus();
      },
      error: () => {
        this.generating.set(false);
        this.toastr.error('Could not start payslip generation.');
      },
    });
  }

  /**
   * Poll generation status while the job runs (§8). On completion, refetch the
   * payslip list so per-row PDF statuses reflect the finished job.
   */
  private streamStatus(): void {
    this.payslipService
      .streamGenerationStatus(this.runId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (s) => this.genStatus.set(s),
        error: () => {
          this.generating.set(false);
          this.toastr.error('Lost track of generation progress.');
        },
        complete: () => {
          this.generating.set(false);
          this.toastr.success('Payslip generation finished.');
          this.loadPayslips();
        },
      });
  }

  // ─── Per-employee retry (DF-31 / ISSUE-162 FR-8) ───────────

  /**
   * Re-queue rendering for ONE employee's failed payslip. Mirrors `generate()`: on
   * a successful enqueue it drives the generation status bar via `streamStatus()`,
   * which polls, then reloads the list (fresh per-row PDF statuses) and toasts on
   * completion. Gated in the template to `Failed` rows and `canGenerate()` users.
   */
  retry(p: IPayslip): void {
    if (this.retryingId() || this.generating()) {
      return;
    }
    this.retryingId.set(p.id);
    this.payslipService
      .retryPayslip(this.runId(), p.employeeId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.retryingId.set(null);
          this.generating.set(true);
          this.streamStatus();
        },
        error: () => {
          this.retryingId.set(null);
          this.toastr.error('Could not retry the payslip.');
        },
      });
  }

  // ─── Per-employee preview (§8) ─────────────────────────────

  preview(p: IPayslip): void {
    this.previewSlip.set(p);
    this.previewLoading.set(true);
    this.revokePreviewUrl();
    this.previewUrl.set(null);

    this.payslipService
      .downloadPayslip(this.runId(), p.employeeId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (resp) => {
          const blob = resp.body;
          if (!blob) {
            this.previewLoading.set(false);
            return;
          }
          this.previewObjectUrl = URL.createObjectURL(blob);
          this.previewUrl.set(
            this.sanitizer.bypassSecurityTrustResourceUrl(
              this.previewObjectUrl,
            ),
          );
          this.previewLoading.set(false);
        },
        error: () => {
          this.previewLoading.set(false);
          this.toastr.error('Could not load the payslip preview.');
        },
      });
  }

  closePreview(): void {
    this.previewSlip.set(null);
    this.previewUrl.set(null);
    this.revokePreviewUrl();
  }

  private revokePreviewUrl(): void {
    if (this.previewObjectUrl) {
      URL.revokeObjectURL(this.previewObjectUrl);
      this.previewObjectUrl = null;
    }
  }

  // ─── Downloads (AC-3) ──────────────────────────────────────

  download(p: IPayslip): void {
    if (this.downloadingId()) {
      return;
    }
    this.downloadingId.set(p.id);
    this.payslipService
      .downloadPayslip(this.runId(), p.employeeId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (resp) => {
          this.downloadingId.set(null);
          const blob = resp.body;
          if (!blob) {
            this.toastr.error('The payslip download was empty.');
            return;
          }
          const filename =
            filenameFromDisposition(
              resp.headers.get('Content-Disposition'),
            ) ?? `${p.employeeNo}_payslip.pdf`;
          downloadBlob(blob, filename);
        },
        error: () => {
          this.downloadingId.set(null);
          this.toastr.error('Could not download the payslip.');
        },
      });
  }

  /** Desktop-only bulk ZIP download (§8, AC-3). */
  downloadAll(): void {
    if (this.zipDownloading()) {
      return;
    }
    this.zipDownloading.set(true);
    this.payslipService
      .downloadZip(this.runId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (resp) => {
          this.zipDownloading.set(false);
          const blob = resp.body;
          if (!blob) {
            this.toastr.error('The ZIP download was empty.');
            return;
          }
          const filename =
            filenameFromDisposition(
              resp.headers.get('Content-Disposition'),
            ) ?? `payslips-${this.runId()}.zip`;
          downloadBlob(blob, filename);
        },
        error: () => {
          this.zipDownloading.set(false);
          this.toastr.error('Could not download the payslip archive.');
        },
      });
  }
}

/** Parse a filename from a Content-Disposition header, if present (BR-5). */
function filenameFromDisposition(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (utf8?.[1]) {
    return decodeURIComponent(utf8[1]);
  }
  const quoted = /filename="?([^";]+)"?/i.exec(header);
  return quoted?.[1] ?? null;
}

/** Trigger a browser download for a blob with the given filename. */
function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
