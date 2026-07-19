import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateModule } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { CompanySettingsService } from '../../services/company-settings.service';
import {
  IOrgProfile,
  FISCAL_MONTHS,
  COMPANY_SIZES,
} from '../../models/company-settings.models';

/**
 * US-ADM-006 AC-1: Organization Profile section.
 * Dirty-form tracking gates the Save button; toast on save (changes audited
 * server-side per NFR-4).
 */
@Component({
  selector: 'app-org-profile-section',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="cs-section">
      <header class="cs-section-head">
        <h2 class="cs-section-title">{{ 'admin.companySettings.org.title' | translate }}</h2>
        <p class="cs-section-sub">{{ 'admin.companySettings.org.subtitle' | translate }}</p>
      </header>

      <form [formGroup]="form" (ngSubmit)="onSave()" class="cs-form">
        <div class="cs-grid">
          <div class="cs-field">
            <label class="cs-label" for="org-name">
              {{ 'admin.companySettings.org.name' | translate }}
            </label>
            <input id="org-name" type="text" formControlName="name" class="cs-input" />
            @if (form.get('name')?.invalid && form.get('name')?.touched) {
              <p class="cs-error">{{ 'admin.companySettings.org.nameRequired' | translate }}</p>
            }
          </div>

          <div class="cs-field">
            <label class="cs-label" for="org-legal">
              {{ 'admin.companySettings.org.legalName' | translate }}
            </label>
            <input id="org-legal" type="text" formControlName="legalName" class="cs-input" />
          </div>

          <div class="cs-field">
            <label class="cs-label" for="org-reg">
              {{ 'admin.companySettings.org.registrationNumber' | translate }}
            </label>
            <input id="org-reg" type="text" formControlName="registrationNumber" class="cs-input" />
          </div>

          <div class="cs-field">
            <label class="cs-label" for="org-industry">
              {{ 'admin.companySettings.org.industry' | translate }}
            </label>
            <input id="org-industry" type="text" formControlName="industry" class="cs-input" />
          </div>

          <div class="cs-field">
            <label class="cs-label" for="org-size">
              {{ 'admin.companySettings.org.companySize' | translate }}
            </label>
            <select id="org-size" formControlName="companySize" class="cs-input">
              <option value="">—</option>
              @for (opt of companySizes; track opt.value) {
                <option [value]="opt.value">{{ opt.label }}</option>
              }
            </select>
          </div>

          <div class="cs-field">
            <label class="cs-label" for="org-fiscal">
              {{ 'admin.companySettings.org.fiscalYearStart' | translate }}
            </label>
            <select id="org-fiscal" formControlName="fiscalYearStartMonth" class="cs-input">
              @for (m of fiscalMonths; track m.value) {
                <option [value]="m.value">{{ m.label }}</option>
              }
            </select>
            <p class="cs-hint">{{ 'admin.companySettings.org.fiscalHint' | translate }}</p>
          </div>

          <div class="cs-field">
            <label class="cs-label" for="org-probation">Probation Period (days)</label>
            <input
              id="org-probation"
              type="number"
              min="0"
              formControlName="probationPeriodDays"
              class="cs-input"
            />
            @if (form.get('probationPeriodDays')?.invalid && form.get('probationPeriodDays')?.touched) {
              <p class="cs-error">Probation period cannot be negative.</p>
            }
          </div>

          <div class="cs-field">
            <label class="cs-label" for="org-country">Default Country Code</label>
            <input
              id="org-country"
              type="text"
              maxlength="2"
              placeholder="e.g. LK"
              formControlName="defaultCountryCode"
              class="cs-input"
            />
            <p class="cs-hint">2-letter ISO code; stored upper-cased.</p>
          </div>

          <div class="cs-field">
            <label class="cs-label" for="org-payroll-email">Payroll Sender Email</label>
            <input
              id="org-payroll-email"
              type="email"
              formControlName="payrollFromEmail"
              class="cs-input"
            />
            @if (form.get('payrollFromEmail')?.invalid && form.get('payrollFromEmail')?.touched) {
              <p class="cs-error">Enter a valid email address.</p>
            }
          </div>
        </div>

        <div class="cs-field cs-span">
          <label class="cs-label" for="org-address">
            {{ 'admin.companySettings.org.address' | translate }}
          </label>
          <textarea id="org-address" rows="3" formControlName="address" class="cs-input"></textarea>
        </div>

        <div class="cs-field cs-span">
          <label class="cs-label" for="org-disclaimer">Payslip Footer Disclaimer</label>
          <textarea
            id="org-disclaimer"
            rows="3"
            formControlName="payslipFooterDisclaimer"
            class="cs-input"
          ></textarea>
          <p class="cs-hint">Shown at the foot of every payslip; leave blank to clear.</p>
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
export class OrgProfileSectionComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(CompanySettingsService);
  private readonly toastr = inject(ToastrService);

  /** Initial value pushed by the container once settings load. */
  readonly value = input<IOrgProfile | null>(null);

  readonly fiscalMonths = FISCAL_MONTHS;
  readonly companySizes = COMPANY_SIZES;
  readonly saving = signal(false);

  readonly form: FormGroup = this.fb.group({
    name: ['', [Validators.required]],
    legalName: [''],
    registrationNumber: [''],
    industry: [''],
    companySize: [''],
    fiscalYearStartMonth: ['1'],
    address: [''],
    // ISSUE-322: these 4 were previously unmapped + being wiped on every save.
    probationPeriodDays: [90, [Validators.min(0)]],
    defaultCountryCode: [''],
    payslipFooterDisclaimer: [''],
    payrollFromEmail: ['', [Validators.email]],
  });

  constructor() {
    // Patch the form once loaded settings arrive, then mark pristine so Save
    // stays disabled until the admin actually edits something.
    effect(() => {
      const v = this.value();
      if (v) {
        this.form.reset({
          name: v.name ?? '',
          legalName: v.legalName ?? '',
          registrationNumber: v.registrationNumber ?? '',
          industry: v.industry ?? '',
          companySize: v.companySize ?? '',
          fiscalYearStartMonth: String(v.fiscalYearStartMonth ?? 1),
          address: v.address ?? '',
          probationPeriodDays: v.probationPeriodDays ?? 90,
          defaultCountryCode: v.defaultCountryCode ?? '',
          payslipFooterDisclaimer: v.payslipFooterDisclaimer ?? '',
          payrollFromEmail: v.payrollFromEmail ?? '',
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
    const raw = this.form.getRawValue();
    const body: IOrgProfile = {
      name: raw.name,
      legalName: raw.legalName,
      registrationNumber: raw.registrationNumber,
      industry: raw.industry,
      companySize: raw.companySize,
      fiscalYearStartMonth: Number(raw.fiscalYearStartMonth),
      address: raw.address,
      // ISSUE-322: full-replace PUT — resend these 4 so they are preserved,
      // never silently reset to BE defaults. Blank clears the nullable ones.
      probationPeriodDays: Number(raw.probationPeriodDays),
      defaultCountryCode: raw.defaultCountryCode?.trim() || null,
      payslipFooterDisclaimer: raw.payslipFooterDisclaimer?.trim() || null,
      payrollFromEmail: raw.payrollFromEmail?.trim() || null,
    };
    this.service.updateOrgProfile(body).subscribe({
      next: () => {
        this.saving.set(false);
        this.form.markAsPristine();
        this.toastr.success('Organization profile saved.');
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.toastr.error(err.error?.message || 'Failed to save organization profile.');
      },
    });
  }
}
