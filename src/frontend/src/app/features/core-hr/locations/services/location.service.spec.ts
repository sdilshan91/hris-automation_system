import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { LocationService } from './location.service';
import {
  ILocation,
  ICreateLocationRequest,
  IUpdateLocationRequest,
} from '../models/location.models';
import { environment } from '../../../../../environments/environment';

describe('LocationService', () => {
  let service: LocationService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/locations`;

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

  const mockLocation2: ILocation = {
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
    employeeCount: 10,
    createdAt: '2026-02-01T00:00:00Z',
    updatedAt: '2026-02-01T00:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        LocationService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(LocationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getLocations', () => {
    it('should return all locations for the tenant', () => {
      service.getLocations().subscribe((locations) => {
        expect(locations.length).toBe(2);
        expect(locations[0].name).toBe('Headquarters');
        expect(locations[1].name).toBe('Branch Office');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockLocation, mockLocation2]);
    });

    it('should pass activeOnly param when specified', () => {
      service.getLocations(true).subscribe((locations) => {
        expect(locations.length).toBe(1);
      });

      const req = httpMock.expectOne(`${baseUrl}?activeOnly=true`);
      expect(req.request.method).toBe('GET');
      req.flush([mockLocation]);
    });

    it('should return an empty array when no locations exist', () => {
      service.getLocations().subscribe((locations) => {
        expect(locations.length).toBe(0);
      });

      const req = httpMock.expectOne(baseUrl);
      req.flush([]);
    });
  });

  describe('getLocation', () => {
    it('should return a single location by ID', () => {
      service.getLocation('loc-1').subscribe((location) => {
        expect(location.locationId).toBe('loc-1');
        expect(location.name).toBe('Headquarters');
      });

      const req = httpMock.expectOne(`${baseUrl}/loc-1`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockLocation);
    });
  });

  describe('createLocation', () => {
    it('should create a new location', () => {
      const request: ICreateLocationRequest = {
        name: 'New Office',
        city: 'London',
        country: 'United Kingdom',
        timeZone: 'Europe/London',
        isActive: true,
      };

      service.createLocation(request).subscribe((location) => {
        expect(location.name).toBe('New Office');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({
        ...mockLocation,
        locationId: 'loc-3',
        name: 'New Office',
        city: 'London',
        country: 'United Kingdom',
        timeZone: 'Europe/London',
      });
    });

    it('should create a location with only required fields', () => {
      const request: ICreateLocationRequest = {
        name: 'Minimal Office',
        timeZone: 'UTC',
        isActive: true,
      };

      service.createLocation(request).subscribe((location) => {
        expect(location.name).toBe('Minimal Office');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.body.city).toBeUndefined();
      req.flush({ ...mockLocation, name: 'Minimal Office', timeZone: 'UTC' });
    });
  });

  describe('updateLocation', () => {
    it('should update an existing location', () => {
      const request: IUpdateLocationRequest = {
        name: 'Updated HQ',
        timeZone: 'America/Chicago',
        isActive: true,
      };

      service.updateLocation('loc-1', request).subscribe((location) => {
        expect(location.name).toBe('Updated HQ');
      });

      const req = httpMock.expectOne(`${baseUrl}/loc-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockLocation, name: 'Updated HQ', timeZone: 'America/Chicago' });
    });
  });

  describe('deactivateLocation', () => {
    it('should deactivate a location (FR-5, FR-6)', () => {
      service.deactivateLocation('loc-1').subscribe();

      const req = httpMock.expectOne(`${baseUrl}/loc-1/deactivate`);
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(null);
    });
  });
});
