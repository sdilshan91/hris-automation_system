import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { TeamCalendarService } from './team-calendar.service';
import {
  ITeamCalendarResponse,
  ITeamCalendarEntry,
} from '../models/team-calendar.models';
import { environment } from '../../../../environments/environment';

describe('TeamCalendarService (US-LV-009)', () => {
  let service: TeamCalendarService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/leaves/team-calendar`;

  const managerEntry: ITeamCalendarEntry = {
    employeeId: 'e-1',
    employeeName: 'Ada Lovelace',
    leaveTypeName: 'Annual Leave',
    color: '#2563eb',
    startDate: '2026-06-10',
    endDate: '2026-06-12',
    status: 'Approved',
    totalDays: 3,
    isHalfDay: false,
    halfDaySession: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        TeamCalendarService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(TeamCalendarService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getTeamCalendar', () => {
    it('GETs with from/to date-range params (FR-1)', () => {
      service.getTeamCalendar('2026-06-01', '2026-06-30').subscribe((res) => {
        expect(res.entries.length).toBe(1);
        expect(res.holidays.length).toBe(1);
      });

      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl &&
          r.params.get('from') === '2026-06-01' &&
          r.params.get('to') === '2026-06-30'
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.params.get('employeeId')).toBeNull();
      expect(req.request.params.get('status')).toBeNull();
      const body: ITeamCalendarResponse = {
        entries: [managerEntry],
        holidays: [{ date: '2026-06-15', name: 'Mid-year Holiday' }],
      };
      req.flush(body);
    });

    it('passes employee + status filter params when provided (FR-6)', () => {
      service
        .getTeamCalendar('2026-06-01', '2026-06-30', {
          employeeId: 'e-1',
          status: 'Pending',
        })
        .subscribe();

      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl &&
          r.params.get('employeeId') === 'e-1' &&
          r.params.get('status') === 'Pending'
      );
      expect(req.request.params.get('employeeId')).toBe('e-1');
      expect(req.request.params.get('status')).toBe('Pending');
      req.flush({ entries: [], holidays: [] });
    });

    it('normalizes a bare entries array body to the combined shape', () => {
      service.getTeamCalendar('2026-06-01', '2026-06-30').subscribe((res) => {
        expect(res.entries.length).toBe(1);
        expect(res.holidays).toEqual([]);
      });
      const req = httpMock.expectOne((r) => r.url === baseUrl);
      req.flush([managerEntry]);
    });

    it('tolerates a null body (renders empty)', () => {
      service.getTeamCalendar('2026-06-01', '2026-06-30').subscribe((res) => {
        expect(res.entries).toEqual([]);
        expect(res.holidays).toEqual([]);
      });
      const req = httpMock.expectOne((r) => r.url === baseUrl);
      req.flush(null);
    });
  });

  describe('normalize', () => {
    it('wraps a bare array', () => {
      const res = TeamCalendarService.normalize([managerEntry]);
      expect(res.entries.length).toBe(1);
      expect(res.holidays).toEqual([]);
    });

    it('defaults missing fields on a combined object', () => {
      const res = TeamCalendarService.normalize({} as ITeamCalendarResponse);
      expect(res.entries).toEqual([]);
      expect(res.holidays).toEqual([]);
    });
  });

  describe('parseError', () => {
    it('parses a typed error body', () => {
      const err = {
        error: { message: 'No team', code: 'no_team' },
      } as HttpErrorResponse;
      const parsed = TeamCalendarService.parseError(err);
      expect(parsed!.message).toBe('No team');
      expect(parsed!.code).toBe('no_team');
    });

    it('returns null for a non-object body', () => {
      expect(
        TeamCalendarService.parseError({ error: 'oops' } as HttpErrorResponse)
      ).toBeNull();
    });
  });
});
