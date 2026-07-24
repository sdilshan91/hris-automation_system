import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import {
  SsoSettingsComponent,
  isEntraTenantId,
  isEmailDomain,
  guidEntryValidator,
  domainEntryValidator,
  PRIVILEGED_ROLE_NAMES,
} from './sso-settings.component';
import { FormControl } from '@angular/forms';
import { AuthService } from '../../../../core/auth/auth.service';
import { RolesService } from '../../../admin/roles/services/roles.service';
import { ITenantAuthSettings } from '../../../../core/auth/auth.models';
import { IRole } from '../../../admin/roles/models/role.models';

const GUID_A = '11111111-1111-1111-1111-111111111111';
const GUID_B = '22222222-2222-2222-2222-222222222222';

function makeRole(name: string): IRole {
  return {
    roleId: name.replace(/\s/g, '-').toLowerCase(),
    tenantId: 't1',
    name,
    description: '',
    isBuiltIn: true,
    permissions: [],
    userCount: 0,
    createdAt: '2026-01-01T00:00:00Z',
  };
}

describe('SsoSettingsComponent', () => {
  let component: SsoSettingsComponent;
  let fixture: ComponentFixture<SsoSettingsComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let rolesServiceSpy: jasmine.SpyObj<RolesService>;

  const entitledSettings: ITenantAuthSettings = {
    mfaPolicy: 'optional',
    mfaRequiredRoles: [],
    // preserved sibling policy fields
    idleTimeoutMinutes: 60,
    maxFailedAttempts: 5,
    // SSO fields
    ssoEnabled: true,
    allowedEntraTenantIds: [GUID_A],
    allowedEmailDomains: ['contoso.com'],
    jitEnabled: true,
    jitDefaultRole: 'Employee',
    enforcementMode: 'optional',
    ssoEntitled: true,
  };

  const roles: IRole[] = [
    makeRole('Employee'),
    makeRole('HR Officer'),
    makeRole('Tenant Admin'),
    makeRole('System Admin'),
  ];

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', [
      'getTenantAuthSettings',
      'updateTenantAuthSettings',
      'hasRole',
    ]);
    rolesServiceSpy = jasmine.createSpyObj('RolesService', ['getRoles']);

    authServiceSpy.getTenantAuthSettings.and.returnValue(of(entitledSettings));
    authServiceSpy.updateTenantAuthSettings.and.returnValue(of(undefined));
    authServiceSpy.hasRole.and.returnValue(true);
    rolesServiceSpy.getRoles.and.returnValue(of(roles));

    await TestBed.configureTestingModule({
      imports: [SsoSettingsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AuthService, useValue: authServiceSpy },
        { provide: RolesService, useValue: rolesServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SsoSettingsComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load settings and populate chips + form on init', () => {
    fixture.detectChanges();
    expect(authServiceSpy.getTenantAuthSettings).toHaveBeenCalled();
    expect(component.isEntitled()).toBeTrue();
    expect(component.entraIds()).toEqual([GUID_A]);
    expect(component.domains()).toEqual(['contoso.com']);
    expect(component.form.value.ssoEnabled).toBeTrue();
    expect(component.form.value.jitEnabled).toBeTrue();
    expect(component.form.value.jitDefaultRole).toBe('Employee');
    expect(component.form.value.enforcementMode).toBe('optional');
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error when loading fails', () => {
    authServiceSpy.getTenantAuthSettings.and.returnValue(
      throwError(() => ({ status: 500, error: { message: 'Server error' } }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Server error');
  });

  // ─── AC-2: entitlement gating ────────────────────────────────

  it('should mark not-entitled when ssoEntitled is false', () => {
    authServiceSpy.getTenantAuthSettings.and.returnValue(
      of({ ...entitledSettings, ssoEntitled: false })
    );
    fixture.detectChanges();
    expect(component.isEntitled()).toBeFalse();
  });

  it('should fail-closed to not-entitled when ssoEntitled is absent', () => {
    const noFlag: ITenantAuthSettings = {
      mfaPolicy: 'off',
      mfaRequiredRoles: [],
    };
    authServiceSpy.getTenantAuthSettings.and.returnValue(of(noFlag));
    fixture.detectChanges();
    expect(component.isEntitled()).toBeFalse();
  });

  it('should render the "Available on higher plans" badge when not entitled', () => {
    authServiceSpy.getTenantAuthSettings.and.returnValue(
      of({ ...entitledSettings, ssoEntitled: false })
    );
    fixture.detectChanges();
    const badge: HTMLElement | null =
      fixture.nativeElement.querySelector('.plan-badge');
    expect(badge?.textContent).toContain('Available on higher plans');
  });

  it('should disable the enable toggle when not entitled', () => {
    authServiceSpy.getTenantAuthSettings.and.returnValue(
      of({ ...entitledSettings, ssoEntitled: false })
    );
    fixture.detectChanges();
    expect(component.enableToggleDisabled()).toBeTrue();
  });

  // ─── AC-3 / FR-4: GUID + domain validation ───────────────────

  it('isEntraTenantId validates GUID format', () => {
    expect(isEntraTenantId(GUID_A)).toBeTrue();
    expect(isEntraTenantId('not-a-guid')).toBeFalse();
    expect(isEntraTenantId('11111111-1111-1111-1111-11111111111')).toBeFalse();
  });

  it('isEmailDomain validates domain format', () => {
    expect(isEmailDomain('contoso.com')).toBeTrue();
    expect(isEmailDomain('sub.contoso.co.uk')).toBeTrue();
    expect(isEmailDomain('nope')).toBeFalse();
    expect(isEmailDomain('-bad.com')).toBeFalse();
    expect(isEmailDomain('bad-.com')).toBeFalse();
  });

  it('guidEntryValidator flags a malformed GUID but passes blank', () => {
    const v = guidEntryValidator();
    expect(v(new FormControl(''))).toBeNull();
    expect(v(new FormControl(GUID_A))).toBeNull();
    expect(v(new FormControl('bad'))).toEqual({ guid: true });
  });

  it('domainEntryValidator flags a malformed domain but passes blank', () => {
    const v = domainEntryValidator();
    expect(v(new FormControl(''))).toBeNull();
    expect(v(new FormControl('contoso.com'))).toBeNull();
    expect(v(new FormControl('bad'))).toEqual({ domain: true });
  });

  it('should add a valid Entra tenant ID and reject an invalid one', () => {
    fixture.detectChanges();
    const evt = new Event('keydown');
    evt.preventDefault = jasmine.createSpy();

    component.newTenantIdControl.setValue(GUID_B);
    component.addTenantId(evt);
    expect(component.entraIds()).toContain(GUID_B);

    component.newTenantIdControl.setValue('not-a-guid');
    component.addTenantId(evt);
    expect(component.entraIds()).not.toContain('not-a-guid');
    expect(component.newTenantIdControl.hasError('guid')).toBeTrue();
  });

  it('should not add a duplicate tenant ID', () => {
    fixture.detectChanges();
    const evt = new Event('keydown');
    evt.preventDefault = jasmine.createSpy();
    const before = component.entraIds().length;
    component.newTenantIdControl.setValue(GUID_A); // already present
    component.addTenantId(evt);
    expect(component.entraIds().length).toBe(before);
  });

  it('should add a valid domain and reject an invalid one', () => {
    fixture.detectChanges();
    const evt = new Event('keydown');
    evt.preventDefault = jasmine.createSpy();

    component.newDomainControl.setValue('fabrikam.com');
    component.addDomain(evt);
    expect(component.domains()).toContain('fabrikam.com');

    component.newDomainControl.setValue('nope');
    component.addDomain(evt);
    expect(component.domains()).not.toContain('nope');
    expect(component.newDomainControl.hasError('domain')).toBeTrue();
  });

  it('should remove tenant IDs and domains', () => {
    fixture.detectChanges();
    component.removeTenantId(GUID_A);
    expect(component.entraIds()).not.toContain(GUID_A);
    component.removeDomain('contoso.com');
    expect(component.domains()).not.toContain('contoso.com');
  });

  // ─── AC-4: empty allow-list blocks enable (fail-closed) ──────

  it('canEnableSso is false only when both lists are empty', () => {
    fixture.detectChanges();
    component.entraIds.set([]);
    component.domains.set([]);
    expect(component.canEnableSso()).toBeFalse();
    component.domains.set(['contoso.com']);
    expect(component.canEnableSso()).toBeTrue();
  });

  it('should disable the enable toggle when the allow-list is empty and SSO is off', () => {
    fixture.detectChanges();
    component.entraIds.set([]);
    component.domains.set([]);
    component.form.patchValue({ ssoEnabled: false });
    expect(component.enableToggleDisabled()).toBeTrue();
    expect(component.enableToggleTooltip()).toContain('at least one');
  });

  it('should allow the enable toggle once the allow-list is non-empty', () => {
    fixture.detectChanges();
    component.form.patchValue({ ssoEnabled: false });
    expect(component.entraIds().length).toBeGreaterThan(0);
    expect(component.enableToggleDisabled()).toBeFalse();
  });

  it('should force SSO off when the last allow-list entry is removed', () => {
    fixture.detectChanges();
    component.domains.set([]); // leave only the single tenant id
    component.form.patchValue({ ssoEnabled: true });
    component.removeTenantId(GUID_A);
    expect(component.canEnableSso()).toBeFalse();
    expect(component.form.get('ssoEnabled')?.value).toBeFalse();
  });

  it('should block save (and not call the API) when enabling with an empty allow-list', () => {
    fixture.detectChanges();
    component.entraIds.set([]);
    component.domains.set([]);
    component.form.patchValue({ ssoEnabled: true });
    component.form.markAsDirty();
    component.onSave();
    expect(authServiceSpy.updateTenantAuthSettings).not.toHaveBeenCalled();
    expect(component.form.get('ssoEnabled')?.value).toBeFalse();
    expect(component.isSaving()).toBeFalse();
  });

  // ─── AC-5 / BR-5: JIT default-role privilege filter ──────────

  it('should exclude privileged roles from the JIT default-role options', () => {
    fixture.detectChanges();
    const names = component.nonPrivilegedRoles().map((r) => r.name);
    expect(names).toContain('Employee');
    expect(names).toContain('HR Officer');
    for (const priv of PRIVILEGED_ROLE_NAMES) {
      expect(names).not.toContain(priv);
    }
  });

  // ─── Save merges SSO fields, preserves siblings, drops entitlement ──

  it('should merge SSO fields onto existing settings on save (siblings preserved)', () => {
    fixture.detectChanges();
    component.form.patchValue({
      jitEnabled: false,
      enforcementMode: 'sso_only',
    });
    component.form.markAsDirty();
    component.onSave();

    expect(authServiceSpy.updateTenantAuthSettings).toHaveBeenCalledTimes(1);
    const arg = authServiceSpy.updateTenantAuthSettings.calls.mostRecent()
      .args[0] as ITenantAuthSettings;
    // SSO fields present
    expect(arg.ssoEnabled).toBeTrue();
    expect(arg.allowedEntraTenantIds).toEqual([GUID_A]);
    expect(arg.allowedEmailDomains).toEqual(['contoso.com']);
    expect(arg.jitEnabled).toBeFalse();
    expect(arg.enforcementMode).toBe('sso_only');
    // Sibling policy fields preserved
    expect(arg.mfaPolicy).toBe('optional');
    expect(arg.idleTimeoutMinutes).toBe(60);
    expect(arg.maxFailedAttempts).toBe(5);
    // Read-only entitlement flag never echoed back
    expect(arg.ssoEntitled).toBeUndefined();
  });

  it('should send null jitDefaultRole when the field is blank', () => {
    fixture.detectChanges();
    component.form.patchValue({ jitDefaultRole: '' });
    component.form.markAsDirty();
    component.onSave();
    const arg = authServiceSpy.updateTenantAuthSettings.calls.mostRecent()
      .args[0] as ITenantAuthSettings;
    expect(arg.jitDefaultRole).toBeNull();
  });

  it('should not save when not entitled', () => {
    authServiceSpy.getTenantAuthSettings.and.returnValue(
      of({ ...entitledSettings, ssoEntitled: false })
    );
    fixture.detectChanges();
    component.form.markAsDirty();
    component.onSave();
    expect(authServiceSpy.updateTenantAuthSettings).not.toHaveBeenCalled();
  });

  it('should handle save errors', () => {
    authServiceSpy.updateTenantAuthSettings.and.returnValue(
      throwError(() => ({ status: 500, error: { message: 'Save failed' } }))
    );
    fixture.detectChanges();
    component.form.markAsDirty();
    component.onSave();
    expect(component.isSaving()).toBeFalse();
  });

  // ─── Readonly (non-admin) ────────────────────────────────────

  it('should be readonly for non-admin users', () => {
    authServiceSpy.hasRole.and.returnValue(false);
    fixture.detectChanges();
    expect(component.isReadonly()).toBeTrue();
    expect(component.enableToggleDisabled()).toBeTrue();
  });

  // ─── Onboarding: admin-consent URL builder (US-AUTH-016) ─────

  it('should build the admin-consent URL from the entered directory ID', () => {
    fixture.detectChanges();
    expect(component.adminConsentUrl()).toContain('/organizations/adminconsent');
    component.consentDirectoryId.setValue(GUID_A);
    expect(component.adminConsentUrl()).toContain(`/${GUID_A}/adminconsent`);
  });
});
