import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  FormControl,
  AbstractControl,
  ValidationErrors,
  ValidatorFn,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  ITenantAuthSettings,
  ITenantUser,
  SsoEnforcementMode,
  SsoOnboardingStatus,
} from '../../../../core/auth/auth.models';
import { RolesService } from '../../../admin/roles/services/roles.service';
import { IRole } from '../../../admin/roles/models/role.models';
import { TenantService } from '../../../../core/tenant/tenant.service';
import { environment } from '../../../../../environments/environment';
import { SsoOnboardingWizardComponent } from '../sso-onboarding-wizard/sso-onboarding-wizard.component';

/**
 * Roles whose members qualify as designated break-glass administrators
 * (US-AUTH-016 BR-2: restricted to admin accounts, not ordinary users).
 */
export const ADMIN_ROLE_NAMES = ['Tenant Admin', 'Tenant Owner'];

/** Well-formed GUID (Entra directory / tenant id, US-AUTH-012 FR-4 / AC-3). */
export const GUID_PATTERN =
  /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/** RFC-ish domain: labels 1-63 chars, no leading/trailing hyphen, valid TLD (US-AUTH-012 FR-4). */
export const DOMAIN_PATTERN =
  /^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))*\.[A-Za-z]{2,}$/;

export function isEntraTenantId(value: string): boolean {
  return GUID_PATTERN.test(value.trim());
}

export function isEmailDomain(value: string): boolean {
  return DOMAIN_PATTERN.test(value.trim().toLowerCase());
}

/** Validator for the "add Entra tenant id" input — non-blocking blank, invalid GUID flagged. */
export function guidEntryValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const v = (control.value ?? '').toString().trim();
    if (!v) return null;
    return isEntraTenantId(v) ? null : { guid: true };
  };
}

/** Validator for the "add email domain" input. */
export function domainEntryValidator(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const v = (control.value ?? '').toString().trim();
    if (!v) return null;
    return isEmailDomain(v) ? null : { domain: true };
  };
}

/**
 * Roles that must never be offered as a JIT default role (US-AUTH-012 BR-5 /
 * AC-5 privilege-escalation guard). The backend independently rejects these.
 */
export const PRIVILEGED_ROLE_NAMES = [
  'System Admin',
  'SystemAdmin',
  'Tenant Owner',
  'Tenant Admin',
];

/**
 * US-AUTH-012: Per-tenant SSO (Microsoft Entra ID) configuration card for
 * Tenant Admin > Security Settings. Reuses the existing GET/PUT tenant
 * auth-settings endpoint (no new endpoint) and merges the SSO fields back onto
 * the full ITenantAuthSettings so sibling MFA/session/lockout policy is preserved
 * — same pattern as SessionPolicySettingsComponent / LockoutPolicySettingsComponent.
 */
@Component({
  selector: 'app-sso-settings',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, SsoOnboardingWizardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlide', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate(
          '300ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
  ],
  template: `
    <div class="settings-container" [@fadeSlide]>
      <div class="settings-card" [class.is-locked]="!isEntitled()">
        <div class="settings-header">
          <div class="header-titles">
            <h2 class="settings-title">Single Sign-On (Microsoft Entra ID)</h2>
            <p class="settings-subtitle">
              Let your employees sign in with Microsoft and restrict access to your
              organization's trusted directories and email domains.
            </p>
          </div>
          @if (!isEntitled()) {
            <span class="plan-badge" title="Upgrade your plan to enable Single Sign-On">
              Available on higher plans
            </span>
          }
        </div>

        @if (isLoading()) {
          <div class="loading-section">
            <div class="spinner"></div>
            <p class="loading-text">Loading SSO settings...</p>
          </div>
        } @else if (loadError()) {
          <div class="error-section">
            <p class="error-text">{{ loadError() }}</p>
            <button class="btn-secondary mt-3" (click)="loadSettings()">Retry</button>
          </div>
        } @else {
          @if (!isEntitled()) {
            <div class="upgrade-note">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 text-brand-500 flex-shrink-0" aria-hidden="true">
                <path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd" />
              </svg>
              <p class="text-sm text-neutral-600">
                Single Sign-On is not included in your current subscription plan.
                Upgrade your plan to enable Microsoft Entra ID sign-in for your organization.
              </p>
            </div>
          }

          <fieldset class="settings-body" [disabled]="!isEntitled() || isReadonly()">
            <form [formGroup]="form" (ngSubmit)="submitSettings()">
              <!-- Enable toggle -->
              <div class="form-section">
                <div class="toggle-row">
                  <div class="toggle-label-block">
                    <label class="label-notion" for="ssoEnabled">Enable Single Sign-On</label>
                    <p class="field-hint">
                      When enabled, users from your trusted directories/domains can sign in
                      with Microsoft. SSO is disabled by default.
                    </p>
                  </div>
                  <label
                    class="toggle-switch"
                    for="ssoEnabled"
                    [attr.title]="enableToggleTooltip()"
                  >
                    <input
                      id="ssoEnabled"
                      type="checkbox"
                      formControlName="ssoEnabled"
                      class="toggle-input"
                      [attr.disabled]="enableToggleDisabled() ? '' : null"
                      [attr.aria-describedby]="!canEnableSso() ? 'sso-enable-hint' : null"
                    />
                    <span class="toggle-slider"></span>
                  </label>
                </div>
                @if (!canEnableSso()) {
                  <p id="sso-enable-hint" class="field-hint text-amber-600">
                    Add at least one trusted directory ID or email domain before enabling SSO.
                  </p>
                }
              </div>

              <!-- Allowed Entra directory (tenant) IDs -->
              <div class="form-section">
                <label class="label-notion">Trusted Entra directory (tenant) IDs</label>
                <p class="field-hint">
                  Only users whose Microsoft directory <code>tid</code> matches one of these
                  GUIDs may enter your workspace.
                </p>

                <div class="chips-container">
                  @for (id of entraIds(); track id) {
                    <div class="chip">
                      <span class="chip-label">{{ id }}</span>
                      <button
                        type="button"
                        class="chip-remove"
                        (click)="removeTenantId(id)"
                        [attr.aria-label]="'Remove ' + id"
                      >
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5">
                          <path d="M5.28 4.22a.75.75 0 0 0-1.06 1.06L6.94 8l-2.72 2.72a.75.75 0 1 0 1.06 1.06L8 9.06l2.72 2.72a.75.75 0 1 0 1.06-1.06L9.06 8l2.72-2.72a.75.75 0 0 0-1.06-1.06L8 6.94 5.28 4.22Z" />
                        </svg>
                      </button>
                    </div>
                  }
                </div>

                <div class="add-row">
                  <input
                    type="text"
                    class="input-notion"
                    placeholder="00000000-0000-0000-0000-000000000000"
                    [formControl]="newTenantIdControl"
                    (keydown.enter)="addTenantId($event)"
                  />
                  <button type="button" class="btn-secondary btn-add" (click)="addTenantId($event)">
                    Add
                  </button>
                </div>
                @if (newTenantIdControl.hasError('guid') && newTenantIdControl.dirty) {
                  <p class="field-error">Enter a valid GUID (Entra directory / tenant ID).</p>
                }
              </div>

              <!-- Allowed email domains -->
              <div class="form-section">
                <label class="label-notion">Allowed email domains</label>
                <p class="field-hint">
                  Users signing in with a verified email at one of these domains are trusted
                  (e.g. <code>contoso.com</code>).
                </p>

                <div class="chips-container">
                  @for (domain of domains(); track domain) {
                    <div class="chip">
                      <span class="chip-label">{{ domain }}</span>
                      <button
                        type="button"
                        class="chip-remove"
                        (click)="removeDomain(domain)"
                        [attr.aria-label]="'Remove ' + domain"
                      >
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5">
                          <path d="M5.28 4.22a.75.75 0 0 0-1.06 1.06L6.94 8l-2.72 2.72a.75.75 0 1 0 1.06 1.06L8 9.06l2.72 2.72a.75.75 0 1 0 1.06-1.06L9.06 8l2.72-2.72a.75.75 0 0 0-1.06-1.06L8 6.94 5.28 4.22Z" />
                        </svg>
                      </button>
                    </div>
                  }
                </div>

                <div class="add-row">
                  <input
                    type="text"
                    class="input-notion"
                    placeholder="contoso.com"
                    [formControl]="newDomainControl"
                    (keydown.enter)="addDomain($event)"
                  />
                  <button type="button" class="btn-secondary btn-add" (click)="addDomain($event)">
                    Add
                  </button>
                </div>
                @if (newDomainControl.hasError('domain') && newDomainControl.dirty) {
                  <p class="field-error">Enter a valid domain (e.g. contoso.com).</p>
                }
              </div>

              <!-- JIT provisioning -->
              <div class="form-section">
                <div class="toggle-row">
                  <div class="toggle-label-block">
                    <label class="label-notion" for="jitEnabled">
                      Just-in-time user provisioning
                    </label>
                    <p class="field-hint">
                      Automatically create a workspace account the first time an allow-listed
                      user signs in with Microsoft.
                    </p>
                  </div>
                  <label class="toggle-switch" for="jitEnabled">
                    <input
                      id="jitEnabled"
                      type="checkbox"
                      formControlName="jitEnabled"
                      class="toggle-input"
                    />
                    <span class="toggle-slider"></span>
                  </label>
                </div>
              </div>

              @if (form.get('jitEnabled')?.value) {
                <div class="form-section" [@fadeSlide]>
                  <label class="label-notion" for="jitDefaultRole">Default role for new users</label>
                  <p class="field-hint">
                    New JIT-provisioned users receive this role. Privileged/admin roles are not
                    selectable.
                  </p>
                  <select
                    id="jitDefaultRole"
                    formControlName="jitDefaultRole"
                    class="input-notion select-input max-w-xs"
                  >
                    <option value="">Select a role...</option>
                    @for (role of nonPrivilegedRoles(); track role.roleId) {
                      <option [value]="role.name">{{ role.name }}</option>
                    }
                  </select>
                </div>
              }

              <!-- Enforcement mode (US-AUTH-016 AC-1/AC-3) -->
              <div class="form-section enforcement-block">
                <label class="label-notion" for="enforcementMode">Enforcement</label>
                <p class="field-hint">
                  Choose whether local password sign-in remains available alongside SSO.
                </p>
                <select
                  id="enforcementMode"
                  formControlName="enforcementMode"
                  class="input-notion select-input max-w-xs"
                >
                  <option value="optional">Optional — allow both SSO and password sign-in</option>
                  <option value="sso_only">SSO only — require Microsoft sign-in</option>
                </select>

                <!-- Reassuring break-glass safety-net copy -->
                <div class="safety-note">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 text-emerald-500 flex-shrink-0" aria-hidden="true">
                    <path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd" />
                  </svg>
                  <p class="text-sm text-emerald-700">
                    Your designated break-glass administrators can always sign in with their
                    password, even under SSO-only — so your organization can never lock itself out.
                  </p>
                </div>

                <!-- Break-glass admin picker (AC-3 / BR-1/BR-2) -->
                <div class="break-glass" [class.is-required]="form.get('enforcementMode')?.value === 'sso_only' && !hasBreakGlass()">
                  <label class="label-notion">Designated break-glass administrators</label>
                  <p class="field-hint">
                    These admins keep local (password) access even when SSO-only is enforced.
                    Select at least one before requiring SSO-only sign-in.
                  </p>
                  @if (breakGlassCandidates().length) {
                    <ul class="bg-list" role="group" aria-label="Break-glass administrators">
                      @for (u of breakGlassCandidates(); track u.userId) {
                        <li class="bg-item">
                          <label class="bg-checkbox">
                            <input
                              type="checkbox"
                              [checked]="isBreakGlassSelected(u.userId)"
                              (change)="toggleBreakGlass(u.userId)"
                            />
                            <span class="bg-name">
                              {{ u.displayName }}
                              <span class="bg-email">{{ u.email }}</span>
                            </span>
                          </label>
                        </li>
                      }
                    </ul>
                  } @else {
                    <p class="field-hint text-amber-600">
                      No eligible administrator accounts found. Add a Tenant Admin with local
                      credentials before enforcing SSO-only.
                    </p>
                  }
                  @if (form.get('enforcementMode')?.value === 'sso_only' && !hasBreakGlass()) {
                    <p class="field-error">
                      Designate at least one break-glass administrator to require SSO-only sign-in.
                    </p>
                  }
                </div>

                @if (form.get('enforcementMode')?.value === 'sso_only') {
                  <div class="warning-box" [@fadeSlide]>
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 text-amber-500 flex-shrink-0" aria-hidden="true">
                      <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
                    </svg>
                    <p class="text-sm text-amber-700">
                      SSO-only enforcement blocks password sign-in for everyone except your
                      designated break-glass admins. You'll be asked to confirm before this
                      takes effect.
                    </p>
                  </div>
                }
              </div>

              <!-- Onboarding: directory ID + admin-consent URL (US-AUTH-016) -->
              <div class="form-section onboarding-block">
                <label class="label-notion" for="consentDirectoryId">
                  Admin consent (Microsoft Entra onboarding)
                </label>
                <p class="field-hint">
                  Enter your Entra Directory (tenant) ID to generate the one-time admin-consent
                  URL. A directory admin opens it once to grant this app permission for your
                  organization.
                </p>
                <input
                  id="consentDirectoryId"
                  type="text"
                  class="input-notion max-w-md"
                  placeholder="Your Entra Directory (tenant) ID"
                  [formControl]="consentDirectoryId"
                />
                <div class="consent-url-row">
                  <code class="consent-url">{{ adminConsentUrl() }}</code>
                  <button
                    type="button"
                    class="btn-secondary btn-copy"
                    (click)="copyConsentUrl()"
                    aria-label="Copy admin consent URL"
                  >
                    Copy
                  </button>
                </div>
              </div>

              <!-- Guided onboarding wizard (US-AUTH-016 AC-4/AC-5/AC-6) -->
              <div class="form-section">
                <app-sso-onboarding-wizard
                  [clientId]="ssoClientId"
                  [allowedEntraTenantIds]="entraIds()"
                  [subdomain]="tenantSubdomain()"
                  [ssoEnabled]="!!form.get('ssoEnabled')?.value"
                  [onboardingStatus]="onboardingStatus()"
                  [disabled]="isReadonly() || !isEntitled() || isSaving()"
                  (consentStarted)="onConsentStarted()"
                  (directoryConfirmed)="onDirectoryConfirmed($event)"
                  (enableSso)="onWizardEnableSso()"
                />
              </div>

              <!-- Save -->
              @if (!isReadonly() && isEntitled()) {
                <div class="form-actions">
                  <button
                    type="submit"
                    class="btn-primary"
                    [disabled]="isSaving() || !form.dirty"
                  >
                    @if (isSaving()) {
                      <span class="btn-spinner"></span>
                      Saving...
                    } @else {
                      Save changes
                    }
                  </button>
                </div>
              } @else if (isReadonly() && isEntitled()) {
                <div class="readonly-notice">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 text-neutral-400" aria-hidden="true">
                    <path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd" />
                  </svg>
                  <span class="text-sm text-neutral-500">
                    Only Tenant Admins can modify SSO settings.
                  </span>
                </div>
              }
            </form>
          </fieldset>
        }
      </div>
    </div>

    <!-- Guarded SSO-only confirmation dialog (US-AUTH-016 AC-1/AC-3, §8) -->
    @if (showEnforceConfirm()) {
      <div class="dialog-backdrop" (click)="cancelEnforcement()">
        <div
          class="dialog-panel"
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="enforce-dialog-title"
          aria-describedby="enforce-dialog-desc"
          (click)="$event.stopPropagation()"
          [@fadeSlide]
        >
          <div class="dialog-icon">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-6 h-6 text-amber-500" aria-hidden="true">
              <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
            </svg>
          </div>
          <h3 id="enforce-dialog-title" class="dialog-title">Require Microsoft sign-in for everyone?</h3>
          <p id="enforce-dialog-desc" class="dialog-text">
            After this change, ordinary users can no longer sign in with an email and password —
            only Microsoft sign-in will be accepted. This can lock users out if your SSO
            configuration is wrong.
          </p>
          <div class="dialog-safety">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 text-emerald-500 flex-shrink-0" aria-hidden="true">
              <path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd" />
            </svg>
            <p class="text-sm text-emerald-700">
              Your {{ breakGlassIds().length }} designated break-glass
              {{ breakGlassIds().length === 1 ? 'administrator keeps' : 'administrators keep' }}
              password access, so you can always get back in.
            </p>
          </div>
          <div class="dialog-actions">
            <button type="button" class="btn-secondary" (click)="cancelEnforcement()">Cancel</button>
            <button type="button" class="btn-danger" (click)="confirmEnforcement()">
              Require SSO-only sign-in
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [
    `
    :host {
      display: block;
    }

    .settings-container {
      @apply mx-auto max-w-2xl px-4 py-6 sm:px-6;
    }

    .settings-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion overflow-hidden;
    }

    .settings-card.is-locked {
      @apply opacity-95;
    }

    .settings-header {
      @apply px-6 py-5 border-b border-neutral-50 flex items-start justify-between gap-3;
    }

    .header-titles {
      @apply min-w-0;
    }

    .settings-title {
      @apply text-lg font-semibold text-neutral-900;
    }

    .settings-subtitle {
      @apply mt-1 text-sm text-neutral-500;
    }

    .plan-badge {
      @apply inline-flex flex-shrink-0 items-center rounded-full bg-amber-50
        px-3 py-1 text-xs font-medium text-amber-700 ring-1 ring-inset ring-amber-200;
    }

    .upgrade-note {
      @apply mx-6 mt-5 flex items-start gap-3 rounded-lg bg-brand-50/60 border border-brand-100 px-4 py-3;
    }

    .loading-section {
      @apply flex flex-col items-center justify-center py-12;
    }

    .spinner {
      @apply w-7 h-7 border-2 border-neutral-200 border-t-brand-600 rounded-full;
      animation: spin 0.7s linear infinite;
    }

    .loading-text {
      @apply mt-3 text-sm text-neutral-500;
    }

    .error-section {
      @apply px-6 py-8 text-center;
    }

    .error-text {
      @apply text-sm text-red-600;
    }

    .settings-body {
      @apply px-6 py-5 space-y-6 border-0 m-0 min-w-0;
    }

    .settings-body[disabled] {
      @apply opacity-60;
    }

    .form-section {
      @apply space-y-2;
    }

    .field-hint {
      @apply text-xs text-neutral-400;
    }

    .field-hint code,
    .field-error code {
      @apply px-1 py-0.5 rounded bg-neutral-100 text-neutral-600 text-[0.7rem];
    }

    .field-error {
      @apply text-xs text-red-600 mt-1;
    }

    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }

    .chips-container {
      @apply flex flex-wrap gap-2;
    }

    .chip {
      @apply inline-flex items-center gap-1 px-3 py-1 rounded-full
        bg-brand-50 text-brand-700 text-sm font-medium break-all;
    }

    .chip-remove {
      @apply p-0.5 rounded-full hover:bg-brand-100 transition-colors
        text-brand-500 hover:text-brand-700 bg-transparent border-none cursor-pointer;
    }

    .add-row {
      @apply mt-1 flex flex-col gap-2 sm:flex-row sm:items-center;
    }

    .add-row .input-notion {
      @apply flex-1 min-w-0;
    }

    .btn-add {
      @apply flex-shrink-0;
    }

    .warning-box {
      @apply mt-2 flex items-start gap-3 rounded-lg bg-amber-50 border border-amber-200 px-4 py-3;
    }

    /* ─── Enforcement + break-glass (US-AUTH-016) ─── */
    .enforcement-block {
      @apply rounded-lg bg-neutral-50/60 border border-neutral-100 p-4 space-y-3;
    }

    .safety-note {
      @apply flex items-start gap-3 rounded-lg bg-emerald-50 border border-emerald-100 px-4 py-3;
    }

    .break-glass {
      @apply rounded-lg bg-white border border-neutral-200 p-4 space-y-2;
    }

    .break-glass.is-required {
      @apply border-red-300 ring-1 ring-red-100;
    }

    .bg-list {
      @apply mt-1 space-y-1 list-none m-0 p-0;
    }

    .bg-item {
      @apply rounded-md hover:bg-neutral-50 transition-colors;
    }

    .bg-checkbox {
      @apply flex items-center gap-3 cursor-pointer px-2 py-1.5;
    }

    .bg-checkbox input {
      @apply h-4 w-4 rounded border-neutral-300 text-brand-600 focus:ring-brand-500;
    }

    .bg-name {
      @apply text-sm text-neutral-800 flex flex-col sm:flex-row sm:items-center sm:gap-2;
    }

    .bg-email {
      @apply text-xs text-neutral-400;
    }

    /* ─── Guarded confirmation dialog ─── */
    .dialog-backdrop {
      @apply fixed inset-0 z-50 flex items-center justify-center bg-neutral-900/40 px-4;
    }

    .dialog-panel {
      @apply w-full max-w-md rounded-xl bg-white p-6 shadow-xl space-y-3;
    }

    .dialog-icon {
      @apply flex h-11 w-11 items-center justify-center rounded-full bg-amber-50;
    }

    .dialog-title {
      @apply text-base font-semibold text-neutral-900;
    }

    .dialog-text {
      @apply text-sm text-neutral-600;
    }

    .dialog-safety {
      @apply flex items-start gap-3 rounded-lg bg-emerald-50 border border-emerald-100 px-4 py-3;
    }

    .dialog-actions {
      @apply flex justify-end gap-2 pt-2;
    }

    .btn-danger {
      @apply inline-flex items-center justify-center rounded-lg bg-red-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200 hover:bg-red-700;
    }

    .onboarding-block {
      @apply rounded-lg bg-neutral-50 border border-neutral-100 p-4;
    }

    .consent-url-row {
      @apply mt-2 flex flex-col gap-2 sm:flex-row sm:items-center;
    }

    .consent-url {
      @apply flex-1 min-w-0 break-all rounded-md bg-white border border-neutral-200
        px-3 py-2 text-xs text-neutral-600;
    }

    .btn-copy {
      @apply flex-shrink-0;
    }

    .form-actions {
      @apply pt-2;
    }

    /* ─── Toggle switch ─────────────────────────── */

    .toggle-row {
      @apply flex items-start justify-between gap-4;
    }

    .toggle-label-block {
      @apply flex-1;
    }

    .toggle-switch {
      @apply relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer
        rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out;
      background-color: theme('colors.neutral.200');
    }

    .toggle-input {
      @apply sr-only;
    }

    .toggle-input:checked + .toggle-slider {
      transform: translateX(1.25rem);
    }

    .toggle-switch:has(.toggle-input:checked) {
      background-color: theme('colors.brand.600');
    }

    .toggle-switch:has(.toggle-input:disabled) {
      @apply cursor-not-allowed opacity-50;
    }

    .toggle-slider {
      @apply pointer-events-none inline-block h-5 w-5 transform rounded-full
        bg-white shadow ring-0 transition duration-200 ease-in-out;
    }

    /* ─── Buttons ───────────────────────────────── */

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

    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    .readonly-notice {
      @apply flex items-center gap-2 rounded-lg bg-neutral-50 border border-neutral-200 px-4 py-3;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
  ],
})
export class SsoSettingsComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly rolesService = inject(RolesService);
  private readonly tenantService = inject(TenantService);
  private readonly fb = inject(FormBuilder);
  private readonly toastr = inject(ToastrService);

  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly isSaving = signal(false);
  readonly isReadonly = signal(false);
  readonly isEntitled = signal(false);

  readonly entraIds = signal<string[]>([]);
  readonly domains = signal<string[]>([]);
  readonly roles = signal<IRole[]>([]);

  // ─── Break-glass + onboarding (US-AUTH-016) ──────────────────
  /** Candidate designated admins (Tenant Admin/Owner accounts) for break-glass. */
  readonly breakGlassCandidates = signal<ITenantUser[]>([]);
  /** Selected designated break-glass admin user IDs. */
  readonly breakGlassIds = signal<string[]>([]);
  /** Admin-consent onboarding lifecycle status (persisted round-trip). */
  readonly onboardingStatus = signal<SsoOnboardingStatus>('not_started');
  /** Whether the guarded SSO-only confirmation dialog is open. */
  readonly showEnforceConfirm = signal(false);

  /**
   * US-AUTH-016 AC-3/BR-1: at least one designated break-glass admin is mandatory
   * before `sso_only` can be enabled, so a tenant can never lock itself out.
   */
  readonly hasBreakGlass = computed(() => this.breakGlassIds().length > 0);

  /** JIT default-role options exclude privileged roles (BR-5 / AC-5). */
  readonly nonPrivilegedRoles = computed(() =>
    this.roles().filter((r) => !PRIVILEGED_ROLE_NAMES.includes(r.name))
  );

  /** AC-4 fail-closed: SSO can only be enabled once the allow-list is non-empty. */
  readonly canEnableSso = computed(
    () => this.entraIds().length > 0 || this.domains().length > 0
  );

  form!: FormGroup;
  readonly newTenantIdControl = new FormControl('', {
    nonNullable: true,
    validators: [guidEntryValidator()],
  });
  readonly newDomainControl = new FormControl('', {
    nonNullable: true,
    validators: [domainEntryValidator()],
  });
  /** Local-only helper; not persisted. Drives the admin-consent URL builder. */
  readonly consentDirectoryId = new FormControl('', { nonNullable: true });
  private readonly consentDirId = signal('');

  /** Full settings reference so MFA/session/lockout fields are merged back on save. */
  private currentSettings: ITenantAuthSettings | null = null;

  /** Vendor multi-tenant app client id, passed to the onboarding wizard. */
  readonly ssoClientId = environment.ssoClientId || '';

  readonly adminConsentUrl = computed(() => {
    const dir = this.consentDirId().trim() || 'organizations';
    const clientId = environment.ssoClientId || '{client-id}';
    return `https://login.microsoftonline.com/${dir}/adminconsent?client_id=${clientId}`;
  });

  /** Resolved tenant subdomain (for the wizard's test-login challenge). */
  tenantSubdomain(): string {
    return this.tenantService.subdomain();
  }

  ngOnInit(): void {
    this.form = this.fb.group({
      ssoEnabled: [{ value: false, disabled: false }],
      jitEnabled: [false],
      jitDefaultRole: [''],
      enforcementMode: ['optional' as SsoEnforcementMode],
    });

    this.isReadonly.set(!this.authService.hasRole('Tenant Admin'));
    this.consentDirectoryId.valueChanges.subscribe((v) =>
      this.consentDirId.set(v ?? '')
    );

    this.loadSettings();
    this.loadRoles();
    this.loadBreakGlassCandidates();
  }

  loadSettings(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.authService.getTenantAuthSettings().subscribe({
      next: (settings: ITenantAuthSettings) => {
        this.currentSettings = settings;
        this.isEntitled.set(settings.ssoEntitled === true);
        this.entraIds.set(settings.allowedEntraTenantIds ?? []);
        this.domains.set(settings.allowedEmailDomains ?? []);
        this.breakGlassIds.set(settings.breakGlassAdminUserIds ?? []);
        this.onboardingStatus.set(settings.ssoOnboardingStatus ?? 'not_started');
        this.form.patchValue({
          ssoEnabled: settings.ssoEnabled ?? false,
          jitEnabled: settings.jitEnabled ?? false,
          jitDefaultRole: settings.jitDefaultRole ?? '',
          enforcementMode: settings.enforcementMode ?? 'optional',
        });
        this.form.markAsPristine();
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(err.error?.message || 'Failed to load SSO settings.');
      },
    });
  }

  private loadRoles(): void {
    // Non-fatal: dropdown is only shown when JIT is enabled; ignore failures silently.
    this.rolesService.getRoles().subscribe({
      next: (roles) => this.roles.set(roles),
      error: () => this.roles.set([]),
    });
  }

  /**
   * Load the pool of designated-admin candidates for the break-glass picker.
   * Non-fatal: the enforcement gate still fails closed with an empty list.
   */
  private loadBreakGlassCandidates(): void {
    this.authService.getTenantUsers().subscribe({
      next: (users) =>
        this.breakGlassCandidates.set(
          users.filter(
            (u) =>
              u.isActive &&
              u.roles.some((r) => ADMIN_ROLE_NAMES.includes(r))
          )
        ),
      error: () => this.breakGlassCandidates.set([]),
    });
  }

  /** Toggle a designated break-glass admin in/out of the selection. */
  toggleBreakGlass(userId: string): void {
    this.breakGlassIds.update((ids) =>
      ids.includes(userId)
        ? ids.filter((x) => x !== userId)
        : [...ids, userId]
    );
    this.form.markAsDirty();
  }

  isBreakGlassSelected(userId: string): boolean {
    return this.breakGlassIds().includes(userId);
  }

  /** Enable toggle is blocked until the allow-list is non-empty (AC-4, fail-closed). */
  enableToggleDisabled(): boolean {
    if (this.isReadonly() || !this.isEntitled()) return true;
    // Allow toggling OFF freely; only block toggling ON with an empty allow-list.
    return !this.form.get('ssoEnabled')?.value && !this.canEnableSso();
  }

  enableToggleTooltip(): string | null {
    if (this.enableToggleDisabled() && !this.canEnableSso()) {
      return 'Add at least one trusted directory ID or email domain before enabling SSO.';
    }
    return null;
  }

  addTenantId(event: Event): void {
    event.preventDefault();
    const raw = (this.newTenantIdControl.value ?? '').trim().toLowerCase();
    if (!raw) return;
    if (!isEntraTenantId(raw)) {
      this.newTenantIdControl.markAsDirty();
      this.newTenantIdControl.updateValueAndValidity();
      return;
    }
    if (!this.entraIds().includes(raw)) {
      this.entraIds.update((list) => [...list, raw]);
      this.form.markAsDirty();
    }
    this.newTenantIdControl.reset('');
  }

  removeTenantId(id: string): void {
    this.entraIds.update((list) => list.filter((x) => x !== id));
    this.enforceFailClosed();
    this.form.markAsDirty();
  }

  addDomain(event: Event): void {
    event.preventDefault();
    const raw = (this.newDomainControl.value ?? '').trim().toLowerCase();
    if (!raw) return;
    if (!isEmailDomain(raw)) {
      this.newDomainControl.markAsDirty();
      this.newDomainControl.updateValueAndValidity();
      return;
    }
    if (!this.domains().includes(raw)) {
      this.domains.update((list) => [...list, raw]);
      this.form.markAsDirty();
    }
    this.newDomainControl.reset('');
  }

  removeDomain(domain: string): void {
    this.domains.update((list) => list.filter((x) => x !== domain));
    this.enforceFailClosed();
    this.form.markAsDirty();
  }

  /** If the allow-list becomes empty, force SSO off so it can never be enabled empty. */
  private enforceFailClosed(): void {
    if (!this.canEnableSso() && this.form.get('ssoEnabled')?.value) {
      this.form.patchValue({ ssoEnabled: false });
    }
  }

  copyConsentUrl(): void {
    const url = this.adminConsentUrl();
    navigator.clipboard?.writeText(url).then(
      () => this.toastr.success('Admin consent URL copied.'),
      () => this.toastr.error('Could not copy the URL.')
    );
  }

  /**
   * Template submit handler (US-AUTH-016 AC-1/AC-3). Applies the enforcement guards
   * BEFORE persisting:
   *  - `sso_only` requires ≥1 designated break-glass admin (AC-3/BR-1) — else blocked;
   *  - enabling `sso_only` opens a guarded confirmation dialog explaining the lockout
   *    risk + the break-glass safety net before the change commits.
   * `onSave()` remains the plain persist path.
   */
  submitSettings(): void {
    if (this.isReadonly() || !this.isEntitled() || this.isSaving()) {
      return;
    }

    const mode = this.form.get('enforcementMode')?.value as SsoEnforcementMode;
    if (mode === 'sso_only') {
      if (!this.hasBreakGlass()) {
        // AC-3: refuse to lock the tenant out; require a designated break-glass admin.
        this.toastr.error(
          'Designate at least one break-glass administrator before requiring SSO-only sign-in.'
        );
        return;
      }
      // Only gate the transition INTO sso_only behind the confirmation dialog.
      const wasSsoOnly = this.currentSettings?.enforcementMode === 'sso_only';
      if (!wasSsoOnly) {
        this.showEnforceConfirm.set(true);
        return;
      }
    }

    this.onSave();
  }

  /** Guarded dialog confirm — proceed with the SSO-only enforcement change. */
  confirmEnforcement(): void {
    this.showEnforceConfirm.set(false);
    this.onSave();
  }

  /** Guarded dialog cancel — abort and keep the prior enforcement mode. */
  cancelEnforcement(): void {
    this.showEnforceConfirm.set(false);
    this.form.patchValue({ enforcementMode: 'optional' });
  }

  // ─── Onboarding wizard handlers (US-AUTH-016 AC-4/AC-5) ──────

  /** AC-4: wizard opened admin-consent → mark onboarding pending and persist. */
  onConsentStarted(): void {
    this.onboardingStatus.set('consent_pending');
    this.form.markAsDirty();
  }

  /** AC-5: wizard confirmed the captured directory id → add to allow-list, mark consented. */
  onDirectoryConfirmed(directoryId: string): void {
    const id = directoryId.trim().toLowerCase();
    if (id && isEntraTenantId(id) && !this.entraIds().includes(id)) {
      this.entraIds.update((list) => [...list, id]);
    }
    this.onboardingStatus.set('consented');
    this.form.markAsDirty();
    this.onSave();
  }

  /** AC-5 final step: enable SSO from the wizard. */
  onWizardEnableSso(): void {
    if (!this.canEnableSso()) {
      this.toastr.error(
        'Add at least one trusted directory or email domain before enabling SSO.'
      );
      return;
    }
    this.form.patchValue({ ssoEnabled: true });
    this.onboardingStatus.set('enabled');
    this.form.markAsDirty();
    this.onSave();
  }

  onSave(): void {
    if (this.isReadonly() || !this.isEntitled() || this.isSaving()) {
      return;
    }

    const ssoEnabled = !!this.form.get('ssoEnabled')?.value;
    // AC-4 fail-closed guard: never submit enabled with an empty allow-list.
    if (ssoEnabled && !this.canEnableSso()) {
      this.form.patchValue({ ssoEnabled: false });
      this.toastr.error(
        'Add at least one trusted directory or email domain before enabling SSO.'
      );
      return;
    }

    this.isSaving.set(true);

    const updated: ITenantAuthSettings = {
      ...(this.currentSettings as ITenantAuthSettings),
      ssoEnabled: !!this.form.get('ssoEnabled')?.value,
      allowedEntraTenantIds: this.entraIds(),
      allowedEmailDomains: this.domains(),
      jitEnabled: !!this.form.get('jitEnabled')?.value,
      jitDefaultRole: this.form.get('jitDefaultRole')?.value || null,
      enforcementMode: this.form.get('enforcementMode')
        ?.value as SsoEnforcementMode,
      breakGlassAdminUserIds: this.breakGlassIds(),
      ssoOnboardingStatus: this.onboardingStatus(),
    };
    // Never echo the read-only entitlement flag back on write.
    delete updated.ssoEntitled;

    this.authService.updateTenantAuthSettings(updated).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.form.markAsPristine();
        this.currentSettings = updated;
        this.toastr.success('SSO settings saved.');
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.toastr.error(
          err.error?.message || 'Failed to save SSO settings. Please try again.'
        );
      },
    });
  }
}
