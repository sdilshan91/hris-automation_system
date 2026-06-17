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
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subscription } from 'rxjs';
import { DataExportService } from '../../services/data-export.service';
import {
  EXPORT_ENTITY_CATALOGUE,
  DEFAULT_FORMAT_OPTIONS,
  IExportFormatOptions,
  IExportRecord,
  IInitiateExportRequest,
  ExportScope,
  allEntityCodes,
  canDownload,
  exportStatusClass,
  formatFileSize,
  isInProgress,
} from '../../models/data-export.models';

/**
 * US-ADM-010: reusable on-demand data-export panel — the shared body for BOTH
 * personas (the Tenant-Admin export page and the System-Admin tenant-detail
 * dialog). Behaviour is identical; only the target base differs, driven by the
 * optional `tenantId` input:
 *   - omitted → Tenant-Admin path (implicit tenant via ITenantContext, AC-1).
 *   - set     → System-Admin path targeting that tenant (AC-6).
 *
 * Renders (§8):
 *  - a "full export" toggle OR module-grouped entity checkboxes (with dividers),
 *  - CSV format preferences (delimiter, date format),
 *  - a "Start Export" button → confirmation toast (AC-1), surfacing rate-limit /
 *    in-progress / suspended errors,
 *  - an export-history list with status badges, file size, and a download button
 *    (enabled only when downloadAvailable && Completed),
 *  - a queued → processing → complete progress indicator, with light polling
 *    while any export is in progress (cleaned up on destroy).
 */
@Component({
  selector: 'app-export-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
  ],
  templateUrl: './export-panel.component.html',
  styles: [
    `
      .pref-select {
        @apply rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
          text-neutral-800 transition-colors focus:border-indigo-400
          focus:outline-none focus:ring-1 focus:ring-indigo-400;
      }
      .progress-bar {
        @apply h-full rounded-full bg-indigo-500 transition-all duration-500
          ease-out;
      }
    `,
  ],
})
export class ExportPanelComponent implements OnInit, OnDestroy {
  private readonly service = inject(DataExportService);
  private readonly toastr = inject(ToastrService);
  private readonly translate = inject(TranslateService);

  /** When set, target the System-Admin path for this tenant (AC-6). */
  readonly tenantId = input<string | undefined>(undefined);

  /** Light poll cadence (ms) while an export is in progress. */
  private readonly pollIntervalMs = 5000;

  readonly catalogue = EXPORT_ENTITY_CATALOGUE;
  readonly exportStatusClass = exportStatusClass;
  readonly formatFileSize = formatFileSize;
  readonly canDownload = canDownload;
  readonly isInProgress = isInProgress;

  // ─── Scope selection (AC-1, §8) ──────────────────────────
  /** Full export toggle — when true, entity checkboxes are ignored. */
  readonly fullExport = signal(true);
  /** Selected entity codes for a partial export. */
  readonly selected = signal<Set<string>>(new Set());

  // ─── Format preferences (FR-1) ───────────────────────────
  readonly delimiter = signal<IExportFormatOptions['delimiter']>(
    DEFAULT_FORMAT_OPTIONS.delimiter
  );
  readonly dateFormat = signal<IExportFormatOptions['dateFormat']>(
    DEFAULT_FORMAT_OPTIONS.dateFormat
  );

  // ─── Submit + history state ──────────────────────────────
  readonly submitting = signal(false);
  readonly history = signal<IExportRecord[]>([]);
  readonly historyLoading = signal(true);

  private pollSub: Subscription | null = null;

  /** Whether the Start button is allowed (full export, or ≥1 entity chosen). */
  readonly canStart = computed(
    () => !this.submitting() && (this.fullExport() || this.selected().size > 0)
  );

  /** True while any history row is Queued/Processing (drives polling + banner). */
  readonly hasInProgress = computed(() =>
    this.history().some((r) => isInProgress(r.status))
  );

  /** The most recent in-progress export (for the progress indicator, §8). */
  readonly activeExport = computed(
    () => this.history().find((r) => isInProgress(r.status)) ?? null
  );

  ngOnInit(): void {
    this.loadHistory();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  // ─── Scope selection ─────────────────────────────────────

  setFullExport(value: boolean): void {
    this.fullExport.set(value);
  }

  isSelected(code: string): boolean {
    return this.selected().has(code);
  }

  toggleEntity(code: string): void {
    this.selected.update((set) => {
      const next = new Set(set);
      if (next.has(code)) {
        next.delete(code);
      } else {
        next.add(code);
      }
      return next;
    });
  }

  selectAll(): void {
    this.selected.set(new Set(allEntityCodes()));
  }

  clearSelection(): void {
    this.selected.set(new Set());
  }

  // ─── Initiate export (AC-1 / AC-6) ───────────────────────

  /** Build the request body honouring the full-toggle vs entity selection. */
  private buildRequest(): IInitiateExportRequest {
    const scope: ExportScope = this.fullExport()
      ? 'full'
      : Array.from(this.selected());
    return {
      scope,
      formatOptions: {
        delimiter: this.delimiter(),
        dateFormat: this.dateFormat(),
      },
    };
  }

  startExport(): void {
    if (!this.canStart()) {
      return;
    }
    this.submitting.set(true);
    this.service.initiate(this.buildRequest(), this.tenantId()).subscribe({
      next: () => {
        this.submitting.set(false);
        this.toastr.success(this.tr('dataExport.start.success'));
        // Refresh history so the new Queued row + progress indicator appear.
        this.loadHistory();
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.toastr.error(this.errorMessageFor(err));
      },
    });
  }

  /**
   * Map a failed initiate to a clear, user-facing message (BR-5 / FR-9 / BR-2).
   * Prefers the server-provided message when present; otherwise falls back to a
   * status-based i18n key.
   */
  private errorMessageFor(err: HttpErrorResponse): string {
    const serverMsg =
      typeof err.error === 'string'
        ? err.error
        : (err.error?.message as string | undefined);
    if (serverMsg) {
      return serverMsg;
    }
    if (err.status === 409) {
      return this.tr('dataExport.error.inProgress');
    }
    if (err.status === 403) {
      return this.tr('dataExport.error.suspended');
    }
    return this.tr('dataExport.error.generic');
  }

  // ─── History + polling (§8) ──────────────────────────────

  loadHistory(): void {
    this.historyLoading.set(true);
    this.service.getHistory(this.tenantId()).subscribe({
      next: (records) => {
        this.history.set(records);
        this.historyLoading.set(false);
        this.syncPolling();
      },
      error: () => {
        this.history.set([]);
        this.historyLoading.set(false);
      },
    });
  }

  /** Start polling if something is in progress, stop it otherwise. */
  private syncPolling(): void {
    if (this.hasInProgress()) {
      this.startPolling();
    } else {
      this.stopPolling();
    }
  }

  /**
   * Light poll loop while an export is in progress (FR-9). Re-fetches the whole
   * history each tick (small list); stops itself once nothing is in progress.
   * Uses a recursive setTimeout so we never stack overlapping requests.
   */
  private startPolling(): void {
    if (this.pollSub) {
      return;
    }
    const tick = () => {
      this.pollSub = this.service.getHistory(this.tenantId()).subscribe({
        next: (records) => {
          this.history.set(records);
          if (this.hasInProgress()) {
            this.schedule(tick);
          } else {
            this.stopPolling();
          }
        },
        error: () => this.stopPolling(),
      });
    };
    this.schedule(tick);
  }

  private schedule(fn: () => void): void {
    // Marker subscription so stopPolling can cancel a pending tick.
    const handle = setTimeout(fn, this.pollIntervalMs);
    this.pollSub = new Subscription(() => clearTimeout(handle));
  }

  private stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = null;
  }

  // ─── Download (AC-3) ─────────────────────────────────────

  download(record: IExportRecord): void {
    if (!canDownload(record)) {
      return;
    }
    this.service.download(record.id, this.tenantId()).subscribe({
      next: (resp) => this.saveDownload(resp, `export-${record.id}.zip`),
      error: (err: HttpErrorResponse) => {
        if (err.status === 410 || err.status === 404) {
          this.toastr.warning(this.tr('dataExport.download.expired'));
          // Reflect expiry in the list so the button disables.
          this.loadHistory();
        } else {
          this.toastr.error(this.tr('dataExport.download.error'));
        }
      },
    });
  }

  /** Save a blob response, deriving the filename from Content-Disposition. */
  private saveDownload(resp: HttpResponse<Blob>, fallbackName: string): void {
    const blob = resp.body;
    if (!blob) {
      return;
    }
    const filename =
      filenameFromDisposition(resp.headers.get('Content-Disposition')) ??
      fallbackName;
    downloadBlob(blob, filename);
  }

  // ─── i18n helper ─────────────────────────────────────────

  private tr(key: string): string {
    return this.translate.instant(key);
  }
}

/** Parse a filename from a Content-Disposition header, if present. */
function filenameFromDisposition(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(header);
  return match ? decodeURIComponent(match[1]) : null;
}

/** Trigger a browser download for a blob with the given filename. */
function downloadBlob(blob: Blob, filename: string): void {
  if (
    typeof document === 'undefined' ||
    typeof URL === 'undefined' ||
    !URL.createObjectURL
  ) {
    return;
  }
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
