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
  SalaryComponentListItemWire,
  SalaryComponentWire,
  SalaryStructureListItemWire,
  SalaryStructureWire,
} from '../models/payroll.models';

describe('PayrollService', () => {
  let service: PayrollService;
  let httpMock: HttpTestingController;
  const componentsUrl = `${environment.apiBaseUrl}/payroll/salary-components`;
  const structuresUrl = `${environment.apiBaseUrl}/payroll/salary-structures`;

  // WIRE shapes — exactly what the API sends. `PayrollSalaryComponentListItemDto` is the LIST row and
  // deliberately has no `formulaExpression`; `PayrollSalaryComponentDto` (create/update) does.
  const wireComponentListItem: SalaryComponentListItemWire = {
    id: 'c-1',
    name: 'Basic Salary',
    code: 'BASIC',
    type: 'Earning',
    typeName: 'Earning',
    calculationMethod: 'Fixed',
    calculationMethodName: 'Fixed amount',
    defaultValue: 1000,
    isTaxable: true,
    isStatutory: false,
    isActive: true,
    processingOrder: 1,
  };

  const wireComponent: SalaryComponentWire = {
    ...wireComponentListItem,
    formulaExpression: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: null,
  };

  /** What the list wire maps to — note `formulaExpression: null`, which the list DTO cannot carry. */
  const mappedComponent: ISalaryComponent = {
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

  // `PayrollSalaryStructureListItemDto` — the LIST row. It has `componentCount` but NO `description`.
  const wireStructureListItem: SalaryStructureListItemWire = {
    id: 's-1',
    name: 'Full-Time',
    code: 'FT',
    effectiveFrom: '2026-01-01',
    isDefault: true,
    isActive: true,
    componentCount: 3,
    createdAt: '2026-01-01T00:00:00Z',
  };

  // `PayrollSalaryStructureDto` — the FULL shape returned by clone. It has `description` and
  // `components`, but no `componentCount`.
  const wireStructure: SalaryStructureWire = {
    id: 's-1',
    name: 'Full-Time',
    code: 'FT',
    description: 'Standard',
    effectiveFrom: '2026-01-01',
    isDefault: true,
    isActive: true,
    components: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: null,
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
    it('GETs the components url and maps a bare array', () => {
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      const req = httpMock.expectOne(componentsUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([wireComponentListItem]);

      expect(result).toEqual([mappedComponent]);
      // The mapper builds a NEW object; the wire row is never handed to components as-is.
      expect(result![0]).not.toBe(
        wireComponentListItem as unknown as ISalaryComponent,
      );
    });

    it('reads the PagedResultOf… { items, totalCount } page envelope', () => {
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      httpMock.expectOne(componentsUrl).flush({
        items: [wireComponentListItem],
        totalCount: 1,
        page: 1,
        pageSize: 25,
      });
      expect(result).toEqual([mappedComponent]);
    });

    it('still tolerates the legacy { data } envelope', () => {
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      httpMock.expectOne(componentsUrl).flush({ data: [wireComponentListItem] });
      expect(result).toEqual([mappedComponent]);
    });

    it('treats a null items collection as an empty page', () => {
      // The generated contract types `items` as `T[] | null`, not merely optional.
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      httpMock
        .expectOne(componentsUrl)
        .flush({ items: null, totalCount: 0, page: 1, pageSize: 25 });
      expect(result).toEqual([]);
    });

    it('defaults to [] for an unexpected shape', () => {
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      httpMock.expectOne(componentsUrl).flush(null);
      expect(result).toEqual([]);
    });

    it('fails CLOSED on an omitted isActive — never presents a component as live', () => {
      // A default here is a decision: an absent flag must not make an inactive component assignable.
      const { isActive: _omitted, ...withoutIsActive } = wireComponentListItem;
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      httpMock.expectOne(componentsUrl).flush([withoutIsActive]);
      expect(result![0].isActive).toBeFalse();
    });

    it('maps an omitted defaultValue to null, not 0', () => {
      // "no default configured" (rendered "—") must not become a real amount of zero.
      const { defaultValue: _omitted, ...withoutDefault } = wireComponentListItem;
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      httpMock.expectOne(componentsUrl).flush([withoutDefault]);
      expect(result![0].defaultValue).toBeNull();
    });

    it('cannot carry formulaExpression — the list DTO does not have the field', () => {
      // Documents the wipe risk: PayrollSalaryComponentListItemDto has no formulaExpression, so the
      // edit form patched from a list row would post `null` over a real formula. See the D1 report.
      let result: ISalaryComponent[] | undefined;
      service.listComponents().subscribe((r) => (result = r));

      httpMock
        .expectOne(componentsUrl)
        .flush([{ ...wireComponentListItem, calculationMethod: 'Formula' }]);
      expect(result![0].calculationMethod).toBe('Formula');
      expect(result![0].formulaExpression).toBeNull();
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
      req.flush(wireComponent);

      expect(result).toEqual(mappedComponent);
      expect(result).not.toBe(wireComponent as unknown as ISalaryComponent);
    });

    it('keeps formulaExpression from the FULL create response', () => {
      const request: ISalaryComponentRequest = {
        name: 'PF',
        code: 'PF',
        type: 'Statutory',
        calculationMethod: 'Formula',
        defaultValue: null,
        formulaExpression: 'basic * 0.12',
        isTaxable: false,
        isStatutory: true,
      };
      let result: ISalaryComponent | undefined;
      service.createComponent(request).subscribe((r) => (result = r));

      httpMock.expectOne(componentsUrl).flush({
        ...wireComponent,
        calculationMethod: 'Formula',
        formulaExpression: 'basic * 0.12',
      });

      expect(result!.formulaExpression).toBe('basic * 0.12');
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
      req.flush({ ...wireComponent, defaultValue: 2000 });
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

      // ⚠ GAP-010 / ISSUE-372: `/payroll/salary-components/reorder` does not exist either (the sibling
      // custom-fields and leave-types entities DO have one). Same caveat as validate-formula above.
      const req = httpMock.expectOne(`${componentsUrl}/reorder`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ orderedIds: ['c-2', 'c-1'] });
      req.flush([wireComponentListItem]);

      expect(result).toEqual([mappedComponent]);
    });
  });

  // ─── testFormula ─────────────────────────────────────────

  describe('testFormula', () => {
    // ⚠ GAP-010 / ISSUE-372: `/payroll/salary-components/validate-formula` DOES NOT EXIST on the API — zero
    // contract paths, zero controller routes. This arm passes because HttpTestingController answers whatever
    // the service asks for, so it proves the service builds a URL, not that the URL works. Kept (not deleted)
    // so the coverage is not silently lost, but it is NOT evidence that the "Test formula" button works.
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
    /** What the LIST wire maps to. `description` is null because the list DTO has no such field. */
    const mappedListStructure = {
      id: 's-1',
      name: 'Full-Time',
      code: 'FT',
      description: null,
      effectiveFrom: '2026-01-01',
      isDefault: true,
      isActive: true,
      componentCount: 3,
    };

    it('GETs the structures url and maps the list', () => {
      let result: ISalaryStructure[] | undefined;
      service.listStructures().subscribe((r) => (result = r));

      const req = httpMock.expectOne(structuresUrl);
      expect(req.request.method).toBe('GET');
      req.flush([wireStructureListItem]);

      expect(result).toEqual([mappedListStructure]);
      expect(result![0]).not.toBe(
        wireStructureListItem as unknown as ISalaryStructure,
      );
    });

    it('reads the PagedResultOf… { items, totalCount } page envelope', () => {
      let result: ISalaryStructure[] | undefined;
      service.listStructures().subscribe((r) => (result = r));

      httpMock.expectOne(structuresUrl).flush({
        items: [wireStructureListItem],
        totalCount: 1,
        page: 1,
        pageSize: 25,
      });
      expect(result).toEqual([mappedListStructure]);
    });

    it('still tolerates the legacy { data } envelope', () => {
      let result: ISalaryStructure[] | undefined;
      service.listStructures().subscribe((r) => (result = r));

      httpMock.expectOne(structuresUrl).flush({ data: [wireStructureListItem] });
      expect(result).toEqual([mappedListStructure]);
    });

    it('has no description on the list wire — the card block can never render', () => {
      // Documents a dead UI control rather than hiding it: `@if (s.description)` is unreachable from
      // the list, because PayrollSalaryStructureListItemDto does not carry the field. D1 report.
      let result: ISalaryStructure[] | undefined;
      service.listStructures().subscribe((r) => (result = r));

      httpMock.expectOne(structuresUrl).flush([wireStructureListItem]);
      expect(result![0].description).toBeNull();
    });

    it('fails CLOSED on omitted isActive / isDefault flags', () => {
      const {
        isActive: _a,
        isDefault: _d,
        ...withoutFlags
      } = wireStructureListItem;
      let result: ISalaryStructure[] | undefined;
      service.listStructures().subscribe((r) => (result = r));

      httpMock.expectOne(structuresUrl).flush([withoutFlags]);
      expect(result![0].isActive).toBeFalse();
      expect(result![0].isDefault).toBeFalse();
    });
  });

  // ─── cloneStructure ──────────────────────────────────────

  describe('cloneStructure', () => {
    it('POSTs to the clone url and maps the FULL structure DTO', () => {
      let result: ISalaryStructure | undefined;
      service.cloneStructure('s-1').subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${structuresUrl}/s-1/clone`);
      expect(req.request.method).toBe('POST');
      req.flush({ ...wireStructure, id: 's-2', name: 'Full-Time (copy)' });

      expect(result?.id).toBe('s-2');
      // The FULL DTO — unlike the list row — DOES carry a description.
      expect(result?.description).toBe('Standard');
    });

    it('sends an EMPTY body where the contract expects { name, code }', () => {
      // Pinned, not endorsed: PayrollCloneSalaryStructureRequest has Name/Code and there is no
      // validator, so the API persists a nameless, codeless clone. Flagged in the D1 report; asserted
      // here so the day someone fixes the caller, this test is what tells them the contract changed.
      service.cloneStructure('s-1').subscribe();

      const req = httpMock.expectOne(`${structuresUrl}/s-1/clone`);
      expect(req.request.body).toEqual({});
      req.flush(wireStructure);
    });

    it('derives componentCount from the components array, and leaves it unknown when absent', () => {
      let withComponents: ISalaryStructure | undefined;
      service.cloneStructure('s-1').subscribe((r) => (withComponents = r));
      httpMock.expectOne(`${structuresUrl}/s-1/clone`).flush({
        ...wireStructure,
        components: [{ id: 'sc-1' }, { id: 'sc-2' }],
      });
      expect(withComponents!.componentCount).toBe(2);

      // No array on the wire means the count is genuinely UNKNOWN — it must not claim 0.
      const { components: _omitted, ...withoutComponents } = wireStructure;
      let withoutCount: ISalaryStructure | undefined;
      service.cloneStructure('s-1').subscribe((r) => (withoutCount = r));
      httpMock.expectOne(`${structuresUrl}/s-1/clone`).flush(withoutComponents);
      expect(withoutCount!.componentCount).toBeUndefined();
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
