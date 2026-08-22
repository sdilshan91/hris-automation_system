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
  SalaryGradeWire,
} from '../models/salary-grade.models';
import { environment } from '../../../../../environments/environment';

describe('SalaryGradeService', () => {
  let service: SalaryGradeService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/salary-grades`;

  // Fixtures are the REAL wire shape (`SalaryGradesSalaryGradeDto`) — including the wire-only
  // createdAt/updatedAt fields the FE never renders — so the flush exercises `mapSalaryGrade`.
  const mockGrade: SalaryGradeWire = {
    id: 'sg-1',
    code: 'G1',
    name: 'Grade 1',
    minAmount: 30000,
    midAmount: 40000,
    maxAmount: 50000,
    currency: 'USD',
    description: 'Entry level',
    isActive: true,
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: null,
  };

  const mockGrade2: SalaryGradeWire = {
    id: 'sg-2',
    code: 'G2',
    name: 'Grade 2',
    minAmount: 50000,
    midAmount: null,
    maxAmount: 70000,
    currency: 'USD',
    description: null,
    isActive: true,
    createdAt: '2026-06-02T00:00:00Z',
    updatedAt: null,
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

    // Mapper contract arm. Exercises `mapSalaryGrade` against a wire DTO that OMITS `description`
    // (all wire fields are optional). The un-migrated service cast the body straight to ISalaryGrade,
    // so `description` was `undefined`; the mapper defaults it to `null`. `toBeNull()` fails for
    // `undefined`, so this arm goes red against the pre-migration direct cast — and against any
    // mutation of the mapper's `code` / `description` defaulting lines.
    it('maps the wire DTO → ISalaryGrade, defaulting fields the wire omits', () => {
      let received: ISalaryGrade | undefined;
      service.get('sg-9').subscribe((g) => (received = g));

      const req = httpMock.expectOne(`${baseUrl}/sg-9`);
      req.flush({
        id: 'sg-9',
        code: 'G9',
        name: 'Grade 9',
        minAmount: 90000,
        midAmount: null,
        maxAmount: 120000,
        currency: 'USD',
        isActive: true,
        createdAt: '2026-06-09T00:00:00Z',
        updatedAt: null,
        // description deliberately absent — the wire may omit it.
      });

      expect(received!.code).toBe('G9');
      expect(received!.description).toBeNull();
    });

    /**
     * B5: `referencingJobTitleCount` drives the confirm shown before deactivating a grade that job titles
     * still point at. Mutation testing found nothing pinned it through the mapper — the form arms set the
     * count directly on the component input, so a mapper hard-coding 0 stayed green while every warning in
     * the product silently stopped appearing.
     */
    it('carries the referencing job-title count through the mapper', () => {
      let received: ISalaryGrade | undefined;
      service.get('sg-7').subscribe((g) => (received = g));

      httpMock.expectOne(`${baseUrl}/sg-7`).flush({
        id: 'sg-7',
        code: 'G7',
        name: 'Grade 7',
        minAmount: 1,
        midAmount: null,
        maxAmount: 2,
        currency: 'USD',
        isActive: true,
        referencingJobTitleCount: 4,
      });

      expect(received!.referencingJobTitleCount)
        .withContext('a dropped count reads as "nothing uses this grade" and silences every warning')
        .toBe(4);
    });

    /**
     * `isActive` is the field B5 exists for, and every wire fixture in this file sets it TRUE — so a
     * mapper hard-coding `true` sailed through. Reading a DEACTIVATED grade is the arm that pins it.
     */
    it('carries a FALSE isActive through the mapper', () => {
      let received: ISalaryGrade | undefined;
      service.get('sg-6').subscribe((g) => (received = g));

      httpMock.expectOne(`${baseUrl}/sg-6`).flush({
        id: 'sg-6',
        code: 'G6',
        name: 'Grade 6',
        minAmount: 1,
        midAmount: null,
        maxAmount: 2,
        currency: 'USD',
        isActive: false,
      });

      expect(received!.isActive)
        .withContext('a mapper defaulting to true makes every deactivated grade look active')
        .toBeFalse();
    });

    it('defaults the referencing count to 0 when the wire omits it', () => {
      let received: ISalaryGrade | undefined;
      service.get('sg-8').subscribe((g) => (received = g));

      httpMock.expectOne(`${baseUrl}/sg-8`).flush({
        id: 'sg-8',
        code: 'G8',
        name: 'Grade 8',
        minAmount: 1,
        midAmount: null,
        maxAmount: 2,
        currency: 'USD',
        isActive: true,
      });

      expect(received!.referencingJobTitleCount).toBe(0);
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

    /**
     * THE WRITE-SIDE ARM FOR THE FIELD B5 EXISTS FOR. The arm above sends `isActive: true`, so a
     * `toSalaryGradeUpdateWire` that hard-coded `true` passed it — deactivation would silently stop
     * working through the exact seam B5 built, and nothing would go red. Every form spec stubs this
     * service, so this is the only layer that can catch it.
     */
    it('puts a FALSE isActive on the wire when the grade is being deactivated', () => {
      const request: ISalaryGradeRequest = {
        code: 'G1',
        name: 'Grade 1',
        minAmount: 32000,
        midAmount: 41000,
        maxAmount: 52000,
        currency: 'USD',
        description: 'Entry level',
        isActive: false,
      };

      service.update('sg-1', request).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/sg-1`);
      expect(req.request.body.isActive)
        .withContext('the toggle is only real if the false actually reaches the API')
        .toBeFalse();
      req.flush({ ...mockGrade, isActive: false });
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
