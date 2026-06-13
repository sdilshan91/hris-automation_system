import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { CustomFieldService } from './custom-field.service';
import {
  ICustomFieldDefinition,
  ICustomFieldListResponse,
  ICreateCustomFieldRequest,
} from '../models/custom-field.models';
import { environment } from '../../../../../environments/environment';

describe('CustomFieldService', () => {
  let service: CustomFieldService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/custom-fields`;

  const mockDefinition: ICustomFieldDefinition = {
    customFieldId: 'cf-1',
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

  const mockListResponse: ICustomFieldListResponse = {
    definitions: [mockDefinition],
    planLimits: { currentCount: 1, maxAllowed: 5 },
  };

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
    it('should return definitions with plan limits', () => {
      service.getCustomFields('employee').subscribe((response) => {
        expect(response.definitions.length).toBe(1);
        expect(response.definitions[0].fieldName).toBe('T-Shirt Size');
        expect(response.planLimits.currentCount).toBe(1);
        expect(response.planLimits.maxAllowed).toBe(5);
      });

      const req = httpMock.expectOne(`${baseUrl}?entityType=employee`);
      expect(req.request.method).toBe('GET');
      req.flush(mockListResponse);
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
      req.flush([mockDefinition]);
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
        customFieldId: 'cf-2',
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
      req.flush(response);
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
      service.reorderCustomFields({ orderedIds: ['cf-2', 'cf-1'] }).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/reorder`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.orderedIds).toEqual(['cf-2', 'cf-1']);
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
