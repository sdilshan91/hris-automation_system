import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { LocationListComponent } from './location-list.component';
import { LocationService } from '../../services/location.service';
import { ILocation } from '../../models/location.models';

describe('LocationListComponent', () => {
  let component: LocationListComponent;
  let fixture: ComponentFixture<LocationListComponent>;
  let locationServiceSpy: jasmine.SpyObj<LocationService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let routerSpy: jasmine.SpyObj<Router>;

  const mockLocations: ILocation[] = [
    {
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
    },
    {
      locationId: 'loc-2',
      tenantId: 'tenant-1',
      name: 'Branch Office',
      addressLine1: null,
      addressLine2: null,
      city: 'Colombo',
      stateProvince: null,
      country: 'Sri Lanka',
      postalCode: null,
      timeZone: 'Asia/Colombo',
      phone: null,
      isActive: true,
      employeeCount: 0,
      createdAt: '2026-02-01T00:00:00Z',
      updatedAt: '2026-02-01T00:00:00Z',
    },
    {
      locationId: 'loc-3',
      tenantId: 'tenant-1',
      name: 'Closed Office',
      addressLine1: null,
      addressLine2: null,
      city: 'London',
      stateProvince: null,
      country: 'United Kingdom',
      postalCode: null,
      timeZone: 'Europe/London',
      phone: null,
      isActive: false,
      employeeCount: 0,
      createdAt: '2026-03-01T00:00:00Z',
      updatedAt: '2026-03-01T00:00:00Z',
    },
  ];

  beforeEach(async () => {
    locationServiceSpy = jasmine.createSpyObj('LocationService', [
      'getLocations',
      'deactivateLocation',
    ]);
    locationServiceSpy.getLocations.and.returnValue(of(mockLocations));
    locationServiceSpy.deactivateLocation.and.returnValue(of(undefined));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    await TestBed.configureTestingModule({
      imports: [LocationListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: LocationService, useValue: locationServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        { provide: Router, useValue: routerSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LocationListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load locations on init', () => {
    fixture.detectChanges();
    expect(locationServiceSpy.getLocations).toHaveBeenCalled();
    expect(component.locations().length).toBe(3);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error state when loading fails', () => {
    locationServiceSpy.getLocations.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Internal server error' },
      }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Internal server error');
    expect(component.isLoading()).toBeFalse();
  });

  it('should use default error message when backend message is missing', () => {
    locationServiceSpy.getLocations.and.returnValue(
      throwError(() => ({ status: 0 }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe(
      'Failed to load locations. Please try again.'
    );
  });

  // --- Search / Filter ----------------------------------------

  it('should filter locations by name', () => {
    fixture.detectChanges();
    expect(component.filteredLocations().length).toBe(3);

    component.searchQuery.set('Head');
    expect(component.filteredLocations().length).toBe(1);
    expect(component.filteredLocations()[0].name).toBe('Headquarters');
  });

  it('should filter locations by city', () => {
    fixture.detectChanges();
    component.searchQuery.set('Colombo');
    expect(component.filteredLocations().length).toBe(1);
    expect(component.filteredLocations()[0].name).toBe('Branch Office');
  });

  it('should filter locations by country', () => {
    fixture.detectChanges();
    component.searchQuery.set('United Kingdom');
    expect(component.filteredLocations().length).toBe(1);
    expect(component.filteredLocations()[0].name).toBe('Closed Office');
  });

  it('should filter locations by time zone', () => {
    fixture.detectChanges();
    component.searchQuery.set('Asia/Colombo');
    expect(component.filteredLocations().length).toBe(1);
    expect(component.filteredLocations()[0].name).toBe('Branch Office');
  });

  it('should return all locations when search query is empty', () => {
    fixture.detectChanges();
    component.searchQuery.set('');
    expect(component.filteredLocations().length).toBe(3);
  });

  it('should return no results for non-matching query', () => {
    fixture.detectChanges();
    component.searchQuery.set('nonexistent');
    expect(component.filteredLocations().length).toBe(0);
  });

  // --- Form slide-over ----------------------------------------

  it('should open create form with null location', () => {
    fixture.detectChanges();
    component.openCreate();
    expect(component.formOpen()).toBeTrue();
    expect(component.editingLocation()).toBeNull();
  });

  it('should open edit form with the selected location', () => {
    fixture.detectChanges();
    const loc = mockLocations[0];
    component.openEdit(loc);
    expect(component.formOpen()).toBeTrue();
    expect(component.editingLocation()).toBe(loc);
  });

  it('should close form and clear editing state', () => {
    fixture.detectChanges();
    component.openEdit(mockLocations[0]);
    component.closeForm();
    expect(component.formOpen()).toBeFalse();
    expect(component.editingLocation()).toBeNull();
  });

  it('should reload locations when form saved', () => {
    fixture.detectChanges();
    locationServiceSpy.getLocations.calls.reset();

    component.onFormSaved();
    expect(component.formOpen()).toBeFalse();
    expect(locationServiceSpy.getLocations).toHaveBeenCalled();
  });

  // --- Deactivation -------------------------------------------

  it('should open deactivation dialog', () => {
    fixture.detectChanges();
    const loc = mockLocations[1]; // Branch Office
    component.confirmDeactivate(loc);
    expect(component.locationToDeactivate()).toBe(loc);
  });

  it('should cancel deactivation', () => {
    fixture.detectChanges();
    component.confirmDeactivate(mockLocations[1]);
    component.cancelDeactivate();
    expect(component.locationToDeactivate()).toBeNull();
  });

  it('should deactivate a location', () => {
    fixture.detectChanges();
    const loc = mockLocations[1]; // Branch Office, 0 employees
    component.confirmDeactivate(loc);
    component.deactivateLocation();

    expect(locationServiceSpy.deactivateLocation).toHaveBeenCalledWith(
      loc.locationId
    );
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.locationToDeactivate()).toBeNull();
  });

  it('should handle deactivation error with has_active_employees code (AC-3)', () => {
    fixture.detectChanges();
    locationServiceSpy.deactivateLocation.and.returnValue(
      throwError(() => ({
        status: 422,
        error: {
          message: 'This location has 5 active employees. Reassign them before deactivating.',
          code: 'has_active_employees',
          employeeCount: 5,
        },
      }))
    );

    const loc = mockLocations[0]; // Headquarters with 25 employees
    component.confirmDeactivate(loc);
    component.deactivateLocation();

    expect(toastrSpy.warning).toHaveBeenCalled();
    expect(component.isDeactivating()).toBeFalse();
  });

  it('should show generic error toast on unexpected deactivation failure', () => {
    fixture.detectChanges();
    locationServiceSpy.deactivateLocation.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Unexpected error' },
      }))
    );

    const loc = mockLocations[1];
    component.confirmDeactivate(loc);
    component.deactivateLocation();

    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('should do nothing if no location is selected for deactivation', () => {
    fixture.detectChanges();
    component.deactivateLocation();
    expect(locationServiceSpy.deactivateLocation).not.toHaveBeenCalled();
  });

  // --- Employee directory navigation (AC-2, FR-7) -------------

  it('should navigate to employee directory when employee count > 0', () => {
    fixture.detectChanges();
    const loc = mockLocations[0]; // 25 employees
    const event = new Event('click');
    spyOn(event, 'stopPropagation');

    component.navigateToDirectory(loc, event);

    expect(event.stopPropagation).toHaveBeenCalled();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/employees'], {
      queryParams: { location: 'Headquarters' },
    });
  });

  it('should not navigate when employee count is 0', () => {
    fixture.detectChanges();
    const loc = mockLocations[1]; // 0 employees
    const event = new Event('click');
    spyOn(event, 'stopPropagation');

    component.navigateToDirectory(loc, event);

    expect(routerSpy.navigate).not.toHaveBeenCalled();
  });
});
