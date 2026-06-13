import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  OnInit,
  OnChanges,
  SimpleChanges,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject, forkJoin } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { LeaveEntitlementService } from '../../services/leave-entitlement.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import {
  IEntitlementOverride,
  IEffectiveEntitlement,
  ILookupItem,
} from '../../models/leave-entitlement.models';

/**
 * US-LV-002 AC-3: Per-employee leave entitlement overrides.
 *
 * Displayed on the employee profile page under a "Leave" tab.
 * Shows:
 *   - Current effective entitlements per leave type (computed by backend)
 *   - Existing overrides with edit/delete
 *   - Form to create a new override
 */
@Component({
  selector: 'app-employee-leave-overrides',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
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
    <div @fadeIn>
      <!-- Current effective entitlements -->
      <div class="mb-5">
        <h4 class="text-sm font-semibold text-neutral-900 mb-3">Effective Entitlements</h4>
        @if (isLoadingEffective()) {
          <div class="space-y-2">
            @for (_ of [1,2,3]; track $index) {
              <div class="skeleton-line w-full h-8"></div>
            }
          </div>
        } @else if (effectiveEntitlements().length === 0) {
          <p class="text-sm text-neutral-400">No entitlements computed for this employee.</p>
        } @else {
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-2">
            @for (ent of effectiveEntitlements(); track ent.leaveTypeId) {
              <div class="flex items-center justify-between p-3 rounded-lg bg-neutral-50 border border-neutral-100">
                <div>
                  <p class="text-sm font-medium text-neutral-900">{{ ent.leaveTypeName }}</p>
                  <p class="text-xs text-neutral-400">
                    Source: {{ ent.source === 'override' ? 'Override' : ent.source === 'rule' ? 'Rule' : 'Default' }}
                  </p>
                </div>
                <span class="text-lg font-semibold text-brand-700">{{ ent.entitlementDays }}</span>
              </div>
            }
          </div>
        }
      </div>

      <!-- Existing overrides -->
      <div class="mb-5">
        <h4 class="text-sm font-semibold text-neutral-900 mb-3">Overrides</h4>
        @if (isLoadingOverrides()) {
          <div class="space-y-2">
            @for (_ of [1,2]; track $index) {
              <div class="skeleton-line w-full h-10"></div>
            }
          </div>
        } @else if (overrides().length === 0) {
          <p class="text-sm text-neutral-400">No per-employee overrides set.</p>
        } @else {
          <div class="space-y-2">
            @for (ov of overrides(); track ov.overrideId) {
              <div class="flex items-center justify-between p-3 rounded-lg border border-neutral-100 bg-white hover:bg-neutral-50/50 transition-colors">
                <div>
                  <p class="text-sm font-medium text-neutral-900">
                    {{ ov.leaveTypeName }} ({{ ov.leaveYear }})
                  </p>
                  <p class="text-xs text-neutral-500">
                    {{ ov.entitlementDays }} days
                    @if (ov.reason) { &mdash; {{ ov.reason }} }
                  </p>
                </div>
                <button type="button"
                  class="w-7 h-7 rounded-md flex items-center justify-center text-neutral-400 hover:text-red-600 hover:bg-red-50 transition-colors"
                  (click)="deleteOverride(ov)"
                  [attr.aria-label]="'Delete override for ' + ov.leaveTypeName">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                    <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                  </svg>
                </button>
              </div>
            }
          </div>
        }
      </div>

      <!-- Add/edit override form -->
      <div class="border-t border-neutral-100 pt-4">
        <h4 class="text-sm font-semibold text-neutral-900 mb-3">Set Override</h4>
        <form [formGroup]="overrideForm" (ngSubmit)="submitOverride()">
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label class="label-sm" for="ov-lt">Leave Type</label>
              <select id="ov-lt" formControlName="leaveTypeId" class="input-sm select-input" aria-required="true">
                <option value="">Select...</option>
                @for (lt of leaveTypeLookups(); track lt.id) {
                  <option [value]="lt.id">{{ lt.name }}</option>
                }
              </select>
            </div>
            <div>
              <label class="label-sm" for="ov-year">Leave Year</label>
              <input id="ov-year" type="number" formControlName="leaveYear" class="input-sm" [min]="2020" aria-required="true" />
            </div>
            <div>
              <label class="label-sm" for="ov-days">Entitlement Days</label>
              <input id="ov-days" type="number" formControlName="entitlementDays" class="input-sm"
                min="0" step="0.5" aria-required="true" />
            </div>
            <div>
              <label class="label-sm" for="ov-reason">Reason</label>
              <input id="ov-reason" type="text" formControlName="reason" class="input-sm"
                placeholder="Optional reason" />
            </div>
          </div>
          <div class="flex items-center justify-end gap-3 mt-3">
            <button type="submit" class="btn-primary-sm" [disabled]="isSaving() || overrideForm.invalid">
              @if (isSaving()) {
                <span class="btn-spinner-sm"></span> Saving...
              } @else {
                Save Override
              }
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }

    .label-sm { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .input-sm {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2
        text-sm text-neutral-900 placeholder-neutral-400
        transition-all duration-150
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
    .btn-primary-sm {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-spinner-sm {
      @apply inline-block w-3.5 h-3.5 mr-1.5 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    .skeleton-line {
      @apply rounded bg-neutral-200;
      animation: shimmer 1.5s ease-in-out infinite;
    }
    @keyframes shimmer {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.4; }
    }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class EmployeeLeaveOverridesComponent implements OnInit, OnChanges, OnDestroy {
  private readonly entitlementService = inject(LeaveEntitlementService);
  private readonly leaveTypeService = inject(LeaveTypeService);
  private readonly toastr = inject(ToastrService);
  private readonly fb = inject(FormBuilder);
  private readonly destroy$ = new Subject<void>();

  /** Employee ID to show overrides for */
  readonly employeeId = input.required<string>();

  readonly overrides = signal<IEntitlementOverride[]>([]);
  readonly effectiveEntitlements = signal<IEffectiveEntitlement[]>([]);
  readonly leaveTypeLookups = signal<ILookupItem[]>([]);
  readonly isLoadingOverrides = signal(true);
  readonly isLoadingEffective = signal(true);
  readonly isSaving = signal(false);

  overrideForm!: FormGroup;

  ngOnInit(): void {
    this.overrideForm = this.fb.group({
      leaveTypeId: ['', [Validators.required]],
      leaveYear: [new Date().getFullYear(), [Validators.required, Validators.min(2020)]],
      entitlementDays: [null as number | null, [Validators.required, Validators.min(0)]],
      reason: [''],
    });

    this.loadData();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['employeeId'] && !changes['employeeId'].firstChange) {
      this.loadData();
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadData(): void {
    const empId = this.employeeId();
    if (!empId) return;

    this.isLoadingOverrides.set(true);
    this.isLoadingEffective.set(true);

    // Load overrides + effective entitlements + leave types in parallel
    forkJoin({
      overrides: this.entitlementService.getOverrides(empId),
      effective: this.entitlementService.getEffectiveEntitlements(empId),
      leaveTypes: this.leaveTypeService.getLeaveTypes(),
    })
    .pipe(takeUntil(this.destroy$))
    .subscribe({
      next: ({ overrides, effective, leaveTypes }) => {
        this.overrides.set(overrides);
        this.effectiveEntitlements.set(effective);
        this.leaveTypeLookups.set(
          leaveTypes.map(lt => ({ id: lt.leaveTypeId, name: lt.name }))
        );
        this.isLoadingOverrides.set(false);
        this.isLoadingEffective.set(false);
      },
      error: () => {
        this.isLoadingOverrides.set(false);
        this.isLoadingEffective.set(false);
        this.toastr.error('Failed to load leave entitlement data.');
      },
    });
  }

  submitOverride(): void {
    if (this.overrideForm.invalid) {
      this.overrideForm.markAllAsTouched();
      return;
    }

    const v = this.overrideForm.value;
    this.isSaving.set(true);

    this.entitlementService
      .upsertOverride(this.employeeId(), {
        leaveTypeId: v.leaveTypeId,
        leaveYear: v.leaveYear,
        entitlementDays: v.entitlementDays,
        reason: v.reason || null,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.isSaving.set(false);
          this.toastr.success('Override saved successfully.');
          this.overrideForm.reset({
            leaveTypeId: '',
            leaveYear: new Date().getFullYear(),
            entitlementDays: null,
            reason: '',
          });
          this.loadData();
        },
        error: (err: HttpErrorResponse) => {
          this.isSaving.set(false);
          this.toastr.error(LeaveEntitlementService.parseError(err));
        },
      });
  }

  deleteOverride(ov: IEntitlementOverride): void {
    if (!confirm(`Delete override for ${ov.leaveTypeName} (${ov.leaveYear})?`)) {
      return;
    }
    this.entitlementService
      .deleteOverride(ov.overrideId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.overrides.set(this.overrides().filter(o => o.overrideId !== ov.overrideId));
          this.toastr.success('Override deleted.');
          // Refresh effective entitlements
          this.entitlementService
            .getEffectiveEntitlements(this.employeeId())
            .pipe(takeUntil(this.destroy$))
            .subscribe(eff => this.effectiveEntitlements.set(eff));
        },
        error: (err: HttpErrorResponse) => {
          this.toastr.error(LeaveEntitlementService.parseError(err));
        },
      });
  }
}
