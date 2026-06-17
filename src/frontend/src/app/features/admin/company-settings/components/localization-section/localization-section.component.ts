import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  computed,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateModule } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { CompanySettingsService } from '../../services/company-settings.service';
import {
  ILocalization,
  LANGUAGES,
  DATE_FORMATS,
  NUMBER_FORMATS,
  CURRENCIES,
  TIME_ZONES,
  formatDatePreview,
} from '../../models/company-settings.models';

/**
 * US-ADM-006 AC-3: Localization section. Live-previews the chosen date / number
 * / currency format from the form value.
 */
@Component({
  selector: 'app-localization-section',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="cs-section">
      <header class="cs-section-head">
        <h2 class="cs-section-title">{{ 'admin.companySettings.localization.title' | translate }}</h2>
        <p class="cs-section-sub">{{ 'admin.companySettings.localization.subtitle' | translate }}</p>
      </header>

      <form [formGroup]="form" (ngSubmit)="onSave()" class="cs-form">
        <div class="cs-grid">
          <div class="cs-field">
            <label class="cs-label" for="loc-lang">
              {{ 'admin.companySettings.localization.language' | translate }}
            </label>
            <select id="loc-lang" formControlName="defaultLanguage" class="cs-input">
              @for (o of languages; track o.value) {
                <option [value]="o.value">{{ o.label }}</option>
              }
            </select>
          </div>

          <div class="cs-field">
            <label class="cs-label" for="loc-tz">
              {{ 'admin.companySettings.localization.timeZone' | translate }}
            </label>
            <select id="loc-tz" formControlName="timeZone" class="cs-input">
              @for (o of timeZones; track o.value) {
                <option [value]="o.value">{{ o.label }}</option>
              }
            </select>
          </div>

          <div class="cs-field">
            <label class="cs-label" for="loc-date">
              {{ 'admin.companySettings.localization.dateFormat' | translate }}
            </label>
            <select id="loc-date" formControlName="dateFormat" class="cs-input">
              @for (o of dateFormats; track o.value) {
                <option [value]="o.value">{{ o.label }}</option>
              }
            </select>
            <p class="cs-hint" data-test="date-preview">
              {{ 'admin.companySettings.localization.example' | translate }}: {{ datePreview() }}
            </p>
          </div>

          <div class="cs-field">
            <label class="cs-label" for="loc-num">
              {{ 'admin.companySettings.localization.numberFormat' | translate }}
            </label>
            <select id="loc-num" formControlName="numberFormat" class="cs-input">
              @for (o of numberFormats; track o.value) {
                <option [value]="o.value">{{ o.label }}</option>
              }
            </select>
            <p class="cs-hint" data-test="number-preview">
              {{ 'admin.companySettings.localization.example' | translate }}: {{ numberPreview() }}
            </p>
          </div>

          <div class="cs-field">
            <label class="cs-label" for="loc-cur">
              {{ 'admin.companySettings.localization.currency' | translate }}
            </label>
            <select id="loc-cur" formControlName="currency" class="cs-input">
              @for (o of currencies; track o.value) {
                <option [value]="o.value">{{ o.label }}</option>
              }
            </select>
            <p class="cs-hint" data-test="currency-preview">
              {{ 'admin.companySettings.localization.example' | translate }}: {{ currencyPreview() }}
            </p>
          </div>
        </div>

        <div class="cs-actions">
          <button
            type="submit"
            class="cs-btn-primary"
            [disabled]="saving() || !form.dirty || form.invalid"
          >
            @if (saving()) {
              <span class="cs-btn-spinner"></span>
            }
            {{ 'admin.companySettings.save' | translate }}
          </button>
        </div>
      </form>
    </section>
  `,
  styleUrls: ['../../company-settings.styles.css'],
})
export class LocalizationSectionComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(CompanySettingsService);
  private readonly toastr = inject(ToastrService);

  readonly value = input<ILocalization | null>(null);

  readonly languages = LANGUAGES;
  readonly dateFormats = DATE_FORMATS;
  readonly numberFormats = NUMBER_FORMATS;
  readonly currencies = CURRENCIES;
  readonly timeZones = TIME_ZONES;
  readonly saving = signal(false);

  readonly form: FormGroup = this.fb.group({
    defaultLanguage: ['en', [Validators.required]],
    dateFormat: ['dd MMM yyyy', [Validators.required]],
    numberFormat: ['1,234.56', [Validators.required]],
    currency: ['USD', [Validators.required]],
    timeZone: ['UTC', [Validators.required]],
  });

  /** Live form value as a signal so previews recompute on every change. */
  private readonly liveValue = toSignal(this.form.valueChanges, {
    initialValue: this.form.value,
  });

  /** AC-3: date-format example preview reflects the selected token. */
  readonly datePreview = computed(() =>
    formatDatePreview(this.liveValue()?.dateFormat ?? 'dd MMM yyyy')
  );

  readonly numberPreview = computed(() => this.liveValue()?.numberFormat ?? '1,234.56');

  readonly currencyPreview = computed(() => {
    const cur = this.liveValue()?.currency ?? 'USD';
    const num = this.liveValue()?.numberFormat ?? '1,234.56';
    return `${cur} ${num}`;
  });

  constructor() {
    effect(() => {
      const v = this.value();
      if (v) {
        this.form.reset({
          defaultLanguage: v.defaultLanguage ?? 'en',
          dateFormat: v.dateFormat ?? 'dd MMM yyyy',
          numberFormat: v.numberFormat ?? '1,234.56',
          currency: v.currency ?? 'USD',
          timeZone: v.timeZone ?? 'UTC',
        });
        this.form.markAsPristine();
      }
    });
  }

  onSave(): void {
    if (this.saving() || this.form.invalid || !this.form.dirty) {
      return;
    }
    this.saving.set(true);
    const body = this.form.getRawValue() as ILocalization;
    this.service.updateLocalization(body).subscribe({
      next: () => {
        this.saving.set(false);
        this.form.markAsPristine();
        this.toastr.success('Localization settings saved.');
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.toastr.error(err.error?.message || 'Failed to save localization settings.');
      },
    });
  }
}
