import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  output,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormControl } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { environment } from '../../../../../environments/environment';
import { SsoOnboardingStatus } from '../../../../core/auth/auth.models';

/**
 * Well-formed Entra directory / tenant GUID. Defined locally (rather than imported
 * from SsoSettingsComponent) to avoid a circular module dependency — the settings
 * page imports this wizard.
 */
const GUID_PATTERN =
  /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

function isEntraTenantId(value: string): boolean {
  return GUID_PATTERN.test(value.trim());
}

/** Wizard step index (1-based, matches US-AUTH-016 §8 numbering). */
export type OnboardingStep = 1 | 2 | 3 | 4 | 5;

/**
 * US-AUTH-016 (AC-4/AC-5/AC-6, §8): guided Microsoft admin-consent onboarding wizard.
 *
 * Five steps: (1) grant admin consent → opens the Microsoft admin-consent URL;
 * (2) confirm the captured Entra Directory ID; (3) review the allow-list; (4) an
 * optional test login; (5) enable SSO. On a failed/declined consent return the
 * wizard shows remediation copy and keeps the tenant on its prior login mode
 * (the parent never enables SSO on failure).
 *
 * Presentational/smart-lite: it owns wizard-local UI state (step, entered directory
 * ID) but the authoritative settings + persistence live in the parent
 * SsoSettingsComponent, which reacts to the outputs below. This keeps a single
 * source of truth for the tenant auth settings (no duplicate GET/PUT).
 */
@Component({
  selector: 'app-sso-onboarding-wizard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlide', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <section class="wizard" [@fadeSlide]>
      <header class="wizard-header">
        <div>
          <h3 class="wizard-title">Set up Microsoft sign-in</h3>
          <p class="wizard-subtitle">
            A guided flow to register your organization's Entra directory and turn on
            SSO safely.
          </p>
        </div>
        @if (onboardingStatus() === 'enabled') {
          <span class="status-pill status-ready">SSO enabled</span>
        } @else if (onboardingStatus() === 'consented') {
          <span class="status-pill status-consented">Consent granted</span>
        } @else if (onboardingStatus() === 'consent_pending') {
          <span class="status-pill status-pending">Consent pending</span>
        }
      </header>

      <!-- Consent-return remediation (AC-6) -->
      @if (consentFailed()) {
        <div class="remediation" role="alert" [@fadeSlide]>
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 text-red-500 flex-shrink-0" aria-hidden="true">
            <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
          </svg>
          <div>
            <p class="remediation-title">Admin consent didn't complete</p>
            <p class="remediation-text">
              Microsoft sign-in was not enabled and your workspace stays on its current
              login mode. Ask a Microsoft 365 <strong>Global Administrator</strong> to grant
              consent, then try again.
            </p>
          </div>
        </div>
      }

      <!-- Stepper -->
      <ol class="stepper" aria-label="Onboarding steps">
        @for (s of steps; track s.index) {
          <li
            class="step"
            [class.is-active]="step() === s.index"
            [class.is-done]="step() > s.index"
            [attr.aria-current]="step() === s.index ? 'step' : null"
          >
            <span class="step-dot">{{ s.index }}</span>
            <span class="step-label">{{ s.label }}</span>
          </li>
        }
      </ol>

      <div class="wizard-body">
        <!-- Step 1: grant admin consent -->
        @if (step() === 1) {
          <div class="step-panel" [@fadeSlide]>
            <p class="step-copy">
              Enter your Microsoft Entra <strong>Directory (tenant) ID</strong>, then open
              the one-time admin-consent page. A Microsoft 365 Global Administrator grants
              this app permission for your whole organization.
            </p>
            <label class="label-notion" for="wizardDirId">Entra Directory (tenant) ID</label>
            <input
              id="wizardDirId"
              type="text"
              class="input-notion max-w-md"
              placeholder="00000000-0000-0000-0000-000000000000"
              [formControl]="directoryId"
            />
            @if (directoryId.value && !directoryIdValid()) {
              <p class="field-error">Enter a valid GUID (Entra Directory / tenant ID).</p>
            }
            <div class="consent-url-row">
              <code class="consent-url">{{ adminConsentUrl() }}</code>
            </div>
            <div class="step-actions">
              <button
                type="button"
                class="btn-primary"
                [disabled]="disabled()"
                (click)="grantConsent()"
              >
                Grant admin consent
              </button>
            </div>
          </div>
        }

        <!-- Step 2: confirm captured directory ID -->
        @if (step() === 2) {
          <div class="step-panel" [@fadeSlide]>
            <p class="step-copy">
              Confirm the Entra Directory ID that was consented. It will be added to your
              trusted-directory allow-list so only users from this directory can sign in.
            </p>
            <div class="confirm-box">
              <span class="confirm-label">Directory (tenant) ID</span>
              <code class="confirm-value">{{ directoryId.value || 'Not entered' }}</code>
            </div>
            <div class="step-actions">
              <button type="button" class="btn-secondary" (click)="goto(1)">Back</button>
              <button
                type="button"
                class="btn-primary"
                [disabled]="disabled() || !directoryIdValid()"
                (click)="confirmDirectory()"
              >
                Confirm directory ID
              </button>
            </div>
          </div>
        }

        <!-- Step 3: review allow-list -->
        @if (step() === 3) {
          <div class="step-panel" [@fadeSlide]>
            <p class="step-copy">
              Review the directories trusted to sign in to your workspace.
            </p>
            @if (allowedEntraTenantIds().length) {
              <ul class="allowlist">
                @for (id of allowedEntraTenantIds(); track id) {
                  <li class="allow-item"><code>{{ id }}</code></li>
                }
              </ul>
            } @else {
              <p class="field-hint">No trusted directories yet — go back and confirm one first.</p>
            }
            <div class="step-actions">
              <button type="button" class="btn-secondary" (click)="goto(2)">Back</button>
              <button
                type="button"
                class="btn-primary"
                [disabled]="disabled() || !allowedEntraTenantIds().length"
                (click)="goto(4)"
              >
                Looks good
              </button>
            </div>
          </div>
        }

        <!-- Step 4: optional test login -->
        @if (step() === 4) {
          <div class="step-panel" [@fadeSlide]>
            <p class="step-copy">
              <strong>Optional but recommended:</strong> verify a real Microsoft sign-in
              works before you require it for everyone. This opens the Microsoft sign-in in
              this window.
            </p>
            <div class="step-actions">
              <button type="button" class="btn-secondary" (click)="goto(3)">Back</button>
              <button type="button" class="btn-secondary" (click)="testLogin()">
                Test Microsoft sign-in
              </button>
              <button type="button" class="btn-primary" (click)="goto(5)">
                Skip &amp; continue
              </button>
            </div>
          </div>
        }

        <!-- Step 5: enable SSO -->
        @if (step() === 5) {
          <div class="step-panel" [@fadeSlide]>
            <p class="step-copy">
              You're ready. Enabling SSO lets your allow-listed users sign in with Microsoft.
              Password sign-in stays available unless you separately switch enforcement to
              SSO-only (which always keeps a break-glass admin path).
            </p>
            @if (ssoEnabled()) {
              <p class="field-hint text-emerald-600">SSO is already enabled for this workspace.</p>
            }
            <div class="step-actions">
              <button type="button" class="btn-secondary" (click)="goto(4)">Back</button>
              <button
                type="button"
                class="btn-primary"
                [disabled]="disabled() || ssoEnabled()"
                (click)="finish()"
              >
                Enable Single Sign-On
              </button>
            </div>
          </div>
        }
      </div>
    </section>
  `,
  styles: [
    `
    :host { display: block; }
    .wizard {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-5 space-y-4;
    }
    .wizard-header { @apply flex items-start justify-between gap-3; }
    .wizard-title { @apply text-base font-semibold text-neutral-900; }
    .wizard-subtitle { @apply mt-0.5 text-sm text-neutral-500; }
    .status-pill {
      @apply inline-flex flex-shrink-0 items-center rounded-full px-3 py-1 text-xs font-medium;
    }
    .status-ready { @apply bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200; }
    .status-consented { @apply bg-brand-50 text-brand-700 ring-1 ring-inset ring-brand-200; }
    .status-pending { @apply bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200; }
    .remediation {
      @apply flex items-start gap-3 rounded-lg bg-red-50 border border-red-200 px-4 py-3;
    }
    .remediation-title { @apply text-sm font-medium text-red-700; }
    .remediation-text { @apply text-xs text-red-600 mt-0.5; }
    .stepper {
      @apply flex flex-wrap gap-2 sm:gap-3 list-none m-0 p-0;
    }
    .step { @apply flex items-center gap-2 text-xs text-neutral-400; }
    .step-dot {
      @apply flex h-6 w-6 items-center justify-center rounded-full bg-neutral-100
        text-neutral-500 font-medium;
    }
    .step.is-active .step-dot { @apply bg-brand-600 text-white; }
    .step.is-active .step-label { @apply text-neutral-900 font-medium; }
    .step.is-done .step-dot { @apply bg-brand-100 text-brand-700; }
    .wizard-body { @apply pt-1; }
    .step-panel { @apply space-y-3; }
    .step-copy { @apply text-sm text-neutral-600; }
    .field-hint { @apply text-xs text-neutral-400; }
    .field-error { @apply text-xs text-red-600; }
    .consent-url-row { @apply mt-1; }
    .consent-url {
      @apply block break-all rounded-md bg-neutral-50 border border-neutral-200 px-3 py-2 text-xs text-neutral-600;
    }
    .confirm-box {
      @apply flex flex-col gap-1 rounded-lg bg-neutral-50 border border-neutral-100 p-4;
    }
    .confirm-label { @apply text-xs text-neutral-400; }
    .confirm-value { @apply break-all text-sm text-neutral-800; }
    .allowlist { @apply space-y-1 list-none m-0 p-0; }
    .allow-item { @apply break-all rounded-md bg-neutral-50 px-3 py-1.5 text-xs text-neutral-700; }
    .step-actions { @apply flex flex-wrap gap-2 pt-1; }
    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }
  `,
  ],
})
export class SsoOnboardingWizardComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly toastr = inject(ToastrService);

  /** Vendor multi-tenant app client id (from environment). */
  readonly clientId = input<string>('');
  /** Current trusted-directory allow-list (from the parent settings). */
  readonly allowedEntraTenantIds = input<string[]>([]);
  /** Tenant subdomain — carried on the test-login challenge. */
  readonly subdomain = input<string>('');
  /** Whether SSO is already enabled (drives step-5 state). */
  readonly ssoEnabled = input<boolean>(false);
  /** Onboarding lifecycle status (drives the header pill). */
  readonly onboardingStatus = input<SsoOnboardingStatus>('not_started');
  /** Disable all actions (non-admin / not entitled / saving). */
  readonly disabled = input<boolean>(false);

  /** Emitted when the admin-consent page is opened (parent → status `consent_pending`). */
  readonly consentStarted = output<void>();
  /** Emitted with the confirmed Entra Directory ID (parent adds to the allow-list). */
  readonly directoryConfirmed = output<string>();
  /** Emitted when the admin chooses to enable SSO (parent flips `ssoEnabled` + status). */
  readonly enableSso = output<void>();

  readonly steps: { index: OnboardingStep; label: string }[] = [
    { index: 1, label: 'Grant consent' },
    { index: 2, label: 'Confirm directory' },
    { index: 3, label: 'Review allow-list' },
    { index: 4, label: 'Test login' },
    { index: 5, label: 'Enable SSO' },
  ];

  readonly step = signal<OnboardingStep>(1);
  readonly consentFailed = signal(false);

  readonly directoryId = new FormControl('', { nonNullable: true });
  private readonly directoryIdSignal = signal('');

  readonly directoryIdValid = computed(() =>
    GUID_PATTERN.test(this.directoryIdSignal().trim())
  );

  readonly adminConsentUrl = computed(() => {
    const dir = this.directoryIdSignal().trim() || 'organizations';
    const client = this.clientId() || '{client-id}';
    return `https://login.microsoftonline.com/${dir}/adminconsent?client_id=${client}`;
  });

  ngOnInit(): void {
    this.directoryId.valueChanges.subscribe((v) =>
      this.directoryIdSignal.set(v ?? '')
    );

    // AC-5/AC-6: the admin-consent return can carry a result the wizard reacts to.
    // Success → jump to the confirm step; failure/declined → remediation (AC-6).
    const consent = this.route.snapshot.queryParamMap.get('consent');
    if (consent === 'success') {
      const tid = this.route.snapshot.queryParamMap.get('tid');
      if (tid && isEntraTenantId(tid)) {
        this.directoryId.setValue(tid);
      }
      this.step.set(2);
    } else if (consent === 'failed' || consent === 'declined' || consent === 'error') {
      this.consentFailed.set(true);
      this.step.set(1);
    }
  }

  goto(step: OnboardingStep): void {
    this.step.set(step);
  }

  /** Step 1 (AC-4): open the Microsoft admin-consent URL in a new tab. */
  grantConsent(): void {
    if (this.disabled()) return;
    this.consentFailed.set(false);
    window.open(this.adminConsentUrl(), '_blank', 'noopener');
    this.consentStarted.emit();
    this.toastr.info(
      'Complete admin consent in the Microsoft window, then return here to confirm your Directory ID.'
    );
    this.step.set(2);
  }

  /** Step 2 (AC-5): confirm the captured directory ID → parent captures it into the allow-list. */
  confirmDirectory(): void {
    if (this.disabled() || !this.directoryIdValid()) return;
    this.directoryConfirmed.emit(this.directoryId.value.trim().toLowerCase());
    this.step.set(3);
  }

  /** Step 4: optional test sign-in — reuses the same Microsoft challenge as the login page. */
  testLogin(): void {
    const params = new URLSearchParams({ returnUrl: '/admin/tenant/sso-settings' });
    const sub = this.subdomain();
    if (sub) params.set('tenant', sub);
    window.location.href = `${environment.apiBaseUrl}/auth/sso/challenge?${params.toString()}`;
  }

  /** Step 5 (AC-5): explicitly enable SSO — parent persists ssoEnabled + status `enabled`. */
  finish(): void {
    if (this.disabled() || this.ssoEnabled()) return;
    this.enableSso.emit();
  }
}
