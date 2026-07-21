import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { CustomFieldService } from './custom-field.service';
import {
  ICustomFieldDefinition,
  ICustomFieldListResult,
  ICreateCustomFieldRequest,
  IUpdateCustomFieldRequest,
} from '../models/custom-field.models';
import { environment } from '../../../../../environments/environment';

describe('CustomFieldService', () => {
  let service: CustomFieldService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/custom-fields`;

  // The FE-model shape (options already normalized to an array), used to assert what consumers receive.
  const mockDefinition: ICustomFieldDefinition = {
    id: 'cf-1',
    tenantId: 'tenant-1',
    entityType: 'employee',
    fieldName: 'T-Shirt Size',
    fieldKey: 'tshirt_size',
    fieldType: 'dropdown',
    isRequired: false,
    options: ['S', 'M', 'L', 'XL'],
    displayOrder: 0,
    isActive: true,
    usageCount: 5,
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
  };

  // DF-9: what the BACKEND actually puts on the wire — `options` is a JSON-encoded STRING, not an array.
  // The service must parse this into `mockDefinition.options` for consumers.
  const mockWireDefinition = {
    ...mockDefinition,
    options: '["S","M","L","XL"]',
  } as unknown as ICustomFieldDefinition;

  // The backend returns an ARRAY of grouped results ({ entityType, fields[], totalCount, maxAllowed }).
  // The list endpoint returns definitions with the SAME wire shape (options as a JSON string).
  const mockListResult: ICustomFieldListResult[] = [
    {
      entityType: 'employee',
      fields: [mockWireDefinition],
      totalCount: 1,
      maxAllowed: 5,
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        CustomFieldService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(CustomFieldService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getCustomFields', () => {
    it('should map the BE grouped array into definitions + plan limits', () => {
      service.getCustomFields('employee').subscribe((response) => {
        expect(response.definitions.length).toBe(1);
        expect(response.definitions[0].fieldName).toBe('T-Shirt Size');
        expect(response.definitions[0].id).toBe('cf-1');
        // DF-9: the list path also normalizes the wire options string → array.
        expect(response.definitions[0].options).toEqual(['S', 'M', 'L', 'XL']);
        expect(response.planLimits.currentCount).toBe(1);
        expect(response.planLimits.maxAllowed).toBe(5);
      });

      const req = httpMock.expectOne(`${baseUrl}?entityType=employee`);
      expect(req.request.method).toBe('GET');
      req.flush(mockListResult);
    });

    it('should fall back to an empty group when the entity type is absent', () => {
      service.getCustomFields('employee').subscribe((response) => {
        expect(response.definitions).toEqual([]);
        expect(response.planLimits.currentCount).toBe(0);
        expect(response.planLimits.maxAllowed).toBeNull();
      });

      httpMock.expectOne(`${baseUrl}?entityType=employee`).flush([]);
    });
  });

  describe('getActiveCustomFields', () => {
    it('should return active definitions only', () => {
      service.getActiveCustomFields('employee').subscribe((fields) => {
        expect(fields.length).toBe(1);
        expect(fields[0].isActive).toBeTrue();
      });

      const req = httpMock.expectOne(`${baseUrl}/active?entityType=employee`);
      expect(req.request.method).toBe('GET');
      req.flush([mockWireDefinition]);
    });

    it('DF-9: parses the backend `options` JSON STRING into a string[] array for rendering', () => {
      service.getActiveCustomFields('employee').subscribe((fields) => {
        // Before the fix, options arrived as the raw string '["S","M","L","XL"]' and @for iterated
        // its characters. The service must hand consumers a real array.
        expect(Array.isArray(fields[0].options)).toBeTrue();
        expect(fields[0].options).toEqual(['S', 'M', 'L', 'XL']);
      });

      httpMock
        .expectOne(`${baseUrl}/active?entityType=employee`)
        .flush([mockWireDefinition]);
    });

    it('DF-9: leaves a non-option field (options null) as null', () => {
      const textWire = {
        ...mockWireDefinition,
        fieldType: 'text',
        options: null,
      } as unknown as ICustomFieldDefinition;

      service.getActiveCustomFields('employee').subscribe((fields) => {
        expect(fields[0].options).toBeNull();
      });

      httpMock.expectOne(`${baseUrl}/active?entityType=employee`).flush([textWire]);
    });

    it('DF-9: a malformed options string degrades to null, never throws', () => {
      const badWire = {
        ...mockWireDefinition,
        options: 'not-json',
      } as unknown as ICustomFieldDefinition;

      service.getActiveCustomFields('employee').subscribe((fields) => {
        expect(fields[0].options).toBeNull();
      });

      httpMock.expectOne(`${baseUrl}/active?entityType=employee`).flush([badWire]);
    });
  });

  describe('createCustomField', () => {
    it('should POST a new custom field definition', () => {
      const request: ICreateCustomFieldRequest = {
        fieldName: 'Project Code',
        fieldKey: 'project_code',
        fieldType: 'text',
        isRequired: true,
        options: null,
        displayOrder: 1,
        entityType: 'employee',
      };

      const response: ICustomFieldDefinition = {
        ...mockDefinition,
        id: 'cf-2',
        fieldName: 'Project Code',
        fieldKey: 'project_code',
        fieldType: 'text',
        isRequired: true,
        options: null,
        displayOrder: 1,
        usageCount: 0,
      };

      service.createCustomField(request).subscribe((result) => {
        expect(result.fieldName).toBe('Project Code');
        expect(result.fieldKey).toBe('project_code');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.fieldName).toBe('Project Code');
      // A text field carries no options → null on the wire.
      expect(req.request.body.options).toBeNull();
      req.flush(response);
    });

    it('DF-9: stringifies a dropdown field\'s options array into the backend `string?` contract, and parses the response back', () => {
      const request: ICreateCustomFieldRequest = {
        fieldName: 'T-Shirt Size',
        fieldKey: 'tshirt_size',
        fieldType: 'dropdown',
        isRequired: false,
        options: ['S', 'M', 'L', 'XL'],
        displayOrder: 0,
        entityType: 'employee',
      };

      service.createCustomField(request).subscribe((result) => {
        // Response options string is parsed back to an array for the caller.
        expect(result.options).toEqual(['S', 'M', 'L', 'XL']);
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      // The wire body must be the JSON STRING, not the array (backend binds `string?`).
      expect(req.request.body.options).toBe('["S","M","L","XL"]');
      req.flush(mockWireDefinition);
    });
  });

  describe('updateCustomField', () => {
    it('DF-9: stringifies the options array on PUT and parses the response back (same boundary as create)', () => {
      const request: IUpdateCustomFieldRequest = {
        fieldName: 'T-Shirt Size',
        isRequired: true,
        options: ['S', 'M', 'L', 'XL'],
        displayOrder: 2,
      };

      service.updateCustomField('cf-1', request).subscribe((result) => {
        expect(result.options).toEqual(['S', 'M', 'L', 'XL']);
      });

      const req = httpMock.expectOne(`${baseUrl}/cf-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body.options).toBe('["S","M","L","XL"]');
      req.flush(mockWireDefinition);
    });
  });

  describe('deactivateCustomField', () => {
    it('should POST to deactivate endpoint', () => {
      const deactivated = { ...mockDefinition, isActive: false };

      service.deactivateCustomField('cf-1').subscribe((result) => {
        expect(result.isActive).toBeFalse();
      });

      const req = httpMock.expectOne(`${baseUrl}/cf-1/deactivate`);
      expect(req.request.method).toBe('POST');
      req.flush(deactivated);
    });
  });

  describe('activateCustomField', () => {
    it('should POST to activate endpoint', () => {
      service.activateCustomField('cf-1').subscribe((result) => {
        expect(result.isActive).toBeTrue();
      });

      const req = httpMock.expectOne(`${baseUrl}/cf-1/activate`);
      expect(req.request.method).toBe('POST');
      req.flush(mockDefinition);
    });
  });

  describe('reorderCustomFields', () => {
    it('should POST ordered IDs', () => {
      service.reorderCustomFields({ fieldIds: ['cf-2', 'cf-1'] }).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/reorder`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.fieldIds).toEqual(['cf-2', 'cf-1']);
      req.flush(null);
    });
  });

  describe('parseError', () => {
    it('should parse a plan_limit_exceeded error', () => {
      const err = new HttpErrorResponse({
        error: { message: 'Limit reached', code: 'plan_limit_exceeded', maxAllowed: 5 },
        status: 403,
      });
      const result = CustomFieldService.parseError(err);
      expect(result).toBeTruthy();
      expect(result!.code).toBe('plan_limit_exceeded');
      expect(result!.maxAllowed).toBe(5);
    });

    it('should return null for non-object errors', () => {
      const err = new HttpErrorResponse({
        error: 'string error',
        status: 500,
      });
      expect(CustomFieldService.parseError(err)).toBeNull();
    });
  });
});
