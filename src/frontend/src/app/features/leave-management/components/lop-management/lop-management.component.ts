import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

import { LopService } from '../../services/lop.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import { EmployeeService } from '../../../core-hr/employees/services/employee.service';
import { ILeaveType } from '../../models/leave-type.models';
import { IEmployee } from '../../../core-hr/employees/models/employee.models';
import {
  ILopEntry,
  LopSourceFilter,
  LOP_SOURCE_FILTERS,
  lopSourceLabel,
  lopRowClasses,
  lopSourceBadgeClasses,
  canOverrideLop,
  filterLopEntries,
  expandDateRange,
} from '../../models/lop.models';

/** Cross-field validator: start date must be on or before end date. */
function dateRangeValidator(group: AbstractControl): ValidationErrors | null {
  const start = group.get('from')?.value;
  const end = group.get('to')?.value;
  if (!start || !end) {
    return null;
  }
  return start <= end ? null : { dateRange: true };
}

/** Which side panel / dialog is open. */
type ActionPanel = 'none' | 'bulk' | 'compulsory' | 'override';

/**
 * US-LV-011 (§8): HR Loss-of-Pay / Compulsory-Leave management screen.
 *
 * Lists LOP `leave_request` entries with source filters (auto-generated /
 * HR-assigned / employee-requested / compulsory), red/orange row highlighting,
 * and three HR actions:
 *   - Bulk LOP assignment   (multi-select employee picker + date range + reason)
 *   - Compulsory leave       (date range + leave type + "Apply to all")
 *   - Override (BR-3)         (convert a system-generated LOP to another type)
 *
 * Desktop-optimized (§8) — complex actions live in slide-over panels; the layout
 * stacks/scrolls down to 360px so it remains accessible on mobile.
 *
 * Role-gated to HR Officer / Tenant Admin via the route guard (matches leave-config).
 */
@Component({
  selector: 'app-lop-management',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('overlayFade', [
      transition(':enter', [style({ opacity: 0 }), animate('150ms ease-out', style({ opacity: 1 }))]),
      transition(':leave', [animate('120ms ease-in', style({ opacity: 0 }))]),
    ]),
    trigger('slideOver', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('220ms ease-out', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [animate('180ms ease-in', style({ transform: 'translateX(100%)' }))]),
    ]),
    trigger('modalPop', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.96) translateY(8px)' }),
        animate('180ms ease-out', style({ opacity: 1, transform: 'scale(1) translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Loss of Pay (LOP)</h1>
          <p class="text-sm text-neutral-500 mt-1">
            Review LOP entries, assign LOP or compulsory leave, and override auto-generated entries.
          </p>
        </div>
        <div class="flex flex-wrap gap-2">
          <button type="button" class="btn-secondary" (click)="openPanel('compulsory')">
            Compulsory leave
          </button>
          <button type="button" class="btn-primary" (click)="openPanel('bulk')">
            Assign LOP
          </button>
        </div>
      </div>

      <!-- Filters (source chips) -->
      <div class="flex flex-wrap items-center gap-2 mb-4" role="group" aria-label="Filter LOP by source">
        @for (f of sourceFilters; track f.value) {
          <button type="button" class="filter-chip"
            [class.filter-chip-active]="activeFilter() === f.value"
            [attr.aria-pressed]="activeFilter() === f.value"
            (click)="setFilter(f.value)">
            {{ f.label }}
          </button>
        }
      </div>

      <!-- List -->
      @if (isLoading()) {
        <div class="card-notion space-y-3" aria-live="polite" aria-busy="true">
          @for (_ of [1,2,3,4]; track $index) {
            <div class="skeleton-line w-full h-12"></div>
          }
        </div>
      } @else if (filteredEntries().length === 0) {
        <div class="card-notion text-center py-12" data-testid="lop-empty">
          <p class="text-sm text-neutral-500">No LOP entries for this filter.</p>
        </div>
      } @else {
        <!-- Desktop table -->
        <div class="card-notion hidden md:block overflow-x-auto" data-testid="lop-table">
          <table class="w-full text-sm">
            <thead>
              <tr class="text-left text-xs font-medium text-neutral-400 border-b border-neutral-100">
                <th class="py-2.5 pr-3">Employee</th>
                <th class="py-2.5 pr-3">Date</th>
                <th class="py-2.5 pr-3">Days</th>
                <th class="py-2.5 pr-3">Source</th>
                <th class="py-2.5 pr-3">Reason</th>
                <th class="py-2.5 pr-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (e of filteredEntries(); track e.leaveRequestId) {
                <tr class="border-b border-neutral-50" [class]="rowClasses(e)" data-testid="lop-row">
                  <td class="py-2.5 pr-3">
                    <span class="font-medium text-neutral-800">{{ e.employeeName }}</span>
                    @if (e.employeeNo) {
                      <span class="text-xs text-neutral-400 ml-1">#{{ e.employeeNo }}</span>
                    }
                  </td>
                  <td class="py-2.5 pr-3 text-neutral-600 whitespace-nowrap">{{ e.date }}</td>
                  <td class="py-2.5 pr-3 text-neutral-600">{{ e.days }}</td>
                  <td class="py-2.5 pr-3">
                    <span class="src-badge" [class]="badgeClasses(e)">{{ sourceLabel(e) }}</span>
                  </td>
                  <td class="py-2.5 pr-3 text-neutral-500 max-w-[16rem] truncate">{{ e.reason || '—' }}</td>
                  <td class="py-2.5 pr-3 text-right">
                    @if (canOverride(e)) {
                      <button type="button" class="link-btn" (click)="openOverride(e)">Override</button>
                    } @else {
                      <span class="text-xs text-neutral-300">—</span>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile cards (stack/scroll at 360px) -->
        <div class="md:hidden space-y-3">
          @for (e of filteredEntries(); track e.leaveRequestId) {
            <div class="card-notion" [class]="rowClasses(e)" data-testid="lop-card">
              <div class="flex items-start justify-between gap-2">
                <div>
                  <p class="font-medium text-neutral-800">{{ e.employeeName }}</p>
                  <p class="text-xs text-neutral-500 mt-0.5">{{ e.date }} · {{ e.days }} day(s)</p>
                </div>
                <span class="src-badge" [class]="badgeClasses(e)">{{ sourceLabel(e) }}</span>
              </div>
              @if (e.reason) {
                <p class="text-sm text-neutral-500 mt-2">{{ e.reason }}</p>
              }
              @if (canOverride(e)) {
                <button type="button" class="link-btn mt-2" (click)="openOverride(e)">Override</button>
              }
            </div>
          }
        </div>
      }
    </div>

    <!-- ─── Bulk LOP assignment slide-over (FR-3) ─── -->
    @if (panel() === 'bulk') {
      <div class="overlay" @overlayFade (click)="closePanel()"></div>
      <aside class="slideover" @slideOver role="dialog" aria-modal="true" aria-labelledby="bulk-title">
        <form [formGroup]="bulkForm" (ngSubmit)="submitBulk()" class="flex flex-col h-full">
          <div class="slideover-header">
            <h2 id="bulk-title" class="text-lg font-semibold text-neutral-900">Assign LOP</h2>
            <button type="button" class="icon-btn" (click)="closePanel()" aria-label="Close">✕</button>
          </div>
          <div class="slideover-body space-y-5">
            <!-- Multi-select employee picker -->
            <div>
              <label class="label-sm" for="empSearch">Employees</label>
              <input id="empSearch" type="text" class="input-field" [value]="employeeSearch()"
                (input)="onEmployeeSearch($event)" placeholder="Search employees..."
                aria-label="Search employees" />
              @if (selectedEmployeeIds().length > 0) {
                <div class="flex flex-wrap gap-1.5 mt-2">
                  @for (id of selectedEmployeeIds(); track id) {
                    <span class="emp-chip">
                      {{ employeeName(id) }}
                      <button type="button" (click)="toggleEmployee(id)"
                        [attr.aria-label]="'Remove ' + employeeName(id)">✕</button>
                    </span>
                  }
                </div>
              }
              <div class="emp-list mt-2">
                @for (emp of filteredEmployees(); track emp.employeeId) {
                  <button type="button" class="emp-option"
                    [class.emp-option-selected]="isEmployeeSelected(emp.employeeId)"
                    (click)="toggleEmployee(emp.employeeId)">
                    <span>{{ emp.firstName }} {{ emp.lastName }}</span>
                    @if (isEmployeeSelected(emp.employeeId)) {
                      <span class="text-brand-600">✓</span>
                    }
                  </button>
                } @empty {
                  <p class="text-xs text-neutral-400 p-2">No matching employees.</p>
                }
              </div>
              @if (bulkSubmitted() && selectedEmployeeIds().length === 0) {
                <p class="error-text">Select at least one employee.</p>
              }
            </div>

            <!-- Date range -->
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="label-sm" for="bulkFrom">From</label>
                <input id="bulkFrom" type="date" class="input-field" formControlName="from" />
              </div>
              <div>
                <label class="label-sm" for="bulkTo">To</label>
                <input id="bulkTo" type="date" class="input-field" formControlName="to" />
              </div>
            </div>
            @if (bulkForm.errors?.['dateRange']) {
              <p class="error-text">End date must be on or after the start date.</p>
            }
            @if (showBulkError('from') || showBulkError('to')) {
              <p class="error-text">A date range is required.</p>
            }

            <!-- Reason -->
            <div>
              <label class="label-sm" for="bulkReason">Reason</label>
              <textarea id="bulkReason" class="input-field" rows="3" formControlName="reason"
                placeholder="Reason for LOP"></textarea>
              @if (showBulkError('reason')) {
                <p class="error-text">A reason is required.</p>
              }
            </div>
          </div>
          <div class="slideover-footer">
            <button type="button" class="btn-secondary" (click)="closePanel()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="isSaving()">
              @if (isSaving()) { <span class="btn-spinner"></span> Assigning... } @else { Assign LOP }
            </button>
          </div>
        </form>
      </aside>
    }

    <!-- ─── Compulsory leave slide-over (FR-6) ─── -->
    @if (panel() === 'compulsory') {
      <div class="overlay" @overlayFade (click)="closePanel()"></div>
      <aside class="slideover" @slideOver role="dialog" aria-modal="true" aria-labelledby="comp-title">
        <form [formGroup]="compForm" (ngSubmit)="submitCompulsory()" class="flex flex-col h-full">
          <div class="slideover-header">
            <h2 id="comp-title" class="text-lg font-semibold text-neutral-900">Compulsory leave</h2>
            <button type="button" class="icon-btn" (click)="closePanel()" aria-label="Close">✕</button>
          </div>
          <div class="slideover-body space-y-5">
            <p class="text-xs text-neutral-500">
              Deducts from each employee's balance first; if insufficient, it becomes LOP.
            </p>
            <!-- Date range -->
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="label-sm" for="compFrom">From</label>
                <input id="compFrom" type="date" class="input-field" formControlName="from" />
              </div>
              <div>
                <label class="label-sm" for="compTo">To</label>
                <input id="compTo" type="date" class="input-field" formControlName="to" />
              </div>
            </div>
            @if (compForm.errors?.['dateRange']) {
              <p class="error-text">End date must be on or after the start date.</p>
            }
            @if (showCompError('from') || showCompError('to')) {
              <p class="error-text">A date range is required.</p>
            }

            <!-- Leave type -->
            <div>
              <label class="label-sm" for="compType">Leave type</label>
              <select id="compType" class="input-field select-input" formControlName="leaveTypeId">
                <option value="">Select a leave type...</option>
                @for (lt of leaveTypes(); track lt.leaveTypeId) {
                  <option [value]="lt.leaveTypeId">{{ lt.name }}</option>
                }
              </select>
              @if (showCompError('leaveTypeId')) {
                <p class="error-text">Please select a leave type.</p>
              }
            </div>

            <!-- Apply to all -->
            <label class="flex items-center gap-2.5 cursor-pointer">
              <input type="checkbox" class="w-4 h-4 rounded border-neutral-300 text-brand-600"
                formControlName="applyToAll" />
              <span class="text-sm font-medium text-neutral-700">Apply to all employees</span>
            </label>

            <!-- Reason -->
            <div>
              <label class="label-sm" for="compReason">Reason</label>
              <textarea id="compReason" class="input-field" rows="3" formControlName="reason"
                placeholder="e.g. Company shutdown"></textarea>
              @if (showCompError('reason')) {
                <p class="error-text">A reason is required.</p>
              }
            </div>
          </div>
          <div class="slideover-footer">
            <button type="button" class="btn-secondary" (click)="closePanel()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="isSaving()">
              @if (isSaving()) { <span class="btn-spinner"></span> Assigning... } @else { Assign compulsory leave }
            </button>
          </div>
        </form>
      </aside>
    }

    <!-- ─── Override modal (BR-3) ─── -->
    @if (panel() === 'override' && overrideTarget(); as target) {
      <div class="overlay flex items-center justify-center p-4" @overlayFade>
        <div class="override-modal" @modalPop role="dialog" aria-modal="true" aria-labelledby="ovr-title">
          <h2 id="ovr-title" class="text-base font-semibold text-neutral-900">Override LOP entry</h2>
          <p class="text-sm text-neutral-500 mt-1">
            Convert {{ target.employeeName }}'s auto-generated LOP on {{ target.date }} to a leave type.
          </p>
          <form [formGroup]="overrideForm" (ngSubmit)="submitOverride()" class="mt-4 space-y-4">
            <div>
              <label class="label-sm" for="ovrType">Convert to</label>
              <select id="ovrType" class="input-field select-input" formControlName="leaveTypeId">
                <option value="">Select a leave type...</option>
                @for (lt of leaveTypes(); track lt.leaveTypeId) {
                  <option [value]="lt.leaveTypeId">{{ lt.name }}</option>
                }
              </select>
              @if (showOverrideError('leaveTypeId')) {
                <p class="error-text">Please select a leave type.</p>
              }
            </div>
            <div>
              <label class="label-sm" for="ovrReason">Reason</label>
              <textarea id="ovrReason" class="input-field" rows="2" formControlName="reason"
                placeholder="Reason for the override"></textarea>
              @if (showOverrideError('reason')) {
                <p class="error-text">A reason is required.</p>
              }
            </div>
            <div class="flex flex-col-reverse sm:flex-row sm:justify-end gap-2.5 pt-1">
              <button type="button" class="btn-secondary" (click)="closePanel()">Cancel</button>
              <button type="submit" class="btn-primary" [disabled]="isSaving()">
                @if (isSaving()) { <span class="btn-spinner"></span> Saving... } @else { Convert }
              </button>
            </div>
          </form>
        </div>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-5xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .label-sm { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .input-field {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900
        placeholder-neutral-400 transition-all duration-150
        focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-400;
    }
    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }
    .error-text { @apply text-xs text-red-600 mt-1; }

    .filter-chip {
      @apply inline-flex items-center px-3 py-1.5 rounded-full text-xs font-medium
        bg-white text-neutral-600 ring-1 ring-inset ring-neutral-200 transition-all duration-150
        hover:bg-neutral-50;
    }
    .filter-chip-active { @apply bg-brand-600 text-white ring-brand-600 hover:bg-brand-700; }

    .src-badge {
      @apply inline-flex items-center px-2 py-0.5 rounded-md text-xs font-medium ring-1 ring-inset;
    }
    .link-btn {
      @apply text-xs font-medium text-brand-600 hover:text-brand-700 hover:underline transition-colors;
    }

    .emp-chip {
      @apply inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-xs font-medium
        bg-brand-50 text-brand-700;
    }
    .emp-chip button { @apply text-brand-400 hover:text-brand-700; }
    .emp-list { @apply max-h-48 overflow-y-auto rounded-lg border border-neutral-200 divide-y divide-neutral-50; }
    .emp-option {
      @apply w-full flex items-center justify-between px-3 py-2 text-sm text-neutral-700 text-left
        transition-colors hover:bg-neutral-50;
    }
    .emp-option-selected { @apply bg-brand-50/50; }

    .overlay { @apply fixed inset-0 z-40 bg-neutral-900/40 backdrop-blur-sm; }
    .slideover {
      @apply fixed top-0 right-0 z-50 h-full w-full max-w-md bg-white shadow-xl flex flex-col;
    }
    .slideover-header {
      @apply flex items-center justify-between px-5 py-4 border-b border-neutral-100;
    }
    .slideover-body { @apply flex-1 overflow-y-auto px-5 py-5; }
    .slideover-footer {
      @apply flex items-center justify-end gap-3 px-5 py-4 border-t border-neutral-100;
    }
    .icon-btn { @apply text-neutral-400 hover:text-neutral-700 transition-colors text-lg leading-none; }

    .override-modal { @apply w-full max-w-md rounded-xl bg-white shadow-xl ring-1 ring-neutral-200 p-5; }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class LopManagementComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly lopService = inject(LopService);
  private readonly leaveTypeService = inject(LeaveTypeService);
  private readonly employeeService = inject(EmployeeService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly sourceFilters = LOP_SOURCE_FILTERS;

  // ─── Data signals ─────────────────────────────────────────
  readonly entries = signal<ILopEntry[]>([]);
  readonly leaveTypes = signal<ILeaveType[]>([]);
  readonly employees = signal<IEmployee[]>([]);
  readonly isLoading = signal(true);
  readonly isSaving = signal(false);

  readonly activeFilter = signal<LopSourceFilter>('all');
  readonly panel = signal<ActionPanel>('none');
  readonly overrideTarget = signal<ILopEntry | null>(null);

  // Bulk-assign multi-select state.
  readonly employeeSearch = signal('');
  readonly selectedEmployeeIds = signal<string[]>([]);
  readonly bulkSubmitted = signal(false);

  // ─── Forms ────────────────────────────────────────────────
  readonly bulkForm: FormGroup = this.fb.group(
    {
      from: ['', Validators.required],
      to: ['', Validators.required],
      reason: ['', [Validators.required, Validators.maxLength(500)]],
    },
    { validators: [dateRangeValidator] },
  );

  readonly compForm: FormGroup = this.fb.group(
    {
      from: ['', Validators.required],
      to: ['', Validators.required],
      leaveTypeId: ['', Validators.required],
      applyToAll: [true],
      reason: ['', [Validators.required, Validators.maxLength(500)]],
    },
    { validators: [dateRangeValidator] },
  );

  readonly overrideForm: FormGroup = this.fb.group({
    leaveTypeId: ['', Validators.required],
    reason: ['', [Validators.required, Validators.maxLength(500)]],
  });

  // ─── Computed ─────────────────────────────────────────────

  readonly filteredEntries = computed(() =>
    filterLopEntries(this.entries(), this.activeFilter()),
  );

  readonly filteredEmployees = computed(() => {
    const term = this.employeeSearch().trim().toLowerCase();
    const list = this.employees();
    if (!term) {
      return list.slice(0, 50);
    }
    return list
      .filter((e) =>
        `${e.firstName} ${e.lastName} ${e.email}`.toLowerCase().includes(term),
      )
      .slice(0, 50);
  });

  // ─── Lifecycle ────────────────────────────────────────────

  ngOnInit(): void {
    this.loadEntries();
    this.loadLeaveTypes();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ─────────────────────────────────────────

  loadEntries(): void {
    this.isLoading.set(true);
    this.lopService
      .getLopSummary()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (entries) => {
          this.entries.set(entries);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.toastr.error('Failed to load LOP entries.');
        },
      });
  }

  loadLeaveTypes(): void {
    this.leaveTypeService
      .getLeaveTypes()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (types) => this.leaveTypes.set(types.filter((t) => t.isActive)),
        error: () => this.toastr.error('Failed to load leave types.'),
      });
  }

  loadEmployees(): void {
    if (this.employees().length > 0) {
      return;
    }
    this.employeeService
      .getEmployees()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (list) => this.employees.set(list),
        error: () => this.toastr.error('Failed to load employees.'),
      });
  }

  // ─── List helpers ─────────────────────────────────────────

  setFilter(filter: LopSourceFilter): void {
    this.activeFilter.set(filter);
  }

  rowClasses(entry: ILopEntry): string {
    return lopRowClasses(entry.source);
  }

  badgeClasses(entry: ILopEntry): string {
    return lopSourceBadgeClasses(entry.source);
  }

  sourceLabel(entry: ILopEntry): string {
    return lopSourceLabel(entry.source);
  }

  canOverride(entry: ILopEntry): boolean {
    return canOverrideLop(entry);
  }

  // ─── Panel management ─────────────────────────────────────

  openPanel(panel: ActionPanel): void {
    if (panel === 'bulk' || panel === 'compulsory') {
      this.loadEmployees();
    }
    this.panel.set(panel);
  }

  openOverride(entry: ILopEntry): void {
    this.overrideTarget.set(entry);
    this.overrideForm.reset({ leaveTypeId: '', reason: '' });
    this.panel.set('override');
  }

  closePanel(): void {
    if (this.isSaving()) {
      return;
    }
    this.panel.set('none');
    this.overrideTarget.set(null);
  }

  // ─── Employee multi-select ────────────────────────────────

  onEmployeeSearch(event: Event): void {
    this.employeeSearch.set((event.target as HTMLInputElement).value);
  }

  toggleEmployee(id: string): void {
    const current = this.selectedEmployeeIds();
    this.selectedEmployeeIds.set(
      current.includes(id) ? current.filter((x) => x !== id) : [...current, id],
    );
  }

  isEmployeeSelected(id: string): boolean {
    return this.selectedEmployeeIds().includes(id);
  }

  employeeName(id: string): string {
    const e = this.employees().find((x) => x.employeeId === id);
    return e ? `${e.firstName} ${e.lastName}` : id;
  }

  // ─── Validation display ───────────────────────────────────

  showBulkError(control: string): boolean {
    const c = this.bulkForm.get(control);
    return !!c && c.invalid && (c.touched || this.bulkSubmitted());
  }

  showCompError(control: string): boolean {
    const c = this.compForm.get(control);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  showOverrideError(control: string): boolean {
    const c = this.overrideForm.get(control);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  // ─── Submit: bulk LOP (FR-3, AC-3) ────────────────────────

  submitBulk(): void {
    this.bulkSubmitted.set(true);
    if (this.bulkForm.invalid || this.selectedEmployeeIds().length === 0) {
      this.bulkForm.markAllAsTouched();
      this.toastr.warning('Please complete all fields and select at least one employee.');
      return;
    }
    const raw = this.bulkForm.getRawValue();
    const dates = expandDateRange(raw.from, raw.to);
    if (dates.length === 0) {
      this.toastr.error('Invalid date range.');
      return;
    }

    this.isSaving.set(true);
    const ids = this.selectedEmployeeIds();
    let remaining = ids.length;
    let failed = 0;
    // One call per employee — the assign-lop endpoint takes a single employeeId (FR-3).
    for (const employeeId of ids) {
      this.lopService
        .assignLop({ employeeId, dates, reason: raw.reason })
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: () => this.afterBulkOne(--remaining, failed, ids.length),
          error: () => {
            failed++;
            this.afterBulkOne(--remaining, failed, ids.length);
          },
        });
    }
  }

  private afterBulkOne(remaining: number, failed: number, total: number): void {
    if (remaining > 0) {
      return;
    }
    this.isSaving.set(false);
    if (failed === 0) {
      this.toastr.success(`LOP assigned to ${total} employee(s). They will be notified.`);
      this.resetBulk();
      this.closePanel();
      this.loadEntries();
    } else if (failed < total) {
      this.toastr.warning(`Assigned to ${total - failed} of ${total}; ${failed} failed.`);
      this.loadEntries();
    } else {
      this.toastr.error('Failed to assign LOP.');
    }
  }

  private resetBulk(): void {
    this.bulkForm.reset({ from: '', to: '', reason: '' });
    this.selectedEmployeeIds.set([]);
    this.employeeSearch.set('');
    this.bulkSubmitted.set(false);
  }

  // ─── Submit: compulsory leave (FR-6, BR-4) ────────────────

  submitCompulsory(): void {
    if (this.compForm.invalid) {
      this.compForm.markAllAsTouched();
      this.toastr.warning('Please complete all fields.');
      return;
    }
    const raw = this.compForm.getRawValue();
    const dates = expandDateRange(raw.from, raw.to);
    if (dates.length === 0) {
      this.toastr.error('Invalid date range.');
      return;
    }
    if (!raw.applyToAll && this.selectedEmployeeIds().length === 0) {
      // No explicit picker on this panel — applyToAll is the supported scope (§10
      // simplified bulk). Fall back to all when unchecked but no scope chosen.
      this.toastr.info('Applying to all employees.');
    }

    this.isSaving.set(true);
    this.lopService
      .assignCompulsoryLeave({
        dates,
        leaveTypeId: raw.leaveTypeId,
        reason: raw.reason,
        applyToAll: raw.applyToAll,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.isSaving.set(false);
          this.toastr.success(
            `Compulsory leave assigned: ${result.deducted} deducted, ${result.lop} as LOP.`,
          );
          this.compForm.reset({ from: '', to: '', leaveTypeId: '', applyToAll: true, reason: '' });
          this.closePanel();
          this.loadEntries();
        },
        error: (err: HttpErrorResponse) => {
          this.isSaving.set(false);
          this.toastr.error(LopService.parseErrorMessage(err));
        },
      });
  }

  // ─── Submit: override (BR-3) ──────────────────────────────

  submitOverride(): void {
    const target = this.overrideTarget();
    if (!target) {
      return;
    }
    if (this.overrideForm.invalid) {
      this.overrideForm.markAllAsTouched();
      this.toastr.warning('Please select a leave type and provide a reason.');
      return;
    }
    const raw = this.overrideForm.getRawValue();
    this.isSaving.set(true);
    this.lopService
      .overrideLop(target.leaveRequestId, { leaveTypeId: raw.leaveTypeId, reason: raw.reason })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.isSaving.set(false);
          this.toastr.success('LOP entry converted. The employee will be notified.');
          this.closePanel();
          this.loadEntries();
        },
        error: (err: HttpErrorResponse) => {
          this.isSaving.set(false);
          this.toastr.error(LopService.parseErrorMessage(err));
        },
      });
  }
}
