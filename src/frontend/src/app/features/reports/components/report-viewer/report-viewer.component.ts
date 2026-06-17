import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  DestroyRef,
} from '@angular/core';
import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ToastrService } from 'ngx-toastr';
import {
  ReportsService,
  downloadBlob,
  filenameFromDisposition,
} from '../../services/reports.service';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  BalanceBand,
  IExportHistoryItem,
  IReportFilters,
  IReportResult,
  IReportTable,
  ReportExportFormat,
  ReportType,
  emptyReportFilters,
} from '../../models/reports.models';
import { ReportChartComponent } from '../report-chart/report-chart.component';

type ViewMode = 'chart' | 'table';

/** One selectable export format with its file-type icon + i18n label (AC-1). */
interface IExportOption {
  format: ReportExportFormat;
  /** Material icon token rendered by <mat-icon>-equivalent <span>. */
  icon: string;
  /** Tailwind/CSS accent class for the icon (Excel green, PDF red, CSV grey). */
  tone: 'csv' | 'xlsx' | 'pdf';
  /** i18n key for the option label. */
  labelKey: string;
}

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
    DecimalPipe,
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

          <!-- Export dropdown (AC-1). Hidden without Reports.Export. On desktop
               this is a labelled "Export" button; the same menu is reused by the
               mobile overflow below (NFR-5). -->
          @if (canExport()) {
            <div class="rv-export rv-export-desktop">
              <button
                type="button"
                class="rv-btn-primary rv-export-trigger"
                [attr.aria-haspopup]="'menu'"
                [attr.aria-expanded]="exportOpen()"
                [attr.aria-label]="'reports.export.button' | translate"
                [disabled]="exporting()"
                (click)="toggleExportMenu()"
              >
                @if (exporting()) {
                  <span class="rv-spinner" aria-hidden="true"></span>
                  {{ 'reports.export.exporting' | translate }}
                } @else {
                  {{ 'reports.export.button' | translate }}
                  <span aria-hidden="true">▾</span>
                }
              </button>
              @if (exportOpen()) {
                <ul class="rv-export-menu" role="menu">
                  @for (opt of exportOptions; track opt.format) {
                    <li role="none">
                      <button
                        type="button"
                        role="menuitem"
                        class="rv-export-item"
                        (click)="onExport(opt.format)"
                      >
                        <span
                          class="rv-fileicon"
                          [attr.data-tone]="opt.tone"
                          aria-hidden="true"
                          >{{ opt.tone | uppercase }}</span
                        >
                        {{ opt.labelKey | translate }}
                      </button>
                    </li>
                  }
                </ul>
              }
            </div>

            <!-- Mobile overflow (< 768px): the three-dot menu (NFR-5). -->
            <div class="rv-export rv-export-mobile">
              <button
                type="button"
                class="rv-btn-secondary rv-overflow-trigger"
                [attr.aria-haspopup]="'menu'"
                [attr.aria-expanded]="exportOpen()"
                [attr.aria-label]="'reports.export.button' | translate"
                [disabled]="exporting()"
                (click)="toggleExportMenu()"
              >
                @if (exporting()) {
                  <span class="rv-spinner" aria-hidden="true"></span>
                } @else {
                  <span aria-hidden="true">⋮</span>
                }
              </button>
              @if (exportOpen()) {
                <ul class="rv-export-menu rv-export-menu-right" role="menu">
                  @for (opt of exportOptions; track opt.format) {
                    <li role="none">
                      <button
                        type="button"
                        role="menuitem"
                        class="rv-export-item"
                        (click)="onExport(opt.format)"
                      >
                        <span
                          class="rv-fileicon"
                          [attr.data-tone]="opt.tone"
                          aria-hidden="true"
                          >{{ opt.tone | uppercase }}</span
                        >
                        {{ opt.labelKey | translate }}
                      </button>
                    </li>
                  }
                </ul>
              }
            </div>
          }
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
            <label class="rv-field">
              <span>{{ 'reports.viewer.employee' | translate }}</span>
              <input
                type="text"
                [(ngModel)]="employeeId"
                name="employeeId"
                [placeholder]="'reports.viewer.employeePlaceholder' | translate"
              />
            </label>
            <label class="rv-field">
              <span>{{ 'reports.viewer.leaveTypes' | translate }}</span>
              <input
                type="text"
                [(ngModel)]="leaveTypesRaw"
                name="leaveTypes"
                [placeholder]="'reports.viewer.commaSeparated' | translate"
              />
            </label>
            <label class="rv-field">
              <span>{{ 'reports.viewer.shifts' | translate }}</span>
              <input
                type="text"
                [(ngModel)]="shiftsRaw"
                name="shifts"
                [placeholder]="'reports.viewer.commaSeparated' | translate"
              />
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
                @for (row of r.table.rows; track $rowIdx; let $rowIdx = $index) {
                  <tr>
                    @for (cell of row; track $colIdx; let $colIdx = $index) {
                      <td
                        [class.rv-band-ok]="
                          bandAt(r.table, $rowIdx, $colIdx) === 'ok'
                        "
                        [class.rv-band-warn]="
                          bandAt(r.table, $rowIdx, $colIdx) === 'warn'
                        "
                        [class.rv-band-low]="
                          bandAt(r.table, $rowIdx, $colIdx) === 'low'
                        "
                        [attr.title]="bandTitle(r.table, $rowIdx, $colIdx)"
                      >
                        {{ cell }}
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }

      <!-- My Exports history (§8). Collapsible; hidden without Reports.Export. -->
      @if (canExport()) {
        <section class="rv-history no-print" aria-labelledby="rv-history-h">
          <button
            type="button"
            class="rv-history-toggle"
            (click)="toggleHistory()"
            [attr.aria-expanded]="historyOpen()"
          >
            <span id="rv-history-h">{{ 'reports.export.history.title' | translate }}</span>
            <span aria-hidden="true">{{ historyOpen() ? '−' : '+' }}</span>
          </button>
          @if (historyOpen()) {
            <div class="rv-history-body">
              @if (historyLoading()) {
                <div class="rv-skel-row" aria-busy="true"></div>
                <div class="rv-skel-row" aria-busy="true"></div>
              } @else if (exports().length === 0) {
                <p class="rv-history-empty">
                  {{ 'reports.export.history.empty' | translate }}
                </p>
              } @else {
                <table class="rv-history-table">
                  <caption class="rv-sr-only">
                    {{ 'reports.export.history.caption' | translate }}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">{{ 'reports.export.history.format' | translate }}</th>
                      <th scope="col">{{ 'reports.export.history.status' | translate }}</th>
                      <th scope="col">{{ 'reports.export.history.rows' | translate }}</th>
                      <th scope="col">{{ 'reports.export.history.size' | translate }}</th>
                      <th scope="col">{{ 'reports.export.history.requestedAt' | translate }}</th>
                      <th scope="col">
                        <span class="rv-sr-only">{{ 'reports.export.history.actions' | translate }}</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (ex of exports(); track ex.id) {
                      <tr>
                        <td class="rv-history-fmt">
                          <span class="rv-fileicon" [attr.data-tone]="ex.format" aria-hidden="true">{{ ex.format | uppercase }}</span>
                        </td>
                        <td>
                          <span class="rv-badge" [attr.data-status]="ex.status">{{
                            'reports.export.status.' + ex.status | translate
                          }}</span>
                        </td>
                        <td>{{ ex.rowCount | number }}</td>
                        <td>{{ ex.fileSizeBytes ? formatBytes(ex.fileSizeBytes) : '—' }}</td>
                        <td>{{ ex.requestedAt | date: 'short' }}</td>
                        <td class="rv-history-action">
                          <button
                            type="button"
                            class="rv-btn-secondary rv-history-download"
                            [disabled]="!ex.downloadReady"
                            [attr.aria-label]="'reports.export.history.downloadAria' | translate"
                            (click)="onDownload(ex)"
                          >
                            {{ 'reports.export.history.download' | translate }}
                          </button>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              }
            </div>
          }
        </section>
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
      /* US-RPT-002 AC-2: leave-balance color bands (green/yellow/red). Applied
         generically per cell only when the server supplies band data. */
      .rv-table td.rv-band-ok {
        background: #ecfdf5;
        color: #065f46;
        font-weight: 600;
      }
      .rv-table td.rv-band-warn {
        background: #fffbeb;
        color: #92400e;
        font-weight: 600;
      }
      .rv-table td.rv-band-low {
        background: #fef2f2;
        color: #991b1b;
        font-weight: 600;
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
      /* ── Export dropdown (US-RPT-004) ── */
      .rv-export {
        position: relative;
        display: inline-block;
      }
      .rv-export-mobile {
        display: none;
      }
      .rv-export-trigger {
        display: inline-flex;
        align-items: center;
        gap: 0.375rem;
      }
      .rv-export-menu {
        position: absolute;
        top: calc(100% + 0.375rem);
        left: 0;
        z-index: 20;
        min-width: 12rem;
        list-style: none;
        margin: 0;
        padding: 0.375rem;
        background: #fff;
        border: 1px solid #e5e7eb;
        border-radius: 0.625rem;
        box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
      }
      .rv-export-menu-right {
        left: auto;
        right: 0;
      }
      .rv-export-item {
        display: flex;
        align-items: center;
        gap: 0.625rem;
        width: 100%;
        text-align: left;
        background: transparent;
        border: none;
        border-radius: 0.5rem;
        padding: 0.5rem 0.625rem;
        font-size: 0.875rem;
        color: #374151;
        cursor: pointer;
      }
      .rv-export-item:hover,
      .rv-export-item:focus-visible {
        background: #f3f4f6;
      }
      .rv-fileicon {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 2.25rem;
        padding: 0.125rem 0.25rem;
        border-radius: 0.375rem;
        font-size: 0.6875rem;
        font-weight: 700;
        letter-spacing: 0.02em;
        color: #fff;
        background: #6b7280;
      }
      .rv-fileicon[data-tone='xlsx'] {
        background: #16a34a;
      }
      .rv-fileicon[data-tone='pdf'] {
        background: #dc2626;
      }
      .rv-fileicon[data-tone='csv'] {
        background: #6b7280;
      }
      .rv-spinner {
        width: 0.875rem;
        height: 0.875rem;
        border: 2px solid rgba(255, 255, 255, 0.5);
        border-top-color: #fff;
        border-radius: 50%;
        display: inline-block;
        animation: rv-spin 0.6s linear infinite;
      }
      @keyframes rv-spin {
        to {
          transform: rotate(360deg);
        }
      }
      /* ── My Exports history (§8) ── */
      .rv-history {
        background: #fff;
        border: 1px solid #f3f4f6;
        border-radius: 0.75rem;
        margin-top: 1.5rem;
        box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
      }
      .rv-history-toggle {
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
      .rv-history-body {
        padding: 0 1.25rem 1.25rem;
      }
      .rv-history-empty {
        color: #6b7280;
        font-size: 0.875rem;
        padding: 0.5rem 0;
      }
      .rv-history-table {
        width: 100%;
        border-collapse: collapse;
        font-size: 0.875rem;
      }
      .rv-history-table th,
      .rv-history-table td {
        padding: 0.5rem 0.75rem;
        text-align: left;
        border-bottom: 1px solid #f3f4f6;
        vertical-align: middle;
      }
      .rv-history-table th {
        font-weight: 600;
        color: #374151;
      }
      .rv-history-action {
        text-align: right;
      }
      .rv-history-download:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
      .rv-badge {
        display: inline-block;
        padding: 0.125rem 0.5rem;
        border-radius: 9999px;
        font-size: 0.75rem;
        font-weight: 600;
        background: #f3f4f6;
        color: #374151;
      }
      .rv-badge[data-status='Completed'] {
        background: #ecfdf5;
        color: #065f46;
      }
      .rv-badge[data-status='Queued'],
      .rv-badge[data-status='Processing'] {
        background: #eff6ff;
        color: #1d4ed8;
      }
      .rv-badge[data-status='Expired'] {
        background: #f3f4f6;
        color: #6b7280;
      }
      .rv-badge[data-status='Failed'] {
        background: #fef2f2;
        color: #991b1b;
      }
      .rv-skel-row {
        height: 2.25rem;
        border-radius: 0.5rem;
        background: #e5e7eb;
        margin-bottom: 0.5rem;
      }
      .rv-sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
      }
      @media (max-width: 768px) {
        .rv-charts,
        .rv-skel-summary {
          grid-template-columns: 1fr;
        }
        .rv-export-desktop {
          display: none;
        }
        .rv-export-mobile {
          display: inline-block;
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
  private readonly translate = inject(TranslateService);
  private readonly toastr = inject(ToastrService);
  private readonly auth = inject(AuthService);

  /** The report type resolved from the route param (AC-2..AC-4). */
  readonly reportType = signal<ReportType>('headcount');

  readonly result = signal<IReportResult | null>(null);
  readonly loading = signal<boolean>(false);
  readonly loadError = signal<string | null>(null);
  readonly view = signal<ViewMode>('chart');
  readonly filtersOpen = signal<boolean>(false);

  // ── US-RPT-004 export state ──────────────────────────────────────────────
  /** AC-1: the dropdown menu open/close. */
  readonly exportOpen = signal<boolean>(false);
  /** §8: spinner / disabled state while the export POST is in flight. */
  readonly exporting = signal<boolean>(false);
  /** §8: the "My Exports" history. */
  readonly exports = signal<IExportHistoryItem[]>([]);
  readonly historyOpen = signal<boolean>(false);
  readonly historyLoading = signal<boolean>(false);

  /** AC-1: the three offered formats, each with a file-type icon. */
  readonly exportOptions: IExportOption[] = [
    { format: 'csv', icon: 'description', tone: 'csv', labelKey: 'reports.export.format.csv' },
    { format: 'xlsx', icon: 'table_chart', tone: 'xlsx', labelKey: 'reports.export.format.xlsx' },
    { format: 'pdf', icon: 'picture_as_pdf', tone: 'pdf', labelKey: 'reports.export.format.pdf' },
  ];

  /** Permission gate (BR-1, §preconditions): only render export with Reports.Export. */
  readonly canExport = computed(() => this.auth.permissions().includes('Reports.Export'));

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
  // US-RPT-002 FR-2: leave & attendance filters.
  employeeId: string | null = null;
  leaveTypesRaw = '';
  shiftsRaw = '';

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
    this.employeeId = null;
    this.leaveTypesRaw = '';
    this.shiftsRaw = '';
    this.generate(false);
  }

  print(): void {
    window.print();
  }

  // ── US-RPT-004 export flow ────────────────────────────────────────────────

  toggleExportMenu(): void {
    this.exportOpen.update((open) => !open);
  }

  toggleHistory(): void {
    const next = !this.historyOpen();
    this.historyOpen.set(next);
    // Lazy-load the history the first time the panel is opened, or refresh it.
    if (next) {
      this.loadExports();
    }
  }

  /**
   * AC-1..AC-4: trigger an export in the chosen format. The branch is driven
   * purely by the JSON `status` — no content-type sniffing:
   *   - 'Completed' → synchronously download the file now (FR-5 / §8).
   *   - 'Queued'    → toast + refresh the history; the SignalR bell (US-NTF-001)
   *                   surfaces 'ReportExportReady' when the async job finishes.
   * `includeCharts` is sent only for PDF/Excel (the formats that embed charts).
   */
  onExport(format: ReportExportFormat): void {
    this.exportOpen.set(false);
    if (this.exporting()) {
      return;
    }
    this.exporting.set(true);

    const includeCharts = format === 'csv' ? undefined : true;
    this.service
      .exportReport(this.reportType(), this.buildFilters(), format, includeCharts)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          if (res.status === 'Completed') {
            // Sync path — pull the file immediately.
            this.fetchAndSave(res.exportId, format);
          } else {
            // Async path — Hangfire job queued.
            this.exporting.set(false);
            this.toastr.info(
              this.translate.instant('reports.export.queuedToast', {
                format: this.formatLabel(format),
              })
            );
            // Make sure the panel is visible and reflects the new Queued row.
            this.historyOpen.set(true);
            this.loadExports();
          }
        },
        error: (err: HttpErrorResponse) => {
          this.exporting.set(false);
          this.toastr.error(
            err.error?.message ??
              this.translate.instant('reports.export.error')
          );
        },
      });
  }

  /** §8: download a completed export from the history Download button (AC-5). */
  onDownload(item: IExportHistoryItem): void {
    if (!item.downloadReady) {
      return;
    }
    this.service
      .downloadExport(item.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (resp) => this.saveDownload(resp, this.fallbackName(item.format)),
        error: () =>
          this.toastr.error(this.translate.instant('reports.export.downloadError')),
      });
  }

  /** Refresh the "My Exports" list (§8). */
  loadExports(): void {
    this.historyLoading.set(true);
    this.service
      .listExports()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (items) => {
          this.exports.set(items);
          this.historyLoading.set(false);
        },
        error: () => {
          this.historyLoading.set(false);
          this.toastr.error(this.translate.instant('reports.export.historyError'));
        },
      });
  }

  /** Human-readable file size for the history table (§8). */
  formatBytes(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    const units = ['KB', 'MB', 'GB'];
    let value = bytes / 1024;
    let i = 0;
    while (value >= 1024 && i < units.length - 1) {
      value /= 1024;
      i++;
    }
    return `${value.toFixed(value < 10 ? 1 : 0)} ${units[i]}`;
  }

  /** Pull a Completed export's file and save it (sync path). */
  private fetchAndSave(exportId: string, format: ReportExportFormat): void {
    this.service
      .downloadExport(exportId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (resp) => {
          this.saveDownload(resp, this.fallbackName(format));
          this.exporting.set(false);
        },
        error: () => {
          this.exporting.set(false);
          this.toastr.error(this.translate.instant('reports.export.downloadError'));
        },
      });
  }

  /** Save a blob response, deriving the filename from Content-Disposition. */
  private saveDownload(resp: HttpResponse<Blob>, fallback: string): void {
    const blob = resp.body;
    if (!blob) {
      return;
    }
    const filename =
      filenameFromDisposition(resp.headers.get('Content-Disposition')) ?? fallback;
    downloadBlob(blob, filename);
  }

  private fallbackName(format: string): string {
    const ext = format === 'xlsx' ? 'xlsx' : format;
    return `${this.reportType()}.${ext}`;
  }

  private formatLabel(format: ReportExportFormat): string {
    return this.translate.instant(`reports.export.format.${format}`);
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
    filters.employeeId = this.employeeId?.trim() || null;
    filters.leaveTypeIds = this.splitCsv(this.leaveTypesRaw);
    filters.shiftIds = this.splitCsv(this.shiftsRaw);
    return filters;
  }

  /**
   * US-RPT-002 AC-2: the color band for a table cell, or null when the report
   * supplies no band data for it (degrades to a plain cell for non-balance
   * reports). Driven entirely by the server-provided {@link IReportTable.cellBands}.
   */
  bandAt(table: IReportTable, row: number, col: number): BalanceBand | null {
    return table.cellBands?.[row]?.[col] ?? null;
  }

  /** Translated tooltip describing a cell's band (AC-2), or null when no band. */
  bandTitle(table: IReportTable, row: number, col: number): string | null {
    const band = this.bandAt(table, row, col);
    return band ? this.translate.instant(`reports.viewer.band.${band}`) : null;
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
