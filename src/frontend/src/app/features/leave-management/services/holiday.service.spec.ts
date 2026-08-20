import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { HolidayService } from './holiday.service';
import {
  ICreateHolidayRequest,
  IUpdateHolidayRequest,
  HolidayWire,
  HolidayImportResultWire,
} from '../models/holiday.models';
import { environment } from '../../../../environments/environment';

describe('HolidayService', () => {
  let service: HolidayService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/holidays`;

  // The REAL wire shape (`HolidaysHolidayDto`, already unwrapped from the ApiResponse envelope by the
  // apiEnvelopeInterceptor). Field names match the generated contract, incl. the `createdAt`/`updatedAt`
  // the mapper must DROP and a `type` sent as a plain string that the mapper narrows.
  const wireHoliday: HolidayWire = {
    id: 'h-1',
    name: "New Year's Day",
    date: '2026-01-01',
    type: 'Public',
    locationId: null,
    locationName: null,
    description: 'Public holiday',
    isRecurring: true,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: null,
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
    it('requests holidays for a given year and maps the wire DTO to the view-model', () => {
      service.getHolidaysForYear(2026).subscribe((list) => {
        expect(list.length).toBe(1);
        expect(list[0].id).toBe('h-1');
        expect(list[0].name).toBe("New Year's Day");
        expect(list[0].type).toBe('Public');
        expect(list[0].isRecurring).toBeTrue();
        expect(list[0].isActive).toBeTrue();
        // Wire-only fields must not leak into the view-model.
        expect((list[0] as unknown as Record<string, unknown>)['createdAt']).toBeUndefined();
        expect((list[0] as unknown as Record<string, unknown>)['updatedAt']).toBeUndefined();
      });

      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('year') === '2026'
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.params.get('locationId')).toBeNull();
      req.flush([wireHoliday]);
    });

    it('includes a locationId param when provided', () => {
      service.getHolidaysForYear(2026, 'loc-1').subscribe();
      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('locationId') === 'loc-1'
      );
      expect(req.request.params.get('year')).toBe('2026');
      req.flush([]);
    });

    it('defaults a null wire `type` to the safe Public value (fails against un-migrated code)', () => {
      // The wire `type` is `string | null`; the un-migrated service returned the body verbatim, so a null
      // `type` reached the template as null. The mapper defaults it to 'Public' (the safe colour).
      service.getHolidaysForYear(2026).subscribe((list) => {
        expect(list[0].type).toBe('Public');
      });
      const req = httpMock.expectOne((r) => r.url === baseUrl);
      req.flush([{ ...wireHoliday, type: null }]);
    });
  });

  describe('getHolidaysInRange', () => {
    it('requests holidays within a from/to range (FR-6)', () => {
      service.getHolidaysInRange('2026-01-01', '2026-01-31').subscribe((list) => {
        expect(list.length).toBe(1);
        expect(list[0].id).toBe('h-1');
      });
      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl &&
          r.params.get('from') === '2026-01-01' &&
          r.params.get('to') === '2026-01-31'
      );
      expect(req.request.method).toBe('GET');
      req.flush([wireHoliday]);
    });
  });

  describe('createHoliday', () => {
    it('POSTs a create request and maps the wire response (AC-1)', () => {
      const request: ICreateHolidayRequest = {
        name: 'Labour Day',
        date: '2026-05-01',
        type: 'Public',
        locationId: null,
        description: null,
        isRecurring: true,
      };
      service.createHoliday(request).subscribe((h) => {
        expect(h.name).toBe('Labour Day');
        expect(h.id).toBe('h-1');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...wireHoliday, name: 'Labour Day', date: '2026-05-01' });
    });
  });

  describe('updateHoliday', () => {
    it('PUTs an update request and maps the wire response', () => {
      const request: IUpdateHolidayRequest = {
        name: 'Updated',
        date: '2026-01-01',
        type: 'Restricted',
        locationId: 'loc-1',
        description: 'note',
        isRecurring: false,
      };
      service.updateHoliday('h-1', request).subscribe((h) => {
        expect(h.id).toBe('h-1');
        expect(h.type).toBe('Restricted');
      });

      const req = httpMock.expectOne(`${baseUrl}/h-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      req.flush({ ...wireHoliday, name: 'Updated', type: 'Restricted' });
    });
  });

  describe('deactivateHoliday', () => {
    it('POSTs to the deactivate endpoint (BR-4)', () => {
      service.deactivateHoliday('h-1').subscribe((h) => {
        expect(h.isActive).toBeFalse();
      });
      const req = httpMock.expectOne(`${baseUrl}/h-1/deactivate`);
      expect(req.request.method).toBe('POST');
      req.flush({ ...wireHoliday, isActive: false });
    });
  });

  describe('reactivateHoliday', () => {
    it('POSTs to the reactivate endpoint', () => {
      service.reactivateHoliday('h-1').subscribe((h) => {
        expect(h.isActive).toBeTrue();
      });
      const req = httpMock.expectOne(`${baseUrl}/h-1/reactivate`);
      expect(req.request.method).toBe('POST');
      req.flush({ ...wireHoliday, isActive: true });
    });
  });

  describe('importHolidays', () => {
    it('POSTs multipart form data and maps the wire result field-names (AC-3)', () => {
      const file = new File(['name,date,type\nX,2026-01-01,public'], 'h.csv', {
        type: 'text/csv',
      });
      // The REAL wire (`HolidaysHolidayImportResult`) names these `created`/`failed`, and each error row is
      // `{ rowNumber, error, field }` — NOT the `imported`/`skipped`/`{ row, message }` the UI renders. The
      // un-migrated service returned this verbatim, so `r.imported`/`r.skipped`/`r.errors[0].row` were all
      // undefined — this arm fails against un-migrated code.
      const wireResult: HolidayImportResultWire = {
        total: 3,
        created: 2,
        failed: 1,
        errors: [{ rowNumber: 3, error: 'Duplicate date', field: 'date' }],
      };
      service.importHolidays(file).subscribe((r) => {
        expect(r.total).toBe(3);
        expect(r.imported).toBe(2);
        expect(r.skipped).toBe(1);
        expect(r.errors.length).toBe(1);
        expect(r.errors[0].row).toBe(3);
        expect(r.errors[0].message).toBe('Duplicate date');
      });

      const req = httpMock.expectOne(`${baseUrl}/import`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body instanceof FormData).toBeTrue();
      expect((req.request.body as FormData).get('file')).toBeTruthy();
      req.flush(wireResult);
    });

    it('defaults a wire result with no errors array to an empty errors list', () => {
      const file = new File([''], 'h.csv', { type: 'text/csv' });
      service.importHolidays(file).subscribe((r) => {
        expect(r.errors).toEqual([]);
        expect(r.imported).toBe(0);
      });
      const req = httpMock.expectOne(`${baseUrl}/import`);
      req.flush({ total: 0, created: 0, failed: 0 });
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
