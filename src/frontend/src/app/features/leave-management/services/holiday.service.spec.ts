import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { HolidayService } from './holiday.service';
import {
  IHoliday,
  ICreateHolidayRequest,
  IUpdateHolidayRequest,
  IHolidayImportResult,
} from '../models/holiday.models';
import { environment } from '../../../../environments/environment';

describe('HolidayService', () => {
  let service: HolidayService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/holidays`;

  const mockHoliday: IHoliday = {
    id: 'h-1',
    name: "New Year's Day",
    date: '2026-01-01',
    type: 'public',
    locationId: null,
    locationName: null,
    description: 'Public holiday',
    isRecurring: true,
    isActive: true,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        HolidayService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(HolidayService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getHolidaysForYear', () => {
    it('requests holidays for a given year', () => {
      service.getHolidaysForYear(2026).subscribe((list) => {
        expect(list.length).toBe(1);
        expect(list[0].name).toBe("New Year's Day");
      });

      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('year') === '2026'
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.params.get('locationId')).toBeNull();
      req.flush([mockHoliday]);
    });

    it('includes a locationId param when provided', () => {
      service.getHolidaysForYear(2026, 'loc-1').subscribe();
      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('locationId') === 'loc-1'
      );
      expect(req.request.params.get('year')).toBe('2026');
      req.flush([]);
    });
  });

  describe('getHolidaysInRange', () => {
    it('requests holidays within a from/to range (FR-6)', () => {
      service.getHolidaysInRange('2026-01-01', '2026-01-31').subscribe();
      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl &&
          r.params.get('from') === '2026-01-01' &&
          r.params.get('to') === '2026-01-31'
      );
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });
  });

  describe('createHoliday', () => {
    it('POSTs a create request (AC-1)', () => {
      const request: ICreateHolidayRequest = {
        name: 'Labour Day',
        date: '2026-05-01',
        type: 'public',
        locationId: null,
        description: null,
        isRecurring: true,
      };
      service.createHoliday(request).subscribe((h) => {
        expect(h.name).toBe('Labour Day');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockHoliday, name: 'Labour Day', date: '2026-05-01' });
    });
  });

  describe('updateHoliday', () => {
    it('PUTs an update request', () => {
      const request: IUpdateHolidayRequest = {
        name: 'Updated',
        date: '2026-01-01',
        type: 'restricted',
        locationId: 'loc-1',
        description: 'note',
        isRecurring: false,
      };
      service.updateHoliday('h-1', request).subscribe((h) => {
        expect(h.id).toBe('h-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/h-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      req.flush({ ...mockHoliday, name: 'Updated', type: 'restricted' });
    });
  });

  describe('deactivateHoliday', () => {
    it('POSTs to the deactivate endpoint (BR-4)', () => {
      service.deactivateHoliday('h-1').subscribe((h) => {
        expect(h.isActive).toBeFalse();
      });
      const req = httpMock.expectOne(`${baseUrl}/h-1/deactivate`);
      expect(req.request.method).toBe('POST');
      req.flush({ ...mockHoliday, isActive: false });
    });
  });

  describe('reactivateHoliday', () => {
    it('POSTs to the reactivate endpoint', () => {
      service.reactivateHoliday('h-1').subscribe((h) => {
        expect(h.isActive).toBeTrue();
      });
      const req = httpMock.expectOne(`${baseUrl}/h-1/reactivate`);
      expect(req.request.method).toBe('POST');
      req.flush({ ...mockHoliday, isActive: true });
    });
  });

  describe('importHolidays', () => {
    it('POSTs multipart form data to the import endpoint (AC-3)', () => {
      const file = new File(['name,date,type\nX,2026-01-01,public'], 'h.csv', {
        type: 'text/csv',
      });
      const result: IHolidayImportResult = {
        total: 1,
        imported: 1,
        skipped: 0,
        errors: [],
      };
      service.importHolidays(file).subscribe((r) => {
        expect(r.imported).toBe(1);
      });

      const req = httpMock.expectOne(`${baseUrl}/import`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body instanceof FormData).toBeTrue();
      expect((req.request.body as FormData).get('file')).toBeTruthy();
      req.flush(result);
    });
  });

  describe('parseError', () => {
    it('parses a typed error body', () => {
      const err = {
        error: { message: 'Duplicate date', code: 'duplicate_date' },
      } as HttpErrorResponse;
      const parsed = HolidayService.parseError(err);
      expect(parsed!.message).toBe('Duplicate date');
      expect(parsed!.code).toBe('duplicate_date');
    });

    it('returns null for a non-object body', () => {
      expect(
        HolidayService.parseError({ error: 'oops' } as HttpErrorResponse)
      ).toBeNull();
    });
  });
});
