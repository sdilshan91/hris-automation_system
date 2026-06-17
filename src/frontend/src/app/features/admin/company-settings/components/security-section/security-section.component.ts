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
  IPasswordPolicy,
  ISessionPolicy,
} from '../../models/company-settings.models';

/**
 * US-ADM-006 AC-4 / FR-5 / FR-6: Security & Policies section.
 * Password policy and session policy each save independently (their own dirty
 * gate + Save button). Enforced server-side on future password changes / logins.
 */
@Component({
  selector: 'app-security-section',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cs-stack">
      <!-- ── Password policy ───────────────────────── -->
      <section class="cs-section">
        <header class="cs-section-head">
          <h2 class="cs-section-title">
            {{ 'admin.companySettings.password.title' | translate }}
          </h2>
          <p class="cs-section-sub">
            {{ 'admin.companySettings.password.subtitle' | translate }}
          </p>
        </header>

        <form [formGroup]="pwForm" (ngSubmit)="onSavePassword()" class="cs-form">
          <div class="cs-grid">
            <div class="cs-field">
              <label class="cs-label" for="pw-min">
                {{ 'admin.companySettings.password.minLength' | translate }}
              </label>
              <input
                id="pw-min"
                type="number"
                formControlName="minLength"
                class="cs-input max-w-[8rem]"
                min="6"
                max="64"
              />
              @if (pwForm.get('minLength')?.invalid && pwForm.get('minLength')?.touched) {
                <p class="cs-error">{{ 'admin.companySettings.password.minLengthError' | translate }}</p>
              }
            </div>

            <div class="cs-field">
              <label class="cs-label" for="pw-history">
                {{ 'admin.companySettings.password.historyCount' | translate }}
              </label>
              <input
                id="pw-history"
                type="number"
                formControlName="historyCount"
                class="cs-input max-w-[8rem]"
                min="0"
                max="24"
              />
            </div>

            <div class="cs-field">
              <label class="cs-label" for="pw-age">
                {{ 'admin.companySettings.password.maxAgeDays' | translate }}
              </label>
              <input
                id="pw-age"
                type="number"
                formControlName="maxAgeDays"
                class="cs-input max-w-[8rem]"
                min="0"
                max="365"
              />
              <p class="cs-hint">{{ 'admin.companySettings.password.maxAgeHint' | translate }}</p>
            </div>
          </div>

          <hr class="cs-divider" />

          <div class="space-y-1">
            @for (toggle of complexityToggles; track toggle.key) {
              <div class="cs-toggle-row">
                <div class="cs-toggle-copy">
                  <label class="cs-label" [attr.for]="'pw-' + toggle.key">
                    {{ toggle.label | translate }}
                  </label>
                </div>
                <label class="cs-toggle" [attr.for]="'pw-' + toggle.key">
                  <input
                    [id]="'pw-' + toggle.key"
                    type="checkbox"
                    class="cs-toggle-input"
                    [formControlName]="toggle.key"
                  />
                  <span class="cs-toggle-knob"></span>
                </label>
              </div>
            }
          </div>

          <div class="cs-actions">
            <button
              type="submit"
              class="cs-btn-primary"
              [disabled]="savingPw() || !pwForm.dirty || pwForm.invalid"
            >
              @if (savingPw()) {
                <span class="cs-btn-spinner"></span>
              }
              {{ 'admin.companySettings.save' | translate }}
            </button>
          </div>
        </form>
      </section>

      <!-- ── Session policy ────────────────────────── -->
      <section class="cs-section">
        <header class="cs-section-head">
          <h2 class="cs-section-title">
            {{ 'admin.companySettings.session.title' | translate }}
          </h2>
          <p class="cs-section-sub">
            {{ 'admin.companySettings.session.subtitle' | translate }}
          </p>
        </header>

        <form [formGroup]="sessForm" (ngSubmit)="onSaveSession()" class="cs-form">
          <div class="cs-grid">
            <div class="cs-field">
              <label class="cs-label" for="se-idle">
                {{ 'admin.companySettings.session.idleTimeout' | translate }}
              </label>
              <input
                id="se-idle"
                type="number"
                formControlName="idleTimeoutMinutes"
                class="cs-input max-w-[8rem]"
                min="1"
                max="1440"
              />
            </div>

            <div class="cs-field">
              <label class="cs-label" for="se-abs">
                {{ 'admin.companySettings.session.absoluteTimeout' | translate }}
              </label>
              <input
                id="se-abs"
                type="number"
                formControlName="absoluteTimeoutHours"
                class="cs-input max-w-[8rem]"
                min="1"
                max="168"
              />
            </div>

            <div class="cs-field">
              <label class="cs-label" for="se-max">
                {{ 'admin.companySettings.session.maxConcurrent' | translate }}
              </label>
              <input
                id="se-max"
                type="number"
                formControlName="maxConcurrentSessions"
                class="cs-input max-w-[8rem]"
                min="1"
                max="20"
              />
            </div>
          </div>

          <div class="cs-actions">
            <button
              type="submit"
              class="cs-btn-primary"
              [disabled]="savingSess() || !sessForm.dirty || sessForm.invalid"
            >
              @if (savingSess()) {
                <span class="cs-btn-spinner"></span>
              }
              {{ 'admin.companySettings.save' | translate }}
            </button>
          </div>
        </form>
      </section>
    </div>
  `,
  styleUrls: ['../../company-settings.styles.css'],
  styles: [`.cs-stack { @apply space-y-5; }`],
})
export class SecuritySectionComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(CompanySettingsService);
  private readonly toastr = inject(ToastrService);

  readonly passwordPolicy = input<IPasswordPolicy | null>(null);
  readonly sessionPolicy = input<ISessionPolicy | null>(null);

  readonly savingPw = signal(false);
  readonly savingSess = signal(false);

  readonly complexityToggles: { key: string; label: string }[] = [
    { key: 'requireUppercase', label: 'admin.companySettings.password.requireUppercase' },
    { key: 'requireLowercase', label: 'admin.companySettings.password.requireLowercase' },
    { key: 'requireDigit', label: 'admin.companySettings.password.requireDigit' },
    { key: 'requireSpecialCharacter', label: 'admin.companySettings.password.requireSpecial' },
  ];

  readonly pwForm: FormGroup = this.fb.group({
    minLength: [8, [Validators.required, Validators.min(6), Validators.max(64)]],
    requireUppercase: [true],
    requireLowercase: [true],
    requireDigit: [true],
    requireSpecialCharacter: [false],
    historyCount: [3, [Validators.required, Validators.min(0), Validators.max(24)]],
    maxAgeDays: [90, [Validators.required, Validators.min(0), Validators.max(365)]],
  });

  readonly sessForm: FormGroup = this.fb.group({
    idleTimeoutMinutes: [30, [Validators.required, Validators.min(1), Validators.max(1440)]],
    absoluteTimeoutHours: [8, [Validators.required, Validators.min(1), Validators.max(168)]],
    maxConcurrentSessions: [3, [Validators.required, Validators.min(1), Validators.max(20)]],
  });

  constructor() {
    effect(() => {
      const p = this.passwordPolicy();
      if (p) {
        this.pwForm.reset({
          minLength: p.minLength ?? 8,
          requireUppercase: p.requireUppercase ?? true,
          requireLowercase: p.requireLowercase ?? true,
          requireDigit: p.requireDigit ?? true,
          requireSpecialCharacter: p.requireSpecialCharacter ?? false,
          historyCount: p.historyCount ?? 3,
          maxAgeDays: p.maxAgeDays ?? 90,
        });
        this.pwForm.markAsPristine();
      }
    });

    effect(() => {
      const s = this.sessionPolicy();
      if (s) {
        this.sessForm.reset({
          idleTimeoutMinutes: s.idleTimeoutMinutes ?? 30,
          absoluteTimeoutHours: s.absoluteTimeoutHours ?? 8,
          maxConcurrentSessions: s.maxConcurrentSessions ?? 3,
        });
        this.sessForm.markAsPristine();
      }
    });
  }

  onSavePassword(): void {
    if (this.savingPw() || this.pwForm.invalid || !this.pwForm.dirty) {
      return;
    }
    this.savingPw.set(true);
    const raw = this.pwForm.getRawValue();
    const body: IPasswordPolicy = {
      minLength: Number(raw.minLength),
      requireUppercase: !!raw.requireUppercase,
      requireLowercase: !!raw.requireLowercase,
      requireDigit: !!raw.requireDigit,
      requireSpecialCharacter: !!raw.requireSpecialCharacter,
      historyCount: Number(raw.historyCount),
      maxAgeDays: Number(raw.maxAgeDays),
    };
    this.service.updatePasswordPolicy(body).subscribe({
      next: () => {
        this.savingPw.set(false);
        this.pwForm.markAsPristine();
        this.toastr.success('Password policy saved.');
      },
      error: (err: HttpErrorResponse) => {
        this.savingPw.set(false);
        this.toastr.error(err.error?.message || 'Failed to save password policy.');
      },
    });
  }

  onSaveSession(): void {
    if (this.savingSess() || this.sessForm.invalid || !this.sessForm.dirty) {
      return;
    }
    this.savingSess.set(true);
    const raw = this.sessForm.getRawValue();
    const body: ISessionPolicy = {
      idleTimeoutMinutes: Number(raw.idleTimeoutMinutes),
      absoluteTimeoutHours: Number(raw.absoluteTimeoutHours),
      maxConcurrentSessions: Number(raw.maxConcurrentSessions),
    };
    this.service.updateSessionPolicy(body).subscribe({
      next: () => {
        this.savingSess.set(false);
        this.sessForm.markAsPristine();
        this.toastr.success('Session policy saved.');
      },
      error: (err: HttpErrorResponse) => {
        this.savingSess.set(false);
        this.toastr.error(err.error?.message || 'Failed to save session policy.');
      },
    });
  }
}
