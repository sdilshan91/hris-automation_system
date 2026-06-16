import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { ToastrService } from 'ngx-toastr';

import { AuditService } from '../../services/audit.service';
import {
  AUDIT_ACTION_OPTIONS,
  AUDIT_RESOURCE_TYPES,
  AuditExportFormat,
  DIFF_KIND_CLASS,
  IAuditEntry,
  IAuditTrailFilters,
  auditActionLabel,
  auditDotClass,
  buildDiff,
  emptyAuditFilters,
  hasDiff,
  parseSnapshot,
} from '../../models/audit.models';

/**
 * US-PAY-012 (§8): the payroll Audit Trail view — a vertical timeline of audit
 * events with an expandable before/after diff per change (FR-8). Reused in two
 * modes via the `runId` input:
 *
 *  - STANDALONE PAGE (`runId` not set): shows the filter bar (date range, action
 *    type, actor search, resource type — FR-4) + a CSV/Excel export button (FR-5)
 *    and loads the tenant-wide filtered audit trail (AC-4).
 *  - EMBEDDED IN RUN DETAIL (`[runId]="r.id"`): loads the per-run timeline (FR-6)
 *    and hides the filter bar/export (the timeline is already scoped to the run;
 *    the standalone page is where cross-run filtering + export live).
 *
 * Each timeline card shows timestamp, actor, action description, and resource. An
 * "expand diff" toggle reveals a side-by-side before/after table with green
 * additions / red removals / amber modifications. The DIFF view is DESKTOP-ONLY
 * for readability (§8): on mobile the card shows a "view on desktop" note instead
 * of the two-column table; the timeline itself stays scrollable on mobile.
 */
@Component({
  selector: 'app-audit-trail',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div [class.mx-auto]="!runId()" [class.max-w-5xl]="!runId()">
      <!-- Header (standalone page only) -->
      @if (!runId()) {
        <div class="mb-6">
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Audit Trail
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Every payroll-related action, for compliance and investigation.
          </p>
        </div>

        <!-- Filter bar (FR-4) + export (FR-5) -->
        <div
          class="mb-5 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
        >
          <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5">
            <div>
              <label class="audit-label" for="audit-from">From</label>
              <input
                id="audit-from"
                type="date"
                class="audit-input"
                [(ngModel)]="filters.fromUtc"
              />
            </div>
            <div>
              <label class="audit-label" for="audit-to">To</label>
              <input
                id="audit-to"
                type="date"
                class="audit-input"
                [(ngModel)]="filters.toUtc"
              />
            </div>
            <div>
              <label class="audit-label" for="audit-action">Action type</label>
              <select
                id="audit-action"
                class="audit-input"
                [(ngModel)]="filters.action"
              >
                <option [ngValue]="null">All actions</option>
                @for (a of actionOptions; track a.value) {
                  <option [ngValue]="a.value">{{ a.label }}</option>
                }
              </select>
            </div>
            <div>
              <label class="audit-label" for="audit-resource">Resource</label>
              <select
                id="audit-resource"
                class="audit-input"
                [(ngModel)]="filters.resourceType"
              >
                <option [ngValue]="null">All resources</option>
                @for (r of resourceTypes; track r) {
                  <option [ngValue]="r">{{ r }}</option>
                }
              </select>
            </div>
            <div>
              <label class="audit-label" for="audit-actor">Actor user ID</label>
              <input
                id="audit-actor"
                type="text"
                class="audit-input"
                placeholder="User ID…"
                [(ngModel)]="filters.actorUserId"
              />
            </div>
          </div>

          <div class="mt-3 flex flex-wrap items-center justify-between gap-2">
            <div class="flex gap-2">
              <button
                type="button"
                class="rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                [style.background-color]="'var(--brand-primary)'"
                [disabled]="loading()"
                (click)="applyFilters()"
                data-test="apply"
              >
                Apply filters
              </button>
              <button
                type="button"
                class="rounded-lg px-3 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
                [disabled]="loading()"
                (click)="resetFilters()"
                data-test="reset"
              >
                Reset
              </button>
            </div>
            <div class="flex gap-2">
              <button
                type="button"
                class="btn-export"
                [disabled]="exporting() || entries().length === 0"
                (click)="exportTrail('csv')"
                data-test="export-csv"
              >
                Export CSV
              </button>
              <button
                type="button"
                class="btn-export"
                [disabled]="exporting() || entries().length === 0"
                (click)="exportTrail('xlsx')"
                data-test="export-xlsx"
              >
                Export Excel
              </button>
            </div>
          </div>
        </div>
      } @else {
        <h2 class="mb-4 text-sm font-semibold text-neutral-900">Audit Trail</h2>
      }

      <!-- Loading -->
      @if (loading()) {
        <div class="space-y-3">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-16 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (entries().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-300 bg-white px-6 py-12 text-center"
          data-test="empty"
        >
          <p class="text-sm font-medium text-neutral-700">No audit events</p>
          <p class="mt-1 text-sm text-neutral-500">
            No payroll actions match the current filters.
          </p>
        </div>
      } @else {
        <!-- Timeline (scrollable on mobile, §8) -->
        <ol
          @fadeIn
          class="relative space-y-4 border-l border-neutral-200 pl-5"
          data-test="timeline"
        >
          @for (e of entries(); track e.id) {
            <li class="relative" data-test="audit-card">
              <span
                class="absolute -left-[1.4rem] top-1.5 h-2.5 w-2.5 rounded-full ring-4 ring-white"
                [class]="dotClass(e.action)"
                aria-hidden="true"
              ></span>
              <div
                class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
              >
                <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
                  <span class="text-sm font-medium text-neutral-900">
                    {{ actionLabel(e.action) }}
                  </span>
                  <span
                    class="inline-flex items-center rounded-full bg-neutral-100 px-2 py-0.5 text-[11px] font-medium text-neutral-600"
                  >
                    {{ e.resourceType }}
                  </span>
                  <span class="text-xs text-neutral-400">
                    {{ e.timestamp | date: 'medium' }}
                  </span>
                </div>
                <p class="mt-1 text-sm text-neutral-600">
                  by
                  <span class="font-medium text-neutral-800">{{
                    actorLabel(e)
                  }}</span>
                  @if (e.ipAddress) {
                    <span class="text-neutral-400"> · {{ e.ipAddress }}</span>
                  }
                </p>

                <!-- Diff toggle (FR-8) -->
                @if (canDiff(e)) {
                  <button
                    type="button"
                    class="mt-2 text-xs font-medium text-neutral-500 transition hover:text-neutral-800"
                    [attr.aria-expanded]="isExpanded(e.id)"
                    (click)="toggle(e.id)"
                    data-test="diff-toggle"
                  >
                    {{ isExpanded(e.id) ? 'Hide changes' : 'View changes' }}
                    {{ isExpanded(e.id) ? '↑' : '↓' }}
                  </button>

                  @if (isExpanded(e.id)) {
                    <!-- DIFF: desktop-only side-by-side (§8) -->
                    <div class="mt-3 hidden md:block" data-test="diff-table">
                      <table class="w-full text-left text-xs">
                        <thead
                          class="border-b border-neutral-200 text-[11px] uppercase tracking-wide text-neutral-400"
                        >
                          <tr>
                            <th class="py-1.5 pr-2 font-medium">Field</th>
                            <th class="py-1.5 pr-2 font-medium">Before</th>
                            <th class="py-1.5 font-medium">After</th>
                          </tr>
                        </thead>
                        <tbody class="divide-y divide-neutral-100">
                          @for (row of diffRows(e); track row.field) {
                            <tr [class]="diffClass[row.kind]">
                              <td class="py-1.5 pr-2 font-medium align-top">
                                {{ row.field }}
                              </td>
                              <td
                                class="py-1.5 pr-2 align-top font-mono break-all"
                              >
                                {{ row.before ?? '—' }}
                              </td>
                              <td class="py-1.5 align-top font-mono break-all">
                                {{ row.after ?? '—' }}
                              </td>
                            </tr>
                          }
                        </tbody>
                      </table>
                    </div>
                    <!-- Mobile: defer the diff table to desktop (§8) -->
                    <p
                      class="mt-3 rounded-lg bg-neutral-50 px-3 py-2 text-xs text-neutral-500 md:hidden"
                      data-test="diff-mobile-note"
                    >
                      The detailed before/after comparison is best viewed on a
                      larger screen.
                    </p>
                  }
                }
              </div>
            </li>
          }
        </ol>
      }
    </div>
  `,
  styles: [
    `
      .audit-label {
        display: block;
        margin-bottom: 0.25rem;
        font-size: 0.7rem;
        font-weight: 500;
        color: rgb(115 115 115);
      }
      .audit-input {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid rgb(229 229 229);
        padding: 0.5rem 0.625rem;
        font-size: 0.8125rem;
        color: rgb(23 23 23);
        transition: border-color 0.2s;
      }
      .audit-input:focus {
        outline: none;
        border-color: rgb(163 163 163);
      }
      .btn-export {
        border-radius: 0.5rem;
        border: 1px solid rgb(229 229 229);
        padding: 0.5rem 0.75rem;
        font-size: 0.8125rem;
        font-weight: 500;
        color: rgb(64 64 64);
        background: #fff;
        transition: background-color 0.2s;
      }
      .btn-export:hover:not(:disabled) {
        background: rgb(245 245 245);
      }
      .btn-export:disabled {
        opacity: 0.5;
      }
    `,
  ],
})
export class AuditTrailComponent implements OnInit {
  private readonly auditService = inject(AuditService);
  private readonly toastr = inject(ToastrService);

  /** When set, load the per-run timeline (FR-6) and hide the filter bar/export. */
  readonly runId = input<string | null>(null);

  readonly actionOptions = AUDIT_ACTION_OPTIONS;
  readonly resourceTypes = AUDIT_RESOURCE_TYPES;
  readonly diffClass = DIFF_KIND_CLASS;

  readonly entries = signal<IAuditEntry[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly exporting = signal(false);

  /** Which entry diffs are expanded (FR-8). */
  private readonly expanded = signal<ReadonlySet<string>>(new Set());

  /** The live filter-bar model (two-way bound; applied on "Apply filters"). */
  filters: IAuditTrailFilters = emptyAuditFilters();

  /** The filters actually in effect (set on apply); drives the active query. */
  private readonly activeFilters = signal<IAuditTrailFilters>(emptyAuditFilters());

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    const runId = this.runId();
    // Per-run timeline returns a bare array; the standalone trail returns a page.
    const query$: Observable<IAuditEntry[]> = runId
      ? this.auditService.getRunAuditTrail(runId)
      : this.auditService
          .getAuditTrail(this.activeFilters())
          .pipe(map((page) => page.items));
    query$.subscribe({
      next: (list) => {
        this.entries.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load the audit trail.');
        this.loading.set(false);
      },
    });
  }

  /** Apply the filter-bar model and reload (standalone page only, FR-4). */
  applyFilters(): void {
    this.activeFilters.set({ ...this.filters });
    this.load();
  }

  /** Clear all filters and reload. */
  resetFilters(): void {
    this.filters = emptyAuditFilters();
    this.activeFilters.set(emptyAuditFilters());
    this.load();
  }

  /** Export the currently-applied audit trail to a blob (FR-5). */
  exportTrail(format: AuditExportFormat): void {
    this.exporting.set(true);
    this.auditService.exportAuditTrail(this.activeFilters(), format).subscribe({
      next: (resp) => {
        this.exporting.set(false);
        this.saveDownload(resp, `payroll-audit-trail.${format}`);
      },
      error: () => {
        this.exporting.set(false);
        this.toastr.error('Could not export the audit trail.');
      },
    });
  }

  toggle(id: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  isExpanded(id: string): boolean {
    return this.expanded().has(id);
  }

  canDiff(entry: IAuditEntry): boolean {
    return hasDiff(entry);
  }

  diffRows(entry: IAuditEntry) {
    return buildDiff(parseSnapshot(entry.before), parseSnapshot(entry.after));
  }

  /** Display label for the actor: employee no, else user id, else "System". */
  actorLabel(entry: IAuditEntry): string {
    return entry.actorEmployeeNo || entry.actorUserId || 'System';
  }

  actionLabel(action: string): string {
    return auditActionLabel(action);
  }

  dotClass(action: string): string {
    return auditDotClass(action);
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
}

/** Parse a filename from a Content-Disposition header, if present. */
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
