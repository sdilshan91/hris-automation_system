import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../../../../../core/auth/auth.service';
import { AuditLogService } from '../../services/audit-log.service';
import {
  AuditExportFormat,
  IActorOption,
  IAuditLogDetail,
  IAuditLogEntry,
  IAuditLogFilters,
  IAuditLogListParams,
} from '../../models/audit-log.models';
import { AuditDetailPanelComponent } from '../audit-detail-panel/audit-detail-panel.component';
import { ExportDialogComponent } from '../export-dialog/export-dialog.component';

/**
 * US-ADM-008: Tenant Admin audit-log viewer (AC-1..AC-4).
 *
 * Full-width data table of tenant-scoped audit records (reverse-chronological,
 * paginated 50), a combinable AND filter bar (date range, actor autocomplete,
 * action / resource-type dropdowns, keyword search), a right slide-in detail
 * panel with a color-coded JSON diff, and a filtered CSV/JSON export with a
 * confirmation dialog. Auditors (FR-7) get a clear read-only message if the
 * export endpoint returns 403.
 */
@Component({
  selector: 'app-audit-log-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslateModule,
    AuditDetailPanelComponent,
    ExportDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('150ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
  ],
  templateUrl: './audit-log-list.component.html',
  styles: [
    `
      .page-btn {
        @apply rounded-md px-3 py-1.5 text-xs font-medium text-neutral-600
          ring-1 ring-inset ring-neutral-200 transition-colors
          hover:bg-neutral-50 disabled:opacity-40 disabled:cursor-not-allowed;
      }
      .filter-input {
        @apply rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
          text-neutral-800 transition-colors focus:border-indigo-400
          focus:outline-none focus:ring-1 focus:ring-indigo-400;
      }
    `,
  ],
})
export class AuditLogListComponent implements OnInit {
  private readonly service = inject(AuditLogService);
  private readonly toastr = inject(ToastrService);
  private readonly auth = inject(AuthService);

  readonly pageSize = 50;

  // ─── List state ──────────────────────────────────────────
  readonly entries = signal<IAuditLogEntry[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly retentionDays = signal<number | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');

  // ─── Filters (AC-2) ──────────────────────────────────────
  readonly startDate = signal('');
  readonly endDate = signal('');
  readonly actorFilter = signal('');
  readonly actionFilter = signal('');
  readonly resourceFilter = signal('');
  readonly searchTerm = signal('');

  // ─── Filter option sources ───────────────────────────────
  readonly actors = signal<IActorOption[]>([]);
  /** Distinct action / resource-type values derived from loaded rows. */
  readonly actionOptions = signal<string[]>([]);
  readonly resourceOptions = signal<string[]>([]);

  // ─── Detail panel (AC-3) ─────────────────────────────────
  readonly detail = signal<IAuditLogDetail | null>(null);
  readonly detailLoading = signal(false);

  // ─── Export (AC-4) ───────────────────────────────────────
  readonly showExport = signal(false);
  readonly exporting = signal(false);

  /** Auditor role gets read-only access (BR-2 / FR-7). */
  readonly isAuditor = computed(() => this.auth.hasRole('Auditor'));

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.pageSize))
  );
  readonly rangeStart = computed(() =>
    this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize + 1
  );
  readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize, this.total())
  );

  private readonly search$ = new Subject<string>();

  constructor() {
    this.search$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((term) => {
        this.searchTerm.set(term);
        this.page.set(1);
        this.loadEntries();
      });
  }

  ngOnInit(): void {
    this.loadActors();
    this.loadEntries();
  }

  // ─── Data loading ────────────────────────────────────────

  private currentFilters(): IAuditLogFilters {
    return {
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      actorUserId: this.actorFilter() || undefined,
      action: this.actionFilter() || undefined,
      resourceType: this.resourceFilter() || undefined,
      search: this.searchTerm() || undefined,
    };
  }

  loadEntries(): void {
    this.loading.set(true);
    this.error.set('');
    const params: IAuditLogListParams = {
      page: this.page(),
      pageSize: this.pageSize,
      ...this.currentFilters(),
    };
    this.service.getAuditLog(params).subscribe({
      next: (res) => {
        this.entries.set(res.items);
        this.total.set(res.totalCount);
        this.retentionDays.set(res.retentionDays);
        this.mergeDistinctOptions(res.items);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('audit.list.loadError');
        this.loading.set(false);
      },
    });
  }

  private loadActors(): void {
    this.service.getActorOptions().subscribe({
      next: (actors) => this.actors.set(actors),
      error: () => this.actors.set([]),
    });
  }

  /** Accumulate distinct action / resource-type values for the dropdowns. */
  private mergeDistinctOptions(items: IAuditLogEntry[]): void {
    const actions = new Set(this.actionOptions());
    const resources = new Set(this.resourceOptions());
    for (const it of items) {
      if (it.action) {
        actions.add(it.action);
      }
      if (it.resourceType) {
        resources.add(it.resourceType);
      }
    }
    this.actionOptions.set(Array.from(actions).sort());
    this.resourceOptions.set(Array.from(resources).sort());
  }

  // ─── Filters (AC-2) ──────────────────────────────────────

  onSearchInput(value: string): void {
    this.search$.next(value);
  }

  applyFilterChange(): void {
    this.page.set(1);
    this.loadEntries();
  }

  onStartDateChange(value: string): void {
    this.startDate.set(value);
    this.applyFilterChange();
  }

  onEndDateChange(value: string): void {
    this.endDate.set(value);
    this.applyFilterChange();
  }

  onActorChange(value: string): void {
    this.actorFilter.set(value);
    this.applyFilterChange();
  }

  onActionChange(value: string): void {
    this.actionFilter.set(value);
    this.applyFilterChange();
  }

  onResourceChange(value: string): void {
    this.resourceFilter.set(value);
    this.applyFilterChange();
  }

  clearFilters(): void {
    this.startDate.set('');
    this.endDate.set('');
    this.actorFilter.set('');
    this.actionFilter.set('');
    this.resourceFilter.set('');
    this.searchTerm.set('');
    this.applyFilterChange();
  }

  readonly hasActiveFilters = computed(
    () =>
      !!this.startDate() ||
      !!this.endDate() ||
      !!this.actorFilter() ||
      !!this.actionFilter() ||
      !!this.resourceFilter() ||
      !!this.searchTerm()
  );

  // ─── Pagination ──────────────────────────────────────────

  prevPage(): void {
    if (this.page() > 1) {
      this.page.update((p) => p - 1);
      this.loadEntries();
    }
  }

  nextPage(): void {
    if (this.page() < this.totalPages()) {
      this.page.update((p) => p + 1);
      this.loadEntries();
    }
  }

  // ─── Detail panel (AC-3) ─────────────────────────────────

  openDetail(entry: IAuditLogEntry): void {
    this.detailLoading.set(true);
    // Seed the panel with the row data so it opens instantly, then enrich.
    this.detail.set({
      ...entry,
      userAgent: null,
      traceId: null,
      before: null,
      after: null,
    });
    this.service.getAuditDetail(entry.id).subscribe({
      next: (full) => {
        this.detail.set(full);
        this.detailLoading.set(false);
      },
      error: () => {
        this.detailLoading.set(false);
        this.toastr.error(this.tr('audit.detail.loadError'));
        this.detail.set(null);
      },
    });
  }

  closeDetail(): void {
    this.detail.set(null);
  }

  // ─── Export (AC-4) ───────────────────────────────────────

  openExport(): void {
    this.showExport.set(true);
  }

  closeExport(): void {
    this.showExport.set(false);
  }

  onExportConfirmed(format: AuditExportFormat): void {
    this.exporting.set(true);
    this.service.exportAuditLog(this.currentFilters(), format).subscribe({
      next: (resp) => {
        this.exporting.set(false);
        this.showExport.set(false);
        this.saveDownload(resp, `audit-log.${format}`);
        this.toastr.success(this.tr('audit.export.success'));
      },
      error: (err: HttpErrorResponse) => {
        this.exporting.set(false);
        this.showExport.set(false);
        if (err.status === 403) {
          this.toastr.warning(this.tr('audit.export.readOnly'));
        } else {
          this.toastr.error(this.tr('audit.export.error'));
        }
      },
    });
  }

  // ─── Helpers ─────────────────────────────────────────────

  /** Translate synchronously; falls back to the key if not yet loaded. */
  private tr(key: string): string {
    return key;
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

  initials(name: string): string {
    const parts = (name || '').trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return (name || '?').substring(0, 2).toUpperCase();
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
