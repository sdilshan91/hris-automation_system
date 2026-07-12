import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { RecommendationService } from '../../services/recommendation.service';
import {
  BUDGET_EXCEEDED_MESSAGE,
  BUDGET_HEALTH_BAR,
  BUDGET_HEALTH_TEXT,
  IAutoGenerateRule,
  IBudgetTracker,
  ICompletedCycleOption,
  IRecommendationRow,
  IRecommendationWorkspace,
  IUpdateRecommendationRequest,
  RECOMMENDATION_STATUS_BADGE,
  RECOMMENDATION_STATUS_LABEL,
  RECOMMENDATION_TYPES,
  RECOMMENDATION_TYPE_LABEL,
  RecommendationExportFormat,
  RecommendationStatus,
  RecommendationType,
  budgetConsumedPercent,
  budgetHealth,
  budgetRemaining,
  buildAutoPreview,
  formatTenure,
  isOverrideJustified,
} from '../../models/recommendation.models';

/**
 * US-PRF-010 AC-1/AC-2/AC-3 / FR-2/FR-3/FR-5/FR-6/FR-8 / §8: the HR recommendation
 * WORKSPACE. A sortable/filterable, paginated employee table for a completed cycle
 * (final score, current grade/band, tenure, compensation, manager flag, recommendation
 * fields) with:
 *  - an auto-generation WIZARD (rating-threshold → recommendation-type rules) with a
 *    PREVIEW before applying (BR-3);
 *  - INLINE editing of the recommendation type/amount/justification (a manual override
 *    requires a justification, FR-3);
 *  - a side-by-side current-vs-recommended COMPARISON (FR-5) in the edit drawer;
 *  - a BUDGET TRACKER widget (consumed vs remaining, green/amber/red, BR-4 soft warning);
 *  - SUBMIT into the approval workflow (AC-3) with status chips;
 *  - Excel/PDF EXPORT (only formats the backend reports available, FR-6).
 *
 * Sensitive compensation data (NFR-3/NFR-5) — HR/manager role-gated by the parent
 * /performance route guard; the backend enforces Performance.Publish.All + RLS and
 * may omit compensation when the caller lacks visibility (compensationVisible flag).
 * Notion-like, OnPush + signals, responsive from 360px, WCAG 2.1 AA.
 */
@Component({
  selector: 'app-recommendation-workspace',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
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
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('240ms ease-out', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- §8 sensitive-data banner -->
      <div
        class="mb-5 flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-xs text-amber-800"
        role="note"
        data-testid="sensitive-banner"
      >
        <span aria-hidden="true">🔒</span>
        <span
          >Recommendations include sensitive compensation data — restricted to
          authorized HR users.</span
        >
      </div>

      <!-- Header -->
      <div
        class="mb-6 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between"
      >
        <div>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Recommendations
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Promotions, bonuses and increments linked to appraisal outcomes.
          </p>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <a
            [routerLink]="['summary']"
            [queryParams]="{ cycleId: cycleId() }"
            class="inline-flex items-center rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm font-medium text-neutral-700 shadow-sm transition hover:bg-neutral-50"
            data-testid="view-summary"
          >
            View summary
          </a>
          @for (fmt of workspace()?.availableExportFormats ?? []; track fmt) {
            <button
              type="button"
              (click)="onExport(fmt)"
              [disabled]="exporting() !== null"
              class="inline-flex items-center rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm font-medium text-neutral-700 shadow-sm transition hover:bg-neutral-50 disabled:opacity-50"
              [attr.data-testid]="'export-' + fmt"
            >
              {{ exporting() === fmt ? 'Exporting…' : 'Export ' + fmt }}
            </button>
          }
          <button
            type="button"
            (click)="openWizard()"
            [disabled]="!cycleId()"
            class="inline-flex items-center rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
            [style.background-color]="'var(--brand-primary)'"
            data-testid="open-wizard"
          >
            Auto-Generate
          </button>
        </div>
      </div>

      <!-- Cycle picker + budget tracker -->
      <div class="mb-6 grid gap-4 lg:grid-cols-3">
        <label class="lg:col-span-1">
          <span class="mb-1 block text-xs font-medium text-neutral-500"
            >Completed cycle</span
          >
          <select
            [ngModel]="cycleId()"
            (ngModelChange)="onCycleChange($event)"
            class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400"
            data-testid="cycle-select"
          >
            <option value="" disabled>Select a cycle…</option>
            @for (c of cycles(); track c.cycleId) {
              <option [value]="c.cycleId">{{ c.cycleName }}</option>
            }
          </select>
        </label>

        <!-- FR-8/BR-4 budget tracker -->
        @if (budget()?.enabled) {
          <div
            class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm lg:col-span-2"
            data-testid="budget-tracker"
          >
            <div class="flex items-center justify-between text-sm">
              <span class="font-medium text-neutral-700">Budget</span>
              <span [class]="budgetTextClass()" data-testid="budget-remaining">
                {{ budgetRemainingValue() | number: '1.0-0' }}
                {{ budget()?.currency }} remaining
              </span>
            </div>
            <div
              class="mt-2 h-3 w-full overflow-hidden rounded-full bg-neutral-100"
              role="progressbar"
              [attr.aria-valuenow]="budgetPercent()"
              aria-valuemin="0"
              aria-valuemax="100"
            >
              <div
                class="h-full rounded-full transition-all duration-300"
                [class]="budgetBarClass()"
                [style.width.%]="budgetBarWidth()"
                data-testid="budget-bar"
              ></div>
            </div>
            <p class="mt-1 text-xs text-neutral-500">
              {{ budget()?.consumed | number: '1.0-0' }} of
              {{ budget()?.allocated | number: '1.0-0' }} consumed
            </p>
            @if (budgetExceeded()) {
              <p
                class="mt-1 text-xs font-medium text-rose-600"
                data-testid="budget-warning"
              >
                {{ exceededMessage }}
              </p>
            }
          </div>
        }
      </div>

      <!-- Filters -->
      <div class="mb-4 flex flex-wrap items-center gap-2">
        <input
          type="search"
          [ngModel]="search()"
          (ngModelChange)="search.set($event)"
          placeholder="Search employee…"
          class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 sm:w-56"
          aria-label="Search employee"
          data-testid="search"
        />
        <select
          [ngModel]="typeFilter()"
          (ngModelChange)="typeFilter.set($event)"
          class="rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none focus:border-neutral-400"
          aria-label="Filter by recommendation type"
          data-testid="type-filter"
        >
          <option value="all">All types</option>
          @for (t of typeOptions; track t) {
            <option [value]="t">{{ typeLabel(t) }}</option>
          }
        </select>
        <select
          [ngModel]="statusFilter()"
          (ngModelChange)="statusFilter.set($event)"
          class="rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none focus:border-neutral-400"
          aria-label="Filter by status"
          data-testid="status-filter"
        >
          <option value="all">All statuses</option>
          @for (s of statusOptions; track s) {
            <option [value]="s">{{ statusLabel(s) }}</option>
          }
        </select>
      </div>

      @if (loading()) {
        <div class="space-y-2">
          @for (i of [1, 2, 3, 4, 5]; track i) {
            <div class="h-12 animate-pulse rounded-lg bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="reload()">
            Retry
          </button>
        </div>
      } @else if (!cycleId()) {
        <div
          class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-12 text-center text-sm text-neutral-500"
        >
          Select a completed cycle to view recommendations.
        </div>
      } @else if (visibleRows().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-12 text-center text-sm text-neutral-500"
        >
          No employees match these filters.
        </div>
      } @else {
        <!-- Notion-like data table (condensed on mobile, §8) -->
        <div
          class="overflow-hidden rounded-xl border border-neutral-200 bg-white shadow-sm"
          @fadeIn
        >
          <div class="overflow-x-auto">
            <table class="min-w-full divide-y divide-neutral-100 text-sm">
              <thead class="bg-neutral-50 text-xs text-neutral-500">
                <tr>
                  <th class="px-4 py-3 text-left">
                    <button
                      type="button"
                      class="font-medium hover:text-neutral-700"
                      (click)="toggleSort('name')"
                      data-testid="sort-name"
                    >
                      Employee {{ sortGlyph('name') }}
                    </button>
                  </th>
                  <th class="px-4 py-3 text-right">
                    <button
                      type="button"
                      class="font-medium hover:text-neutral-700"
                      (click)="toggleSort('score')"
                      data-testid="sort-score"
                    >
                      Score {{ sortGlyph('score') }}
                    </button>
                  </th>
                  <th class="hidden px-4 py-3 text-left sm:table-cell">
                    Grade
                  </th>
                  <th class="hidden px-4 py-3 text-left md:table-cell">
                    Tenure
                  </th>
                  <th class="hidden px-4 py-3 text-right lg:table-cell">
                    Compensation
                  </th>
                  <th class="px-4 py-3 text-left">Recommendation</th>
                  <th class="px-4 py-3 text-left">Status</th>
                  <th class="px-4 py-3 text-right">Actions</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-neutral-50">
                @for (row of visibleRows(); track row.recommendationId) {
                  <tr class="transition hover:bg-neutral-50">
                    <td class="px-4 py-3">
                      <div class="font-medium text-neutral-900">
                        {{ row.employeeName }}
                      </div>
                      <div class="text-xs text-neutral-400">
                        {{ row.employeeNo }}
                        @if (row.managerFlag) {
                          <span
                            class="ml-1 rounded bg-violet-50 px-1.5 py-0.5 text-violet-600"
                            data-testid="manager-flag"
                            >{{ row.managerFlag }}</span
                          >
                        }
                      </div>
                    </td>
                    <td
                      class="px-4 py-3 text-right font-medium text-neutral-700"
                    >
                      {{ row.finalScore ?? '—' }}
                    </td>
                    <td class="hidden px-4 py-3 sm:table-cell">
                      {{ row.currentGrade ?? '—' }}
                    </td>
                    <td class="hidden px-4 py-3 md:table-cell">
                      {{ tenure(row.tenureMonths) }}
                    </td>
                    <td
                      class="hidden px-4 py-3 text-right lg:table-cell"
                      data-testid="compensation-cell"
                    >
                      {{
                        row.currentCompensation != null
                          ? (row.currentCompensation | number: '1.0-0')
                          : '—'
                      }}
                    </td>
                    <td class="px-4 py-3">
                      <span class="text-neutral-700">{{
                        typeLabel(row.detail.type)
                      }}</span>
                      @if (row.detail.amount != null) {
                        <span class="ml-1 text-xs text-neutral-400"
                          >({{ row.detail.amount | number: '1.0-0' }})</span
                        >
                      }
                    </td>
                    <td class="px-4 py-3">
                      <span
                        class="inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                        [class]="statusBadge(row.status)"
                        data-testid="status-chip"
                        >{{ statusLabel(row.status) }}</span
                      >
                    </td>
                    <td class="px-4 py-3 text-right">
                      <button
                        type="button"
                        (click)="openEdit(row)"
                        class="rounded-md px-2 py-1 text-xs font-medium text-neutral-600 transition hover:bg-neutral-100"
                        [attr.data-testid]="'edit-' + row.recommendationId"
                      >
                        Edit
                      </button>
                      @if (row.status === 'Draft') {
                        <button
                          type="button"
                          (click)="submit(row)"
                          [disabled]="submitting() === row.recommendationId"
                          class="ml-1 rounded-md px-2 py-1 text-xs font-medium text-sky-600 transition hover:bg-sky-50 disabled:opacity-50"
                          [attr.data-testid]="'submit-' + row.recommendationId"
                        >
                          Submit
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>

        <!-- Pagination -->
        <div
          class="mt-4 flex items-center justify-between text-sm text-neutral-500"
        >
          <span>{{ totalCount() }} employees</span>
          <div class="flex items-center gap-2">
            <button
              type="button"
              (click)="prevPage()"
              [disabled]="page() <= 1"
              class="rounded-md border border-neutral-200 px-3 py-1.5 disabled:opacity-40"
              data-testid="prev-page"
            >
              Prev
            </button>
            <span data-testid="page-indicator"
              >Page {{ page() }} of {{ totalPages() }}</span
            >
            <button
              type="button"
              (click)="nextPage()"
              [disabled]="page() >= totalPages()"
              class="rounded-md border border-neutral-200 px-3 py-1.5 disabled:opacity-40"
              data-testid="next-page"
            >
              Next
            </button>
          </div>
        </div>
      }
    </div>

    <!-- ───────── Auto-generate wizard (AC-2/FR-2) ───────── -->
    @if (wizardOpen()) {
      <div
        class="fixed inset-0 z-40 bg-neutral-900/30"
        (click)="closeWizard()"
        data-testid="wizard-backdrop"
      ></div>
      <div
        class="fixed inset-y-0 right-0 z-50 flex w-full max-w-lg flex-col bg-white shadow-xl"
        @drawer
        role="dialog"
        aria-label="Auto-generate recommendations"
        data-testid="wizard"
      >
        <div class="flex items-center justify-between border-b px-5 py-4">
          <h2 class="text-lg font-semibold text-neutral-900">
            Auto-Generate Recommendations
          </h2>
          <button
            type="button"
            (click)="closeWizard()"
            class="rounded-md p-1 text-neutral-400 hover:bg-neutral-100"
            aria-label="Close"
          >
            ✕
          </button>
        </div>

        <div class="flex-1 overflow-y-auto px-5 py-4">
          @if (wizardStep() === 1) {
            <p class="mb-3 text-sm text-neutral-500">
              Configure rating thresholds. The strongest matching rule wins for
              each employee.
            </p>
            @for (rule of wizardRules(); track $index) {
              <div
                class="mb-3 rounded-lg border border-neutral-200 p-3"
                data-testid="rule-row"
              >
                <div class="flex items-center gap-2">
                  <label class="text-xs text-neutral-500">Score ≥</label>
                  <input
                    type="number"
                    [ngModel]="rule.minScore"
                    (ngModelChange)="updateRule($index, 'minScore', +$event)"
                    class="w-20 rounded-md border border-neutral-200 px-2 py-1 text-sm"
                    [attr.data-testid]="'rule-score-' + $index"
                  />
                  <span class="text-xs text-neutral-400">→</span>
                  <select
                    [ngModel]="rule.type"
                    (ngModelChange)="updateRule($index, 'type', $event)"
                    class="flex-1 rounded-md border border-neutral-200 px-2 py-1 text-sm"
                    [attr.data-testid]="'rule-type-' + $index"
                  >
                    @for (t of ruleTypeOptions; track t) {
                      <option [value]="t">{{ typeLabel(t) }}</option>
                    }
                  </select>
                  <button
                    type="button"
                    (click)="removeRule($index)"
                    class="text-neutral-400 hover:text-rose-500"
                    aria-label="Remove rule"
                  >
                    ✕
                  </button>
                </div>
                <input
                  type="number"
                  [ngModel]="rule.amount"
                  (ngModelChange)="updateRule($index, 'amount', $event)"
                  placeholder="Default amount (optional)"
                  class="mt-2 w-full rounded-md border border-neutral-200 px-2 py-1 text-sm"
                  [attr.data-testid]="'rule-amount-' + $index"
                />
              </div>
            }
            <button
              type="button"
              (click)="addRule()"
              class="text-sm font-medium text-sky-600 hover:underline"
              data-testid="add-rule"
            >
              + Add rule
            </button>

            <label
              class="mt-4 flex items-center gap-2 text-sm text-neutral-600"
            >
              <input
                type="checkbox"
                [ngModel]="skipOverrides()"
                (ngModelChange)="skipOverrides.set($event)"
                data-testid="skip-overrides"
              />
              Skip employees with an existing manual recommendation
            </label>
          } @else {
            <!-- Step 2: PREVIEW before applying (BR-3) -->
            <p class="mb-3 text-sm text-neutral-500" data-testid="preview-summary">
              {{ previewAffected() }} of {{ previewRows().length }} employees
              will receive a recommendation.
            </p>
            <div
              class="max-h-[60vh] overflow-y-auto rounded-lg border border-neutral-100"
            >
              <table class="min-w-full text-sm">
                <thead class="bg-neutral-50 text-xs text-neutral-500">
                  <tr>
                    <th class="px-3 py-2 text-left">Employee</th>
                    <th class="px-3 py-2 text-right">Score</th>
                    <th class="px-3 py-2 text-left">Proposed</th>
                  </tr>
                </thead>
                <tbody>
                  @for (p of previewRows(); track p.employeeId) {
                    <tr
                      class="border-t border-neutral-50"
                      data-testid="preview-row"
                    >
                      <td class="px-3 py-2">{{ p.employeeName }}</td>
                      <td class="px-3 py-2 text-right">
                        {{ p.finalScore ?? '—' }}
                      </td>
                      <td class="px-3 py-2">
                        <span
                          [class.text-neutral-400]="p.proposedType === 'None'"
                          >{{ typeLabel(p.proposedType) }}</span
                        >
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>

        <div class="flex items-center justify-between gap-2 border-t px-5 py-4">
          @if (wizardStep() === 2) {
            <button
              type="button"
              (click)="wizardStep.set(1)"
              class="rounded-lg border border-neutral-200 px-4 py-2 text-sm font-medium text-neutral-700"
              data-testid="wizard-back"
            >
              Back
            </button>
          } @else {
            <span></span>
          }
          @if (wizardStep() === 1) {
            <button
              type="button"
              (click)="previewWizard()"
              [disabled]="wizardRules().length === 0"
              class="rounded-lg px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              data-testid="wizard-preview"
            >
              Preview
            </button>
          } @else {
            <button
              type="button"
              (click)="applyWizard()"
              [disabled]="applying()"
              class="rounded-lg px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              data-testid="wizard-apply"
            >
              {{ applying() ? 'Applying…' : 'Apply' }}
            </button>
          }
        </div>
      </div>
    }

    <!-- ───────── Inline edit drawer (§8/FR-3/FR-5) ───────── -->
    @if (editRow(); as row) {
      <div
        class="fixed inset-0 z-40 bg-neutral-900/30"
        (click)="closeEdit()"
        data-testid="edit-backdrop"
      ></div>
      <div
        class="fixed inset-y-0 right-0 z-50 flex w-full max-w-xl flex-col bg-white shadow-xl"
        @drawer
        role="dialog"
        [attr.aria-label]="'Edit recommendation for ' + row.employeeName"
        data-testid="edit-drawer"
      >
        <div class="flex items-center justify-between border-b px-5 py-4">
          <div>
            <h2 class="text-lg font-semibold text-neutral-900">
              {{ row.employeeName }}
            </h2>
            <p class="text-xs text-neutral-400">{{ row.employeeNo }}</p>
          </div>
          <button
            type="button"
            (click)="closeEdit()"
            class="rounded-md p-1 text-neutral-400 hover:bg-neutral-100"
            aria-label="Close"
          >
            ✕
          </button>
        </div>

        <div class="flex-1 overflow-y-auto px-5 py-4">
          <!-- FR-5 comparison cards: current vs recommended -->
          <div class="mb-5 grid gap-3 sm:grid-cols-2">
            <div
              class="rounded-lg border border-neutral-200 bg-neutral-50 p-3"
              data-testid="compare-current"
            >
              <p class="mb-2 text-xs font-medium uppercase text-neutral-400">
                Current
              </p>
              <dl class="space-y-1 text-sm">
                <div class="flex justify-between">
                  <dt class="text-neutral-400">Grade</dt>
                  <dd>{{ row.currentGrade ?? '—' }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-neutral-400">Title</dt>
                  <dd>{{ row.jobTitle ?? '—' }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-neutral-400">Compensation</dt>
                  <dd>
                    {{
                      row.currentCompensation != null
                        ? (row.currentCompensation | number: '1.0-0')
                        : '—'
                    }}
                  </dd>
                </div>
              </dl>
            </div>
            <div
              class="rounded-lg border border-emerald-200 bg-emerald-50 p-3"
              data-testid="compare-recommended"
            >
              <p class="mb-2 text-xs font-medium uppercase text-emerald-500">
                Recommended
              </p>
              <dl class="space-y-1 text-sm">
                <div class="flex justify-between">
                  <dt class="text-neutral-400">Grade</dt>
                  <dd>{{ form.targetGrade || row.currentGrade || '—' }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-neutral-400">Title</dt>
                  <dd>{{ form.targetTitle || row.jobTitle || '—' }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-neutral-400">Compensation</dt>
                  <dd>{{ recommendedCompensation(row) }}</dd>
                </div>
              </dl>
            </div>
          </div>

          <!-- Editable fields -->
          <label class="mb-3 block">
            <span class="mb-1 block text-xs font-medium text-neutral-500"
              >Recommendation type</span
            >
            <select
              [(ngModel)]="form.type"
              class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm"
              data-testid="edit-type"
            >
              @for (t of typeOptions; track t) {
                <option [value]="t">{{ typeLabel(t) }}</option>
              }
            </select>
          </label>

          @if (form.type === 'Bonus' || form.type === 'Increment') {
            <label class="mb-3 block">
              <span class="mb-1 block text-xs font-medium text-neutral-500"
                >Amount</span
              >
              <input
                type="number"
                [(ngModel)]="form.amount"
                class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm"
                data-testid="edit-amount"
              />
            </label>
          }

          @if (form.type === 'Promotion') {
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="mb-3 block">
                <span class="mb-1 block text-xs font-medium text-neutral-500"
                  >Target grade</span
                >
                <input
                  type="text"
                  [(ngModel)]="form.targetGrade"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm"
                  data-testid="edit-grade"
                />
              </label>
              <label class="mb-3 block">
                <span class="mb-1 block text-xs font-medium text-neutral-500"
                  >Effective date</span
                >
                <input
                  type="date"
                  [(ngModel)]="form.effectiveDate"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm"
                  data-testid="edit-effective"
                />
              </label>
            </div>
          }

          <!-- FR-3 mandatory justification on manual override -->
          <label class="mb-1 block">
            <span class="mb-1 block text-xs font-medium text-neutral-500">
              Justification
              @if (justificationRequired()) {
                <span class="text-rose-500">*</span>
              }
            </span>
            <textarea
              [(ngModel)]="form.justification"
              rows="3"
              class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm"
              [class.border-rose-300]="justificationRequired() && !justificationOk()"
              placeholder="Why this recommendation?"
              data-testid="edit-justification"
            ></textarea>
          </label>
          @if (justificationRequired() && !justificationOk()) {
            <p
              class="text-xs font-medium text-rose-600"
              data-testid="justification-error"
            >
              A justification is required to override this recommendation.
            </p>
          }
        </div>

        <div class="flex items-center justify-end gap-2 border-t px-5 py-4">
          <button
            type="button"
            (click)="closeEdit()"
            class="rounded-lg border border-neutral-200 px-4 py-2 text-sm font-medium text-neutral-700"
          >
            Cancel
          </button>
          <button
            type="button"
            (click)="saveEdit(row)"
            [disabled]="!canSaveEdit() || savingEdit()"
            class="rounded-lg px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
            [style.background-color]="'var(--brand-primary)'"
            data-testid="edit-save"
          >
            {{ savingEdit() ? 'Saving…' : 'Save' }}
          </button>
        </div>
      </div>
    }
  `,
})
export class RecommendationWorkspaceComponent implements OnInit {
  private readonly service = inject(RecommendationService);
  private readonly toastr = inject(ToastrService);

  readonly exceededMessage = BUDGET_EXCEEDED_MESSAGE;
  readonly typeOptions = RECOMMENDATION_TYPES;
  readonly ruleTypeOptions: RecommendationType[] = RECOMMENDATION_TYPES.filter(
    (t) => t !== 'None',
  );
  readonly statusOptions: RecommendationStatus[] = [
    'Draft',
    'Submitted',
    'PendingApproval',
    'Approved',
    'Rejected',
  ];

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly cycles = signal<ICompletedCycleOption[]>([]);
  readonly cycleId = signal<string>('');
  readonly workspace = signal<IRecommendationWorkspace | null>(null);
  readonly exporting = signal<RecommendationExportFormat | null>(null);
  readonly submitting = signal<string | null>(null);

  // filters / paging
  readonly search = signal('');
  readonly typeFilter = signal<RecommendationType | 'all'>('all');
  readonly statusFilter = signal<RecommendationStatus | 'all'>('all');
  readonly sortKey = signal<'name' | 'score'>('score');
  readonly sortDir = signal<'asc' | 'desc'>('desc');
  readonly page = signal(1);
  readonly pageSize = 20;

  // wizard
  readonly wizardOpen = signal(false);
  readonly wizardStep = signal<1 | 2>(1);
  readonly wizardRules = signal<IAutoGenerateRule[]>([]);
  readonly skipOverrides = signal(true);
  readonly applying = signal(false);
  private previewSnapshot = signal<ReturnType<typeof buildAutoPreview> | null>(
    null,
  );

  // inline edit
  readonly editRow = signal<IRecommendationRow | null>(null);
  readonly savingEdit = signal(false);
  form: IUpdateRecommendationRequest = this.blankForm();

  readonly rows = computed(() => this.workspace()?.page.rows ?? []);
  readonly budget = computed<IBudgetTracker | null>(
    () => this.workspace()?.budget ?? null,
  );
  readonly totalCount = computed(() => this.workspace()?.page.totalCount ?? 0);
  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize)),
  );

  /** Client-side filter over the current page (server also filters; this keeps the UI snappy). */
  readonly visibleRows = computed(() => {
    const term = this.search().trim().toLowerCase();
    const type = this.typeFilter();
    const status = this.statusFilter();
    return this.rows().filter((r) => {
      if (term && !`${r.employeeName} ${r.employeeNo}`.toLowerCase().includes(term))
        return false;
      if (type !== 'all' && r.detail.type !== type) return false;
      if (status !== 'all' && r.status !== status) return false;
      return true;
    });
  });

  readonly budgetPercent = computed(() => {
    const b = this.budget();
    return b ? budgetConsumedPercent(b) : 0;
  });
  /** Bar width clamps the visual at 100% even when consumed exceeds allocated. */
  readonly budgetBarWidth = computed(() => Math.min(100, this.budgetPercent()));
  readonly budgetRemainingValue = computed(() => {
    const b = this.budget();
    return b ? budgetRemaining(b) : 0;
  });
  readonly budgetExceeded = computed(() => {
    const b = this.budget();
    return b ? budgetHealth(b) === 'Exceeded' : false;
  });
  readonly budgetBarClass = computed(() => {
    const b = this.budget();
    return b ? BUDGET_HEALTH_BAR[budgetHealth(b)] : BUDGET_HEALTH_BAR.Healthy;
  });
  readonly budgetTextClass = computed(() => {
    const b = this.budget();
    return b ? BUDGET_HEALTH_TEXT[budgetHealth(b)] : BUDGET_HEALTH_TEXT.Healthy;
  });

  // wizard preview projections
  readonly previewRows = computed(() => this.previewSnapshot()?.rows ?? []);
  readonly previewAffected = computed(
    () => this.previewSnapshot()?.affectedCount ?? 0,
  );

  // edit gates — METHODS, not computeds: they read `form.*` which are plain object
  // properties (not signals), so a computed would cache against its (empty) signal
  // deps and never re-evaluate as the template mutates `form` via ngModel. (Same
  // reasoning as US-PRF-004's canSave().)
  justificationRequired(): boolean {
    if (!this.editRow()) return false;
    // A non-None recommendation requires a justification (FR-3); clearing to None
    // never does. Mirrors isOverrideJustified.
    return this.form.type !== 'None';
  }
  justificationOk(): boolean {
    return (this.form.justification ?? '').trim().length > 0;
  }
  canSaveEdit(): boolean {
    const row = this.editRow();
    if (!row) return false;
    return isOverrideJustified(this.form, row.autoGenerated);
  }

  ngOnInit(): void {
    this.loadCycles();
  }

  private loadCycles(): void {
    this.service.getCompletedCycles().subscribe({
      next: (cycles) => {
        this.cycles.set(cycles);
        if (cycles.length > 0) {
          this.cycleId.set(cycles[0].cycleId);
          this.reload();
        }
      },
      error: () => this.error.set('Unable to load completed cycles.'),
    });
  }

  onCycleChange(cycleId: string): void {
    this.cycleId.set(cycleId);
    this.page.set(1);
    this.reload();
  }

  reload(): void {
    if (!this.cycleId()) return;
    this.loading.set(true);
    this.error.set(null);
    this.service
      .getWorkspace(this.cycleId(), {
        sort: this.sortKey(),
        dir: this.sortDir(),
        page: this.page(),
        pageSize: this.pageSize,
      })
      .subscribe({
        next: (ws) => {
          this.workspace.set(ws);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Unable to load the recommendation workspace.');
          this.loading.set(false);
        },
      });
  }

  // ── sort / paging ──
  toggleSort(key: 'name' | 'score'): void {
    if (this.sortKey() === key) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortKey.set(key);
      this.sortDir.set(key === 'score' ? 'desc' : 'asc');
    }
    this.page.set(1);
    this.reload();
  }
  sortGlyph(key: 'name' | 'score'): string {
    if (this.sortKey() !== key) return '';
    return this.sortDir() === 'asc' ? '▲' : '▼';
  }
  prevPage(): void {
    if (this.page() > 1) {
      this.page.set(this.page() - 1);
      this.reload();
    }
  }
  nextPage(): void {
    if (this.page() < this.totalPages()) {
      this.page.set(this.page() + 1);
      this.reload();
    }
  }

  // ── wizard (AC-2/FR-2) ──
  openWizard(): void {
    if (this.wizardRules().length === 0) {
      this.wizardRules.set([
        { minScore: 4.5, type: 'Promotion', amount: null, percentage: null },
        { minScore: 3.5, type: 'Bonus', amount: null, percentage: null },
      ]);
    }
    this.wizardStep.set(1);
    this.wizardOpen.set(true);
  }
  closeWizard(): void {
    this.wizardOpen.set(false);
  }
  addRule(): void {
    this.wizardRules.update((rs) => [
      ...rs,
      { minScore: 3, type: 'Bonus', amount: null, percentage: null },
    ]);
  }
  removeRule(index: number): void {
    this.wizardRules.update((rs) => rs.filter((_, i) => i !== index));
  }
  updateRule(
    index: number,
    field: keyof IAutoGenerateRule,
    value: unknown,
  ): void {
    this.wizardRules.update((rs) =>
      rs.map((r, i) =>
        i === index
          ? {
              ...r,
              [field]:
                field === 'amount' && (value === '' || value == null)
                  ? null
                  : value,
            }
          : r,
      ),
    );
  }
  /** Build + show the PREVIEW (BR-3) from the current page rows; pure model fn. */
  previewWizard(): void {
    const preview = buildAutoPreview(
      this.rows(),
      this.wizardRules(),
      this.skipOverrides(),
    );
    this.previewSnapshot.set(preview);
    this.wizardStep.set(2);
  }
  applyWizard(): void {
    this.applying.set(true);
    this.service
      .autoGenerate(
        {
          cycleId: this.cycleId(),
          rules: this.wizardRules(),
          skipManualOverrides: this.skipOverrides(),
        },
        false,
      )
      .subscribe({
        next: () => {
          this.applying.set(false);
          this.wizardOpen.set(false);
          this.toastr.success('Recommendations generated.');
          this.reload();
        },
        error: () => {
          this.applying.set(false);
          this.toastr.error('Auto-generation failed. Please try again.');
        },
      });
  }

  // ── inline edit (§8/FR-3) ──
  openEdit(row: IRecommendationRow): void {
    this.form = {
      type: row.detail.type,
      amount: row.detail.amount,
      percentage: row.detail.percentage,
      targetGrade: row.detail.targetGrade,
      targetTitle: row.detail.targetTitle,
      effectiveDate: row.detail.effectiveDate,
      trainingCourse: row.detail.trainingCourse,
      justification: row.detail.justification,
    };
    this.editRow.set(row);
  }
  closeEdit(): void {
    this.editRow.set(null);
  }
  saveEdit(row: IRecommendationRow): void {
    if (!this.canSaveEdit()) {
      this.toastr.error('A justification is required to override.');
      return;
    }
    this.savingEdit.set(true);
    this.service
      .updateRecommendation(row.employeeId, this.cycleId(), this.form)
      .subscribe({
      next: () => {
        this.savingEdit.set(false);
        this.editRow.set(null);
        this.toastr.success('Recommendation updated.');
        this.reload();
      },
      error: () => {
        this.savingEdit.set(false);
        this.toastr.error('Unable to save the recommendation.');
      },
    });
  }
  /** FR-5 comparison: recommended compensation = override amount or current. */
  recommendedCompensation(row: IRecommendationRow): string {
    const base = row.currentCompensation;
    if (this.form.type === 'Increment' && this.form.amount != null) {
      return base != null
        ? (base + this.form.amount).toLocaleString()
        : this.form.amount.toLocaleString();
    }
    return base != null ? base.toLocaleString() : '—';
  }

  // ── submit (AC-3) ──
  submit(row: IRecommendationRow): void {
    this.submitting.set(row.recommendationId);
    this.service.submit(row.recommendationId).subscribe({
      next: () => {
        this.submitting.set(null);
        this.toastr.success('Recommendation submitted for approval.');
        this.reload();
      },
      error: () => {
        this.submitting.set(null);
        this.toastr.error('Unable to submit the recommendation.');
      },
    });
  }

  // ── export (FR-6) ──
  onExport(format: RecommendationExportFormat): void {
    this.exporting.set(format);
    this.service.export(format, this.cycleId()).subscribe({
      next: (response) => {
        const blob = response.body;
        if (blob) {
          const filename =
            this.filenameFromDisposition(
              response.headers.get('Content-Disposition'),
            ) ?? `recommendations.${format === 'Excel' ? 'xlsx' : 'pdf'}`;
          this.triggerDownload(blob, filename);
        }
        this.exporting.set(null);
      },
      error: () => {
        this.exporting.set(null);
        this.toastr.error('Unable to generate the export.');
      },
    });
  }

  private filenameFromDisposition(disposition: string | null): string | null {
    if (!disposition) return null;
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    return match ? decodeURIComponent(match[1]) : null;
  }
  private triggerDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
  }

  // ── label helpers ──
  typeLabel(t: RecommendationType): string {
    return RECOMMENDATION_TYPE_LABEL[t];
  }
  statusLabel(s: RecommendationStatus): string {
    return RECOMMENDATION_STATUS_LABEL[s];
  }
  statusBadge(s: RecommendationStatus): string {
    return RECOMMENDATION_STATUS_BADGE[s];
  }
  tenure(months: number | null): string {
    return formatTenure(months);
  }

  private blankForm(): IUpdateRecommendationRequest {
    return {
      type: 'None',
      amount: null,
      percentage: null,
      targetGrade: null,
      targetTitle: null,
      effectiveDate: null,
      trainingCourse: null,
      justification: null,
    };
  }
}
