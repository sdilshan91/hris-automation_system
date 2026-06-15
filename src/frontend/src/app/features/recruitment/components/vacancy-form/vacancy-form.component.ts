import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  effect,
  OnInit,
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
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ToastrService } from 'ngx-toastr';
import { VacancyService } from '../../services/vacancy.service';
import { RichTextEditorComponent } from '../rich-text-editor/rich-text-editor.component';
import {
  IVacancy,
  IVacancyRequest,
  ILookupOption,
  VacancyEmploymentType,
  VACANCY_EMPLOYMENT_TYPE_OPTIONS,
} from '../../models/vacancy.models';

/**
 * US-REC-001: Vacancy create / edit form, rendered as a Notion-style right
 * slide-over drawer (full-screen on mobile) with four sections — Basic Info,
 * Description, Requirements, Settings (§8).
 *
 * Two save paths (AC-1, AC-2): "Save as Draft" persists with the minimal `title`
 * requirement; "Publish" first saves, then calls the publish endpoint. The backend
 * is the authority on BR-2 publish-completeness; the FE *also* validates the
 * publish-required fields up front so the user gets immediate feedback.
 */
@Component({
  selector: 'app-vacancy-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RichTextEditorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('300ms ease-out', style({ transform: 'translateX(0)' })),
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
      (click)="attemptClose()"
      aria-hidden="true"
    ></div>

    <!-- Drawer wrap: lets backdrop clicks through, panel stays interactive -->
    <div class="fixed inset-0 z-50 flex justify-end pointer-events-none">
      <section
        @drawer
        class="pointer-events-auto flex h-full w-full sm:max-w-2xl flex-col bg-neutral-50 shadow-2xl"
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="isEdit() ? 'Edit vacancy' : 'Create vacancy'"
      >
        <!-- Header -->
        <header
          class="sticky top-0 z-10 flex items-center justify-between border-b border-neutral-200 bg-white px-5 py-4"
        >
          <div>
            <h2 class="text-lg font-semibold tracking-tight text-neutral-900">
              {{ isEdit() ? 'Edit vacancy' : 'New vacancy' }}
            </h2>
            @if (isEdit() && vacancy()?.referenceNumber) {
              <p class="mt-0.5 text-xs text-neutral-500">
                {{ vacancy()?.referenceNumber }}
              </p>
            }
          </div>
          <button
            type="button"
            class="rounded-lg p-2 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
            aria-label="Close"
            (click)="attemptClose()"
          >
            &#10005;
          </button>
        </header>

        <!-- Body -->
        <form
          [formGroup]="form"
          class="flex-1 overflow-y-auto px-5 py-5 space-y-8"
        >
          <!-- ── Basic Info ── -->
          <fieldset class="space-y-4">
            <legend class="text-sm font-semibold text-neutral-800">
              Basic Info
            </legend>

            <div>
              <label class="form-label" for="title">Title *</label>
              <input
                id="title"
                type="text"
                formControlName="title"
                maxlength="200"
                class="form-input"
                placeholder="e.g. Senior Frontend Engineer"
              />
              @if (showError('title')) {
                <p class="form-error">{{ titleError() }}</p>
              }
            </div>

            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="form-label" for="departmentId">Department</label>
                <select
                  id="departmentId"
                  formControlName="departmentId"
                  class="form-input"
                >
                  <option [ngValue]="null">— Select —</option>
                  @for (d of departments(); track d.id) {
                    <option [ngValue]="d.id">{{ d.label }}</option>
                  }
                </select>
              </div>
              <div>
                <label class="form-label" for="jobTitleId">Job title</label>
                <select
                  id="jobTitleId"
                  formControlName="jobTitleId"
                  class="form-input"
                >
                  <option [ngValue]="null">— Select —</option>
                  @for (j of jobTitles(); track j.id) {
                    <option [ngValue]="j.id">{{ j.label }}</option>
                  }
                </select>
              </div>
            </div>

            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="form-label" for="employmentType">
                  Employment type
                </label>
                <select
                  id="employmentType"
                  formControlName="employmentType"
                  class="form-input"
                >
                  <option [ngValue]="null">— Select —</option>
                  @for (t of employmentTypes; track t) {
                    <option [ngValue]="t">{{ t }}</option>
                  }
                </select>
              </div>
              <div>
                <label class="form-label" for="locationId">Location</label>
                <select
                  id="locationId"
                  formControlName="locationId"
                  class="form-input"
                >
                  <option [ngValue]="null">— Select —</option>
                  @for (l of locations(); track l.id) {
                    <option [ngValue]="l.id">{{ l.label }}</option>
                  }
                </select>
              </div>
            </div>

            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="form-label" for="hiringManagerId">
                  Hiring manager
                </label>
                <input
                  type="search"
                  class="form-input mb-2"
                  placeholder="Search employees…"
                  aria-label="Search hiring manager"
                  [value]="managerSearch()"
                  (input)="onManagerSearch($event)"
                />
                <select
                  id="hiringManagerId"
                  formControlName="hiringManagerId"
                  class="form-input"
                  size="1"
                >
                  <option [ngValue]="null">— Select —</option>
                  @for (e of employees(); track e.id) {
                    <option [ngValue]="e.id">
                      {{ e.label }}{{ e.sublabel ? ' · ' + e.sublabel : '' }}
                    </option>
                  }
                </select>
              </div>
              <div>
                <label class="form-label" for="headcount">Headcount</label>
                <input
                  id="headcount"
                  type="number"
                  min="1"
                  formControlName="headcount"
                  class="form-input"
                />
                @if (showError('headcount')) {
                  <p class="form-error">Headcount must be at least 1.</p>
                }
              </div>
            </div>
          </fieldset>

          <!-- ── Description ── -->
          <fieldset class="space-y-2">
            <legend class="text-sm font-semibold text-neutral-800">
              Description
            </legend>
            <app-rich-text-editor
              formControlName="description"
              ariaLabel="Vacancy description"
              placeholder="Describe the role, responsibilities and team…"
            />
            @if (showError('description')) {
              <p class="form-error">Description is required to publish.</p>
            }
          </fieldset>

          <!-- ── Requirements ── -->
          <fieldset class="space-y-2">
            <legend class="text-sm font-semibold text-neutral-800">
              Requirements
            </legend>
            <app-rich-text-editor
              formControlName="qualifications"
              ariaLabel="Qualifications"
              placeholder="Required qualifications, skills and experience…"
            />
          </fieldset>

          <!-- ── Settings ── -->
          <fieldset class="space-y-4">
            <legend class="text-sm font-semibold text-neutral-800">
              Settings
            </legend>
            <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label class="form-label" for="salaryMin">Salary min</label>
                <input
                  id="salaryMin"
                  type="number"
                  min="0"
                  formControlName="salaryMin"
                  class="form-input"
                />
              </div>
              <div>
                <label class="form-label" for="salaryMax">Salary max</label>
                <input
                  id="salaryMax"
                  type="number"
                  min="0"
                  formControlName="salaryMax"
                  class="form-input"
                />
              </div>
              <div>
                <label class="form-label" for="currency">Currency</label>
                <input
                  id="currency"
                  type="text"
                  maxlength="3"
                  formControlName="currency"
                  class="form-input uppercase"
                  placeholder="USD"
                />
              </div>
            </div>
            @if (form.errors?.['salaryRange']) {
              <p class="form-error">
                Salary max must be greater than or equal to salary min.
              </p>
            }
            <div>
              <label class="form-label" for="applicationDeadline">
                Application deadline
              </label>
              <input
                id="applicationDeadline"
                type="date"
                formControlName="applicationDeadline"
                class="form-input"
              />
            </div>
          </fieldset>
        </form>

        <!-- Footer -->
        <footer
          class="sticky bottom-0 flex flex-col-reverse gap-2 border-t border-neutral-200 bg-white px-5 py-4 sm:flex-row sm:justify-end"
        >
          <button
            type="button"
            class="btn-ghost"
            [disabled]="saving()"
            (click)="attemptClose()"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn-secondary"
            [disabled]="saving()"
            (click)="save(false)"
          >
            Save as Draft
          </button>
          <button
            type="button"
            class="btn-primary"
            [disabled]="saving()"
            (click)="save(true)"
          >
            {{ saving() ? 'Saving…' : 'Publish' }}
          </button>
        </footer>
      </section>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .form-label {
        display: block;
        margin-bottom: 0.25rem;
        font-size: 0.8125rem;
        font-weight: 500;
        color: #404040;
      }
      .form-input {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.5rem 0.75rem;
        font-size: 0.875rem;
        color: #171717;
        transition: border-color 150ms ease, box-shadow 150ms ease;
      }
      .form-input:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 3px #e0e7ff;
      }
      .form-error {
        margin-top: 0.25rem;
        font-size: 0.75rem;
        color: #dc2626;
      }
      .btn-primary,
      .btn-secondary,
      .btn-ghost {
        border-radius: 0.5rem;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        transition: background-color 150ms ease, color 150ms ease;
      }
      .btn-primary {
        background: #4f46e5;
        color: #fff;
      }
      .btn-primary:hover:not(:disabled) {
        background: #4338ca;
      }
      .btn-secondary {
        background: #fff;
        color: #404040;
        border: 1px solid #e5e5e5;
      }
      .btn-secondary:hover:not(:disabled) {
        background: #f5f5f5;
      }
      .btn-ghost {
        color: #525252;
      }
      .btn-ghost:hover:not(:disabled) {
        background: #f5f5f5;
      }
      .btn-primary:disabled,
      .btn-secondary:disabled,
      .btn-ghost:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }
    `,
  ],
})
export class VacancyFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly vacancyService = inject(VacancyService);
  private readonly toastr = inject(ToastrService);

  /** When set, the form loads + edits this vacancy; otherwise it creates one. */
  readonly vacancy = input<IVacancy | null>(null);

  /** Emits the saved vacancy so the parent can refresh the list + close. */
  readonly saved = output<IVacancy>();
  /** Emits when the user dismisses the drawer without saving. */
  readonly closed = output<void>();

  readonly employmentTypes: VacancyEmploymentType[] =
    VACANCY_EMPLOYMENT_TYPE_OPTIONS;

  readonly saving = signal(false);
  readonly departments = signal<ILookupOption[]>([]);
  readonly jobTitles = signal<ILookupOption[]>([]);
  readonly locations = signal<ILookupOption[]>([]);
  readonly employees = signal<ILookupOption[]>([]);
  readonly managerSearch = signal('');

  readonly isEdit = computed(() => this.vacancy() != null);

  readonly form = this.fb.group(
    {
      title: ['', [Validators.required, Validators.maxLength(200)]],
      departmentId: [null as string | null],
      jobTitleId: [null as string | null],
      employmentType: [null as VacancyEmploymentType | null],
      locationId: [null as string | null],
      hiringManagerId: [null as string | null],
      headcount: [null as number | null, [Validators.min(1)]],
      salaryMin: [null as number | null, [Validators.min(0)]],
      salaryMax: [null as number | null, [Validators.min(0)]],
      currency: [null as string | null],
      description: [null as string | null],
      qualifications: [null as string | null],
      applicationDeadline: [null as string | null],
    },
    { validators: [salaryRangeValidator] },
  );

  constructor() {
    // Patch the form whenever a vacancy input arrives (edit mode).
    effect(() => {
      const v = this.vacancy();
      if (v) {
        this.form.patchValue({
          title: v.title,
          departmentId: v.departmentId,
          jobTitleId: v.jobTitleId,
          employmentType: v.employmentType,
          locationId: v.locationId,
          hiringManagerId: v.hiringManagerId,
          headcount: v.headcount,
          salaryMin: v.salaryMin,
          salaryMax: v.salaryMax,
          currency: v.currency,
          description: v.description,
          qualifications: v.qualifications,
          applicationDeadline: v.applicationDeadline,
        });
      }
    });
  }

  ngOnInit(): void {
    this.loadLookups();
  }

  // ─── Lookups ─────────────────────────────────────────────

  private loadLookups(): void {
    forkJoin({
      departments: this.vacancyService.getDepartments().pipe(catchError(() => of([]))),
      jobTitles: this.vacancyService.getJobTitles().pipe(catchError(() => of([]))),
      locations: this.vacancyService.getLocations().pipe(catchError(() => of([]))),
      employees: this.vacancyService.searchEmployees().pipe(catchError(() => of([]))),
    }).subscribe((res) => {
      this.departments.set(res.departments);
      this.jobTitles.set(res.jobTitles);
      this.locations.set(res.locations);
      this.employees.set(res.employees);
    });
  }

  onManagerSearch(event: Event): void {
    const term = (event.target as HTMLInputElement).value;
    this.managerSearch.set(term);
    this.vacancyService
      .searchEmployees(term || undefined)
      .pipe(catchError(() => of([])))
      .subscribe((rows) => this.employees.set(rows));
  }

  // ─── Validation display helpers ──────────────────────────

  showError(controlName: string): boolean {
    const control = this.form.get(controlName);
    return !!control && control.invalid && (control.dirty || control.touched);
  }

  titleError(): string {
    const c = this.form.get('title');
    if (c?.hasError('required')) {
      return 'Title is required.';
    }
    if (c?.hasError('maxlength')) {
      return 'Title must be 200 characters or fewer.';
    }
    return '';
  }

  // ─── Save / Publish ──────────────────────────────────────

  save(publish: boolean): void {
    // Title is always required (Draft floor). Publishing additionally requires the
    // BR-2 set; surface those before hitting the network.
    if (this.form.get('title')?.invalid || this.form.errors?.['salaryRange']) {
      this.form.markAllAsTouched();
      this.toastr.error('Please fix the highlighted fields.');
      return;
    }
    if (publish && !this.publishReady()) {
      this.form.markAllAsTouched();
      this.toastr.error(
        'To publish, fill in department, job title, hiring manager, headcount and description.',
      );
      return;
    }

    this.saving.set(true);
    const request = this.toRequest();
    const existing = this.vacancy();
    const save$ = existing
      ? this.vacancyService.updateVacancy(existing.id, request)
      : this.vacancyService.createVacancy(request);

    save$.subscribe({
      next: (created) => {
        if (publish) {
          this.publishAfterSave(created);
        } else {
          this.finish(created, 'Vacancy saved as draft.');
        }
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.toastr.error(VacancyService.parseErrorMessage(err));
      },
    });
  }

  private publishAfterSave(saved: IVacancy): void {
    this.vacancyService.publishVacancy(saved.id).subscribe({
      next: (published) => this.finish(published, 'Vacancy published.'),
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.toastr.error(VacancyService.parseErrorMessage(err));
      },
    });
  }

  private finish(vacancy: IVacancy, message: string): void {
    this.saving.set(false);
    this.toastr.success(message);
    this.saved.emit(vacancy);
  }

  /** BR-2: title, department, job title, hiring manager, headcount, description. */
  private publishReady(): boolean {
    const v = this.form.getRawValue();
    return (
      !!v.title &&
      !!v.departmentId &&
      !!v.jobTitleId &&
      !!v.hiringManagerId &&
      !!v.headcount &&
      v.headcount >= 1 &&
      !!v.description &&
      v.description.trim().length > 0
    );
  }

  private toRequest(): IVacancyRequest {
    const v = this.form.getRawValue();
    return {
      title: (v.title ?? '').trim(),
      departmentId: v.departmentId,
      jobTitleId: v.jobTitleId,
      employmentType: v.employmentType,
      locationId: v.locationId,
      hiringManagerId: v.hiringManagerId,
      headcount: v.headcount,
      salaryMin: v.salaryMin,
      salaryMax: v.salaryMax,
      currency: v.currency ? v.currency.toUpperCase() : null,
      description: v.description,
      qualifications: v.qualifications,
      applicationDeadline: v.applicationDeadline,
    };
  }

  attemptClose(): void {
    if (this.saving()) {
      return;
    }
    this.closed.emit();
  }
}

/** Cross-field validator: salaryMax must be >= salaryMin when both are set. */
export function salaryRangeValidator(
  group: AbstractControl,
): ValidationErrors | null {
  const min = group.get('salaryMin')?.value;
  const max = group.get('salaryMax')?.value;
  if (min != null && max != null && Number(max) < Number(min)) {
    return { salaryRange: true };
  }
  return null;
}
