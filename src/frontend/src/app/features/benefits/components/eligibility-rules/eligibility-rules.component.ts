import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  input,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import { BenefitService } from '../../services/benefit.service';
import {
  IEligibilityRule,
  ICreateEligibilityRule,
  IBenefitErrorResponse,
  EligibilityAttribute,
  ELIGIBILITY_ATTRIBUTES,
  ELIGIBILITY_OPERATORS,
} from '../../models/benefit.models';

/**
 * US-TRN-003 AC: Eligibility-rule editor embedded in the plan-form (edit mode).
 *
 * Rules are ANDed server-side: an employee is eligible only if ALL rules pass.
 * No rules → the plan is open to all active employees. Add/delete require the
 * Benefits.Manage permission; the list is read-only for View.All users. Only
 * rendered once a plan exists (rules attach to a persisted plan id).
 */
@Component({
  selector: 'app-eligibility-rules',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="rules-section">
      <div class="flex items-center justify-between mb-2">
        <h3 class="text-sm font-semibold text-neutral-800">Eligibility rules</h3>
        <span class="text-xs text-neutral-400">All rules must pass (AND)</span>
      </div>

      @if (isLoading()) {
        <div class="animate-pulse space-y-2">
          <div class="h-8 bg-neutral-50 rounded"></div>
          <div class="h-8 bg-neutral-50 rounded"></div>
        </div>
      } @else {
        @if (rules().length === 0) {
          <p class="text-xs text-neutral-400 mb-3">
            No rules — this plan is open to all active employees.
          </p>
        } @else {
          <ul class="space-y-1.5 mb-3">
            @for (r of rules(); track r.id) {
              <li class="flex items-center justify-between gap-2 rounded-md bg-neutral-50 px-3 py-2">
                <span class="text-sm text-neutral-700 font-mono">
                  {{ r.attribute }} {{ r.operator }} {{ r.value }}
                </span>
                @if (canManage()) {
                  <button
                    type="button"
                    class="text-xs text-red-500 hover:text-red-700 whitespace-nowrap"
                    (click)="remove(r)"
                    [disabled]="deletingId() === r.id"
                    [attr.aria-label]="'Remove rule: ' + r.attribute + ' ' + r.operator + ' ' + r.value"
                  >
                    Remove
                  </button>
                }
              </li>
            }
          </ul>
        }

        @if (canManage()) {
          <div class="grid grid-cols-3 gap-2">
            <div>
              <label class="label-notion text-xs" for="rule-attribute">Attribute</label>
              <select
                id="rule-attribute"
                class="input-notion"
                [ngModel]="newAttribute()"
                (ngModelChange)="newAttribute.set($event)"
              >
                @for (a of attributes; track a) {
                  <option [value]="a">{{ a }}</option>
                }
              </select>
            </div>
            <div>
              <label class="label-notion text-xs" for="rule-operator">Operator</label>
              <select
                id="rule-operator"
                class="input-notion"
                [ngModel]="newOperator()"
                (ngModelChange)="newOperator.set($event)"
              >
                @for (o of operators; track o) {
                  <option [value]="o">{{ o }}</option>
                }
              </select>
            </div>
            <div>
              <label class="label-notion text-xs" for="rule-value">Value</label>
              <input
                id="rule-value"
                type="text"
                class="input-notion"
                [ngModel]="newValue()"
                (ngModelChange)="newValue.set($event)"
                [placeholder]="valuePlaceholder()"
                autocomplete="off"
              />
            </div>
          </div>
          <p class="field-hint mt-1">{{ valueHint() }}</p>
          <div class="flex justify-end mt-2">
            <button
              type="button"
              class="btn-add"
              (click)="add()"
              [disabled]="isAdding() || !newValue().trim()"
            >
              @if (isAdding()) {
                <span class="btn-spinner"></span> Adding...
              } @else {
                Add rule
              }
            </button>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .rules-section {
      @apply rounded-lg border border-neutral-100 p-4;
    }

    .field-hint {
      @apply text-xs text-neutral-400;
    }

    .btn-add {
      @apply inline-flex items-center justify-center rounded-lg bg-neutral-800 px-4 py-2
        text-xs font-medium text-white shadow-sm transition-all duration-200
        hover:bg-neutral-900 disabled:opacity-50 disabled:cursor-not-allowed;
    }

    .btn-spinner {
      @apply inline-block w-3.5 h-3.5 mr-1.5 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class EligibilityRulesComponent implements OnInit {
  private readonly benefitService = inject(BenefitService);
  private readonly authService = inject(AuthService);
  private readonly toastr = inject(ToastrService);

  /** The persisted plan whose rules are edited. */
  readonly planId = input.required<string>();

  readonly rules = signal<IEligibilityRule[]>([]);
  readonly isLoading = signal(true);
  readonly isAdding = signal(false);
  readonly deletingId = signal<string | null>(null);

  // New-rule form state
  readonly newAttribute = signal<EligibilityAttribute>('EmploymentType');
  readonly newOperator = signal<string>('==');
  readonly newValue = signal<string>('');

  readonly attributes = ELIGIBILITY_ATTRIBUTES;
  readonly operators = ELIGIBILITY_OPERATORS;

  readonly canManage = signal(false);

  ngOnInit(): void {
    this.canManage.set(this.authService.hasPermission('Benefits.Manage'));
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.benefitService.getEligibilityRules(this.planId()).subscribe({
      next: (rules) => {
        this.rules.set(rules);
        this.isLoading.set(false);
      },
      error: () => {
        // View.Own users lack rule visibility — fail soft to an empty list.
        this.rules.set([]);
        this.isLoading.set(false);
      },
    });
  }

  /** Contextual placeholder for the value field per selected attribute. */
  valuePlaceholder(): string {
    switch (this.newAttribute()) {
      case 'EmploymentType':
        return 'e.g. FullTime';
      case 'TenureDays':
        return 'e.g. 90';
      case 'Department':
      case 'JobGrade':
        return 'GUID (or comma-separated for In)';
    }
  }

  valueHint(): string {
    switch (this.newAttribute()) {
      case 'EmploymentType':
        return 'Enum name (FullTime, PartTime, Contract, Intern); operator ==/!=.';
      case 'TenureDays':
        return 'Whole number of days since joining; numeric operators.';
      case 'Department':
      case 'JobGrade':
        return 'A single GUID (==/!=) or a comma-separated GUID list (In).';
    }
  }

  add(): void {
    if (this.isAdding() || !this.newValue().trim()) return;
    this.isAdding.set(true);

    const request: ICreateEligibilityRule = {
      attribute: this.newAttribute(),
      operator: this.newOperator(),
      value: this.newValue().trim(),
    };

    this.benefitService.createEligibilityRule(this.planId(), request).subscribe({
      next: (rule) => {
        this.isAdding.set(false);
        this.rules.update((list) => [...list, rule]);
        this.newValue.set('');
        this.toastr.success('Eligibility rule added.');
      },
      error: (err: HttpErrorResponse) => {
        this.isAdding.set(false);
        const body = err.error as IBenefitErrorResponse | undefined;
        this.toastr.error(body?.message || 'Failed to add eligibility rule.');
      },
    });
  }

  remove(rule: IEligibilityRule): void {
    if (this.deletingId()) return;
    this.deletingId.set(rule.id);

    this.benefitService.deleteEligibilityRule(rule.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.rules.update((list) => list.filter((r) => r.id !== rule.id));
        this.toastr.success('Eligibility rule removed.');
      },
      error: (err: HttpErrorResponse) => {
        this.deletingId.set(null);
        const body = err.error as IBenefitErrorResponse | undefined;
        this.toastr.error(body?.message || 'Failed to remove eligibility rule.');
      },
    });
  }
}
