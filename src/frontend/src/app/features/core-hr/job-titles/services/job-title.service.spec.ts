import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { JobTitleService } from './job-title.service';
import {
  IJobTitle,
  ICreateJobTitleRequest,
  IUpdateJobTitleRequest,
} from '../models/job-title.models';
import { environment } from '../../../../../environments/environment';

describe('JobTitleService', () => {
  let service: JobTitleService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/job-titles`;

  const mockJobTitle: IJobTitle = {
    jobTitleId: 'jt-1',
    tenantId: 'tenant-1',
    titleName: 'Software Engineer',
    description: 'Develops software applications',
    gradeId: null,
    gradeName: null,
    isActive: true,
    employeeCount: 10,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const mockJobTitle2: IJobTitle = {
    jobTitleId: 'jt-2',
    tenantId: 'tenant-1',
    titleName: 'Product Manager',
    description: 'Manages product development',
    gradeId: null,
    gradeName: null,
    isActive: true,
    employeeCount: 3,
    createdAt: '2026-01-15T00:00:00Z',
    updatedAt: '2026-01-15T00:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        JobTitleService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(JobTitleService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getJobTitles', () => {
    it('should return all job titles for the tenant', () => {
      service.getJobTitles().subscribe((jobTitles) => {
        expect(jobTitles.length).toBe(2);
        expect(jobTitles[0].titleName).toBe('Software Engineer');
        expect(jobTitles[1].titleName).toBe('Product Manager');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockJobTitle, mockJobTitle2]);
    });

    it('should return an empty array when no job titles exist', () => {
      service.getJobTitles().subscribe((jobTitles) => {
        expect(jobTitles.length).toBe(0);
      });

      const req = httpMock.expectOne(baseUrl);
      req.flush([]);
    });
  });

  describe('getJobTitle', () => {
    it('should return a single job title by ID', () => {
      service.getJobTitle('jt-1').subscribe((jobTitle) => {
        expect(jobTitle.jobTitleId).toBe('jt-1');
        expect(jobTitle.titleName).toBe('Software Engineer');
      });

      const req = httpMock.expectOne(`${baseUrl}/jt-1`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockJobTitle);
    });
  });

  describe('createJobTitle', () => {
    it('should create a new job title', () => {
      const request: ICreateJobTitleRequest = {
        titleName: 'UX Designer',
        description: 'User experience design',
        isActive: true,
      };

      service.createJobTitle(request).subscribe((jobTitle) => {
        expect(jobTitle.titleName).toBe('UX Designer');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({
        ...mockJobTitle,
        jobTitleId: 'jt-3',
        titleName: 'UX Designer',
        description: 'User experience design',
      });
    });

    it('should create a job title without description', () => {
      const request: ICreateJobTitleRequest = {
        titleName: 'QA Engineer',
        isActive: true,
      };

      service.createJobTitle(request).subscribe((jobTitle) => {
        expect(jobTitle.titleName).toBe('QA Engineer');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.body.description).toBeUndefined();
      req.flush({ ...mockJobTitle, titleName: 'QA Engineer' });
    });
  });

  describe('updateJobTitle', () => {
    it('should update an existing job title', () => {
      const request: IUpdateJobTitleRequest = {
        titleName: 'Senior Software Engineer',
        description: 'Updated description',
        isActive: true,
      };

      service.updateJobTitle('jt-1', request).subscribe((jobTitle) => {
        expect(jobTitle.titleName).toBe('Senior Software Engineer');
      });

      const req = httpMock.expectOne(`${baseUrl}/jt-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockJobTitle, titleName: 'Senior Software Engineer' });
    });
  });

  describe('deactivateJobTitle', () => {
    it('should deactivate a job title (FR-5, FR-7)', () => {
      service.deactivateJobTitle('jt-1').subscribe();

      const req = httpMock.expectOne(`${baseUrl}/jt-1/deactivate`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(null);
    });
  });
});
