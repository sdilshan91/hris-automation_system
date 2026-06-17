import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideToastr } from 'ngx-toastr';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { SecuritySectionComponent } from './security-section.component';
import { IPasswordPolicy, ISessionPolicy } from '../../models/company-settings.models';
import { environment } from '../../../../../../environments/environment';

describe('SecuritySectionComponent', () => {
  let fixture: ComponentFixture<SecuritySectionComponent>;
  let component: SecuritySectionComponent;
  let httpMock: HttpTestingController;

  const pwUrl = `${environment.apiBaseUrl}/tenant/settings/password-policy`;
  const sessUrl = `${environment.apiBaseUrl}/tenant/settings/session-policy`;

  const pwInitial: IPasswordPolicy = {
    minLength: 8,
    requireUppercase: true,
    requireLowercase: true,
    requireDigit: true,
    requireSpecialCharacter: false,
    historyCount: 3,
    maxAgeDays: 90,
  };

  const sessInitial: ISessionPolicy = {
    idleTimeoutMinutes: 30,
    absoluteTimeoutHours: 8,
    maxConcurrentSessions: 3,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SecuritySectionComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideToastr(),
        provideAnimationsAsync(),
        provideTranslateService(),
      ],
    });
    fixture = TestBed.createComponent(SecuritySectionComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('passwordPolicy', pwInitial);
    fixture.componentRef.setInput('sessionPolicy', sessInitial);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('binds the min-length input and complexity toggles from the input policy', () => {
    expect(component.pwForm.get('minLength')?.value).toBe(8);
    expect(component.pwForm.get('requireSpecialCharacter')?.value).toBeFalse();
    expect(component.pwForm.dirty).toBeFalse();
  });

  it('PUTs the password policy with toggled complexity + min length', () => {
    component.pwForm.patchValue({ minLength: 12, requireSpecialCharacter: true });
    component.pwForm.markAsDirty();
    component.onSavePassword();

    const req = httpMock.expectOne(pwUrl);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.minLength).toBe(12);
    expect(req.request.body.requireSpecialCharacter).toBeTrue();
    expect(req.request.body.requireUppercase).toBeTrue();
    req.flush(null);
    expect(component.pwForm.dirty).toBeFalse();
  });

  it('does not POST the password policy when not dirty', () => {
    expect(component.pwForm.dirty).toBeFalse();
    component.onSavePassword();
    httpMock.expectNone(pwUrl);
    expect(component.savingPw()).toBeFalse();
  });

  it('PUTs the session policy independently', () => {
    component.sessForm.patchValue({ maxConcurrentSessions: 5 });
    component.sessForm.markAsDirty();
    component.onSaveSession();

    const req = httpMock.expectOne(sessUrl);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.maxConcurrentSessions).toBe(5);
    expect(req.request.body.idleTimeoutMinutes).toBe(30);
    req.flush(null);
  });
});
