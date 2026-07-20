import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { CompanySettingsService } from './company-settings.service';
import {
  ICompanySettings,
  IOrgProfile,
  ILocalization,
  IPasswordPolicy,
  ISessionPolicy,
} from '../models/company-settings.models';
import { environment } from '../../../../../environments/environment';

describe('CompanySettingsService', () => {
  let service: CompanySettingsService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/settings`;

  const mockSettings: ICompanySettings = {
    org: {
      name: 'Acme',
      legalName: 'Acme Inc.',
      registrationNumber: 'REG-1',
      address: '1 Main St',
      industry: 'Tech',
      companySize: '11-50',
      fiscalYearStartMonth: 1,
      defaultCountryCode: 'LK',
      probationPeriodDays: 90,
      leaveCancellationWindowDays: 0,
      payslipFooterDisclaimer: null,
      payrollFromEmail: null,
    },
    branding: {
      logoUrl: 'https://cdn/logo.png',
      emailLogoUrl: null,
      faviconUrl: null,
      primaryColor: '#4f46e5',
    },
    localization: {
      defaultLanguage: 'en',
      dateFormat: 'dd MMM yyyy',
      numberFormat: '1,234.56',
      timeZone: 'UTC',
      currency: 'USD',
    },
    passwordPolicy: {
      minLength: 8,
      requireUppercase: true,
      requireLowercase: true,
      requireDigit: true,
      requireSpecialCharacter: false,
      historyCount: 3,
      maxAgeDays: 90,
    },
    sessionPolicy: {
      idleTimeoutMinutes: 30,
      absoluteTimeoutHours: 8,
      maxConcurrentSessions: 3,
    },
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        CompanySettingsService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(CompanySettingsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('GETs the full settings aggregate', () => {
    service.getSettings().subscribe((s) => {
      expect(s.org.name).toBe('Acme');
      expect(s.branding.primaryColor).toBe('#4f46e5');
    });
    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockSettings);
  });

  it('PUTs the org profile sub-object', () => {
    const org: IOrgProfile = mockSettings.org;
    service.updateOrgProfile(org).subscribe();
    const req = httpMock.expectOne(`${baseUrl}/org-profile`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(org);
    expect(req.request.withCredentials).toBeTrue();
    req.flush(null);
  });

  it('PUTs the localization sub-object', () => {
    const loc: ILocalization = mockSettings.localization;
    service.updateLocalization(loc).subscribe();
    const req = httpMock.expectOne(`${baseUrl}/localization`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(loc);
    req.flush(null);
  });

  it('PUTs the password policy sub-object', () => {
    const policy: IPasswordPolicy = mockSettings.passwordPolicy;
    service.updatePasswordPolicy(policy).subscribe();
    const req = httpMock.expectOne(`${baseUrl}/password-policy`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(policy);
    req.flush(null);
  });

  it('PUTs the session policy sub-object', () => {
    const policy: ISessionPolicy = mockSettings.sessionPolicy;
    service.updateSessionPolicy(policy).subscribe();
    const req = httpMock.expectOne(`${baseUrl}/session-policy`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(policy);
    req.flush(null);
  });

  it('PUTs primary-color wrapped in { primaryColor }', () => {
    service.updatePrimaryColor('#112233').subscribe();
    const req = httpMock.expectOne(`${baseUrl}/primary-color`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ primaryColor: '#112233' });
    req.flush(null);
  });

  it('POSTs a multipart branding upload with file + slot', () => {
    const file = new File(['x'], 'logo.png', { type: 'image/png' });
    service.uploadBranding(file, 'logo').subscribe((res) => {
      expect(res.url).toBe('https://cdn/new-logo.png');
    });
    const req = httpMock.expectOne(`${baseUrl}/branding/upload`);
    expect(req.request.method).toBe('POST');
    const body = req.request.body as FormData;
    expect(body instanceof FormData).toBeTrue();
    expect(body.get('slot')).toBe('logo');
    expect(body.get('file') instanceof File).toBeTrue();
    expect((body.get('file') as File).name).toBe('logo.png');
    req.flush({ url: 'https://cdn/new-logo.png' });
  });
});
