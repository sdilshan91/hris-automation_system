import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { CustomFieldListComponent } from './custom-field-list.component';
import {
  ICustomFieldListResponse,
  ICustomFieldDefinition,
} from '../../models/custom-field.models';
import { environment } from '../../../../../../environments/environment';

describe('CustomFieldListComponent', () => {
  let component: CustomFieldListComponent;
  let fixture: ComponentFixture<CustomFieldListComponent>;
  let httpMock: HttpTestingController;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const baseUrl = `${environment.apiBaseUrl}/tenant/custom-fields`;

  const mockDef1: ICustomFieldDefinition = {
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

  const mockDef2: ICustomFieldDefinition = {
    customFieldId: 'cf-2',
    tenantId: 'tenant-1',
    entityType: 'employee',
    fieldName: 'Project Code',
    fieldKey: 'project_code',
    fieldType: 'text',
    isRequired: true,
    options: null,
    displayOrder: 1,
    isActive: true,
    usageCount: 0,
    createdAt: '2026-06-02T00:00:00Z',
    updatedAt: '2026-06-02T00:00:00Z',
  };

  const mockResponse: ICustomFieldListResponse = {
    definitions: [mockDef1, mockDef2],
    planLimits: { currentCount: 2, maxAllowed: 5 },
  };

  beforeEach(async () => {
    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'info',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [CustomFieldListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CustomFieldListComponent);
    component = fixture.componentInstance;
  });

  function flushListData(response?: ICustomFieldListResponse): void {
    fixture.detectChanges();
    const req = httpMock.expectOne(`${baseUrl}?entityType=employee`);
    req.flush(response ?? mockResponse);
    fixture.detectChanges();
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('should create', () => {
    flushListData();
    expect(component).toBeTruthy();
  });

  it('should display custom field definitions after loading', () => {
    flushListData();
    expect(component.definitions().length).toBe(2);
    expect(component.definitions()[0].fieldName).toBe('T-Shirt Size');
    expect(component.definitions()[1].fieldName).toBe('Project Code');
  });

  it('should display usage count for each field', () => {
    flushListData();
    expect(component.definitions()[0].usageCount).toBe(5);
    expect(component.definitions()[1].usageCount).toBe(0);
  });

  it('should display plan limits', () => {
    flushListData();
    expect(component.planLimits()!.currentCount).toBe(2);
    expect(component.planLimits()!.maxAllowed).toBe(5);
    expect(component.planLimitPercent()).toBe(40);
  });

  it('should detect when at plan limit', () => {
    const atLimitResponse: ICustomFieldListResponse = {
      definitions: [mockDef1, mockDef2],
      planLimits: { currentCount: 5, maxAllowed: 5 },
    };
    flushListData(atLimitResponse);
    expect(component.isAtPlanLimit()).toBeTrue();
  });

  it('should not be at plan limit when maxAllowed is null (unlimited)', () => {
    const unlimitedResponse: ICustomFieldListResponse = {
      definitions: [mockDef1],
      planLimits: { currentCount: 1, maxAllowed: null },
    };
    flushListData(unlimitedResponse);
    expect(component.isAtPlanLimit()).toBeFalse();
  });

  describe('Add Custom Field form', () => {
    beforeEach(() => {
      flushListData();
    });

    it('should open add modal', () => {
      component.openAddModal();
      expect(component.showAddModal()).toBeTrue();
    });

    it('should auto-generate field key from name', () => {
      component.openAddModal();
      component.addFieldForm.get('fieldName')!.setValue('T-Shirt Size');
      component.onFieldNameChange();
      expect(component.addFieldForm.get('fieldKey')!.value).toBe('t_shirt_size');
    });

    it('should select field type', () => {
      component.openAddModal();
      component.selectFieldType('dropdown');
      expect(component.addFieldForm.get('fieldType')!.value).toBe('dropdown');
    });

    it('should show options input for dropdown type', () => {
      component.openAddModal();
      component.selectFieldType('dropdown');
      expect(component.showOptionsInput()).toBeTrue();
    });

    it('should not show options input for text type', () => {
      component.openAddModal();
      component.selectFieldType('text');
      expect(component.showOptionsInput()).toBeFalse();
    });

    it('should add and remove options', () => {
      component.openAddModal();
      component.selectFieldType('dropdown');

      component.optionInputValue.set('Small');
      component.addOption(new Event('keydown'));
      expect(component.currentOptions()).toEqual(['Small']);

      component.optionInputValue.set('Large');
      component.addOption(new Event('keydown'));
      expect(component.currentOptions()).toEqual(['Small', 'Large']);

      component.removeOption(0);
      expect(component.currentOptions()).toEqual(['Large']);
    });

    it('should not add duplicate options', () => {
      component.openAddModal();
      component.selectFieldType('dropdown');

      component.optionInputValue.set('Small');
      component.addOption(new Event('keydown'));
      component.optionInputValue.set('Small');
      component.addOption(new Event('keydown'));
      expect(component.currentOptions()).toEqual(['Small']);
    });

    it('should toggle required', () => {
      component.openAddModal();
      expect(component.addFieldForm.get('isRequired')!.value).toBeFalse();
      component.toggleRequired();
      expect(component.addFieldForm.get('isRequired')!.value).toBeTrue();
    });

    it('should not be valid without required fields', () => {
      component.openAddModal();
      expect(component.isAddFormValid()).toBeFalse();
    });

    it('should be valid with all required fields filled', () => {
      component.openAddModal();
      component.addFieldForm.get('fieldName')!.setValue('Test');
      component.addFieldForm.get('fieldKey')!.setValue('test');
      component.addFieldForm.get('fieldType')!.setValue('text');
      expect(component.isAddFormValid()).toBeTrue();
    });

    it('should require options for dropdown type', () => {
      component.openAddModal();
      component.addFieldForm.get('fieldName')!.setValue('Size');
      component.addFieldForm.get('fieldKey')!.setValue('size');
      component.addFieldForm.get('fieldType')!.setValue('dropdown');
      expect(component.isAddFormValid()).toBeFalse();

      component.optionInputValue.set('S');
      component.addOption(new Event('keydown'));
      expect(component.isAddFormValid()).toBeTrue();
    });
  });

  describe('Plan limit handling', () => {
    it('should show plan limit error when backend rejects creation', fakeAsync(() => {
      flushListData();
      component.openAddModal();
      component.addFieldForm.get('fieldName')!.setValue('Test');
      component.addFieldForm.get('fieldKey')!.setValue('test');
      component.addFieldForm.get('fieldType')!.setValue('text');

      component.submitAddField();

      const req = httpMock.expectOne(baseUrl);
      req.flush(
        { message: 'Plan limit reached', code: 'plan_limit_exceeded', maxAllowed: 5 },
        { status: 403, statusText: 'Forbidden' }
      );
      tick();

      expect(component.planLimitError()).toContain('maximum number of custom fields (5)');
      expect(component.showAddModal()).toBeFalse();
    }));
  });

  describe('Deactivate/Reactivate toggle', () => {
    it('should deactivate an active field', fakeAsync(() => {
      flushListData();

      const deactivated = { ...mockDef1, isActive: false };
      component.toggleActive(mockDef1);

      const req = httpMock.expectOne(`${baseUrl}/cf-1/deactivate`);
      expect(req.request.method).toBe('POST');
      req.flush(deactivated);
      tick();

      expect(component.definitions()[0].isActive).toBeFalse();
      expect(toastrSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('deactivated'));
    }));

    it('should reactivate an inactive field', fakeAsync(() => {
      const inactiveDef = { ...mockDef1, isActive: false };
      const responseWithInactive: ICustomFieldListResponse = {
        definitions: [inactiveDef, mockDef2],
        planLimits: { currentCount: 2, maxAllowed: 5 },
      };
      flushListData(responseWithInactive);

      const activated = { ...mockDef1, isActive: true };
      component.toggleActive(inactiveDef);

      const req = httpMock.expectOne(`${baseUrl}/cf-1/activate`);
      expect(req.request.method).toBe('POST');
      req.flush(activated);
      tick();

      expect(component.definitions()[0].isActive).toBeTrue();
      expect(toastrSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('reactivated'));
    }));
  });

  describe('Reorder', () => {
    it('should swap fields and persist order', fakeAsync(() => {
      flushListData();
      expect(component.definitions()[0].fieldName).toBe('T-Shirt Size');
      expect(component.definitions()[1].fieldName).toBe('Project Code');

      component.moveField(0, 1);

      expect(component.definitions()[0].fieldName).toBe('Project Code');
      expect(component.definitions()[1].fieldName).toBe('T-Shirt Size');

      const req = httpMock.expectOne(`${baseUrl}/reorder`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.orderedIds).toEqual(['cf-2', 'cf-1']);
      req.flush(null);
      tick();
    }));

    it('should not move first field up', fakeAsync(() => {
      flushListData();
      component.moveField(0, -1);
      // No HTTP call expected
      expect(component.definitions()[0].fieldName).toBe('T-Shirt Size');
    }));

    it('should not move last field down', fakeAsync(() => {
      flushListData();
      component.moveField(1, 1);
      // No HTTP call expected
      expect(component.definitions()[1].fieldName).toBe('Project Code');
    }));
  });
});
