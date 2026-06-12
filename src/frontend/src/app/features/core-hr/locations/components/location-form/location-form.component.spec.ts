import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { ComponentRef } from '@angular/core';

import { LocationFormComponent } from './location-form.component';
import { LocationService } from '../../services/location.service';
import { ILocation } from '../../models/location.models';

describe('LocationFormComponent', () => {
  let component: LocationFormComponent;
  let componentRef: ComponentRef<LocationFormComponent>;
  let fixture: ComponentFixture<LocationFormComponent>;
  let locationServiceSpy: jasmine.SpyObj<LocationService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockLocation: ILocation = {
    locationId: 'loc-1',
    tenantId: 'tenant-1',
    name: 'Headquarters',
    addressLine1: '123 Main St',
    addressLine2: 'Suite 100',
    city: 'New York',
    stateProvince: 'NY',
    country: 'United States',
    postalCode: '10001',
    timeZone: 'America/New_York',
    phone: '+1 555 123 4567',
    isActive: true,
    employeeCount: 25,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  beforeEach(async () => {
    locationServiceSpy = jasmine.createSpyObj('LocationService', [
      'createLocation',
      'updateLocation',
    ]);
    locationServiceSpy.createLocation.and.returnValue(of(mockLocation));
    locationServiceSpy.updateLocation.and.returnValue(of(mockLocation));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
    ]);

    await TestBed.configureTestingModule({
      imports: [LocationFormComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: LocationService, useValue: locationServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LocationFormComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
  });

  // --- Create Mode -------------------------------------------

  describe('create mode', () => {
    beforeEach(() => {
      componentRef.setInput('location', null);
      fixture.detectChanges();
    });

    it('should create', () => {
      expect(component).toBeTruthy();
    });

    it('should initialize with empty form', () => {
      expect(component.form.value.name).toBe('');
      expect(component.form.value.timeZone).toBe('');
      expect(component.form.value.isActive).toBeTrue();
    });

    it('should validate required name field', () => {
      const nameCtrl = component.form.get('name')!;
      expect(nameCtrl.valid).toBeFalse();

      nameCtrl.setValue('New Location');
      expect(nameCtrl.valid).toBeTrue();
    });

    it('should validate name max length (150 chars)', () => {
      const nameCtrl = component.form.get('name')!;
      nameCtrl.setValue('A'.repeat(151));
      expect(nameCtrl.hasError('maxlength')).toBeTrue();

      nameCtrl.setValue('A'.repeat(150));
      expect(nameCtrl.valid).toBeTrue();
    });

    it('should validate required time zone field', () => {
      const tzCtrl = component.form.get('timeZone')!;
      expect(tzCtrl.valid).toBeFalse();

      tzCtrl.setValue('America/New_York');
      expect(tzCtrl.valid).toBeTrue();
    });

    it('should call createLocation on submit', () => {
      component.form.patchValue({
        name: 'New Office',
        timeZone: 'Europe/London',
        city: 'London',
        country: 'United Kingdom',
        isActive: true,
      });
      component.form.markAsDirty();

      component.onSubmit();

      expect(locationServiceSpy.createLocation).toHaveBeenCalledWith(
        jasmine.objectContaining({
          name: 'New Office',
          timeZone: 'Europe/London',
          city: 'London',
          country: 'United Kingdom',
          isActive: true,
        })
      );
      expect(toastrSpy.success).toHaveBeenCalled();
    });

    it('should not submit when form is invalid', () => {
      component.onSubmit();
      expect(locationServiceSpy.createLocation).not.toHaveBeenCalled();
    });

    it('should handle duplicate name error (BR-1)', () => {
      locationServiceSpy.createLocation.and.returnValue(
        throwError(() => ({
          status: 409,
          error: {
            message: 'A location with this name already exists.',
            code: 'duplicate_name',
          },
        }))
      );

      component.form.patchValue({ name: 'Headquarters', timeZone: 'UTC' });
      component.form.markAsDirty();
      component.onSubmit();

      expect(component.duplicateNameError()).toBe(
        'A location with this name already exists.'
      );
      expect(component.isSaving()).toBeFalse();
    });

    it('should handle generic error', () => {
      locationServiceSpy.createLocation.and.returnValue(
        throwError(() => ({
          status: 500,
          error: {
            message: 'Unexpected error',
          },
        }))
      );

      component.form.patchValue({ name: 'Test', timeZone: 'UTC' });
      component.form.markAsDirty();
      component.onSubmit();

      expect(toastrSpy.error).toHaveBeenCalledWith('Unexpected error');
    });

    it('should trim whitespace from name', () => {
      component.form.patchValue({
        name: '  Trimmed Name  ',
        timeZone: 'UTC',
      });
      component.form.markAsDirty();
      component.onSubmit();

      expect(locationServiceSpy.createLocation).toHaveBeenCalledWith(
        jasmine.objectContaining({
          name: 'Trimmed Name',
        })
      );
    });

    it('should convert empty optional fields to null', () => {
      component.form.patchValue({
        name: 'Office',
        timeZone: 'UTC',
        city: '',
        phone: '  ',
      });
      component.form.markAsDirty();
      component.onSubmit();

      expect(locationServiceSpy.createLocation).toHaveBeenCalledWith(
        jasmine.objectContaining({
          city: null,
          phone: null,
        })
      );
    });
  });

  // --- Edit Mode ---------------------------------------------

  describe('edit mode', () => {
    beforeEach(() => {
      componentRef.setInput('location', mockLocation);
      fixture.detectChanges();
    });

    it('should populate form with location data', () => {
      expect(component.form.value.name).toBe('Headquarters');
      expect(component.form.value.timeZone).toBe('America/New_York');
      expect(component.form.value.city).toBe('New York');
      expect(component.form.value.country).toBe('United States');
      expect(component.form.value.isActive).toBeTrue();
    });

    it('should auto-expand address section when location has address data', () => {
      expect(component.addressExpanded()).toBeTrue();
    });

    it('should call updateLocation on submit', () => {
      component.form.patchValue({ name: 'Updated HQ' });
      component.form.markAsDirty();

      component.onSubmit();

      expect(locationServiceSpy.updateLocation).toHaveBeenCalledWith(
        'loc-1',
        jasmine.objectContaining({
          name: 'Updated HQ',
        })
      );
      expect(toastrSpy.success).toHaveBeenCalled();
    });

    it('should show default error message when backend message is missing', () => {
      locationServiceSpy.updateLocation.and.returnValue(
        throwError(() => ({
          status: 500,
          error: {},
        }))
      );

      component.form.patchValue({ name: 'Updated' });
      component.form.markAsDirty();
      component.onSubmit();

      expect(toastrSpy.error).toHaveBeenCalledWith('Failed to save location.');
    });
  });
});

// --- Pure function / UI state tests (no httpMock.verify scope) ---

describe('LocationFormComponent (UI state)', () => {
  let component: LocationFormComponent;
  let componentRef: ComponentRef<LocationFormComponent>;
  let fixture: ComponentFixture<LocationFormComponent>;

  beforeEach(async () => {
    const locationServiceSpy = jasmine.createSpyObj('LocationService', [
      'createLocation',
      'updateLocation',
    ]);
    locationServiceSpy.createLocation.and.returnValue(of({} as ILocation));
    locationServiceSpy.updateLocation.and.returnValue(of({} as ILocation));

    const toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
    ]);

    await TestBed.configureTestingModule({
      imports: [LocationFormComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: LocationService, useValue: locationServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LocationFormComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
  });

  it('should toggle address section expansion', () => {
    componentRef.setInput('location', null);
    fixture.detectChanges();

    expect(component.addressExpanded()).toBeFalse();
    component.addressExpanded.set(true);
    expect(component.addressExpanded()).toBeTrue();
  });

  it('should filter time zones by search query', () => {
    componentRef.setInput('location', null);
    fixture.detectChanges();

    component.onTzSearch('colombo');
    const filtered = component.filteredTimeZones();
    expect(filtered.length).toBeGreaterThan(0);
    expect(filtered.some((tz) => tz.id === 'Asia/Colombo')).toBeTrue();
  });

  it('should separate common and other time zones', () => {
    componentRef.setInput('location', null);
    fixture.detectChanges();

    component.onTzSearch('');
    expect(component.commonTzFiltered().length).toBeGreaterThan(0);
    expect(component.otherTzFiltered().length).toBeGreaterThan(0);
  });

  it('should filter countries by search query', () => {
    componentRef.setInput('location', null);
    fixture.detectChanges();

    component.onCountrySearch('Sri Lanka');
    const filtered = component.filteredCountries();
    expect(filtered.length).toBe(1);
    expect(filtered[0].code).toBe('LK');
  });

  it('should select a time zone and update form', () => {
    componentRef.setInput('location', null);
    fixture.detectChanges();

    component.selectTimeZone({ id: 'Asia/Colombo', label: 'Sri Lanka', utcOffset: 'UTC+05:30', isCommon: true });
    expect(component.form.get('timeZone')?.value).toBe('Asia/Colombo');
    expect(component.tzSearch()).toContain('Sri Lanka');
    expect(component.tzDropdownOpen()).toBeFalse();
  });

  it('should select a country and update form', () => {
    componentRef.setInput('location', null);
    fixture.detectChanges();

    component.selectCountry({ code: 'LK', name: 'Sri Lanka' });
    expect(component.form.get('country')?.value).toBe('Sri Lanka');
    expect(component.countrySearch()).toBe('Sri Lanka');
    expect(component.countryDropdownOpen()).toBeFalse();
  });
});
