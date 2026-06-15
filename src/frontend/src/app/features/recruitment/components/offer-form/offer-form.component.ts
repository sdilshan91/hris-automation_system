import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { OfferService } from '../../services/offer.service';
import { VacancyService } from '../../services/vacancy.service';
import { ILookupOption } from '../../models/vacancy.models';
import {
  IOffer,
  IOfferRequest,
  SalaryFrequency,
  SALARY_FREQUENCY_OPTIONS,
  CURRENCY_OPTIONS,
  offerStatusBadge,
  offerStatusLabel,
  formatCompensation,
  defaultExpiryDate,
  toIsoDate,
  canSendOffer,
  canRespondToOffer,
  canWithdrawOffer,
} from '../../models/offer.models';

/**
 * US-REC-007: Offer-letter create form + preview/actions, as a Notion-style right
 * slide-over drawer (full-screen on mobile). Reactive forms + signals + OnPush.
 *
 * Two modes:
 *  - CREATE (AC-1/FR-1): sections Position Details / Compensation / Timeline /
 *    Additional Clauses; reporting manager (employee typeahead) + department
 *    lookup. "Generate" creates the Draft offer and switches to the preview step.
 *  - PREVIEW + actions (AC-2/AC-3/FR-4/FR-8): shows the generated/loaded offer with
 *    a status badge, a "Download" button (streams the server PDF — inline pdf.js
 *    preview is NOT available, pdf.js is not a project dep, see note), and the
 *    lifecycle actions: "Send to Applicant" (Draft→Sent), "Accept"/"Decline"
 *    (Sent→Accepted/Declined; Accept advances the applicant to Hired), and
 *    "Withdraw" (before acceptance).
 *
 * Passing an existing `offer` opens straight in the preview step (offer history /
 * versions, FR-9). The expiry date defaults to +7 days (BR-6).
 */
@Component({
  selector: 'app-offer-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate(
          '260ms cubic-bezier(0.22, 1, 0.36, 1)',
          style({ transform: 'translateX(0)' }),
        ),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
  ],
  template: `
    <!-- Backdrop -->
    <div
      @backdrop
      class="fixed inset-0 z-[55] bg-neutral-900/30"
      (click)="cancel()"
      aria-hidden="true"
    ></div>

    <!-- Drawer wrap -->
    <div class="pointer-events-none fixed inset-0 z-[56] flex justify-end">
      <div
        @drawer
        class="pointer-events-auto flex h-full w-full max-w-xl flex-col bg-white shadow-xl sm:w-[60%]"
        role="dialog"
        aria-modal="true"
        aria-labelledby="offer-form-title"
      >
        <!-- Header -->
        <div
          class="flex items-start justify-between gap-3 border-b border-neutral-100 px-6 py-4"
        >
          <div>
            <h2 id="offer-form-title" class="text-base font-semibold text-neutral-900">
              {{ step() === 'preview' ? 'Offer letter' : 'Generate offer letter' }}
            </h2>
            <p class="mt-0.5 flex flex-wrap items-center gap-2 text-sm text-neutral-500">
              <span>{{ applicantName() || 'Candidate' }}</span>
              @if (currentOffer(); as o) {
                <span
                  class="badge ring-1 ring-inset"
                  [class]="statusBadge(o.status)"
                >
                  {{ statusLabel(o.status) }}
                </span>
                @if (o.version > 1) {
                  <span class="text-xs text-neutral-400">v{{ o.version }}</span>
                }
              }
            </p>
          </div>
          <button
            type="button"
            class="rounded-md p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
            (click)="cancel()"
            aria-label="Close"
          >
            <svg
              class="h-5 w-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              aria-hidden="true"
            >
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto px-6 py-5">
          <!-- ── CREATE step ──────────────────────────────── -->
          @if (step() === 'create') {
            <form [formGroup]="form" class="space-y-7">
              <!-- Position Details -->
              <section>
                <h3 class="section-title">Position details</h3>
                <div class="mt-3 space-y-4">
                  <div>
                    <label for="position" class="lbl">
                      Offered position
                      <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <input
                      id="position"
                      type="text"
                      formControlName="offeredPosition"
                      class="inp"
                      placeholder="e.g. Senior Software Engineer"
                    />
                    @if (showError('offeredPosition')) {
                      <p class="err">A position title is required.</p>
                    }
                  </div>

                  <div>
                    <label for="department" class="lbl">Department</label>
                    <select id="department" formControlName="departmentId" class="inp">
                      <option [ngValue]="null">— Select department —</option>
                      @for (d of departments(); track d.id) {
                        <option [ngValue]="d.id">{{ d.label }}</option>
                      }
                    </select>
                  </div>

                  <!-- Reporting manager (employee typeahead) -->
                  <div>
                    <label for="manager-search" class="lbl">Reporting manager</label>
                    @if (selectedManager(); as m) {
                      <div
                        class="flex items-center justify-between gap-2 rounded-lg border border-neutral-200 bg-neutral-50 px-3 py-2"
                      >
                        <div class="min-w-0">
                          <p class="truncate text-sm font-medium text-neutral-800">
                            {{ m.label }}
                          </p>
                          @if (m.sublabel) {
                            <p class="truncate text-xs text-neutral-500">
                              {{ m.sublabel }}
                            </p>
                          }
                        </div>
                        <button
                          type="button"
                          class="shrink-0 text-xs font-medium text-indigo-600 hover:underline"
                          (click)="clearManager()"
                        >
                          Change
                        </button>
                      </div>
                    } @else {
                      <input
                        id="manager-search"
                        type="text"
                        class="inp"
                        placeholder="Search employees…"
                        [value]="managerQuery()"
                        (input)="onManagerSearch($event)"
                        autocomplete="off"
                      />
                      @if (managerResults().length > 0) {
                        <ul
                          class="mt-1 max-h-44 overflow-y-auto rounded-lg border border-neutral-200 bg-white shadow-sm"
                        >
                          @for (e of managerResults(); track e.id) {
                            <li>
                              <button
                                type="button"
                                class="flex w-full flex-col items-start px-3 py-2 text-left transition hover:bg-indigo-50"
                                (click)="selectManager(e)"
                              >
                                <span class="text-sm text-neutral-800">{{ e.label }}</span>
                                @if (e.sublabel) {
                                  <span class="text-xs text-neutral-500">{{ e.sublabel }}</span>
                                }
                              </button>
                            </li>
                          }
                        </ul>
                      }
                    }
                  </div>
                </div>
              </section>

              <!-- Compensation -->
              <section>
                <h3 class="section-title">Compensation</h3>
                <div class="mt-3 grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <div class="sm:col-span-1">
                    <label for="currency" class="lbl">Currency</label>
                    <select id="currency" formControlName="currency" class="inp">
                      @for (c of currencies; track c) {
                        <option [value]="c">{{ c }}</option>
                      }
                    </select>
                  </div>
                  <div class="sm:col-span-1">
                    <label for="salary" class="lbl">
                      Amount <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <input
                      id="salary"
                      type="number"
                      min="0"
                      step="0.01"
                      formControlName="salaryAmount"
                      class="inp"
                      placeholder="0"
                    />
                    @if (showError('salaryAmount')) {
                      <p class="err">Enter a salary amount greater than 0.</p>
                    }
                  </div>
                  <div class="sm:col-span-1">
                    <label for="frequency" class="lbl">Frequency</label>
                    <select id="frequency" formControlName="salaryFrequency" class="inp">
                      @for (f of frequencies; track f.value) {
                        <option [value]="f.value">{{ f.label }}</option>
                      }
                    </select>
                  </div>
                </div>
                <div class="mt-4">
                  <label for="benefits" class="lbl">Benefits summary</label>
                  <textarea
                    id="benefits"
                    rows="2"
                    formControlName="benefitsSummary"
                    class="inp resize-none"
                    placeholder="Health insurance, annual leave, bonus…"
                  ></textarea>
                </div>
              </section>

              <!-- Timeline -->
              <section>
                <h3 class="section-title">Timeline</h3>
                <div class="mt-3 grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div>
                    <label for="start-date" class="lbl">
                      Start date
                      <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <input
                      id="start-date"
                      type="date"
                      formControlName="startDate"
                      class="inp"
                    />
                    @if (showError('startDate')) {
                      <p class="err">A start date is required.</p>
                    }
                  </div>
                  <div>
                    <label for="expiry-date" class="lbl">
                      Offer expires
                      <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <input
                      id="expiry-date"
                      type="date"
                      formControlName="expiryDate"
                      class="inp"
                    />
                    @if (showError('expiryDate')) {
                      <p class="err">An expiry date is required (defaults to +7 days).</p>
                    }
                    @if (form.errors?.['expiryBeforeStart']) {
                      <p class="err">Expiry must be on or after the start date.</p>
                    }
                  </div>
                  <div>
                    <label for="probation" class="lbl">Probation (months)</label>
                    <input
                      id="probation"
                      type="number"
                      min="0"
                      formControlName="probationMonths"
                      class="inp"
                      placeholder="e.g. 3"
                    />
                  </div>
                </div>
              </section>

              <!-- Additional Clauses -->
              <section>
                <h3 class="section-title">Additional clauses</h3>
                <textarea
                  rows="4"
                  formControlName="customClauses"
                  class="inp mt-3 resize-none"
                  aria-label="Additional clauses"
                  placeholder="Any custom terms, conditions, or notes to include in the letter (optional)"
                ></textarea>
              </section>
            </form>
          }

          <!-- ── PREVIEW step ─────────────────────────────── -->
          @if (step() === 'preview' && currentOffer(); as o) {
            <div class="space-y-5">
              <div
                class="rounded-xl border border-neutral-200 bg-neutral-50 px-4 py-3"
              >
                <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                  Reference
                </p>
                <p class="mt-0.5 font-mono text-sm text-neutral-800">
                  {{ o.offerReferenceNumber || '—' }}
                </p>
              </div>

              <dl class="grid grid-cols-1 gap-x-6 gap-y-4 sm:grid-cols-2">
                <div>
                  <dt class="dt">Position</dt>
                  <dd class="dd">{{ o.offeredPosition }}</dd>
                </div>
                <div>
                  <dt class="dt">Department</dt>
                  <dd class="dd">{{ o.departmentName || '—' }}</dd>
                </div>
                <div>
                  <dt class="dt">Reporting manager</dt>
                  <dd class="dd">{{ o.reportingManagerName || '—' }}</dd>
                </div>
                <div>
                  <dt class="dt">Compensation</dt>
                  <dd class="dd">{{ compensation(o) }}</dd>
                </div>
                <div>
                  <dt class="dt">Start date</dt>
                  <dd class="dd">{{ o.startDate | date: 'mediumDate' }}</dd>
                </div>
                <div>
                  <dt class="dt">Expires</dt>
                  <dd class="dd">{{ o.expiryDate | date: 'mediumDate' }}</dd>
                </div>
                @if (o.probationMonths != null) {
                  <div>
                    <dt class="dt">Probation</dt>
                    <dd class="dd">{{ o.probationMonths }} months</dd>
                  </div>
                }
                @if (o.benefitsSummary) {
                  <div class="sm:col-span-2">
                    <dt class="dt">Benefits</dt>
                    <dd class="dd whitespace-pre-wrap">{{ o.benefitsSummary }}</dd>
                  </div>
                }
                @if (o.customClauses) {
                  <div class="sm:col-span-2">
                    <dt class="dt">Additional clauses</dt>
                    <dd class="dd whitespace-pre-wrap">{{ o.customClauses }}</dd>
                  </div>
                }
              </dl>

              <!-- Offer timeline (generated → sent → response) -->
              <div class="rounded-xl border border-neutral-200 px-4 py-3">
                <p class="dt mb-2">Timeline</p>
                <ol class="space-y-2 text-sm text-neutral-600">
                  <li class="flex items-center gap-2">
                    <span class="dot bg-neutral-400" aria-hidden="true"></span>
                    Generated
                    <span class="text-xs text-neutral-400">{{ o.createdAt | date: 'short' }}</span>
                  </li>
                  @if (o.sentAt) {
                    <li class="flex items-center gap-2">
                      <span class="dot bg-blue-500" aria-hidden="true"></span>
                      Sent to applicant
                      <span class="text-xs text-neutral-400">{{ o.sentAt | date: 'short' }}</span>
                    </li>
                  }
                  @if (o.respondedAt) {
                    <li class="flex items-center gap-2">
                      <span
                        class="dot"
                        [class]="o.response === 'Accepted' ? 'bg-green-500' : 'bg-red-500'"
                        aria-hidden="true"
                      ></span>
                      {{ o.response === 'Accepted' ? 'Accepted' : 'Declined' }}
                      <span class="text-xs text-neutral-400">{{ o.respondedAt | date: 'short' }}</span>
                    </li>
                  }
                </ol>
              </div>

              <!-- Download (server PDF stream) -->
              <div
                class="flex items-center justify-between gap-4 rounded-lg border border-neutral-200 bg-white p-4"
              >
                <div class="flex items-center gap-3 overflow-hidden">
                  <svg
                    class="h-8 w-8 shrink-0 text-neutral-400"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="1.5"
                    aria-hidden="true"
                  >
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                    <path d="M14 2v6h6" />
                  </svg>
                  <span class="truncate text-sm text-neutral-700">Offer letter (PDF)</span>
                </div>
                <button
                  type="button"
                  class="shrink-0 rounded-lg border border-neutral-200 bg-white px-3 py-1.5 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50 disabled:opacity-50"
                  [disabled]="downloading()"
                  (click)="downloadPdf()"
                >
                  {{ downloading() ? 'Downloading…' : 'Download' }}
                </button>
              </div>
              <p class="text-xs text-neutral-400">
                Inline PDF preview is not available (pdf.js is not a project
                dependency); use Download to view the generated letter.
              </p>
            </div>
          }
        </div>

        <!-- Footer -->
        <div
          class="flex flex-wrap items-center justify-end gap-2 border-t border-neutral-100 px-6 py-4"
        >
          @if (step() === 'create') {
            <button
              type="button"
              class="btn-secondary"
              (click)="cancel()"
            >
              Cancel
            </button>
            <button
              type="button"
              class="btn-primary"
              [disabled]="generating()"
              (click)="generate()"
            >
              {{ generating() ? 'Generating…' : 'Generate' }}
            </button>
          } @else if (currentOffer(); as o) {
            <button type="button" class="btn-secondary" (click)="cancel()">
              Close
            </button>
            @if (canWithdraw(o)) {
              <button
                type="button"
                class="btn-danger-outline"
                [disabled]="busy()"
                (click)="withdraw()"
              >
                Withdraw
              </button>
            }
            @if (canRespond(o)) {
              <button
                type="button"
                class="btn-danger-outline"
                [disabled]="busy()"
                (click)="decline()"
              >
                Decline
              </button>
              <button
                type="button"
                class="btn-success"
                [disabled]="busy()"
                (click)="accept()"
              >
                {{ busy() ? 'Saving…' : 'Accept' }}
              </button>
            }
            @if (canSend(o)) {
              <button
                type="button"
                class="btn-primary"
                [disabled]="busy()"
                (click)="send()"
              >
                {{ busy() ? 'Sending…' : 'Send to applicant' }}
              </button>
            }
          }
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .section-title {
        font-size: 0.6875rem;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: #a3a3a3;
      }
      .lbl {
        display: block;
        font-size: 0.8125rem;
        font-weight: 500;
        color: #404040;
        margin-bottom: 0.375rem;
      }
      .inp {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        padding: 0.5rem 0.75rem;
        font-size: 0.875rem;
        color: #262626;
        transition: border-color 150ms ease, box-shadow 150ms ease;
      }
      .inp:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 2px rgb(199 210 254);
      }
      .err {
        margin-top: 0.375rem;
        font-size: 0.75rem;
        color: #dc2626;
      }
      .dt {
        font-size: 0.6875rem;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: #a3a3a3;
      }
      .dd {
        margin-top: 0.125rem;
        font-size: 0.875rem;
        color: #404040;
      }
      .badge {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        padding: 0.0625rem 0.5rem;
        font-size: 0.6875rem;
        font-weight: 500;
      }
      .dot {
        display: inline-block;
        height: 0.5rem;
        width: 0.5rem;
        border-radius: 9999px;
      }
      .btn-primary {
        border-radius: 0.5rem;
        background: #4f46e5;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: white;
        transition: background 150ms ease;
      }
      .btn-primary:hover {
        background: #4338ca;
      }
      .btn-primary:disabled {
        opacity: 0.5;
      }
      .btn-success {
        border-radius: 0.5rem;
        background: #16a34a;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: white;
        transition: background 150ms ease;
      }
      .btn-success:hover {
        background: #15803d;
      }
      .btn-success:disabled {
        opacity: 0.5;
      }
      .btn-secondary {
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: white;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: #525252;
        transition: background 150ms ease;
      }
      .btn-secondary:hover {
        background: #fafafa;
      }
      .btn-danger-outline {
        border-radius: 0.5rem;
        border: 1px solid #fecaca;
        background: white;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: #dc2626;
        transition: background 150ms ease;
      }
      .btn-danger-outline:hover {
        background: #fef2f2;
      }
      .btn-danger-outline:disabled {
        opacity: 0.5;
      }
    `,
  ],
})
export class OfferFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly offerService = inject(OfferService);
  private readonly vacancyService = inject(VacancyService);
  private readonly toastr = inject(ToastrService);

  /** Applicant the offer is for (AC-1). */
  readonly applicantId = input.required<string>();
  /** Candidate name for the header context (optional). */
  readonly applicantName = input<string>('');
  /** An existing offer to preview/act on (FR-9), or null to create a new one. */
  readonly offer = input<IOffer | null>(null);

  /**
   * Emitted after a successful generate/send/respond/withdraw with the latest
   * offer, so the parent can refresh the offer history / applicant detail.
   */
  readonly changed = output<IOffer>();
  /** Emitted when the offer is accepted (so the parent reloads the applicant → Hired, BR-3). */
  readonly accepted = output<IOffer>();
  /** Emitted when the user dismisses the drawer. */
  readonly cancelled = output<void>();

  readonly frequencies = SALARY_FREQUENCY_OPTIONS;
  readonly currencies = CURRENCY_OPTIONS;

  readonly step = signal<'create' | 'preview'>('create');
  readonly generating = signal(false);
  readonly busy = signal(false);
  readonly downloading = signal(false);

  // Department lookup
  readonly departments = signal<ILookupOption[]>([]);

  // Reporting-manager typeahead
  readonly managerQuery = signal('');
  readonly managerResults = signal<ILookupOption[]>([]);
  readonly selectedManager = signal<ILookupOption | null>(null);
  private managerSearchTimer: ReturnType<typeof setTimeout> | null = null;

  readonly form = this.fb.group(
    {
      offeredPosition: ['', Validators.required],
      departmentId: this.fb.control<string | null>(null),
      salaryAmount: this.fb.control<number | null>(null, [
        Validators.required,
        Validators.min(0.01),
      ]),
      currency: ['USD', Validators.required],
      salaryFrequency: this.fb.control<SalaryFrequency>('Annual', Validators.required),
      benefitsSummary: this.fb.control<string>(''),
      startDate: ['', Validators.required],
      expiryDate: [defaultExpiryDate(), Validators.required],
      probationMonths: this.fb.control<number | null>(null),
      customClauses: this.fb.control<string>(''),
    },
    { validators: [expiryAfterStartValidator] },
  );

  constructor() {
    // An existing offer opens straight into the preview step (FR-9 / actions).
    effect(() => {
      if (this.offer()) {
        this.step.set('preview');
      } else {
        this.step.set('create');
      }
    });

    this.loadDepartments();
  }

  private loadDepartments(): void {
    this.vacancyService.getDepartments().subscribe({
      next: (rows) => this.departments.set(rows),
      // Department is optional; a failed lookup just leaves the dropdown empty.
      error: () => this.departments.set([]),
    });
  }

  // ─── Reporting-manager typeahead (employee lookup) ───────

  onManagerSearch(ev: Event): void {
    const q = (ev.target as HTMLInputElement).value;
    this.managerQuery.set(q);
    if (this.managerSearchTimer) {
      clearTimeout(this.managerSearchTimer);
    }
    if (!q.trim()) {
      this.managerResults.set([]);
      return;
    }
    this.managerSearchTimer = setTimeout(() => this.searchManagers(q), 300);
  }

  private searchManagers(q: string): void {
    this.vacancyService.searchEmployees(q).subscribe({
      next: (rows) => this.managerResults.set(rows),
      error: () => this.managerResults.set([]),
    });
  }

  selectManager(e: ILookupOption): void {
    this.selectedManager.set(e);
    this.managerResults.set([]);
    this.managerQuery.set('');
  }

  clearManager(): void {
    this.selectedManager.set(null);
  }

  // ─── Generate (AC-1 / FR-1) ──────────────────────────────

  showError(controlName: string): boolean {
    const c = this.form.get(controlName);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  generate(): void {
    if (this.generating()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const request: IOfferRequest = {
      offeredPosition: (v.offeredPosition ?? '').trim(),
      departmentId: v.departmentId ?? null,
      reportingManagerId: this.selectedManager()?.id ?? null,
      salaryAmount: Number(v.salaryAmount ?? 0),
      currency: v.currency ?? 'USD',
      salaryFrequency: v.salaryFrequency ?? 'Annual',
      benefitsSummary: (v.benefitsSummary ?? '').trim() || null,
      startDate: v.startDate ?? '',
      expiryDate: v.expiryDate ?? '',
      probationMonths:
        v.probationMonths != null && v.probationMonths !== ('' as unknown)
          ? Number(v.probationMonths)
          : null,
      customClauses: (v.customClauses ?? '').trim() || null,
    };
    this.generating.set(true);
    this.offerService.generate(this.applicantId(), request).subscribe({
      next: (saved) => {
        this.generating.set(false);
        this.generatedOffer.set(saved);
        this.step.set('preview');
        this.toastr.success('Offer letter generated.');
        this.changed.emit(saved);
      },
      error: (err: HttpErrorResponse) => {
        this.generating.set(false);
        this.toastr.error(OfferService.parseErrorMessage(err));
      },
    });
  }

  /**
   * The offer generated/updated in THIS session (generate/send/respond/withdraw).
   * Takes priority over the input `offer` so lifecycle actions reflect immediately.
   */
  private readonly generatedOffer = signal<IOffer | null>(null);

  /** The offer currently shown in preview — the locally-updated one, else the input. */
  readonly currentOffer = computed<IOffer | null>(
    () => this.generatedOffer() ?? this.offer(),
  );

  // ─── Lifecycle actions (AC-2 / AC-3 / FR-8) ──────────────

  send(): void {
    const o = this.currentOffer();
    if (!o || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.offerService.send(o.id).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.generatedOffer.set(updated);
        this.toastr.success('Offer sent to the applicant.');
        this.changed.emit(updated);
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.toastr.error(OfferService.parseErrorMessage(err));
      },
    });
  }

  accept(): void {
    this.respond('Accepted');
  }

  decline(): void {
    this.respond('Declined');
  }

  private respond(response: 'Accepted' | 'Declined'): void {
    const o = this.currentOffer();
    if (!o || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.offerService.respond(o.id, response).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.generatedOffer.set(updated);
        this.toastr.success(
          response === 'Accepted'
            ? 'Offer accepted — applicant moved to Hired.'
            : 'Offer declined.',
        );
        this.changed.emit(updated);
        if (response === 'Accepted') {
          this.accepted.emit(updated);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.toastr.error(OfferService.parseErrorMessage(err));
      },
    });
  }

  withdraw(): void {
    const o = this.currentOffer();
    if (!o || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.offerService.withdraw(o.id).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.generatedOffer.set(updated);
        this.toastr.success('Offer withdrawn. The applicant has been notified.');
        this.changed.emit(updated);
      },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.toastr.error(OfferService.parseErrorMessage(err));
      },
    });
  }

  // ─── PDF download (FR-4) ─────────────────────────────────

  downloadPdf(): void {
    const o = this.currentOffer();
    if (!o || this.downloading()) {
      return;
    }
    this.downloading.set(true);
    this.offerService.downloadPdf(o.id).subscribe({
      next: (res) => {
        this.downloading.set(false);
        const blob = res.body;
        if (!blob) {
          this.toastr.error('The offer letter could not be downloaded.');
          return;
        }
        const filename = OfferService.filenameFromResponse(
          res,
          `offer-${o.offerReferenceNumber || o.id}.pdf`,
        );
        this.triggerDownload(blob, filename);
      },
      error: (err: HttpErrorResponse) => {
        this.downloading.set(false);
        this.toastr.error(OfferService.parseErrorMessage(err));
      },
    });
  }

  private triggerDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }

  // ─── Template helpers ────────────────────────────────────

  statusBadge(status: string): string {
    return offerStatusBadge(status);
  }

  statusLabel(status: string): string {
    return offerStatusLabel(status);
  }

  compensation(o: IOffer): string {
    return formatCompensation(o);
  }

  canSend(o: IOffer): boolean {
    return canSendOffer(o);
  }

  canRespond(o: IOffer): boolean {
    return canRespondToOffer(o);
  }

  canWithdraw(o: IOffer): boolean {
    return canWithdrawOffer(o);
  }

  cancel(): void {
    this.cancelled.emit();
  }
}

/** Cross-field: offer expiry must be on or after the start date (BR-6). */
function expiryAfterStartValidator(
  group: AbstractControl,
): ValidationErrors | null {
  const start = group.get('startDate')?.value as string;
  const expiry = group.get('expiryDate')?.value as string;
  if (!start || !expiry) {
    return null;
  }
  return expiry < start ? { expiryBeforeStart: true } : null;
}

// Re-export so a consumer importing the component also gets `toIsoDate` if needed.
export { toIsoDate };
