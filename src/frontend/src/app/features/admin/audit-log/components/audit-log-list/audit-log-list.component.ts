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
import { ActivatedRoute, Router, Params } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import {
  debounceTime,
  distinctUntilChanged,
  switchMap,
} from 'rxjs/operators';
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
 * US-ADM-008 + US-NTF-005: Tenant Admin audit-log viewer (AC-1..AC-4).
 *
 * Full-width data table of tenant-scoped audit records (reverse-chronological,
 * paginated 50), a combinable AND filter bar (date range, actor type-ahead,
 * MULTI-select action / resource-type dropdowns with "Select All", keyword
 * search), a right slide-in detail panel with a color-coded JSON diff, and a
 * filtered CSV/JSON export with a confirmation dialog. Auditors (FR-7) get a
 * clear read-only message if the export endpoint returns 403.
 *
 * US-NTF-005 deltas (additive):
 *  - FR-2 multi-select Action / Resource-Type dropdowns (checkboxes + Select
 *    All), populated from `GET /filter-options` (falls back to row-derived
 *    options when empty), serialized as repeated `actions=`/`resourceTypes=`.
 *  - FR-2 actor type-ahead (debounced 300ms) backed by `GET /actors?search=`.
 *  - FR-3 URL-based filter state: all applied filters + page live in the route
 *    query params (bookmarkable / shareable) and are restored on load.
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
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly pageSize = 50;

  // ─── List state ──────────────────────────────────────────
  readonly entries = signal<IAuditLogEntry[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly retentionDays = signal<number | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');

  // ─── Filters (AC-2 / FR-2) ───────────────────────────────
  readonly startDate = signal('');
  readonly endDate = signal('');
  /** Selected actor id + its display label (for the type-ahead input). */
  readonly actorFilter = signal('');
  readonly actorLabel = signal('');
  /** Multi-select action / resource-type values (US-NTF-005 / FR-2). */
  readonly selectedActions = signal<string[]>([]);
  readonly selectedResources = signal<string[]>([]);
  readonly searchTerm = signal('');

  // ─── Filter option sources ───────────────────────────────
  /**
   * Distinct action / resource-type values. Seeded from `GET /filter-options`
   * (FR-2) and topped up with any values seen in loaded rows so the dropdown
   * never hides a value present in the current page.
   */
  readonly actionOptions = signal<string[]>([]);
  readonly resourceOptions = signal<string[]>([]);

  // ─── Action / Resource dropdown open state ───────────────
  readonly actionMenuOpen = signal(false);
  readonly resourceMenuOpen = signal(false);

  // ─── Actor type-ahead (FR-2) ─────────────────────────────
  readonly actorResults = signal<IActorOption[]>([]);
  readonly actorMenuOpen = signal(false);
  readonly actorSearching = signal(false);
  /** Active option index for keyboard navigation of the actor list. */
  readonly actorActiveIndex = signal(-1);

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
  private readonly actorSearch$ = new Subject<string>();

  constructor() {
    this.search$
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((term) => {
        this.searchTerm.set(term);
        this.page.set(1);
        this.syncUrl();
        this.loadEntries();
      });

    // Actor type-ahead (FR-2): debounce 300ms, switchMap to cancel stale calls.
    this.actorSearch$
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((q) => {
          this.actorSearching.set(true);
          return this.service.searchActors(q);
        }),
        takeUntilDestroyed()
      )
      .subscribe({
        next: (results) => {
          this.actorResults.set(results);
          this.actorActiveIndex.set(-1);
          this.actorMenuOpen.set(true);
          this.actorSearching.set(false);
        },
        error: () => {
          this.actorResults.set([]);
          this.actorSearching.set(false);
        },
      });
  }

  ngOnInit(): void {
    this.restoreFromUrl();
    this.loadFilterOptions();
    this.loadEntries();
  }

  // ─── URL filter state (FR-3) ─────────────────────────────

  /** Restore applied filters + page from the route query params on load. */
  private restoreFromUrl(): void {
    const q = this.route.snapshot.queryParamMap;
    this.startDate.set(q.get('startDate') ?? '');
    this.endDate.set(q.get('endDate') ?? '');
    this.actorFilter.set(q.get('actorUserId') ?? '');
    this.actorLabel.set(q.get('actorLabel') ?? '');
    this.selectedActions.set(q.getAll('actions') ?? []);
    this.selectedResources.set(q.getAll('resourceTypes') ?? []);
    this.searchTerm.set(q.get('search') ?? '');
    const pageRaw = Number(q.get('page'));
    this.page.set(Number.isFinite(pageRaw) && pageRaw > 0 ? pageRaw : 1);
  }

  /** Reflect the current filter + page state into the URL (bookmarkable). */
  private syncUrl(): void {
    const queryParams: Params = {
      startDate: this.startDate() || null,
      endDate: this.endDate() || null,
      actorUserId: this.actorFilter() || null,
      actorLabel: this.actorLabel() || null,
      actions: this.selectedActions().length ? this.selectedActions() : null,
      resourceTypes: this.selectedResources().length
        ? this.selectedResources()
        : null,
      search: this.searchTerm() || null,
      page: this.page() > 1 ? this.page() : null,
    };
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  // ─── Data loading ────────────────────────────────────────

  private currentFilters(): IAuditLogFilters {
    return {
      startDate: this.startDate() || undefined,
      endDate: this.endDate() || undefined,
      actorUserId: this.actorFilter() || undefined,
      actions: this.selectedActions().length ? this.selectedActions() : undefined,
      resourceTypes: this.selectedResources().length
        ? this.selectedResources()
        : undefined,
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

  /**
   * Seed the multi-select dropdowns from the dedicated filter-options endpoint
   * (FR-2). Degrades gracefully: on empty/error the lists stay row-derived.
   */
  private loadFilterOptions(): void {
    this.service.getFilterOptions().subscribe({
      next: (opts) => {
        if (opts?.actions?.length) {
          this.actionOptions.set(
            Array.from(new Set([...this.actionOptions(), ...opts.actions])).sort()
          );
        }
        if (opts?.resourceTypes?.length) {
          this.resourceOptions.set(
            Array.from(
              new Set([...this.resourceOptions(), ...opts.resourceTypes])
            ).sort()
          );
        }
      },
      error: () => {
        /* Fall back to row-derived options (mergeDistinctOptions). */
      },
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
    this.syncUrl();
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

  // ─── Action multi-select (FR-2) ──────────────────────────

  toggleActionMenu(): void {
    this.actionMenuOpen.update((v) => !v);
    this.resourceMenuOpen.set(false);
  }

  isActionSelected(action: string): boolean {
    return this.selectedActions().includes(action);
  }

  toggleAction(action: string): void {
    const next = this.isActionSelected(action)
      ? this.selectedActions().filter((a) => a !== action)
      : [...this.selectedActions(), action];
    this.selectedActions.set(next);
    this.applyFilterChange();
  }

  /** "Select All" — selects every visible option, or clears if all selected. */
  toggleAllActions(): void {
    const all = this.actionOptions();
    this.selectedActions.set(
      this.selectedActions().length === all.length ? [] : [...all]
    );
    this.applyFilterChange();
  }

  readonly allActionsSelected = computed(
    () =>
      this.actionOptions().length > 0 &&
      this.selectedActions().length === this.actionOptions().length
  );

  // ─── Resource multi-select (FR-2) ────────────────────────

  toggleResourceMenu(): void {
    this.resourceMenuOpen.update((v) => !v);
    this.actionMenuOpen.set(false);
  }

  isResourceSelected(resource: string): boolean {
    return this.selectedResources().includes(resource);
  }

  toggleResource(resource: string): void {
    const next = this.isResourceSelected(resource)
      ? this.selectedResources().filter((r) => r !== resource)
      : [...this.selectedResources(), resource];
    this.selectedResources.set(next);
    this.applyFilterChange();
  }

  toggleAllResources(): void {
    const all = this.resourceOptions();
    this.selectedResources.set(
      this.selectedResources().length === all.length ? [] : [...all]
    );
    this.applyFilterChange();
  }

  readonly allResourcesSelected = computed(
    () =>
      this.resourceOptions().length > 0 &&
      this.selectedResources().length === this.resourceOptions().length
  );

  // ─── Actor type-ahead (FR-2) ─────────────────────────────

  onActorInput(value: string): void {
    this.actorLabel.set(value);
    if (!value?.trim()) {
      // Cleared input → clear the selection too.
      this.actorResults.set([]);
      this.actorMenuOpen.set(false);
      if (this.actorFilter()) {
        this.actorFilter.set('');
        this.applyFilterChange();
      }
      return;
    }
    if (value.trim().length < 2) {
      this.actorResults.set([]);
      this.actorMenuOpen.set(false);
      return;
    }
    this.actorSearch$.next(value.trim());
  }

  selectActor(option: IActorOption): void {
    this.actorFilter.set(option.id);
    this.actorLabel.set(`${option.displayName} (${option.email})`);
    this.actorMenuOpen.set(false);
    this.actorResults.set([]);
    this.actorActiveIndex.set(-1);
    this.applyFilterChange();
  }

  clearActor(): void {
    this.actorFilter.set('');
    this.actorLabel.set('');
    this.actorResults.set([]);
    this.actorMenuOpen.set(false);
    this.applyFilterChange();
  }

  /** Keyboard navigation for the actor listbox (NFR-5 a11y). */
  onActorKeydown(event: KeyboardEvent): void {
    const results = this.actorResults();
    if (!this.actorMenuOpen() || results.length === 0) {
      return;
    }
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.actorActiveIndex.set(
          Math.min(this.actorActiveIndex() + 1, results.length - 1)
        );
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.actorActiveIndex.set(Math.max(this.actorActiveIndex() - 1, 0));
        break;
      case 'Enter': {
        const idx = this.actorActiveIndex();
        if (idx >= 0 && idx < results.length) {
          event.preventDefault();
          this.selectActor(results[idx]);
        }
        break;
      }
      case 'Escape':
        this.actorMenuOpen.set(false);
        break;
      default:
        break;
    }
  }

  /** Close any open dropdown when focus/click moves elsewhere. */
  closeMenus(): void {
    this.actionMenuOpen.set(false);
    this.resourceMenuOpen.set(false);
    this.actorMenuOpen.set(false);
  }

  clearFilters(): void {
    this.startDate.set('');
    this.endDate.set('');
    this.actorFilter.set('');
    this.actorLabel.set('');
    this.selectedActions.set([]);
    this.selectedResources.set([]);
    this.searchTerm.set('');
    this.applyFilterChange();
  }

  readonly hasActiveFilters = computed(
    () =>
      !!this.startDate() ||
      !!this.endDate() ||
      !!this.actorFilter() ||
      this.selectedActions().length > 0 ||
      this.selectedResources().length > 0 ||
      !!this.searchTerm()
  );

  // ─── Pagination ──────────────────────────────────────────

  prevPage(): void {
    if (this.page() > 1) {
      this.page.update((p) => p - 1);
      this.syncUrl();
      this.loadEntries();
    }
  }

  nextPage(): void {
    if (this.page() < this.totalPages()) {
      this.page.update((p) => p + 1);
      this.syncUrl();
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
