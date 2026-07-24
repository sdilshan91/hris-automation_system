import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideToastr } from 'ngx-toastr';

import { SsoOnboardingWizardComponent } from './sso-onboarding-wizard.component';

const GUID = '33333333-3333-3333-3333-333333333333';

function setup(consent?: string, tid?: string) {
  const params: Record<string, string> = {};
  if (consent) params['consent'] = consent;
  if (tid) params['tid'] = tid;

  TestBed.configureTestingModule({
    imports: [SsoOnboardingWizardComponent],
    providers: [
      provideNoopAnimations(),
      provideToastr(),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { queryParamMap: convertToParamMap(params) } },
      },
    ],
  });

  const fixture: ComponentFixture<SsoOnboardingWizardComponent> =
    TestBed.createComponent(SsoOnboardingWizardComponent);
  fixture.componentRef.setInput('clientId', 'vendor-client-id');
  fixture.componentRef.setInput('allowedEntraTenantIds', [GUID]);
  fixture.componentRef.setInput('subdomain', 'acme');
  return fixture;
}

describe('SsoOnboardingWizardComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('should create and start on step 1', () => {
    const fixture = setup();
    fixture.detectChanges();
    expect(fixture.componentInstance).toBeTruthy();
    expect(fixture.componentInstance.step()).toBe(1);
  });

  it('builds the admin-consent URL from the entered directory id + client id', () => {
    const fixture = setup();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.adminConsentUrl()).toContain('/organizations/adminconsent');
    expect(c.adminConsentUrl()).toContain('client_id=vendor-client-id');
    c.directoryId.setValue(GUID);
    expect(c.adminConsentUrl()).toContain(`/${GUID}/adminconsent`);
  });

  it('AC-4: grantConsent opens the consent URL, emits consentStarted, advances to step 2', () => {
    const fixture = setup();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    const openSpy = spyOn(window, 'open').and.stub();
    const startedSpy = jasmine.createSpy('consentStarted');
    c.consentStarted.subscribe(startedSpy);

    c.grantConsent();

    expect(openSpy).toHaveBeenCalled();
    expect(startedSpy).toHaveBeenCalled();
    expect(c.step()).toBe(2);
  });

  it('AC-5: confirmDirectory emits the confirmed id and advances only when the GUID is valid', () => {
    const fixture = setup();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    const confirmedSpy = jasmine.createSpy('directoryConfirmed');
    c.directoryConfirmed.subscribe(confirmedSpy);

    // Invalid GUID → no emit, stays put.
    c.directoryId.setValue('not-a-guid');
    c.confirmDirectory();
    expect(confirmedSpy).not.toHaveBeenCalled();

    // Valid GUID → emit + advance.
    c.directoryId.setValue(GUID);
    c.confirmDirectory();
    expect(confirmedSpy).toHaveBeenCalledWith(GUID);
    expect(c.step()).toBe(3);
  });

  it('step navigation moves forward and back', () => {
    const fixture = setup();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.goto(4);
    expect(c.step()).toBe(4);
    c.goto(3);
    expect(c.step()).toBe(3);
  });

  it('AC-5: finish emits enableSso when not disabled and SSO not already enabled', () => {
    const fixture = setup();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    const enableSpy = jasmine.createSpy('enableSso');
    c.enableSso.subscribe(enableSpy);
    c.finish();
    expect(enableSpy).toHaveBeenCalled();
  });

  it('does not emit enableSso when already enabled', () => {
    const fixture = setup();
    fixture.componentRef.setInput('ssoEnabled', true);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    const enableSpy = jasmine.createSpy('enableSso');
    c.enableSso.subscribe(enableSpy);
    c.finish();
    expect(enableSpy).not.toHaveBeenCalled();
  });

  it('AC-6: a failed consent return shows remediation and keeps step 1', () => {
    const fixture = setup('failed');
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.consentFailed()).toBeTrue();
    expect(c.step()).toBe(1);
  });

  it('AC-5: a successful consent return with a tid prefills the directory id and jumps to step 2', () => {
    const fixture = setup('success', GUID);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.directoryId.value).toBe(GUID);
    expect(c.step()).toBe(2);
    expect(c.consentFailed()).toBeFalse();
  });
});
