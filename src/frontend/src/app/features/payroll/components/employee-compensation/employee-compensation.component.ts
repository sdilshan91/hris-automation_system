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
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  FormArray,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AuthService } from '@core/auth/auth.service';
import { EmployeeSalaryService } from '../../services/employee-salary.service';
import { PayrollService } from '../../services/payroll.service';
import { ISalaryStructure } from '../../models/payroll.models';
import {
  ICtcBreakdown,
  IEmployeeCompensation,
  ISalaryRevision,
  ISalaryAssignmentRequest,
  IComponentOverride,
  ISalaryComponentLine,
} from '../../models/employee-salary.models';
import { TrappedDialogDirective } from '../../../../shared/directives';

/**
 * US-PAY-002: Employee "Compensation" tab (§8). Embedded by EmployeeProfile
 * component (same child pattern as employee-documents / employee-leave-overrides).
 *
 * Shows the employee's current salary structure, CTC, and component breakdown in
 * a Notion-style card layout. HR officers can open an assign drawer to assign /
 * revise the structure with a CTC breakdown PREVIEW before confirm (FR-3, AC-1,
 * AC-3), and view the salary revision history as an expandable timeline (FR-4).
 *
 * Read-only for non-HR viewers (assign/revise gated on AuthService roles).
 */
@Component({
  selector: 'app-employee-compensation',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TrappedDialogDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('expand', [
      transition(':enter', [
        style({ opacity: 0, height: '0', overflow: 'hidden' }),
        animate('200ms ease-out', style({ opacity: 1, height: '*' })),
      ]),
      transition(':leave', [
        animate('180ms ease-in', style({ opacity: 0, height: '0', overflow: 'hidden' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('260ms cubic-bezier(0.4,0,0.2,1)', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms cubic-bezier(0.4,0,0.2,1)', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
  ],
  template: `
    <div class="space-y-6">
      <!-- ─── Current compensation card (§8) ─────────────────── -->
      @if (isLoadingComp()) {
        <div class="space-y-3" aria-busy="true">
          <div class="skeleton-line w-40 h-5"></div>
          <div class="skeleton-line w-full h-24"></div>
        </div>
      } @else if (compError()) {
        <div class="rounded-lg bg-rose-50 border border-rose-100 p-4 text-sm text-rose-700">
          {{ compError() }}
          <button type="button" class="ml-2 underline" (click)="loadCompensation()">Retry</button>
        </div>
      } @else if (compensation()) {
        <div @fadeIn>
          <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 mb-4">
            <div>
              <h4 class="text-sm font-semibold text-neutral-900">Current Compensation</h4>
              @if (compensation()!.effectiveFrom) {
                <p class="text-xs text-neutral-400">
                  Effective {{ compensation()!.effectiveFrom | date: 'mediumDate' }}
                </p>
              }
            </div>
            @if (canAssign()) {
              <button
                type="button"
                class="btn-primary !py-2"
                (click)="openAssignDrawer()"
              >
                {{ compensation()!.salaryStructureId ? 'Revise Salary' : 'Assign Salary' }}
              </button>
            }
          </div>

          @if (!compensation()!.salaryStructureId) {
            <!-- BR-5: Payroll Incomplete -->
            <div
              class="rounded-lg bg-amber-50 border border-amber-100 p-4 flex items-start gap-3"
              role="status"
            >
              <span class="badge bg-amber-100 text-amber-800 ring-amber-600/20">Payroll Incomplete</span>
              <p class="text-sm text-amber-800">
                No salary structure has been assigned to this employee. They are excluded
                from payroll runs until a structure is assigned.
              </p>
            </div>
          } @else {
            <!-- Summary stat cards: structure / annual CTC / monthly CTC -->
            <div class="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-5">
              <div class="comp-stat">
                <p class="comp-stat-label">Salary Structure</p>
                <p class="comp-stat-value truncate">{{ compensation()!.salaryStructureName }}</p>
              </div>
              <div class="comp-stat">
                <p class="comp-stat-label">Annual CTC</p>
                <p class="comp-stat-value">{{ compensation()!.annualCtc | number: '1.2-2' }}</p>
              </div>
              <div class="comp-stat">
                <p class="comp-stat-label">Monthly CTC</p>
                <p class="comp-stat-value">{{ compensation()!.monthlyCtc | number: '1.2-2' }}</p>
              </div>
            </div>

            <!-- Component breakdown (Notion inline table; stacks on mobile) -->
            <div class="rounded-lg border border-neutral-100 overflow-hidden">
              <table class="w-full text-sm">
                <thead class="bg-neutral-50 text-neutral-500">
                  <tr>
                    <th class="text-left font-medium px-4 py-2.5">Component</th>
                    <th class="text-right font-medium px-4 py-2.5">Monthly</th>
                    <th class="text-right font-medium px-4 py-2.5 hidden sm:table-cell">Annual</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-neutral-100">
                  @for (line of compensation()!.lines; track line.salaryComponentId) {
                    <tr [class.bg-brand-50]="line.isOverride">
                      <td class="px-4 py-2.5 text-neutral-800">
                        {{ line.componentName }}
                        @if (line.isOverride) {
                          <span class="ml-1.5 badge bg-brand-100 text-brand-700 ring-brand-600/20">Override</span>
                        }
                      </td>
                      <td class="px-4 py-2.5 text-right tabular-nums text-neutral-900">
                        {{ line.monthlyAmount | number: '1.2-2' }}
                      </td>
                      <td class="px-4 py-2.5 text-right tabular-nums text-neutral-500 hidden sm:table-cell">
                        {{ line.annualAmount | number: '1.2-2' }}
                      </td>
                    </tr>
                  }
                </tbody>
                <tfoot class="bg-neutral-50 font-semibold">
                  <tr>
                    <td class="px-4 py-2.5 text-neutral-900">Total</td>
                    <td class="px-4 py-2.5 text-right tabular-nums text-neutral-900">
                      {{ compMonthlyTotal() | number: '1.2-2' }}
                    </td>
                    <td class="px-4 py-2.5 text-right tabular-nums text-neutral-900 hidden sm:table-cell">
                      {{ compAnnualTotal() | number: '1.2-2' }}
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>
          }
        </div>
      }

      <!-- ─── Salary revision history timeline (FR-4) ────────── -->
      <div @fadeIn>
        <h4 class="text-sm font-semibold text-neutral-900 mb-3">Salary Revision History</h4>
        @if (isLoadingHistory()) {
          <div class="skeleton-line w-full h-16"></div>
        } @else if (revisions().length === 0) {
          <p class="text-sm text-neutral-400 py-3">No salary revisions recorded yet.</p>
        } @else {
          <ol class="relative border-l border-neutral-200 ml-2 space-y-4">
            @for (rev of revisions(); track rev.revisionId) {
              <li class="ml-5">
                <span class="absolute -left-1.5 w-3 h-3 rounded-full bg-brand-500 border-2 border-white"></span>
                <button
                  type="button"
                  class="w-full text-left rounded-lg hover:bg-neutral-50 transition-colors duration-200 px-3 py-2 -ml-3"
                  [attr.aria-expanded]="expandedRevision() === rev.revisionId"
                  (click)="toggleRevision(rev.revisionId)"
                >
                  <div class="flex items-center justify-between gap-2">
                    <div>
                      <p class="text-sm font-medium text-neutral-900">
                        {{ rev.newStructureName }}
                        <span class="text-neutral-400 font-normal">
                          · CTC {{ rev.newAnnualCtc | number: '1.0-0' }}
                        </span>
                      </p>
                      <p class="text-xs text-neutral-400">
                        Effective {{ rev.effectiveFrom | date: 'mediumDate' }}
                        @if (rev.changedByName) { · by {{ rev.changedByName }} }
                      </p>
                    </div>
                    <svg
                      xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                      class="w-4 h-4 text-neutral-400 transition-transform duration-200 flex-shrink-0"
                      [class.rotate-180]="expandedRevision() === rev.revisionId"
                      aria-hidden="true"
                    >
                      <path fill-rule="evenodd" d="M5.23 7.21a.75.75 0 0 1 1.06.02L10 11.168l3.71-3.938a.75.75 0 1 1 1.08 1.04l-4.25 4.5a.75.75 0 0 1-1.08 0l-4.25-4.5a.75.75 0 0 1 .02-1.06Z" clip-rule="evenodd"/>
                    </svg>
                  </div>
                </button>
                @if (expandedRevision() === rev.revisionId) {
                  <div @expand class="mt-2 grid grid-cols-2 gap-3 px-3 pb-2 overflow-hidden">
                    <div class="rounded-lg bg-neutral-50 p-3">
                      <p class="text-xs font-medium text-neutral-400 uppercase tracking-wide mb-1">Before</p>
                      <p class="text-sm text-neutral-700">{{ rev.oldStructureName || 'None' }}</p>
                      <p class="text-xs text-neutral-500">
                        CTC {{ rev.oldAnnualCtc != null ? (rev.oldAnnualCtc | number: '1.0-0') : '—' }}
                      </p>
                    </div>
                    <div class="rounded-lg bg-brand-50 p-3">
                      <p class="text-xs font-medium text-brand-600 uppercase tracking-wide mb-1">After</p>
                      <p class="text-sm text-neutral-900">{{ rev.newStructureName }}</p>
                      <p class="text-xs text-neutral-600">CTC {{ rev.newAnnualCtc | number: '1.0-0' }}</p>
                    </div>
                    @if (rev.reason) {
                      <div class="col-span-2 text-xs text-neutral-500">
                        <span class="font-medium text-neutral-600">Reason:</span> {{ rev.reason }}
                      </div>
                    }
                  </div>
                }
              </li>
            }
          </ol>
        }
      </div>
    </div>

    <!-- ─── Assign / revise drawer (right slide-over) ───────── -->
    @if (showDrawer()) {
      <div class="fixed inset-0 z-50">
        <div @backdrop class="absolute inset-0 bg-neutral-900/30" (click)="closeDrawer()"></div>
        <div class="absolute inset-0 flex justify-end pointer-events-none">
          <div
            @drawer
            class="pointer-events-auto w-full max-w-lg h-full bg-white shadow-xl overflow-y-auto"
            role="dialog"
            aria-modal="true"
            aria-label="Assign salary structure"
            appTrappedDialog
            (dismiss)="closeDrawer()"
          >
            <form [formGroup]="assignForm" (ngSubmit)="confirmAssign()" class="flex flex-col min-h-full">
              <div class="flex items-center justify-between px-6 py-4 border-b border-neutral-100 sticky top-0 bg-white z-10">
                <h3 class="text-base font-semibold text-neutral-900">Assign Salary Structure</h3>
                <button type="button" class="text-neutral-400 hover:text-neutral-600" (click)="closeDrawer()" aria-label="Close">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5">
                    <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                  </svg>
                </button>
              </div>

              <div class="flex-1 px-6 py-5 space-y-4">
                <div>
                  <label class="label-notion" for="as-structure">Salary Structure</label>
                  <select id="as-structure" formControlName="salaryStructureId" class="input-notion">
                    <option value="">Select a structure…</option>
                    @for (s of activeStructures(); track s.id) {
                      <option [value]="s.id">{{ s.name }}</option>
                    }
                  </select>
                  @if (isInvalid('salaryStructureId')) {
                    <p class="field-error">A salary structure is required.</p>
                  }
                </div>

                <div class="grid grid-cols-2 gap-3">
                  <div>
                    <label class="label-notion" for="as-ctc">Annual CTC</label>
                    <input id="as-ctc" type="number" min="0" step="0.01" formControlName="annualCtc" class="input-notion" />
                    @if (isInvalid('annualCtc')) {
                      <p class="field-error">Enter a positive CTC.</p>
                    }
                  </div>
                  <div>
                    <label class="label-notion" for="as-eff">Effective From</label>
                    <input id="as-eff" type="date" formControlName="effectiveFrom" class="input-notion" />
                    @if (isInvalid('effectiveFrom')) {
                      <p class="field-error">An effective date is required.</p>
                    }
                  </div>
                </div>

                <div>
                  <label class="label-notion" for="as-reason">Reason (optional)</label>
                  <input id="as-reason" type="text" formControlName="reason" class="input-notion" maxlength="500" />
                </div>

                <!-- Per-component overrides (AC-3) — visually distinct -->
                <div class="rounded-lg border border-dashed border-brand-200 bg-brand-50/40 p-3">
                  <div class="flex items-center justify-between mb-2">
                    <p class="text-sm font-medium text-neutral-700">Component overrides</p>
                    <button type="button" class="text-xs font-medium text-brand-600 hover:text-brand-700" (click)="addOverride()">
                      + Add override
                    </button>
                  </div>
                  @if (overrides.length === 0) {
                    <p class="text-xs text-neutral-400">No overrides — all values are calculated from the structure.</p>
                  }
                  <div formArrayName="overrides" class="space-y-2">
                    @for (ov of overrides.controls; track $index; let i = $index) {
                      <div [formGroupName]="i" class="flex items-center gap-2">
                        <input
                          type="text"
                          formControlName="salaryComponentId"
                          class="input-notion flex-1 ring-1 ring-brand-200"
                          placeholder="Component id"
                          [attr.aria-label]="'Override component id ' + (i + 1)"
                        />
                        <input
                          type="number" min="0" step="0.01"
                          formControlName="annualAmount"
                          class="input-notion w-32 ring-1 ring-brand-200"
                          placeholder="Annual"
                          [attr.aria-label]="'Override annual amount ' + (i + 1)"
                        />
                        <button type="button" class="text-neutral-400 hover:text-rose-500" (click)="removeOverride(i)" [attr.aria-label]="'Remove override ' + (i + 1)">
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                            <path d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5Z"/>
                          </svg>
                        </button>
                      </div>
                    }
                  </div>
                </div>

                <!-- Preview button + breakdown (FR-3) -->
                <button
                  type="button"
                  class="btn-secondary w-full"
                  [disabled]="assignForm.invalid || isPreviewing()"
                  (click)="loadPreview()"
                >
                  @if (isPreviewing()) { <span class="btn-spinner"></span> Calculating… } @else { Preview CTC Breakdown }
                </button>

                @if (preview()) {
                  <div @expand class="rounded-lg border border-neutral-100 overflow-hidden">
                    @if (!preview()!.balanced) {
                      <div class="bg-amber-50 text-amber-800 text-xs px-4 py-2 border-b border-amber-100">
                        Component total does not reconcile to the declared CTC.
                      </div>
                    }
                    <table class="w-full text-sm">
                      <thead class="bg-neutral-50 text-neutral-500">
                        <tr>
                          <th class="text-left font-medium px-4 py-2">Component</th>
                          <th class="text-right font-medium px-4 py-2">Monthly Amount</th>
                        </tr>
                      </thead>
                      <tbody class="divide-y divide-neutral-100">
                        @for (line of preview()!.lines; track line.salaryComponentId) {
                          <tr [class.bg-brand-50]="line.isOverride">
                            <td class="px-4 py-2 text-neutral-800">
                              {{ line.componentName }}
                              @if (line.isOverride) {
                                <span class="ml-1.5 badge bg-brand-100 text-brand-700 ring-brand-600/20">Override</span>
                              }
                            </td>
                            <td class="px-4 py-2 text-right tabular-nums">{{ line.monthlyAmount | number: '1.2-2' }}</td>
                          </tr>
                        }
                      </tbody>
                      <tfoot class="bg-neutral-50 font-semibold">
                        <tr>
                          <td class="px-4 py-2 text-neutral-900">Total</td>
                          <td class="px-4 py-2 text-right tabular-nums text-neutral-900">
                            {{ preview()!.totalMonthly | number: '1.2-2' }}
                          </td>
                        </tr>
                      </tfoot>
                    </table>
                  </div>
                }
              </div>

              <div class="px-6 py-4 border-t border-neutral-100 flex justify-end gap-2 sticky bottom-0 bg-white">
                <button type="button" class="btn-secondary" (click)="closeDrawer()">Cancel</button>
                <button type="submit" class="btn-primary" [disabled]="assignForm.invalid || !preview() || isSaving()">
                  @if (isSaving()) { <span class="btn-spinner"></span> Saving… } @else { Confirm Assignment }
                </button>
              </div>
            </form>
          </div>
        </div>
      </div>
    }
  `,
  styles: [
    `
      .comp-stat { @apply rounded-lg bg-neutral-50 px-4 py-3; }
      .comp-stat-label { @apply text-xs text-neutral-400; }
      .comp-stat-value { @apply text-base font-semibold text-neutral-900 mt-0.5; }
      .field-error { @apply text-xs text-rose-600 mt-1; }
      .badge { @apply inline-flex items-center text-xs font-medium px-2 py-0.5 rounded-full ring-1 ring-inset; }
      .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
      @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
      .btn-spinner {
        @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
        animation: spin 0.6s linear infinite;
      }
      @keyframes spin { to { transform: rotate(360deg); } }
    `,
  ],
})
export class EmployeeCompensationComponent implements OnInit, OnDestroy {
  /** Employee whose compensation is shown (passed by EmployeeProfileComponent). */
  readonly employeeId = input.required<string>();

  private readonly salaryService = inject(EmployeeSalaryService);
  private readonly payrollService = inject(PayrollService);
  private readonly authService = inject(AuthService);
  private readonly toastr = inject(ToastrService);
  private readonly fb = inject(FormBuilder);
  private readonly destroy$ = new Subject<void>();

  // ─── State ───────────────────────────────────────────────
  readonly compensation = signal<IEmployeeCompensation | null>(null);
  readonly isLoadingComp = signal(true);
  readonly compError = signal<string | null>(null);

  readonly revisions = signal<ISalaryRevision[]>([]);
  readonly isLoadingHistory = signal(true);
  readonly expandedRevision = signal<string | null>(null);

  readonly activeStructures = signal<ISalaryStructure[]>([]);
  readonly showDrawer = signal(false);
  readonly preview = signal<ICtcBreakdown | null>(null);
  readonly isPreviewing = signal(false);
  readonly isSaving = signal(false);

  readonly assignForm: FormGroup = this.fb.group({
    salaryStructureId: ['', Validators.required],
    annualCtc: [null, [Validators.required, Validators.min(0.01)]],
    effectiveFrom: ['', Validators.required],
    reason: [''],
    overrides: this.fb.array([]),
  });

  // ─── Derived ─────────────────────────────────────────────
  readonly compMonthlyTotal = computed(() =>
    this.sum(this.compensation()?.lines, (l) => l.monthlyAmount),
  );
  readonly compAnnualTotal = computed(() =>
    this.sum(this.compensation()?.lines, (l) => l.annualAmount),
  );

  get overrides(): FormArray {
    return this.assignForm.get('overrides') as FormArray;
  }

  /** HR Officer / Tenant Admin may assign or revise (read-only otherwise). */
  canAssign(): boolean {
    return (
      this.authService.hasRole('HR Officer') ||
      this.authService.hasRole('Tenant Admin')
    );
  }

  ngOnInit(): void {
    this.loadCompensation();
    this.loadHistory();
    // Reset the preview whenever the inputs change — a stale preview must never
    // be the one that gets confirmed (FR-3: preview == what is saved).
    this.assignForm.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.preview.set(null));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadCompensation(): void {
    this.isLoadingComp.set(true);
    this.compError.set(null);
    this.salaryService
      .getCurrentCompensation(this.employeeId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (comp) => {
          this.compensation.set(comp);
          this.isLoadingComp.set(false);
        },
        error: () => {
          this.compError.set('Failed to load compensation.');
          this.isLoadingComp.set(false);
        },
      });
  }

  loadHistory(): void {
    this.isLoadingHistory.set(true);
    this.salaryService
      .getRevisionHistory(this.employeeId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (revs) => {
          this.revisions.set(revs);
          this.isLoadingHistory.set(false);
        },
        error: () => this.isLoadingHistory.set(false),
      });
  }

  toggleRevision(id: string): void {
    this.expandedRevision.set(this.expandedRevision() === id ? null : id);
  }

  // ─── Assign drawer ───────────────────────────────────────
  openAssignDrawer(): void {
    this.preview.set(null);
    this.overrides.clear();
    this.assignForm.reset({
      salaryStructureId: '',
      annualCtc: null,
      effectiveFrom: '',
      reason: '',
    });
    this.showDrawer.set(true);
    // Lazy-load active structures the first time the drawer opens (FR-7: only
    // active structures are assignable).
    if (this.activeStructures().length === 0) {
      this.payrollService
        .listStructures()
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (list) =>
            this.activeStructures.set(list.filter((s) => s.isActive)),
          error: () => this.toastr.error('Failed to load salary structures.'),
        });
    }
  }

  closeDrawer(): void {
    this.showDrawer.set(false);
  }

  addOverride(): void {
    this.overrides.push(
      this.fb.group({
        salaryComponentId: ['', Validators.required],
        annualAmount: [null, [Validators.required, Validators.min(0)]],
      }),
    );
  }

  removeOverride(index: number): void {
    this.overrides.removeAt(index);
  }

  isInvalid(control: string): boolean {
    const c = this.assignForm.get(control);
    return !!c && c.invalid && (c.dirty || c.touched);
  }

  loadPreview(): void {
    if (this.assignForm.invalid) {
      this.assignForm.markAllAsTouched();
      return;
    }
    this.isPreviewing.set(true);
    this.salaryService
      .previewBreakdown(this.buildRequest())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (bd) => {
          this.preview.set(bd);
          this.isPreviewing.set(false);
        },
        error: () => {
          this.isPreviewing.set(false);
          this.toastr.error('Failed to calculate the CTC breakdown.');
        },
      });
  }

  confirmAssign(): void {
    if (this.assignForm.invalid || !this.preview()) {
      this.assignForm.markAllAsTouched();
      return;
    }
    this.isSaving.set(true);
    this.salaryService
      .assign(this.buildRequest())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.isSaving.set(false);
          this.toastr.success('Salary structure assigned.');
          this.showDrawer.set(false);
          this.loadCompensation();
          this.loadHistory();
        },
        error: () => {
          this.isSaving.set(false);
          this.toastr.error('Failed to assign the salary structure.');
        },
      });
  }

  private buildRequest(): ISalaryAssignmentRequest {
    const v = this.assignForm.getRawValue();
    const overrides: IComponentOverride[] = (v.overrides ?? []).map(
      (o: { salaryComponentId: string; annualAmount: number }) => ({
        salaryComponentId: o.salaryComponentId,
        annualAmount: o.annualAmount,
      }),
    );
    return {
      employeeId: this.employeeId(),
      salaryStructureId: v.salaryStructureId,
      annualCtc: v.annualCtc,
      effectiveFrom: v.effectiveFrom,
      overrides,
      reason: v.reason || null,
    };
  }

  private sum(
    lines: ISalaryComponentLine[] | undefined | null,
    pick: (l: ISalaryComponentLine) => number,
  ): number {
    return (lines ?? []).reduce((acc, l) => acc + pick(l), 0);
  }
}
