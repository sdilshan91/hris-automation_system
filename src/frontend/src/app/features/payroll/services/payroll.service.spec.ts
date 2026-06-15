import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PayrollService } from './payroll.service';
import { environment } from '../../../../environments/environment';
import {
  ISalaryComponent,
  ISalaryComponentRequest,
  ISalaryStructure,
  IFormulaTestResult,
} from '../models/payroll.models';

describe('PayrollService', () => {
  let service: PayrollService;
  let httpMock: HttpTestingController;
  const componentsUrl = `${environment.apiBaseUrl}/payroll/salary-components`;
  const structuresUrl = `${environment.apiBaseUrl}/payroll/salary-structures`;

  const mockComponent: ISalaryComponent = {
    id: 'c-1',
    name: 'Basic Salary',
    code: 'BASIC',
    type: 'Earning',
    calculationMethod: 'Fixed',
    defaultValue: 1000,
    formulaExpression: null,
    isTaxable: true,
    isStatutory: false,
    isActive: true,
    processingOrder: 1,
  };

  const mockStructure: ISalaryStructure = {
    id: 's-1',
    name: 'Full-Time',
    code: 'FT',
    description: 'Standard',
    effectiveFrom: '2026-01-01',
    isDefault: true,
    isActive: true,
    componentCount: 3,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PayrollService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PayrollService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── listComponents ──────────────────────────────────────

  describe('listComponents', () => {
    it('GETs the components url and returns a bare array', () => {
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      const req = httpMock.expectOne(componentsUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockComponent]);

      expect(result).toEqual([mockComponent]);
    });

    it('tolerates a { data } envelope', () => {
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      httpMock.expectOne(componentsUrl).flush({ data: [mockComponent] });
      expect(result).toEqual([mockComponent]);
    });

    it('defaults to [] for an unexpected shape', () => {
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      httpMock.expectOne(componentsUrl).flush(null);
      expect(result).toEqual([]);
    });
  });

  // ─── createComponent ─────────────────────────────────────

  describe('createComponent', () => {
    it('POSTs the request body and returns the created component', () => {
      const request: ISalaryComponentRequest = {
        name: 'Basic Salary',
        code: 'BASIC',
        type: 'Earning',
        calculationMethod: 'Fixed',
        defaultValue: 1000,
        formulaExpression: null,
        isTaxable: true,
        isStatutory: false,
      };
      let result: ISalaryComponent | undefined;
      service.createComponent(request).subscribe((r) => (result = r));

      const req = httpMock.expectOne(componentsUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush(mockComponent);

      expect(result).toEqual(mockComponent);
    });
  });

  // ─── updateComponent ─────────────────────────────────────

  describe('updateComponent', () => {
    it('PUTs to the component id url', () => {
      const request: ISalaryComponentRequest = {
        name: 'Basic',
        code: 'BASIC',
        type: 'Earning',
        calculationMethod: 'Fixed',
        defaultValue: 2000,
        formulaExpression: null,
        isTaxable: true,
        isStatutory: false,
      };
      service.updateComponent('c-1', request).subscribe();

      const req = httpMock.expectOne(`${componentsUrl}/c-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      req.flush({ ...mockComponent, defaultValue: 2000 });
    });
  });

  // ─── deleteComponent ─────────────────────────────────────

  describe('deleteComponent', () => {
    it('DELETEs the component id url', () => {
      let done = false;
      service.deleteComponent('c-1').subscribe(() => (done = true));

      const req = httpMock.expectOne(`${componentsUrl}/c-1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);

      expect(done).toBeTrue();
    });
  });

  // ─── reorderComponents ───────────────────────────────────

  describe('reorderComponents', () => {
    it('POSTs the ordered ids to /reorder and returns the re-sequenced list', () => {
      let result: ISalaryComponent[] | undefined;
      service.reorderComponents(['c-2', 'c-1']).subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${componentsUrl}/reorder`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ orderedIds: ['c-2', 'c-1'] });
      req.flush([mockComponent]);

      expect(result).toEqual([mockComponent]);
    });
  });

  // ─── testFormula ─────────────────────────────────────────

  describe('testFormula', () => {
    it('POSTs the expression + sample values to /validate-formula', () => {
      const expected: IFormulaTestResult = { valid: true, result: 120 };
      let result: IFormulaTestResult | undefined;
      service
        .testFormula({
          expression: 'basic * 0.12',
          sampleValues: { basic: 1000, gross: 1500 },
        })
        .subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${componentsUrl}/validate-formula`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({
        expression: 'basic * 0.12',
        sampleValues: { basic: 1000, gross: 1500 },
      });
      req.flush(expected);

      expect(result).toEqual(expected);
    });
  });

  // ─── listStructures ──────────────────────────────────────

  describe('listStructures', () => {
    it('GETs the structures url and returns the list', () => {
      let result: ISalaryStructure[] | undefined;
      service.listStructures().subscribe((r) => (result = r));

      const req = httpMock.expectOne(structuresUrl);
      expect(req.request.method).toBe('GET');
      req.flush([mockStructure]);

      expect(result).toEqual([mockStructure]);
    });

    it('tolerates a { data } envelope', () => {
      let result: ISalaryStructure[] | undefined;
      service.listStructures().subscribe((r) => (result = r));

      httpMock.expectOne(structuresUrl).flush({ data: [mockStructure] });
      expect(result).toEqual([mockStructure]);
    });
  });

  // ─── cloneStructure ──────────────────────────────────────

  describe('cloneStructure', () => {
    it('POSTs to the clone url and returns the new structure', () => {
      let result: ISalaryStructure | undefined;
      service.cloneStructure('s-1').subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${structuresUrl}/s-1/clone`);
      expect(req.request.method).toBe('POST');
      req.flush({ ...mockStructure, id: 's-2', name: 'Full-Time (copy)' });

      expect(result?.id).toBe('s-2');
    });
  });

  // ─── parseInUseError (AC-5) ──────────────────────────────

  describe('parseInUseError', () => {
    it('extracts the affected count from a 409', () => {
      const err = errorResponse(409, {
        code: 'component_in_use',
        affectedEmployeeCount: 7,
        message: 'In use',
      });
      const parsed = service.parseInUseError(err);
      expect(parsed).toEqual({
        code: 'component_in_use',
        affectedEmployeeCount: 7,
        message: 'In use',
      });
    });

    it('matches on the body code even when status is not 409', () => {
      const err = errorResponse(400, {
        code: 'component_in_use',
        affectedEmployeeCount: 2,
      });
      const parsed = service.parseInUseError(err);
      expect(parsed?.affectedEmployeeCount).toBe(2);
    });

    it('defaults the count to 0 when the body omits it', () => {
      const err = errorResponse(409, {});
      const parsed = service.parseInUseError(err);
      expect(parsed?.affectedEmployeeCount).toBe(0);
    });

    it('returns null for an unrelated error', () => {
      const err = errorResponse(500, { code: 'server_error' });
      expect(service.parseInUseError(err)).toBeNull();
    });

    it('returns null for a non-HttpErrorResponse', () => {
      expect(service.parseInUseError(new Error('boom'))).toBeNull();
    });
  });

  function errorResponse(status: number, body: Record<string, unknown>) {
    let captured: unknown;
    service.deleteComponent('c-err').subscribe({
      error: (e) => (captured = e),
    });
    httpMock
      .expectOne(`${componentsUrl}/c-err`)
      .flush(body, { status, statusText: 'Error' });
    return captured;
  }
});
