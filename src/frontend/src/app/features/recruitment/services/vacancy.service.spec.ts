import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';

import { VacancyService } from './vacancy.service';
import { environment } from '../../../../environments/environment';
import { IVacancy, IVacancyRequest, IVacancyPage } from '../models/vacancy.models';

describe('VacancyService', () => {
  let service: VacancyService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/recruitment/vacancies`;
  const root = environment.apiBaseUrl;

  const mockVacancy: IVacancy = {
    id: 'vac-1',
    referenceNumber: 'VAC-2026-0001',
    title: 'Senior Engineer',
    departmentId: 'dept-1',
    departmentName: 'Engineering',
    jobTitleId: 'jt-1',
    jobTitleName: 'Engineer',
    employmentType: 'FullTime',
    locationId: 'loc-1',
    locationName: 'HQ',
    hiringManagerId: 'emp-1',
    hiringManagerName: 'Jane Doe',
    headcount: 2,
    filledCount: 0,
    salaryMin: 100,
    salaryMax: 200,
    currency: 'USD',
    description: '<p>Role</p>',
    qualifications: '<p>Skills</p>',
    applicationDeadline: '2026-12-31',
    status: 'Draft',
    slug: 'senior-engineer',
    createdAt: '2026-06-01T00:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        VacancyService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(VacancyService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('listVacancies', () => {
    it('GETs the base url and returns the page envelope', () => {
      const page: IVacancyPage = {
        data: [mockVacancy],
        total: 1,
        page: 1,
        pageSize: 20,
      };
      service.listVacancies().subscribe((res) => {
        expect(res.data.length).toBe(1);
        expect(res.total).toBe(1);
      });
      const req = httpMock.expectOne((r) => r.url === base);
      expect(req.request.method).toBe('GET');
      req.flush(page);
    });

    it('passes status, departmentId and search as query params', () => {
      service
        .listVacancies({ status: 'Open', departmentId: 'dept-1', search: 'eng' })
        .subscribe();
      const req = httpMock.expectOne(
        (r) => r.url === base && r.params.get('status') === 'Open',
      );
      expect(req.request.params.get('departmentId')).toBe('dept-1');
      expect(req.request.params.get('search')).toBe('eng');
      req.flush({ data: [], total: 0, page: 1, pageSize: 0 });
    });

    it('normalizes a bare array response into a page envelope', () => {
      service.listVacancies().subscribe((res) => {
        expect(res.data.length).toBe(1);
        expect(res.total).toBe(1);
        expect(res.page).toBe(1);
      });
      const req = httpMock.expectOne((r) => r.url === base);
      req.flush([mockVacancy]);
    });
  });

  it('getVacancy GETs /:id', () => {
    service.getVacancy('vac-1').subscribe((v) => expect(v.id).toBe('vac-1'));
    const req = httpMock.expectOne(`${base}/vac-1`);
    expect(req.request.method).toBe('GET');
    req.flush(mockVacancy);
  });

  it('createVacancy POSTs the request body', () => {
    const request: IVacancyRequest = {
      title: 'New role',
      departmentId: null,
      jobTitleId: null,
      employmentType: null,
      locationId: null,
      hiringManagerId: null,
      headcount: null,
      salaryMin: null,
      salaryMax: null,
      currency: null,
      description: null,
      qualifications: null,
      applicationDeadline: null,
    };
    service.createVacancy(request).subscribe((v) => expect(v.id).toBe('vac-1'));
    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.title).toBe('New role');
    req.flush(mockVacancy);
  });

  it('updateVacancy PUTs to /:id', () => {
    service
      .updateVacancy('vac-1', { ...mockVacancy } as unknown as IVacancyRequest)
      .subscribe((v) => expect(v.id).toBe('vac-1'));
    const req = httpMock.expectOne(`${base}/vac-1`);
    expect(req.request.method).toBe('PUT');
    req.flush(mockVacancy);
  });

  it('publishVacancy POSTs to /:id/publish', () => {
    service.publishVacancy('vac-1').subscribe();
    const req = httpMock.expectOne(`${base}/vac-1/publish`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...mockVacancy, status: 'Open' });
  });

  it('closeVacancy POSTs to /:id/close', () => {
    service.closeVacancy('vac-1').subscribe();
    const req = httpMock.expectOne(`${base}/vac-1/close`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...mockVacancy, status: 'Closed' });
  });

  it('changeStatus POSTs to /:id/status with the status body', () => {
    service.changeStatus('vac-1', 'OnHold').subscribe();
    const req = httpMock.expectOne(`${base}/vac-1/status`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ status: 'OnHold' });
    req.flush({ ...mockVacancy, status: 'OnHold' });
  });

  describe('lookups', () => {
    it('getDepartments normalizes to ILookupOption', () => {
      service.getDepartments().subscribe((rows) => {
        expect(rows).toEqual([{ id: 'dept-1', label: 'Engineering' }]);
      });
      const req = httpMock.expectOne(`${root}/tenant/departments`);
      req.flush([{ departmentId: 'dept-1', name: 'Engineering' }]);
    });

    it('getJobTitles normalizes to ILookupOption', () => {
      service.getJobTitles().subscribe((rows) => {
        expect(rows).toEqual([{ id: 'jt-1', label: 'Engineer' }]);
      });
      const req = httpMock.expectOne(`${root}/tenant/job-titles`);
      req.flush([{ jobTitleId: 'jt-1', titleName: 'Engineer' }]);
    });

    it('searchEmployees builds a label + sublabel and passes search param', () => {
      service.searchEmployees('jane').subscribe((rows) => {
        expect(rows[0]).toEqual({
          id: 'emp-1',
          label: 'Jane Doe',
          sublabel: 'Engineer',
        });
      });
      const req = httpMock.expectOne(
        (r) => r.url === `${root}/tenant/employees` && r.params.get('search') === 'jane',
      );
      req.flush([
        {
          employeeId: 'emp-1',
          firstName: 'Jane',
          lastName: 'Doe',
          jobTitleName: 'Engineer',
        },
      ]);
    });
  });

  describe('parseError', () => {
    it('extracts a typed error body', () => {
      const err = new HttpErrorResponse({
        error: { message: 'Cannot publish', code: 'incomplete' },
        status: 400,
      });
      expect(VacancyService.parseError(err)?.message).toBe('Cannot publish');
      expect(VacancyService.parseErrorMessage(err)).toBe('Cannot publish');
    });

    it('falls back to a generic message', () => {
      const err = new HttpErrorResponse({ error: 'x', status: 500 });
      expect(VacancyService.parseError(err)).toBeNull();
      expect(VacancyService.parseErrorMessage(err)).toBe(
        'An unexpected error occurred.',
      );
    });
  });
});
