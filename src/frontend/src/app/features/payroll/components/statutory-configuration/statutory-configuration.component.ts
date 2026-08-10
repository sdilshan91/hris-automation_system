import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { StatutoryService } from '../../services/statutory.service';
import { validateSlabs } from '../../services/slab-validation';
import {
  ApplicableOn,
  APPLICABLE_ON_LABELS,
  APPLICABLE_ON_OPTIONS,
  IStatutoryRule,
  IStatutoryRuleRequest,
  ISocialSecurityRule,
  ITaxSlab,
  ITestCalculationResult,
  StatutoryRuleType,
  STATUTORY_RULE_TYPE_LABELS,
} from '../../models/statutory.models';

type Tab = 'IncomeTax' | 'ProvidentFund' | 'OtherDeductions';

/** Editable EPF/ETF form-model (a render-friendly copy of ISocialSecurityRule). */
interface ISocialSecurityForm {
  employeeRate: number | null;
  employerRate: number | null;
  wageCeilingAnnual: number | null;
  applicableOn: ApplicableOn;
}

/**
 * US-PAY-006: Statutory Deductions Configuration (§8). A desktop-focused
 * tenant-admin / HR config page with three tabs — Income Tax, Provident Fund,
 * Other Deductions — each in a Notion-like card layout, plus a fiscal-year
 * selector (AC-3 versioning), a real-time Test Calculation panel (FR-5), and a
 * collapsible version-history timeline (FR-4).
 *
 * The income-tax editor is an inline add/remove-row table with REAL-TIME
 * contiguity validation (FR-6) — overlapping/gap rows highlight red and Save is
 * gated until the slab set is valid (validation logic lives in the pure
 * `validateSlabs` helper). Mobile (§8) shows a read-only summary with a clear
 * "configure on desktop" message.
 */
@Component({
  selector: 'app-statutory-configuration',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, DecimalPipe],
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
    trigger('collapse', [
      transition(':enter', [
        style({ opacity: 0, height: 0 }),
        animate('220ms ease-out', style({ opacity: 1, height: '*' })),
      ]),
      transition(':leave', [
        animate('180ms ease-in', style({ opacity: 0, height: 0 })),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <nav class="mb-1 text-xs text-neutral-500" aria-label="Breadcrumb">
          <a routerLink="/payroll" class="hover:text-neutral-700">Payroll</a>
          <span class="px-1">/</span>
          <span class="text-neutral-700">Statutory configuration</span>
        </nav>
        <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
          Statutory configuration
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          Configure income tax slabs, provident fund and other statutory
          deductions per fiscal year. Payroll applies the rules in effect for the
          run period.
        </p>
      </div>

      <!-- Fiscal-year selector (AC-3) -->
      <div class="mb-6 flex flex-wrap items-center gap-2">
        <span class="text-xs font-medium uppercase tracking-wide text-neutral-500"
          >Fiscal year</span
        >
        <!-- Wrap the tablist and the Add-year action together so the tablist
             holds ONLY role="tab" children (aria-required-children). -->
        <div class="flex flex-wrap items-center gap-1.5">
          <div class="flex flex-wrap gap-1.5" role="tablist" aria-label="Fiscal year">
            @for (fy of fiscalYears(); track fy) {
              <button
                type="button"
                role="tab"
                [attr.aria-selected]="fy === fiscalYear()"
                class="rounded-lg px-3 py-1.5 text-sm font-medium transition"
                [class]="
                  fy === fiscalYear()
                    ? 'bg-neutral-900 text-white'
                    : 'bg-white text-neutral-600 ring-1 ring-neutral-200 hover:bg-neutral-50'
                "
                (click)="selectFiscalYear(fy)"
              >
                {{ fy }}
              </button>
            }
          </div>
          <button
            type="button"
            class="rounded-lg px-3 py-1.5 text-sm font-medium text-neutral-500 ring-1 ring-dashed ring-neutral-300 hover:bg-neutral-50"
            (click)="addFiscalYear()"
          >
            + Add year
          </button>
        </div>
      </div>

      <!-- Mobile read-only notice (§8) -->
      <div
        class="mb-6 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800 lg:hidden"
        role="note"
      >
        Statutory configuration is a desktop activity. Below is a read-only
        summary of the current rules for {{ fiscalYear() }} — open this page on a
        larger screen to make changes.
      </div>

      @if (loading()) {
        <div class="space-y-3">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-24 animate-pulse rounded-xl bg-neutral-100"></div>
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
      } @else {
        <!-- Mobile read-only view -->
        <div class="space-y-4 lg:hidden" data-testid="mobile-readonly">
          <div class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
            <h3 class="text-sm font-semibold text-neutral-800">Income tax slabs</h3>
            @if (taxSlabs().length === 0) {
              <p class="mt-2 text-sm text-neutral-400">No slabs configured.</p>
            } @else {
              <ul class="mt-2 space-y-1 text-sm text-neutral-600">
                @for (s of taxSlabs(); track $index) {
                  <li>
                    {{ s.slabFrom | number }} –
                    {{ s.slabTo === null ? '∞' : (s.slabTo | number) }} @
                    {{ s.ratePercentage }}%
                  </li>
                }
              </ul>
            }
          </div>
          <div class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
            <h3 class="text-sm font-semibold text-neutral-800">
              Provident fund
            </h3>
            <p class="mt-2 text-sm text-neutral-600">
              Employee {{ epf().employeeRate ?? 0 }}% · Employer
              {{ epf().employerRate ?? 0 }}% · Ceiling
              {{
                epf().wageCeilingAnnual === null
                  ? 'none'
                  : (epf().wageCeilingAnnual | number)
              }}
            </p>
          </div>
        </div>

        <!-- Desktop editor -->
        <div class="hidden gap-6 lg:grid lg:grid-cols-3">
          <!-- Config column (tabs + cards) -->
          <div class="lg:col-span-2">
            <!-- Tabs -->
            <div
              class="mb-4 flex gap-1 border-b border-neutral-200"
              role="tablist"
              aria-label="Statutory sections"
            >
              @for (t of tabs; track t.key) {
                <button
                  type="button"
                  role="tab"
                  [attr.aria-selected]="t.key === activeTab()"
                  class="-mb-px border-b-2 px-4 py-2 text-sm font-medium transition"
                  [class]="
                    t.key === activeTab()
                      ? 'border-neutral-900 text-neutral-900'
                      : 'border-transparent text-neutral-500 hover:text-neutral-700'
                  "
                  (click)="activeTab.set(t.key)"
                >
                  {{ t.label }}
                </button>
              }
            </div>

            <!-- Income Tax tab -->
            @if (activeTab() === 'IncomeTax') {
              <div
                @fadeIn
                class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
              >
                <div class="mb-3 flex items-center justify-between">
                  <h2 class="text-base font-semibold text-neutral-900">
                    Income tax slabs
                  </h2>
                  <button
                    type="button"
                    class="rounded-lg bg-neutral-100 px-3 py-1.5 text-sm font-medium text-neutral-700 hover:bg-neutral-200"
                    (click)="addSlab()"
                  >
                    + Add slab
                  </button>
                </div>

                <!-- header -->
                <div
                  class="grid grid-cols-12 gap-2 px-1 pb-2 text-xs font-medium uppercase tracking-wide text-neutral-400"
                >
                  <span class="col-span-4">From</span>
                  <span class="col-span-4">To (blank = unlimited)</span>
                  <span class="col-span-3">Rate %</span>
                  <span class="col-span-1"></span>
                </div>

                @for (s of taxSlabs(); track $index; let i = $index) {
                  <div
                    class="grid grid-cols-12 items-center gap-2 rounded-lg px-1 py-1.5"
                    [class.bg-rose-50]="slabIssue(i) !== null"
                    [class.ring-1]="slabIssue(i) !== null"
                    [class.ring-rose-300]="slabIssue(i) !== null"
                  >
                    <input
                      type="number"
                      class="col-span-4 rounded-md border border-neutral-200 px-2 py-1.5 text-sm focus:border-neutral-400 focus:outline-none"
                      [class.border-rose-300]="slabIssue(i) !== null"
                      [attr.aria-label]="'Slab ' + (i + 1) + ' from'"
                      [ngModel]="s.slabFrom"
                      (ngModelChange)="updateSlab(i, 'slabFrom', $event)"
                    />
                    <input
                      type="number"
                      placeholder="∞"
                      class="col-span-4 rounded-md border border-neutral-200 px-2 py-1.5 text-sm focus:border-neutral-400 focus:outline-none"
                      [class.border-rose-300]="slabIssue(i) !== null"
                      [attr.aria-label]="'Slab ' + (i + 1) + ' to'"
                      [ngModel]="s.slabTo"
                      (ngModelChange)="updateSlab(i, 'slabTo', $event)"
                    />
                    <input
                      type="number"
                      class="col-span-3 rounded-md border border-neutral-200 px-2 py-1.5 text-sm focus:border-neutral-400 focus:outline-none"
                      [class.border-rose-300]="slabIssue(i) !== null"
                      [attr.aria-label]="'Slab ' + (i + 1) + ' rate'"
                      [ngModel]="s.ratePercentage"
                      (ngModelChange)="updateSlab(i, 'ratePercentage', $event)"
                    />
                    <button
                      type="button"
                      class="col-span-1 rounded p-1 text-neutral-400 hover:bg-rose-50 hover:text-rose-600"
                      [attr.aria-label]="'Remove slab ' + (i + 1)"
                      (click)="removeSlab(i)"
                    >
                      ✕
                    </button>
                  </div>
                }

                @if (taxSlabs().length === 0) {
                  <p class="px-1 py-4 text-sm text-neutral-400">
                    No slabs yet — add the first slab (e.g. 0 – 250000 @ 0%).
                  </p>
                }

                <!-- validation messages (FR-6) -->
                @if (taxSlabs().length > 0 && !slabsValid()) {
                  <p
                    class="mt-3 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700"
                    role="alert"
                  >
                    Tax slabs must be contiguous — fix the highlighted rows
                    (overlapping or gapped income ranges) before saving.
                  </p>
                }

                <div class="mt-4 flex justify-end">
                  <button
                    type="button"
                    class="rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-40"
                    [style.background-color]="'var(--brand-primary)'"
                    [disabled]="!slabsValid() || saving()"
                    (click)="saveIncomeTax()"
                  >
                    {{ saving() ? 'Saving…' : 'Save tax slabs' }}
                  </button>
                </div>
              </div>
            }

            <!-- Provident Fund tab -->
            @if (activeTab() === 'ProvidentFund') {
              <div
                @fadeIn
                class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
              >
                <h2 class="mb-4 text-base font-semibold text-neutral-900">
                  Provident fund / social security
                </h2>
                <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <label class="block">
                    <span class="text-sm font-medium text-neutral-700"
                      >Employee rate (%)</span
                    >
                    <input
                      type="number"
                      class="mt-1 w-full rounded-md border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none"
                      [ngModel]="epf().employeeRate"
                      (ngModelChange)="updateEpf('employeeRate', $event)"
                    />
                  </label>
                  <label class="block">
                    <span class="text-sm font-medium text-neutral-700"
                      >Employer rate (%)</span
                    >
                    <input
                      type="number"
                      class="mt-1 w-full rounded-md border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none"
                      [ngModel]="epf().employerRate"
                      (ngModelChange)="updateEpf('employerRate', $event)"
                    />
                  </label>
                  <label class="block">
                    <span class="text-sm font-medium text-neutral-700"
                      >Annual wage ceiling</span
                    >
                    <input
                      type="number"
                      placeholder="No ceiling"
                      class="mt-1 w-full rounded-md border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none"
                      [ngModel]="epf().wageCeilingAnnual"
                      (ngModelChange)="updateEpf('wageCeilingAnnual', $event)"
                    />
                  </label>
                  <label class="block">
                    <span class="text-sm font-medium text-neutral-700"
                      >Applicable on</span
                    >
                    <select
                      class="mt-1 w-full rounded-md border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none"
                      [ngModel]="epf().applicableOn"
                      (ngModelChange)="updateEpf('applicableOn', $event)"
                    >
                      @for (a of applicableOnOptions; track a) {
                        <option [value]="a">{{ applicableOnLabels[a] }}</option>
                      }
                    </select>
                  </label>
                </div>
                <p class="mt-3 text-xs text-neutral-500">
                  EPF is computed on min(applicable base, monthly ceiling) × rate
                  (AC-2). Monthly ceiling = annual ÷ 12 (BR-8).
                </p>
                <div class="mt-4 flex justify-end">
                  <button
                    type="button"
                    class="rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-40"
                    [style.background-color]="'var(--brand-primary)'"
                    [disabled]="saving()"
                    (click)="saveProvidentFund()"
                  >
                    {{ saving() ? 'Saving…' : 'Save provident fund' }}
                  </button>
                </div>
              </div>
            }

            <!-- Other Deductions tab -->
            @if (activeTab() === 'OtherDeductions') {
              <div
                @fadeIn
                class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
              >
                <h2 class="mb-4 text-base font-semibold text-neutral-900">
                  Other deductions
                </h2>
                <label class="block">
                  <span class="text-sm font-medium text-neutral-700"
                    >Item name</span
                  >
                  <input
                    type="text"
                    placeholder="e.g. Professional tax"
                    class="mt-1 w-full rounded-md border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none"
                    [(ngModel)]="otherName"
                  />
                </label>
                <div class="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <label class="block">
                    <span class="text-sm font-medium text-neutral-700"
                      >Type</span
                    >
                    <select
                      class="mt-1 w-full rounded-md border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none"
                      [(ngModel)]="otherType"
                    >
                      <option value="ProfessionalTax">Professional tax</option>
                      <option value="Custom">Custom statutory</option>
                    </select>
                  </label>
                  <label class="block">
                    <span class="text-sm font-medium text-neutral-700"
                      >Employee rate (%)</span
                    >
                    <input
                      type="number"
                      class="mt-1 w-full rounded-md border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none"
                      [(ngModel)]="otherRate"
                    />
                  </label>
                </div>
                <div class="mt-4 flex justify-end">
                  <button
                    type="button"
                    class="rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-40"
                    [style.background-color]="'var(--brand-primary)'"
                    [disabled]="saving() || !otherName.trim()"
                    (click)="saveOtherDeduction()"
                  >
                    {{ saving() ? 'Saving…' : 'Save deduction' }}
                  </button>
                </div>
              </div>
            }

            <!-- Version history (FR-4) -->
            <div class="mt-6">
              <button
                type="button"
                class="flex items-center gap-2 text-sm font-medium text-neutral-700 hover:text-neutral-900"
                [attr.aria-expanded]="historyOpen()"
                (click)="historyOpen.set(!historyOpen())"
              >
                <span>{{ historyOpen() ? '▾' : '▸' }}</span>
                Version history ({{ rules().length }})
              </button>
              @if (historyOpen()) {
                <ol
                  @collapse
                  class="mt-3 space-y-3 border-l border-neutral-200 pl-4"
                  data-testid="version-history"
                >
                  @for (r of historyEntries(); track r.id) {
                    <li class="relative">
                      <span
                        class="absolute -left-[21px] top-1.5 h-2 w-2 rounded-full bg-neutral-300"
                        aria-hidden="true"
                      ></span>
                      <p class="text-sm font-medium text-neutral-800">
                        {{ ruleTypeLabels[r.ruleType] }} — {{ r.fiscalYear }}
                      </p>
                      <p class="text-xs text-neutral-500">
                        Effective {{ r.effectiveFrom }}
                        @if (r.effectiveTo) {
                          → {{ r.effectiveTo }}
                        } @else {
                          (current)
                        }
                        @if (r.updatedAt) {
                          · updated {{ r.updatedAt }}
                        }
                      </p>
                    </li>
                  }
                  @if (historyEntries().length === 0) {
                    <li class="text-sm text-neutral-400">No history yet.</li>
                  }
                </ol>
              }
            </div>
          </div>

          <!-- Test Calculation panel (FR-5) -->
          <aside class="lg:col-span-1">
            <div
              class="sticky top-6 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            >
              <h2 class="text-base font-semibold text-neutral-900">
                Test calculation
              </h2>
              <p class="mt-1 text-xs text-neutral-500">
                Enter a sample monthly gross to preview statutory deductions for
                {{ fiscalYear() }}.
              </p>
              <label class="mt-4 block">
                <span class="text-sm font-medium text-neutral-700"
                  >Monthly gross</span
                >
                <input
                  type="number"
                  class="mt-1 w-full rounded-md border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none"
                  [(ngModel)]="sampleGross"
                  aria-label="Sample monthly gross"
                />
              </label>
              <button
                type="button"
                class="mt-3 w-full rounded-lg bg-neutral-900 px-4 py-2 text-sm font-medium text-white hover:bg-neutral-800 disabled:opacity-40"
                [disabled]="testing() || !sampleGross"
                (click)="runTestCalculation()"
              >
                {{ testing() ? 'Calculating…' : 'Calculate' }}
              </button>

              @if (testResult(); as r) {
                <dl
                  @fadeIn
                  class="mt-5 space-y-2 border-t border-neutral-100 pt-4 text-sm"
                  data-testid="test-result"
                >
                  <div class="flex justify-between">
                    <dt class="text-neutral-500">Income tax</dt>
                    <dd class="font-mono tabular-nums text-neutral-800">
                      {{ r.incomeTax | number: '1.2-2' }}
                    </dd>
                  </div>
                  <div class="flex justify-between">
                    <dt class="text-neutral-500">Employee EPF</dt>
                    <dd class="font-mono tabular-nums text-neutral-800">
                      {{ r.employeeEpf | number: '1.2-2' }}
                    </dd>
                  </div>
                  <div class="flex justify-between">
                    <dt class="text-neutral-500">Employer EPF</dt>
                    <dd class="font-mono tabular-nums text-neutral-500">
                      {{ r.employerEpf | number: '1.2-2' }}
                    </dd>
                  </div>
                  <div class="flex justify-between">
                    <dt class="text-neutral-500">ETF</dt>
                    <dd class="font-mono tabular-nums text-neutral-800">
                      {{ r.etf | number: '1.2-2' }}
                    </dd>
                  </div>
                  @if (r.otherDeductions) {
                    <div class="flex justify-between">
                      <dt class="text-neutral-500">Other</dt>
                      <dd class="font-mono tabular-nums text-neutral-800">
                        {{ r.otherDeductions | number: '1.2-2' }}
                      </dd>
                    </div>
                  }
                  <div
                    class="flex justify-between border-t border-neutral-100 pt-2 font-semibold"
                  >
                    <dt class="text-neutral-700">Total deductions</dt>
                    <dd class="font-mono tabular-nums text-rose-600">
                      {{ r.totalDeductions | number: '1.2-2' }}
                    </dd>
                  </div>
                  <div class="flex justify-between">
                    <dt class="text-neutral-700">Net pay</dt>
                    <dd class="font-mono tabular-nums text-emerald-700">
                      {{ r.netPay | number: '1.2-2' }}
                    </dd>
                  </div>
                </dl>
              }
            </div>
          </aside>
        </div>
      }
    </div>
  `,
})
export class StatutoryConfigurationComponent implements OnInit {
  private readonly statutory = inject(StatutoryService);
  private readonly toastr = inject(ToastrService);

  readonly ruleTypeLabels = STATUTORY_RULE_TYPE_LABELS;
  readonly applicableOnLabels = APPLICABLE_ON_LABELS;
  readonly applicableOnOptions = APPLICABLE_ON_OPTIONS;

  readonly tabs: { key: Tab; label: string }[] = [
    { key: 'IncomeTax', label: 'Income Tax' },
    { key: 'ProvidentFund', label: 'Provident Fund' },
    { key: 'OtherDeductions', label: 'Other Deductions' },
  ];

  /** Default country until tenant jurisdiction config lands (BR-1). */
  private readonly countryCode = 'LK';

  readonly activeTab = signal<Tab>('IncomeTax');
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly testing = signal(false);
  readonly error = signal<string | null>(null);
  readonly historyOpen = signal(false);

  readonly fiscalYears = signal<string[]>([]);
  readonly fiscalYear = signal<string>(this.defaultFiscalYear());
  readonly rules = signal<IStatutoryRule[]>([]);

  readonly taxSlabs = signal<ITaxSlab[]>([]);
  readonly epf = signal<ISocialSecurityForm>({
    employeeRate: null,
    employerRate: null,
    wageCeilingAnnual: null,
    applicableOn: 'Basic',
  });

  // Other-deductions form (template-driven via ngModel).
  otherName = '';
  otherType: StatutoryRuleType = 'ProfessionalTax';
  otherRate: number | null = null;

  sampleGross: number | null = null;
  readonly testResult = signal<ITestCalculationResult | null>(null);

  /** FR-6 real-time validation, recomputed from the slab signal. */
  private readonly slabValidation = computed(() => validateSlabs(this.taxSlabs()));
  readonly slabsValid = computed(() => this.slabValidation().valid);

  /** Version history newest-first (FR-4). */
  readonly historyEntries = computed(() =>
    [...this.rules()].sort((a, b) =>
      (b.updatedAt ?? b.effectiveFrom).localeCompare(
        a.updatedAt ?? a.effectiveFrom,
      ),
    ),
  );

  ngOnInit(): void {
    this.statutory.listFiscalYears().subscribe({
      next: (years) => {
        const list = years.length ? years : [this.fiscalYear()];
        this.fiscalYears.set(list);
        if (!list.includes(this.fiscalYear())) {
          this.fiscalYear.set(list[0]);
        }
        this.load();
      },
      error: () => {
        // Fall back to the default FY; still load its rules.
        this.fiscalYears.set([this.fiscalYear()]);
        this.load();
      },
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.statutory.listRules(this.fiscalYear()).subscribe({
      next: (rules) => {
        this.rules.set(rules);
        this.hydrateForms(rules);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load statutory rules.');
        this.loading.set(false);
      },
    });
  }

  /** Populate the editors from the loaded rules for the active fiscal year. */
  private hydrateForms(rules: IStatutoryRule[]): void {
    const tax = rules.find((r) => r.ruleType === 'IncomeTax');
    this.taxSlabs.set(tax?.taxSlabs ? tax.taxSlabs.map((s) => ({ ...s })) : []);

    const epf = rules.find((r) => r.ruleType === 'EPF')?.socialSecurity;
    this.epf.set({
      employeeRate: epf?.employeeRate ?? null,
      employerRate: epf?.employerRate ?? null,
      wageCeilingAnnual: epf?.wageCeilingAnnual ?? null,
      applicableOn: epf?.applicableOn ?? 'Basic',
    });
  }

  // ─── Fiscal year (AC-3) ────────────────────────────────────

  selectFiscalYear(fy: string): void {
    if (fy === this.fiscalYear()) {
      return;
    }
    this.fiscalYear.set(fy);
    this.testResult.set(null);
    this.load();
  }

  addFiscalYear(): void {
    const next = this.nextFiscalYear();
    if (this.fiscalYears().includes(next)) {
      this.toastr.info(`Fiscal year ${next} already exists.`);
      this.selectFiscalYear(next);
      return;
    }
    this.fiscalYears.set([next, ...this.fiscalYears()]);
    // Start the new year from a clean slate; it persists on first save.
    this.fiscalYear.set(next);
    this.rules.set([]);
    this.taxSlabs.set([]);
    this.epf.set({
      employeeRate: null,
      employerRate: null,
      wageCeilingAnnual: null,
      applicableOn: 'Basic',
    });
    this.testResult.set(null);
  }

  // ─── Tax slab editor (FR-1/FR-6) ───────────────────────────

  slabIssue(index: number): string | null {
    return this.slabValidation().issues.get(index) ?? null;
  }

  addSlab(): void {
    const slabs = this.taxSlabs();
    const last = slabs[slabs.length - 1];
    const from = last && last.slabTo != null ? last.slabTo : 0;
    this.taxSlabs.set([...slabs, { slabFrom: from, slabTo: null, ratePercentage: 0 }]);
  }

  removeSlab(index: number): void {
    this.taxSlabs.set(this.taxSlabs().filter((_, i) => i !== index));
  }

  updateSlab(
    index: number,
    field: 'slabFrom' | 'slabTo' | 'ratePercentage',
    value: unknown,
  ): void {
    const next = this.taxSlabs().map((s, i) => {
      if (i !== index) {
        return s;
      }
      // slabTo accepts blank (= unlimited); the others coerce to a number.
      if (field === 'slabTo') {
        const num = value === '' || value === null ? null : Number(value);
        return { ...s, slabTo: num };
      }
      return { ...s, [field]: Number(value) };
    });
    this.taxSlabs.set(next);
  }

  saveIncomeTax(): void {
    if (!this.slabsValid()) {
      return;
    }
    const request: IStatutoryRuleRequest = {
      ruleType: 'IncomeTax',
      ruleName: 'Income Tax',
      countryCode: this.countryCode,
      fiscalYear: this.fiscalYear(),
      effectiveFrom: this.fiscalYearStart(this.fiscalYear()),
      taxSlabs: this.taxSlabs(),
    };
    this.persist('IncomeTax', request, 'Tax slabs saved.');
  }

  // ─── Provident fund (FR-2/AC-2) ────────────────────────────

  updateEpf(
    field: keyof ISocialSecurityForm,
    value: unknown,
  ): void {
    const current = this.epf();
    if (field === 'applicableOn') {
      this.epf.set({ ...current, applicableOn: value as ApplicableOn });
      return;
    }
    const num = value === '' || value === null ? null : Number(value);
    this.epf.set({ ...current, [field]: num });
  }

  saveProvidentFund(): void {
    const f = this.epf();
    const socialSecurity: ISocialSecurityRule = {
      employeeRate: f.employeeRate ?? 0,
      employerRate: f.employerRate ?? 0,
      wageCeilingAnnual: f.wageCeilingAnnual,
      applicableOn: f.applicableOn,
    };
    const request: IStatutoryRuleRequest = {
      ruleType: 'EPF',
      ruleName: 'Employee Provident Fund',
      countryCode: this.countryCode,
      fiscalYear: this.fiscalYear(),
      effectiveFrom: this.fiscalYearStart(this.fiscalYear()),
      socialSecurity,
    };
    this.persist('EPF', request, 'Provident fund saved.');
  }

  // ─── Other deductions (FR-3) ───────────────────────────────

  saveOtherDeduction(): void {
    const name = this.otherName.trim();
    if (!name) {
      return;
    }
    const request: IStatutoryRuleRequest = {
      ruleType: this.otherType,
      ruleName: name,
      countryCode: this.countryCode,
      fiscalYear: this.fiscalYear(),
      effectiveFrom: this.fiscalYearStart(this.fiscalYear()),
      socialSecurity: {
        employeeRate: this.otherRate ?? 0,
        employerRate: 0,
        wageCeilingAnnual: null,
        applicableOn: 'Gross',
      },
    };
    this.persist(this.otherType, request, 'Deduction saved.');
  }

  // ─── Test calculation (FR-5) ───────────────────────────────

  runTestCalculation(): void {
    if (!this.sampleGross) {
      return;
    }
    this.testing.set(true);
    this.statutory
      .testCalculation({
        fiscalYear: this.fiscalYear(),
        monthlyGross: this.sampleGross,
      })
      .subscribe({
        next: (result) => {
          this.testResult.set(result);
          this.testing.set(false);
        },
        error: () => {
          this.testing.set(false);
          this.toastr.error('Could not run the test calculation.');
        },
      });
  }

  // ─── Persist helper ────────────────────────────────────────

  /**
   * Create or update the rule of `ruleType` for the active fiscal year. An
   * existing rule (same type + FY) is updated; otherwise a new one is created.
   */
  private persist(
    ruleType: StatutoryRuleType,
    request: IStatutoryRuleRequest,
    successMessage: string,
  ): void {
    this.saving.set(true);
    const existing = this.rules().find(
      (r) => r.ruleType === ruleType && r.fiscalYear === this.fiscalYear(),
    );
    const call$ = existing
      ? this.statutory.updateRule(existing.id, request)
      : this.statutory.createRule(request);
    call$.subscribe({
      next: () => {
        this.saving.set(false);
        this.toastr.success(successMessage);
        this.otherName = '';
        this.otherRate = null;
        if (!this.fiscalYears().includes(this.fiscalYear())) {
          this.fiscalYears.set([this.fiscalYear(), ...this.fiscalYears()]);
        }
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.toastr.error('Could not save the statutory rule.');
      },
    });
  }

  // ─── Fiscal-year helpers ───────────────────────────────────

  /** Current fiscal year as "YYYY-YYYY" (April-start convention). */
  private defaultFiscalYear(): string {
    const now = new Date();
    const startYear = now.getMonth() >= 3 ? now.getFullYear() : now.getFullYear() - 1;
    return `${startYear}-${startYear + 1}`;
  }

  /** The fiscal year after the currently-selected one. */
  private nextFiscalYear(): string {
    const start = Number(this.fiscalYear().split('-')[0]) + 1;
    return `${start}-${start + 1}`;
  }

  /** ISO start date of a "YYYY-YYYY" fiscal year (April 1). */
  private fiscalYearStart(fy: string): string {
    const start = fy.split('-')[0];
    return `${start}-04-01`;
  }
}
