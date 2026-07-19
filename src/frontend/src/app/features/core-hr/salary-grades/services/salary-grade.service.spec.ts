import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { SalaryGradeService } from './salary-grade.service';
import {
  ISalaryGrade,
  ISalaryGradeRequest,
} from '../models/salary-grade.models';
import { environment } from '../../../../../environments/environment';

describe('SalaryGradeService', () => {
  let service: SalaryGradeService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/salary-grades`;

  const mockGrade: ISalaryGrade = {
    id: 'sg-1',
    code: 'G1',
    name: 'Grade 1',
    minAmount: 30000,
    midAmount: 40000,
    maxAmount: 50000,
    currency: 'USD',
    description: 'Entry level',
    isActive: true,
  };

  const mockGrade2: ISalaryGrade = {
    id: 'sg-2',
    code: 'G2',
    name: 'Grade 2',
    minAmount: 50000,
    midAmount: null,
    maxAmount: 70000,
    currency: 'USD',
    description: null,
    isActive: true,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        SalaryGradeService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(SalaryGradeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('list', () => {
    it('should GET the active grades from the tenant endpoint', () => {
      service.list().subscribe((grades) => {
        expect(grades.length).toBe(2);
        expect(grades[0].code).toBe('G1');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.params.has('includeInactive')).toBeFalse();
      req.flush([mockGrade, mockGrade2]);
    });

    it('should pass includeInactive=true when requested', () => {
      service.list(true).subscribe();

      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl && r.params.get('includeInactive') === 'true'
      );
      expect(req.request.method).toBe('GET');
      req.flush([mockGrade, mockGrade2]);
    });
  });

  describe('get', () => {
    it('should GET a single grade by id', () => {
      service.get('sg-1').subscribe((grade) => {
        expect(grade.id).toBe('sg-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/sg-1`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockGrade);
    });
  });

  describe('create', () => {
    it('should POST the grade body (id-less) to the base endpoint', () => {
      const request: ISalaryGradeRequest = {
        code: 'G3',
        name: 'Grade 3',
        minAmount: 70000,
        midAmount: 85000,
        maxAmount: 100000,
        currency: 'USD',
        description: 'Senior',
        isActive: true,
      };

      service.create(request).subscribe((grade) => {
        expect(grade.code).toBe('G3');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockGrade, id: 'sg-3', code: 'G3', name: 'Grade 3' });
    });
  });

  describe('update', () => {
    it('should PUT the grade body to the id-scoped endpoint', () => {
      const request: ISalaryGradeRequest = {
        code: 'G1',
        name: 'Grade 1 (revised)',
        minAmount: 32000,
        midAmount: 41000,
        maxAmount: 52000,
        currency: 'USD',
        description: 'Entry level',
        isActive: true,
      };

      service.update('sg-1', request).subscribe((grade) => {
        expect(grade.id).toBe('sg-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/sg-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockGrade, name: 'Grade 1 (revised)' });
    });
  });

  describe('deactivate', () => {
    it('should DELETE (soft-delete) the id-scoped endpoint', () => {
      service.deactivate('sg-1').subscribe();

      const req = httpMock.expectOne(`${baseUrl}/sg-1`);
      expect(req.request.method).toBe('DELETE');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(null);
    });
  });
});
