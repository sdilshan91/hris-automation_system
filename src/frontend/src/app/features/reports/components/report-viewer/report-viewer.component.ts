import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  DestroyRef,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateModule } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReportsService } from '../../services/reports.service';
import {
  IReportFilters,
  IReportResult,
  ReportType,
  emptyReportFilters,
} from '../../models/reports.models';
import { ReportChartComponent } from '../report-chart/report-chart.component';

type ViewMode = 'chart' | 'table';

/**
 * US-RPT-001 AC-2..AC-4: the report viewer. A collapsible filter bar (date
 * range, departments, location, employment type, status), charts in the main
 * area, and a data table below — togglable via a chart/table view switcher
 * (FR-4). A Refresh button regenerates bypassing the cache (FR-8) and a Print
 * button produces a print-friendly layout (§8). Single-column < 768px (NFR-4).
 *
 * Accessibility (NFR-5): each chart canvas carries role="img" + aria-label
 * (handled by ReportChartComponent), and the table view IS the screen-reader
 * alternative — always reachable via the view toggle.
 */
@Component({
  selector: 'app-report-viewer',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslateModule,
    DatePipe,
    ReportChartComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="rv-page">
      <header class="rv-head no-print">
        <div>
          <h1 class="rv-title">{{ headerTitle() }}</h1>
          @if (result(); as r) {
            <p class="rv-sub">
              {{ 'reports.viewer.generatedAt' | translate }}:
              {{ r.metadata.generatedAt | date: 'medium' }}
            </p>
          }
        </div>
        <div class="rv-actions">
          <!-- View toggle (FR-4) -->
          <div
            class="rv-toggle"
            role="group"
            [attr.aria-label]="'reports.viewer.viewToggle' | translate"
          >
            <button
              type="button"
              class="rv-toggle-btn"
              [class.rv-toggle-active]="view() === 'chart'"
              [attr.aria-pressed]="view() === 'chart'"
              (click)="setView('chart')"
            >
              {{ 'reports.viewer.chartView' | translate }}
            </button>
            <button
              type="button"
              class="rv-toggle-btn"
              [class.rv-toggle-active]="view() === 'table'"
              [attr.aria-pressed]="view() === 'table'"
              (click)="setView('table')"
            >
              {{ 'reports.viewer.tableView' | translate }}
            </button>
          </div>
          <button
            type="button"
            class="rv-btn-secondary"
            (click)="refresh()"
            [disabled]="loading()"
          >
            {{ 'reports.viewer.refresh' | translate }}
          </button>
          <button type="button" class="rv-btn-secondary" (click)="print()">
            {{ 'reports.viewer.print' | translate }}
          </button>
        </div>
      </header>

      <!-- Collapsible filter bar (FR-2) -->
      <div class="rv-filters no-print">
        <button
          type="button"
          class="rv-filters-toggle"
          (click)="filtersOpen.set(!filtersOpen())"
          [attr.aria-expanded]="filtersOpen()"
        >
          {{ 'reports.viewer.filters' | translate }}
          <span aria-hidden="true">{{ filtersOpen() ? '−' : '+' }}</span>
        </button>
        @if (filtersOpen()) {
          <form class="rv-filters-body" (ngSubmit)="apply()">
            <label class="rv-field">
              <span>{{ 'reports.viewer.dateFrom' | translate }}</span>
              <input type="date" [(ngModel)]="dateFrom" name="dateFrom" />
            </label>
            <label class="rv-field">
              <span>{{ 'reports.viewer.dateTo' | translate }}</span>
              <input type="date" [(ngModel)]="dateTo" name="dateTo" />
            </label>
            <label class="rv-field">
              <span>{{ 'reports.viewer.departments' | translate }}</span>
              <input
                type="text"
                [(ngModel)]="departmentsRaw"
                name="departments"
                [placeholder]="'reports.viewer.commaSeparated' | translate"
              />
            </label>
            <label class="rv-field">
              <span>{{ 'reports.viewer.locations' | translate }}</span>
              <input
                type="text"
                [(ngModel)]="locationsRaw"
                name="locations"
                [placeholder]="'reports.viewer.commaSeparated' | translate"
              />
            </label>
            <label class="rv-field">
              <span>{{ 'reports.viewer.employmentType' | translate }}</span>
              <select
                multiple
                [(ngModel)]="employmentTypes"
                name="employmentTypes"
              >
                <option value="full-time">Full-time</option>
                <option value="part-time">Part-time</option>
                <option value="contract">Contract</option>
                <option value="intern">Intern</option>
              </select>
            </label>
            <label class="rv-field">
              <span>{{ 'reports.viewer.status' | translate }}</span>
              <select multiple [(ngModel)]="employeeStatuses" name="statuses">
                <option value="active">Active</option>
                <option value="probation">Probation</option>
                <option value="resigned">Resigned</option>
                <option value="terminated">Terminated</option>
                <option value="contract_ended">Contract ended</option>
              </select>
            </label>
            <div class="rv-filters-actions">
              <button type="submit" class="rv-btn-primary" [disabled]="loading()">
                {{ 'reports.viewer.apply' | translate }}
              </button>
              <button type="button" class="rv-btn-secondary" (click)="reset()">
                {{ 'reports.viewer.clear' | translate }}
              </button>
            </div>
          </form>
        }
      </div>

      @if (loading()) {
        <!-- Skeleton loaders for charts + table (§8) -->
        <div class="rv-skeletons" aria-busy="true">
          <div class="rv-skel-summary">
            @for (s of [0, 1, 2]; track s) {
              <div class="rv-skel-stat"></div>
            }
          </div>
          <div class="rv-skel-chart"></div>
          <div class="rv-skel-chart"></div>
        </div>
      } @else if (loadError()) {
        <div class="rv-error" role="alert">
          <p>{{ loadError() }}</p>
          <button type="button" class="rv-btn-secondary" (click)="apply()">
            {{ 'reports.viewer.retry' | translate }}
          </button>
        </div>
      } @else if (result(); as r) {
        <!-- Summary KPIs -->
        @if (r.metadata.summary.length) {
          <div class="rv-summary">
            @for (stat of r.metadata.summary; track stat.label) {
              <div class="rv-stat" [attr.data-tone]="stat.tone ?? 'neutral'">
                <span class="rv-stat-value">{{ stat.value }}</span>
                <span class="rv-stat-label">{{ stat.label | translate }}</span>
              </div>
            }
          </div>
        }

        <!-- Chart view -->
        @if (view() === 'chart') {
          <div class="rv-charts">
            @for (chart of r.charts; track chart.title) {
              <figure class="rv-chart-card">
                <figcaption class="rv-chart-title">{{ chart.title }}</figcaption>
                <app-report-chart [chart]="chart" />
              </figure>
            }
            <p class="rv-a11y-note">
              {{ 'reports.viewer.tableAlternativeHint' | translate }}
            </p>
          </div>
        } @else {
          <!-- Table view (the WCAG screen-reader alternative, NFR-5) -->
          <div class="rv-table-wrap">
            <table class="rv-table">
              <caption class="rv-table-caption">
                {{ r.metadata.title }}
              </caption>
              <thead>
                <tr>
                  @for (col of r.table.columns; track col) {
                    <th scope="col">{{ col }}</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (row of r.table.rows; track $index) {
                  <tr>
                    @for (cell of row; track $index) {
                      <td>{{ cell }}</td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </section>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .rv-page {
        padding: 1.5rem;
        max-width: 80rem;
        margin: 0 auto;
      }
      .rv-head {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        gap: 1rem;
        flex-wrap: wrap;
        margin-bottom: 1.25rem;
      }
      .rv-title {
        font-size: 1.5rem;
        font-weight: 700;
        color: #111827;
      }
      .rv-sub {
        color: #6b7280;
        font-size: 0.875rem;
        margin-top: 0.25rem;
      }
      .rv-actions {
        display: flex;
        gap: 0.5rem;
        align-items: center;
        flex-wrap: wrap;
      }
      .rv-toggle {
        display: inline-flex;
        background: #f3f4f6;
        border-radius: 0.5rem;
        padding: 0.125rem;
      }
      .rv-toggle-btn {
        border: none;
        background: transparent;
        padding: 0.375rem 0.75rem;
        border-radius: 0.375rem;
        cursor: pointer;
        font-size: 0.875rem;
        color: #374151;
      }
      .rv-toggle-active {
        background: #fff;
        box-shadow: 0 1px 2px rgba(0, 0, 0, 0.08);
        font-weight: 600;
      }
      .rv-btn-primary {
        background: #4f46e5;
        color: #fff;
        border: none;
        border-radius: 0.5rem;
        padding: 0.5rem 1rem;
        font-weight: 500;
        cursor: pointer;
      }
      .rv-btn-primary:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }
      .rv-btn-secondary {
        background: #f3f4f6;
        color: #374151;
        border: none;
        border-radius: 0.5rem;
        padding: 0.5rem 1rem;
        cursor: pointer;
      }
      .rv-filters {
        background: #fff;
        border: 1px solid #f3f4f6;
        border-radius: 0.75rem;
        margin-bottom: 1.25rem;
        box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
      }
      .rv-filters-toggle {
        width: 100%;
        text-align: left;
        background: transparent;
        border: none;
        padding: 0.875rem 1.25rem;
        font-weight: 600;
        color: #111827;
        cursor: pointer;
        display: flex;
        justify-content: space-between;
      }
      .rv-filters-body {
        padding: 0 1.25rem 1.25rem;
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
        gap: 1rem;
      }
      .rv-field {
        display: flex;
        flex-direction: column;
        font-size: 0.8125rem;
        color: #374151;
        gap: 0.25rem;
      }
      .rv-field input,
      .rv-field select {
        border: 1px solid #d1d5db;
        border-radius: 0.5rem;
        padding: 0.5rem;
        font-size: 0.875rem;
      }
      .rv-filters-actions {
        grid-column: 1 / -1;
        display: flex;
        gap: 0.5rem;
      }
      .rv-summary {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
        gap: 1rem;
        margin-bottom: 1.5rem;
      }
      .rv-stat {
        background: #fff;
        border: 1px solid #f3f4f6;
        border-radius: 0.75rem;
        padding: 1rem 1.25rem;
        box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
      }
      .rv-stat-value {
        display: block;
        font-size: 1.5rem;
        font-weight: 700;
        color: #111827;
      }
      .rv-stat[data-tone='positive'] .rv-stat-value {
        color: #059669;
      }
      .rv-stat[data-tone='negative'] .rv-stat-value {
        color: #dc2626;
      }
      .rv-stat-label {
        color: #6b7280;
        font-size: 0.8125rem;
      }
      .rv-charts {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(24rem, 1fr));
        gap: 1.25rem;
      }
      .rv-chart-card {
        background: #fff;
        border: 1px solid #f3f4f6;
        border-radius: 0.75rem;
        padding: 1.25rem;
        margin: 0;
        box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
      }
      .rv-chart-title {
        font-weight: 600;
        color: #111827;
        margin-bottom: 0.75rem;
      }
      .rv-a11y-note {
        grid-column: 1 / -1;
        font-size: 0.8125rem;
        color: #6b7280;
      }
      .rv-table-wrap {
        overflow-x: auto;
        background: #fff;
        border: 1px solid #f3f4f6;
        border-radius: 0.75rem;
        box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
      }
      .rv-table {
        width: 100%;
        border-collapse: collapse;
        font-size: 0.875rem;
      }
      .rv-table-caption {
        text-align: left;
        padding: 1rem 1.25rem 0.5rem;
        font-weight: 600;
        color: #111827;
      }
      .rv-table th,
      .rv-table td {
        padding: 0.625rem 1rem;
        text-align: left;
        border-bottom: 1px solid #f3f4f6;
      }
      .rv-table th {
        background: #f9fafb;
        font-weight: 600;
        color: #374151;
      }
      .rv-error {
        background: #fef2f2;
        border: 1px solid #fecaca;
        color: #b91c1c;
        padding: 1.25rem;
        border-radius: 0.75rem;
      }
      .rv-skel-summary {
        display: grid;
        grid-template-columns: repeat(3, 1fr);
        gap: 1rem;
        margin-bottom: 1.5rem;
      }
      .rv-skel-stat {
        height: 4.5rem;
        border-radius: 0.75rem;
        background: #e5e7eb;
      }
      .rv-skel-chart {
        height: 18rem;
        border-radius: 0.75rem;
        background: #e5e7eb;
        margin-bottom: 1.25rem;
      }
      @media (max-width: 768px) {
        .rv-charts,
        .rv-skel-summary {
          grid-template-columns: 1fr;
        }
      }
      @media print {
        .no-print {
          display: none !important;
        }
        .rv-chart-card,
        .rv-stat,
        .rv-table-wrap {
          box-shadow: none;
          border: 1px solid #e5e7eb;
        }
      }
    `,
  ],
})
export class ReportViewerComponent implements OnInit {
  private readonly service = inject(ReportsService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  /** The report type resolved from the route param (AC-2..AC-4). */
  readonly reportType = signal<ReportType>('headcount');

  readonly result = signal<IReportResult | null>(null);
  readonly loading = signal<boolean>(false);
  readonly loadError = signal<string | null>(null);
  readonly view = signal<ViewMode>('chart');
  readonly filtersOpen = signal<boolean>(false);

  readonly headerTitle = computed(
    () => this.result()?.metadata.title ?? this.titleCase(this.reportType())
  );

  // ── ngModel-bound filter fields ──────────────────────────────────────────
  dateFrom: string | null = null;
  dateTo: string | null = null;
  departmentsRaw = '';
  locationsRaw = '';
  employmentTypes: string[] = [];
  employeeStatuses: string[] = [];

  ngOnInit(): void {
    const type = this.route.snapshot.paramMap.get('type') as ReportType | null;
    if (type) {
      this.reportType.set(type);
    }
    this.generate(false);
  }

  setView(mode: ViewMode): void {
    this.view.set(mode);
  }

  /** Apply the current filter form and regenerate. */
  apply(): void {
    this.generate(false);
  }

  /** FR-8: bypass the cache and regenerate. */
  refresh(): void {
    this.generate(true);
  }

  reset(): void {
    this.dateFrom = null;
    this.dateTo = null;
    this.departmentsRaw = '';
    this.locationsRaw = '';
    this.employmentTypes = [];
    this.employeeStatuses = [];
    this.generate(false);
  }

  print(): void {
    window.print();
  }

  /** Build the filter payload from the bound form fields. */
  buildFilters(): IReportFilters {
    const filters = emptyReportFilters();
    filters.dateFrom = this.dateFrom || null;
    filters.dateTo = this.dateTo || null;
    filters.departmentIds = this.splitCsv(this.departmentsRaw);
    filters.locationIds = this.splitCsv(this.locationsRaw);
    filters.employmentTypes = [...this.employmentTypes];
    filters.employeeStatuses = [...this.employeeStatuses];
    return filters;
  }

  private generate(refresh: boolean): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service
      .generateReport(this.reportType(), this.buildFilters(), refresh)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.result.set(res);
          this.loading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.loadError.set(
            err.error?.message ?? 'Could not generate the report.'
          );
          this.loading.set(false);
        },
      });
  }

  private splitCsv(raw: string): string[] {
    return raw
      .split(',')
      .map((s) => s.trim())
      .filter((s) => s.length > 0);
  }

  private titleCase(type: ReportType): string {
    return type
      .split('-')
      .map((w) => w.charAt(0).toUpperCase() + w.slice(1))
      .join(' ');
  }
}
