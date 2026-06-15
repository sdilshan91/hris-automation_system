import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  output,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import {
  debounceTime,
  distinctUntilChanged,
  switchMap,
} from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ToastrService } from 'ngx-toastr';
import { InterviewService } from '../../services/interview.service';
import { ILookupOption } from '../../models/vacancy.models';
import {
  IInterview,
  IInterviewRequest,
  InterviewType,
  INTERVIEW_TYPE_OPTIONS,
  DEFAULT_DURATION_MINUTES,
  DURATION_OPTIONS,
  isPastDateTime,
  todayIsoDate,
  nameInitials,
} from '../../models/interview.models';

/** Validator: the interviewer array must hold at least one id (FR-1, ≥1 required). */
function minOneValidator(control: AbstractControl): ValidationErrors | null {
  const value = control.value as string[] | null;
  return (value?.length ?? 0) >= 1 ? null : { required: true };
}

/**
 * US-REC-005: Schedule / reschedule interview form (FR-1 / AC-1 / AC-3).
 *
 * Right slide-over (full-screen on mobile, NFR-5) opened from the applicant detail
 * Interviews tab. Reactive forms + signals + OnPush. Fields: interviewer multi-
 * select (searchable employee dropdown with avatar + department, ≥1 required),
 * date (future only — BR-3 / NFR-6), start time, duration (default 60), interview
 * type (in-person/video/phone), location (required for in-person), video link
 * (required for video), and notes.
 *
 * In `interview` mode the form is pre-filled and submits a reschedule (AC-3);
 * otherwise it schedules a new round (AC-1 — the backend assigns the round number).
 *
 * Soft interviewer-conflict warnings (FR-7) returned on a successful 2xx are
 * surfaced as toasts; the schedule still succeeds.
 */
@Component({
  selector: 'app-interview-form',
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
      class="fixed inset-0 z-40 bg-neutral-900/30"
      (click)="close()"
      aria-hidden="true"
    ></div>

    <!-- Drawer wrap -->
    <div class="pointer-events-none fixed inset-0 z-50 flex justify-end">
      <div
        @drawer
        class="pointer-events-auto flex h-full w-full max-w-xl flex-col bg-white shadow-xl sm:w-[55%]"
        role="dialog"
        aria-modal="true"
        aria-labelledby="interview-form-title"
      >
        <!-- Header -->
        <div
          class="flex items-start justify-between gap-3 border-b border-neutral-100 px-6 py-4"
        >
          <div>
            <h2
              id="interview-form-title"
              class="text-base font-semibold text-neutral-900"
            >
              {{ isEdit() ? 'Reschedule interview' : 'Schedule interview' }}
            </h2>
            <p class="mt-0.5 text-sm text-neutral-500">
              {{ applicantName() || 'Applicant' }}
            </p>
          </div>
          <button
            type="button"
            class="rounded-md p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
            (click)="close()"
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

        <!-- Body (form) -->
        <form
          [formGroup]="form"
          (ngSubmit)="submit()"
          class="flex flex-1 flex-col overflow-hidden"
        >
          <div class="flex-1 space-y-5 overflow-y-auto px-6 py-5">
            <!-- Interviewers (searchable multi-select) -->
            <div>
              <label class="lbl" for="interviewer-search">
                Interviewers <span class="text-red-500">*</span>
              </label>

              <!-- Selected chips -->
              @if (selected().length) {
                <div class="mb-2 flex flex-wrap gap-1.5">
                  @for (s of selected(); track s.id) {
                    <span
                      class="inline-flex items-center gap-1.5 rounded-full bg-indigo-50 py-1 pl-1 pr-2 text-xs font-medium text-indigo-700"
                    >
                      <span
                        class="flex h-5 w-5 items-center justify-center rounded-full bg-indigo-200 text-[0.6rem] text-indigo-800"
                        aria-hidden="true"
                        >{{ initials(s.label) }}</span
                      >
                      {{ s.label }}
                      <button
                        type="button"
                        class="text-indigo-400 hover:text-indigo-700"
                        (click)="removeInterviewer(s.id)"
                        [attr.aria-label]="'Remove ' + s.label"
                      >
                        ×
                      </button>
                    </span>
                  }
                </div>
              }

              <input
                id="interviewer-search"
                type="text"
                class="inp"
                placeholder="Search employees…"
                autocomplete="off"
                [value]="query()"
                (input)="onSearch($event)"
                role="combobox"
                aria-expanded="true"
                aria-controls="interviewer-results"
              />

              <!-- Results dropdown -->
              @if (query().length > 0) {
                <ul
                  id="interviewer-results"
                  class="mt-1 max-h-52 overflow-y-auto rounded-lg border border-neutral-200 bg-white shadow-sm"
                  role="listbox"
                >
                  @if (searching()) {
                    <li class="px-3 py-2 text-sm text-neutral-400">Searching…</li>
                  } @else if (results().length === 0) {
                    <li class="px-3 py-2 text-sm text-neutral-400">
                      No employees found.
                    </li>
                  } @else {
                    @for (r of results(); track r.id) {
                      <li role="option" [attr.aria-selected]="isSelected(r.id)">
                        <button
                          type="button"
                          class="flex w-full items-center gap-2.5 px-3 py-2 text-left transition hover:bg-neutral-50 disabled:opacity-40"
                          [disabled]="isSelected(r.id)"
                          (click)="addInterviewer(r)"
                        >
                          <span
                            class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-neutral-100 text-[0.65rem] font-semibold text-neutral-600"
                            aria-hidden="true"
                            >{{ initials(r.label) }}</span
                          >
                          <span class="min-w-0">
                            <span
                              class="block truncate text-sm text-neutral-800"
                              >{{ r.label }}</span
                            >
                            @if (r.sublabel) {
                              <span
                                class="block truncate text-xs text-neutral-400"
                                >{{ r.sublabel }}</span
                              >
                            }
                          </span>
                        </button>
                      </li>
                    }
                  }
                </ul>
              }
              @if (showError('interviewerIds')) {
                <p class="err">At least one interviewer is required.</p>
              }
            </div>

            <!-- Date + time -->
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label class="lbl" for="iv-date"
                  >Date <span class="text-red-500">*</span></label
                >
                <input
                  id="iv-date"
                  type="date"
                  class="inp"
                  formControlName="scheduledDate"
                  [min]="minDate"
                />
                @if (showError('scheduledDate')) {
                  <p class="err">A future date is required.</p>
                }
              </div>
              <div>
                <label class="lbl" for="iv-time"
                  >Start time <span class="text-red-500">*</span></label
                >
                <input
                  id="iv-time"
                  type="time"
                  class="inp"
                  formControlName="startTime"
                />
                @if (showError('startTime')) {
                  <p class="err">A start time is required.</p>
                }
              </div>
            </div>

            <!-- Past date/time cross-field error (BR-3 / NFR-6) -->
            @if (pastError()) {
              <p class="err -mt-2">
                The interview must be scheduled in the future.
              </p>
            }

            <!-- Duration + type -->
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label class="lbl" for="iv-duration"
                  >Duration (minutes) <span class="text-red-500">*</span></label
                >
                <select
                  id="iv-duration"
                  class="inp"
                  formControlName="durationMinutes"
                >
                  @for (d of durations; track d) {
                    <option [ngValue]="d">{{ d }} min</option>
                  }
                </select>
              </div>
              <div>
                <label class="lbl" for="iv-type"
                  >Type <span class="text-red-500">*</span></label
                >
                <select
                  id="iv-type"
                  class="inp"
                  formControlName="interviewType"
                >
                  @for (t of typeOptions; track t.value) {
                    <option [ngValue]="t.value">{{ t.label }}</option>
                  }
                </select>
              </div>
            </div>

            <!-- Conditional: location (in-person) -->
            @if (form.controls.interviewType.value === 'InPerson') {
              <div>
                <label class="lbl" for="iv-location"
                  >Location <span class="text-red-500">*</span></label
                >
                <input
                  id="iv-location"
                  type="text"
                  class="inp"
                  placeholder="e.g. Room 3B, HQ"
                  formControlName="location"
                />
                @if (showError('location')) {
                  <p class="err">Location is required for in-person interviews.</p>
                }
              </div>
            }

            <!-- Conditional: video link (video) -->
            @if (form.controls.interviewType.value === 'Video') {
              <div>
                <label class="lbl" for="iv-video"
                  >Video meeting link <span class="text-red-500">*</span></label
                >
                <input
                  id="iv-video"
                  type="url"
                  class="inp"
                  placeholder="https://…"
                  formControlName="videoLink"
                />
                @if (showError('videoLink')) {
                  <p class="err">A valid meeting link is required for video interviews.</p>
                }
              </div>
            }

            <!-- Notes -->
            <div>
              <label class="lbl" for="iv-notes">Notes for interviewers</label>
              <textarea
                id="iv-notes"
                rows="3"
                class="inp resize-none"
                placeholder="Optional — agenda, focus areas, links…"
                formControlName="notes"
              ></textarea>
            </div>
          </div>

          <!-- Footer -->
          <div
            class="flex items-center justify-end gap-2 border-t border-neutral-100 px-6 py-4"
          >
            <button
              type="button"
              class="btn-secondary"
              (click)="close()"
              [disabled]="saving()"
            >
              Cancel
            </button>
            <button type="submit" class="btn-primary" [disabled]="saving()">
              {{
                saving()
                  ? 'Saving…'
                  : isEdit()
                    ? 'Save changes'
                    : 'Schedule interview'
              }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .lbl {
        display: block;
        margin-bottom: 0.375rem;
        font-size: 0.8125rem;
        font-weight: 500;
        color: #404040;
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
        box-shadow: 0 0 0 3px rgb(129 140 248 / 0.2);
      }
      .err {
        margin-top: 0.25rem;
        font-size: 0.75rem;
        color: #dc2626;
      }
      .btn-primary {
        border-radius: 0.5rem;
        background: #4f46e5;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: #fff;
        transition: background 150ms ease;
      }
      .btn-primary:hover:not(:disabled) {
        background: #4338ca;
      }
      .btn-secondary {
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: #525252;
        transition: background 150ms ease;
      }
      .btn-secondary:hover:not(:disabled) {
        background: #fafafa;
      }
      .btn-primary:disabled,
      .btn-secondary:disabled {
        cursor: not-allowed;
        opacity: 0.5;
      }
    `,
  ],
})
export class InterviewFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly interviewService = inject(InterviewService);
  private readonly toastr = inject(ToastrService);

  /** Applicant the interview belongs to (required for create). */
  readonly applicantId = input.required<string>();
  /** Vacancy the applicant applied to (sent in the request body). */
  readonly vacancyId = input.required<string>();
  /** Applicant display name shown in the header. */
  readonly applicantName = input<string>('');
  /**
   * When set, the form is in reschedule mode (AC-3) — pre-filled from this
   * interview; submit calls reschedule instead of schedule.
   */
  readonly interview = input<IInterview | null>(null);

  /** Emitted when the drawer is dismissed without saving. */
  readonly cancelled = output<void>();
  /** Emitted after a successful schedule/reschedule with the saved interview. */
  readonly saved = output<IInterview>();

  readonly typeOptions = INTERVIEW_TYPE_OPTIONS;
  readonly durations = DURATION_OPTIONS;
  readonly minDate = todayIsoDate();

  readonly saving = signal(false);

  // Interviewer multi-select state.
  readonly query = signal('');
  readonly results = signal<ILookupOption[]>([]);
  readonly searching = signal(false);
  readonly selected = signal<ILookupOption[]>([]);

  private readonly search$ = new Subject<string>();

  readonly form = this.fb.nonNullable.group({
    // Holds the selected interviewer ids; kept in sync with `selected()`.
    interviewerIds: this.fb.nonNullable.control<string[]>(
      [],
      [minOneValidator],
    ),
    scheduledDate: this.fb.nonNullable.control('', [Validators.required]),
    startTime: this.fb.nonNullable.control('', [Validators.required]),
    durationMinutes: this.fb.nonNullable.control(DEFAULT_DURATION_MINUTES, [
      Validators.required,
      Validators.min(1),
    ]),
    interviewType: this.fb.nonNullable.control<InterviewType>('InPerson', [
      Validators.required,
    ]),
    location: this.fb.nonNullable.control(''),
    videoLink: this.fb.nonNullable.control(''),
    notes: this.fb.nonNullable.control(''),
  });

  readonly isEdit = computed(() => this.interview() != null);

  /** Cross-field: true when the chosen date+time is in the past (BR-3 / NFR-6). */
  readonly pastError = signal(false);

  constructor() {
    // Debounced employee search for the interviewer dropdown (§8).
    this.search$
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((q) => {
          this.searching.set(true);
          return this.interviewService.searchEmployees(q);
        }),
        takeUntilDestroyed(),
      )
      .subscribe({
        next: (rows) => {
          this.results.set(rows);
          this.searching.set(false);
        },
        error: () => {
          this.results.set([]);
          this.searching.set(false);
        },
      });

    // Apply conditional required validators when the interview type changes (FR-1).
    this.form.controls.interviewType.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((type) => this.applyConditionalValidators(type));

    // Pre-fill in reschedule mode (AC-3). effect() reacts to the input signal.
    effect(() => {
      const iv = this.interview();
      if (iv) {
        this.prefill(iv);
      }
    });
  }

  // ─── Interviewer multi-select ────────────────────────────

  onSearch(event: Event): void {
    const q = (event.target as HTMLInputElement).value;
    this.query.set(q);
    if (q.trim().length === 0) {
      this.results.set([]);
      return;
    }
    this.search$.next(q.trim());
  }

  isSelected(id: string): boolean {
    return this.selected().some((s) => s.id === id);
  }

  addInterviewer(option: ILookupOption): void {
    if (this.isSelected(option.id)) {
      return;
    }
    this.selected.update((list) => [...list, option]);
    this.syncInterviewerControl();
    this.query.set('');
    this.results.set([]);
  }

  removeInterviewer(id: string): void {
    this.selected.update((list) => list.filter((s) => s.id !== id));
    this.syncInterviewerControl();
  }

  private syncInterviewerControl(): void {
    const ctrl = this.form.controls.interviewerIds;
    ctrl.setValue(this.selected().map((s) => s.id));
    ctrl.markAsDirty();
    ctrl.markAsTouched();
  }

  initials(name: string): string {
    return nameInitials(name);
  }

  // ─── Validation helpers ──────────────────────────────────

  private applyConditionalValidators(type: InterviewType): void {
    const location = this.form.controls.location;
    const video = this.form.controls.videoLink;
    if (type === 'InPerson') {
      location.setValidators([Validators.required]);
      video.clearValidators();
      video.setValue('');
    } else if (type === 'Video') {
      video.setValidators([Validators.required, Validators.pattern(/^https?:\/\/.+/i)]);
      location.clearValidators();
      location.setValue('');
    } else {
      location.clearValidators();
      video.clearValidators();
    }
    location.updateValueAndValidity();
    video.updateValueAndValidity();
  }

  /** True when a control is invalid AND touched/dirty — drives inline errors. */
  showError(name: keyof typeof this.form.controls): boolean {
    const ctrl = this.form.controls[name];
    return ctrl.invalid && (ctrl.touched || ctrl.dirty);
  }

  // ─── Prefill (reschedule) ────────────────────────────────

  private prefill(iv: IInterview): void {
    this.selected.set(
      iv.interviewers.map((p) => ({
        id: p.employeeId,
        label: p.name,
        sublabel: p.department ?? null,
      })),
    );
    this.form.patchValue({
      interviewerIds: iv.interviewers.map((p) => p.employeeId),
      scheduledDate: iv.scheduledDate,
      startTime: iv.startTime,
      durationMinutes: iv.durationMinutes || DEFAULT_DURATION_MINUTES,
      interviewType: iv.interviewType,
      location: iv.location ?? '',
      videoLink: iv.videoLink ?? '',
      notes: iv.notes ?? '',
    });
    this.applyConditionalValidators(iv.interviewType);
  }

  // ─── Submit ──────────────────────────────────────────────

  submit(): void {
    this.form.markAllAsTouched();
    this.syncInterviewerControl();

    const raw = this.form.getRawValue();
    // Cross-field future check (BR-3 / NFR-6) — soft, mirrors the date `min`.
    const past = isPastDateTime(raw.scheduledDate, raw.startTime);
    this.pastError.set(past);

    if (this.form.invalid || past) {
      return;
    }

    const request: IInterviewRequest = {
      applicantId: this.applicantId(),
      vacancyId: this.vacancyId(),
      interviewerIds: raw.interviewerIds,
      interviewType: raw.interviewType,
      scheduledDate: raw.scheduledDate,
      startTime: raw.startTime,
      durationMinutes: raw.durationMinutes,
      location: raw.interviewType === 'InPerson' ? raw.location : null,
      videoLink: raw.interviewType === 'Video' ? raw.videoLink : null,
      notes: raw.notes || null,
    };

    this.saving.set(true);
    const iv = this.interview();
    const call$ = iv
      ? this.interviewService.reschedule(iv.id, request)
      : this.interviewService.schedule(this.applicantId(), request);

    call$.subscribe({
      next: (result) => {
        this.saving.set(false);
        this.toastr.success(
          iv ? 'Interview rescheduled.' : 'Interview scheduled.',
        );
        // Soft conflict warnings (FR-7) — the schedule SUCCEEDED, just warn.
        for (const w of result.warnings) {
          this.toastr.warning(w, 'Scheduling conflict');
        }
        this.saved.emit(result.interview);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.toastr.error(InterviewService.parseErrorMessage(err));
      },
    });
  }

  close(): void {
    this.cancelled.emit();
  }
}
