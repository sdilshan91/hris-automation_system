import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  HttpErrorResponse,
  HttpEventType,
} from '@angular/common/http';

import { CareersService, IUploadEvent } from './careers.service';
import { environment } from '../../../../environments/environment';
import {
  IApplicant,
  IPublicVacancy,
  IApplicationRequest,
  RESUME_MAX_BYTES,
} from '../models/applicant.models';

describe('CareersService', () => {
  let service: CareersService;
  let httpMock: HttpTestingController;
  const careersBase = `${environment.apiBaseUrl}/careers/vacancies`;
  const recruitmentBase = `${environment.apiBaseUrl}/recruitment/vacancies`;

  const mockVacancy: IPublicVacancy = {
    id: 'vac-1',
    slug: 'vac-slug',
    referenceNumber: 'VAC-2026-0001',
    title: 'Senior Engineer',
    departmentName: 'Engineering',
    employmentType: 'FullTime',
    locationName: 'HQ',
    description: '<p>Role</p>',
    qualifications: '<p>Skills</p>',
    salaryMin: 100,
    salaryMax: 200,
    salaryCurrency: 'USD',
    applicationDeadline: '2026-12-31',
    publishedAt: '2026-06-01T00:00:00Z',
  };

  const mockApplicant: IApplicant = {
    id: 'app-1',
    applicationReferenceNumber: 'APP-2026-0001',
    vacancyId: 'vac-1',
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    phone: null,
    coverLetter: null,
    resumeFileName: 'resume.pdf',
    stage: 'Applied',
    source: 'Public',
    isInternal: false,
    appliedAt: '2026-06-15T00:00:00Z',
  };

  const request: IApplicationRequest = {
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    phone: null,
    coverLetter: null,
  };

  const pdf = (name = 'resume.pdf', size = 1000) => {
    const file = new File(['x'], name, { type: 'application/pdf' });
    Object.defineProperty(file, 'size', { value: size });
    return file;
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        CareersService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(CareersService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── Public reads ──────────────────────────────────────────

  describe('listOpenVacancies', () => {
    it('GETs the careers base and returns a bare array', () => {
      service.listOpenVacancies().subscribe((rows) => {
        expect(rows.length).toBe(1);
        expect(rows[0].id).toBe('vac-1');
      });
      const req = httpMock.expectOne((r) => r.url === careersBase);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeFalsy();
      req.flush([mockVacancy]);
    });

    it('normalizes a { data } envelope into a bare array', () => {
      service.listOpenVacancies().subscribe((rows) => {
        expect(rows.length).toBe(1);
      });
      const req = httpMock.expectOne((r) => r.url === careersBase);
      req.flush({ data: [mockVacancy] });
    });

    it('passes search + filter query params', () => {
      service
        .listOpenVacancies({
          search: 'eng',
          departmentName: 'Engineering',
          locationName: 'HQ',
          employmentType: 'FullTime',
        })
        .subscribe();
      const req = httpMock.expectOne(
        (r) => r.url === careersBase && r.params.get('search') === 'eng',
      );
      expect(req.request.params.get('departmentName')).toBe('Engineering');
      expect(req.request.params.get('locationName')).toBe('HQ');
      expect(req.request.params.get('employmentType')).toBe('FullTime');
      req.flush([]);
    });
  });

  it('getPublicVacancy GETs /:id anonymously', () => {
    service.getPublicVacancy('vac-1').subscribe((v) => expect(v.id).toBe('vac-1'));
    const req = httpMock.expectOne(`${careersBase}/vac-1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeFalsy();
    req.flush(mockVacancy);
  });

  // ─── Apply (multipart + progress) ──────────────────────────

  describe('applyPublic', () => {
    it('POSTs multipart to the public apply endpoint and streams progress then done', () => {
      const events: IUploadEvent[] = [];
      service.applyPublic('vac-1', request, pdf()).subscribe((e) => events.push(e));

      const req = httpMock.expectOne(`${careersBase}/vac-1/apply`);
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBeFalsy();
      const body = req.request.body as FormData;
      expect(body instanceof FormData).toBeTrue();
      expect(body.get('firstName')).toBe('Ada');
      expect(body.get('email')).toBe('ada@example.com');
      expect(body.get('resume')).toBeTruthy();

      // Simulate an upload-progress event then the final response.
      req.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 100 });
      req.flush(mockApplicant);

      expect(events.some((e) => e.type === 'progress' && e.progress === 50)).toBeTrue();
      const done = events.find((e) => e.type === 'done');
      expect(done).toBeTruthy();
      expect(done && (done as { applicant: IApplicant }).applicant.applicationReferenceNumber).toBe(
        'APP-2026-0001',
      );
    });

    it('surfaces a duplicate (409) on the error channel', () => {
      let err: HttpErrorResponse | undefined;
      service.applyPublic('vac-1', request, pdf()).subscribe({
        error: (e: HttpErrorResponse) => (err = e),
      });
      const req = httpMock.expectOne(`${careersBase}/vac-1/apply`);
      req.flush({ message: 'Already applied', code: 'duplicate' }, { status: 409, statusText: 'Conflict' });
      expect(err).toBeDefined();
      expect(CareersService.isDuplicate(err!)).toBeTrue();
    });
  });

  describe('applyInternal', () => {
    it('POSTs multipart with credentials to the recruitment applicants endpoint', () => {
      service.applyInternal('vac-1', request, pdf()).subscribe();
      const req = httpMock.expectOne(`${recruitmentBase}/vac-1/applicants`);
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.body instanceof FormData).toBeTrue();
      req.flush({ ...mockApplicant, isInternal: true, source: 'Internal' });
    });
  });

  // ─── buildFormData ─────────────────────────────────────────

  describe('buildFormData', () => {
    it('appends optional fields only when present', () => {
      const withOptional = CareersService.buildFormData(
        { ...request, phone: '123', coverLetter: 'Hi' },
        pdf(),
      );
      expect(withOptional.get('phone')).toBe('123');
      expect(withOptional.get('coverLetter')).toBe('Hi');

      const withoutOptional = CareersService.buildFormData(request, pdf());
      expect(withoutOptional.has('phone')).toBeFalse();
      expect(withoutOptional.has('coverLetter')).toBeFalse();
    });
  });

  // ─── Client-side validation (AC-2) ─────────────────────────

  describe('validateResume', () => {
    it('accepts a valid PDF under the size limit', () => {
      expect(CareersService.validateResume(pdf('cv.pdf', 1024))).toBeNull();
    });

    it('rejects a file over 25 MB', () => {
      expect(CareersService.validateResume(pdf('cv.pdf', RESUME_MAX_BYTES + 1))).toBe(
        'too_large',
      );
    });

    it('rejects a disallowed extension even with a faked PDF mime', () => {
      const exe = new File(['x'], 'malware.exe', { type: 'application/pdf' });
      Object.defineProperty(exe, 'size', { value: 100 });
      expect(CareersService.validateResume(exe)).toBe('bad_type');
    });

    it('rejects a disallowed mime when present', () => {
      const png = new File(['x'], 'cv.pdf', { type: 'image/png' });
      Object.defineProperty(png, 'size', { value: 100 });
      expect(CareersService.validateResume(png)).toBe('bad_type');
    });

    it('accepts a .doc with an empty mime type (browser quirk)', () => {
      const doc = new File(['x'], 'cv.doc', { type: '' });
      Object.defineProperty(doc, 'size', { value: 100 });
      expect(CareersService.validateResume(doc)).toBeNull();
    });
  });

  // ─── Error helpers ─────────────────────────────────────────

  describe('error helpers', () => {
    it('parseErrorMessage extracts the message', () => {
      const err = new HttpErrorResponse({ error: { message: 'Boom' }, status: 400 });
      expect(CareersService.parseErrorMessage(err)).toBe('Boom');
    });

    it('parseErrorMessage falls back to a generic message', () => {
      const err = new HttpErrorResponse({ error: 'x', status: 500 });
      expect(CareersService.parseErrorMessage(err)).toContain('unexpected error');
    });

    it('isDuplicate detects the duplicate code without a 409', () => {
      const err = new HttpErrorResponse({
        error: { message: 'dup', code: 'duplicate' },
        status: 400,
      });
      expect(CareersService.isDuplicate(err)).toBeTrue();
    });
  });
});
