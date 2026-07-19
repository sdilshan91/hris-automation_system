import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideToastr } from 'ngx-toastr';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { OrgProfileSectionComponent } from './org-profile-section.component';
import { IOrgProfile } from '../../models/company-settings.models';
import { environment } from '../../../../../../environments/environment';

describe('OrgProfileSectionComponent', () => {
  let fixture: ComponentFixture<OrgProfileSectionComponent>;
  let component: OrgProfileSectionComponent;
  let httpMock: HttpTestingController;

  const orgUrl = `${environment.apiBaseUrl}/tenant/settings/org-profile`;

  const initial: IOrgProfile = {
    name: 'Acme',
    legalName: 'Acme Inc.',
    registrationNumber: 'REG-1',
    address: '1 Main St',
    industry: 'Tech',
    companySize: '11-50',
    fiscalYearStartMonth: 4,
    defaultCountryCode: 'LK',
    probationPeriodDays: 60,
    payslipFooterDisclaimer: 'Confidential payslip',
    payrollFromEmail: 'payroll@acme.test',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OrgProfileSectionComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideToastr(),
        provideAnimationsAsync(),
        provideTranslateService(),
      ],
    });
    fixture = TestBed.createComponent(OrgProfileSectionComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('value', initial);
    fixture.detectChanges();
  });

  afterEach(() => httpMock?.verify());

  it('patches the form from the input value and stays pristine', () => {
    httpMock = TestBed.inject(HttpTestingController);
    expect(component.form.get('name')?.value).toBe('Acme');
    expect(component.form.get('fiscalYearStartMonth')?.value).toBe('4');
    expect(component.form.dirty).toBeFalse();
  });

  // ISSUE-322 regression guard: all 4 formerly-unmapped fields must LOAD from
  // the GET value so they round-trip instead of resetting to BE defaults.
  it('loads all 4 previously-unmapped fields from the input value', () => {
    httpMock = TestBed.inject(HttpTestingController);
    expect(component.form.get('defaultCountryCode')?.value).toBe('LK');
    expect(component.form.get('probationPeriodDays')?.value).toBe(60);
    expect(component.form.get('payslipFooterDisclaimer')?.value).toBe('Confidential payslip');
    expect(component.form.get('payrollFromEmail')?.value).toBe('payroll@acme.test');
  });

  it('keeps Save disabled until the form is dirty, then enables it', () => {
    httpMock = TestBed.inject(HttpTestingController);
    const button = (): HTMLButtonElement =>
      fixture.nativeElement.querySelector('button[type="submit"]');
    expect(button().disabled).toBeTrue();

    component.form.get('name')?.setValue('Acme Corp');
    component.form.markAsDirty();
    fixture.detectChanges();
    expect(button().disabled).toBeFalse();
  });

  it('does not call the service when not dirty', () => {
    httpMock = TestBed.inject(HttpTestingController);
    expect(component.form.dirty).toBeFalse();
    component.onSave();
    httpMock.expectNone(orgUrl);
    expect(component.saving()).toBeFalse();
  });

  it('PUTs the org-profile body (with numeric fiscal month) on save', () => {
    httpMock = TestBed.inject(HttpTestingController);
    component.form.patchValue({ name: 'Acme Corp', industry: 'Software' });
    component.form.markAsDirty();
    component.onSave();

    const req = httpMock.expectOne(orgUrl);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.name).toBe('Acme Corp');
    expect(req.request.body.industry).toBe('Software');
    expect(req.request.body.fiscalYearStartMonth).toBe(4);
    expect(typeof req.request.body.fiscalYearStartMonth).toBe('number');
    req.flush(null);
    expect(component.form.dirty).toBeFalse();
  });

  // ISSUE-322 regression guard: the full-replace PUT must RESEND all 4 fields —
  // dropping any wipes the stored value to a BE default (data-loss).
  it('sends all 4 previously-unmapped fields in the save payload', () => {
    httpMock = TestBed.inject(HttpTestingController);
    component.form.patchValue({ name: 'Acme Corp' });
    component.form.markAsDirty();
    component.onSave();

    const req = httpMock.expectOne(orgUrl);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.defaultCountryCode).toBe('LK');
    expect(req.request.body.probationPeriodDays).toBe(60);
    expect(typeof req.request.body.probationPeriodDays).toBe('number');
    expect(req.request.body.payslipFooterDisclaimer).toBe('Confidential payslip');
    expect(req.request.body.payrollFromEmail).toBe('payroll@acme.test');
    req.flush(null);
  });
});
