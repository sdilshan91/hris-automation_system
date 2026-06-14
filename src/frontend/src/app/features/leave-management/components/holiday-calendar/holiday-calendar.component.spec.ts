import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { HolidayCalendarComponent } from './holiday-calendar.component';
import { HolidayService } from '../../services/holiday.service';
import { LocationService } from '../../../core-hr/locations/services/location.service';
import { IHoliday } from '../../models/holiday.models';
import { ILocation } from '../../../core-hr/locations/models/location.models';

function makeHoliday(o: Partial<IHoliday> = {}): IHoliday {
  return {
    id: 'h-1',
    name: 'New Year',
    date: '2026-01-01',
    type: 'public',
    locationId: null,
    locationName: null,
    description: null,
    isRecurring: true,
    isActive: true,
    ...o,
  };
}

function makeLocation(o: Partial<ILocation> = {}): ILocation {
  return {
    locationId: 'loc-1',
    tenantId: 't-1',
    name: 'New York',
    addressLine1: null,
    addressLine2: null,
    city: null,
    stateProvince: null,
    country: null,
    postalCode: null,
    timeZone: 'America/New_York',
    phone: null,
    isActive: true,
    employeeCount: 0,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...o,
  };
}

describe('HolidayCalendarComponent (US-LV-007)', () => {
  let component: HolidayCalendarComponent;
  let fixture: ComponentFixture<HolidayCalendarComponent>;
  let holidaySpy: jasmine.SpyObj<HolidayService>;
  let locationSpy: jasmine.SpyObj<LocationService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const holidays: IHoliday[] = [
    makeHoliday({ id: 'h-1', name: 'New Year', date: '2026-01-01', type: 'public' }),
    makeHoliday({
      id: 'h-2',
      name: 'Diwali',
      date: '2026-11-12',
      type: 'restricted',
      locationId: 'loc-1',
      locationName: 'New York',
    }),
    makeHoliday({ id: 'h-3', name: 'Founders Day', date: '2026-06-15', type: 'optional', isActive: false }),
  ];

  const locations: ILocation[] = [makeLocation()];

  function configure(initialHolidays: IHoliday[] = holidays, initialLocations: ILocation[] = locations) {
    holidaySpy = jasmine.createSpyObj<HolidayService>('HolidayService', [
      'getHolidaysForYear',
      'createHoliday',
      'updateHoliday',
      'deactivateHoliday',
      'reactivateHoliday',
      'importHolidays',
    ]);
    locationSpy = jasmine.createSpyObj<LocationService>('LocationService', ['getLocations']);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', ['success', 'error']);

    holidaySpy.getHolidaysForYear.and.returnValue(of(initialHolidays));
    locationSpy.getLocations.and.returnValue(of(initialLocations));

    TestBed.configureTestingModule({
      imports: [HolidayCalendarComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: HolidayService, useValue: holidaySpy },
        { provide: LocationService, useValue: locationSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(HolidayCalendarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('creates and loads holidays + locations on init', () => {
    configure();
    expect(component).toBeTruthy();
    expect(component.holidays().length).toBe(3);
    expect(component.locations().length).toBe(1);
    expect(component.isLoading()).toBeFalse();
  });

  it('sets an error message when the load fails', () => {
    holidaySpy = jasmine.createSpyObj<HolidayService>('HolidayService', ['getHolidaysForYear']);
    locationSpy = jasmine.createSpyObj<LocationService>('LocationService', ['getLocations']);
    holidaySpy.getHolidaysForYear.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'boom' } }))
    );
    locationSpy.getLocations.and.returnValue(of(locations));

    TestBed.configureTestingModule({
      imports: [HolidayCalendarComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: HolidayService, useValue: holidaySpy },
        { provide: LocationService, useValue: locationSpy },
      ],
    });
    fixture = TestBed.createComponent(HolidayCalendarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.loadError()).toBe('boom');
  });

  describe('view toggle (AC-4)', () => {
    beforeEach(() => configure());

    it('defaults to the calendar view', () => {
      expect(component.viewMode()).toBe('calendar');
    });

    it('switches to the list view and back', () => {
      component.setView('list');
      expect(component.viewMode()).toBe('list');
      component.setView('calendar');
      expect(component.viewMode()).toBe('calendar');
    });

    it('renders the list table when in list view', () => {
      component.setView('list');
      fixture.detectChanges();
      const table = fixture.nativeElement.querySelector('[data-test="list-table"]');
      expect(table).toBeTruthy();
    });
  });

  describe('calendar rendering + type colors (AC-4)', () => {
    beforeEach(() => configure());

    it('builds a 42-cell month grid', () => {
      expect(component.monthGrid().length).toBe(42);
    });

    it('only shows active holidays in the calendar markers', () => {
      // h-3 (Founders Day) is inactive and must be excluded.
      const ids = component.activeFilteredHolidays().map((h) => h.id);
      expect(ids).toContain('h-1');
      expect(ids).not.toContain('h-3');
    });

    it('maps holiday types to their documented colors', () => {
      expect(component.typeColor('public')).toBe('#2563eb');
      expect(component.typeColor('restricted')).toBe('#ea580c');
      expect(component.typeColor('optional')).toBe('#16a34a');
    });

    it('groups active holidays by month for the mobile fallback (NFR-4)', () => {
      const byMonth = component.holidaysByMonth();
      expect(byMonth.length).toBe(12);
      expect(byMonth[0].map((h) => h.id)).toContain('h-1'); // Jan
    });
  });

  describe('year navigation (AC-4)', () => {
    beforeEach(() => configure());

    it('reloads holidays when the year changes via arrows', () => {
      const before = component.year();
      holidaySpy.getHolidaysForYear.calls.reset();
      holidaySpy.getHolidaysForYear.and.returnValue(of([]));
      component.changeYear(1);
      expect(component.year()).toBe(before + 1);
      expect(holidaySpy.getHolidaysForYear).toHaveBeenCalledWith(before + 1);
    });

    it('reloads when a new year is typed in', () => {
      holidaySpy.getHolidaysForYear.calls.reset();
      holidaySpy.getHolidaysForYear.and.returnValue(of([]));
      component.setYear(2030);
      expect(component.year()).toBe(2030);
      expect(holidaySpy.getHolidaysForYear).toHaveBeenCalledWith(2030);
    });

    it('ignores a no-op year change', () => {
      holidaySpy.getHolidaysForYear.calls.reset();
      component.setYear(component.year());
      expect(holidaySpy.getHolidaysForYear).not.toHaveBeenCalled();
    });
  });

  describe('location filter', () => {
    beforeEach(() => configure());

    it('shows location-specific + tenant-wide holidays when a location is chosen', () => {
      component.setLocationFilter('loc-1');
      const ids = component.filteredHolidays().map((h) => h.id);
      // h-2 is scoped to loc-1; h-1/h-3 are tenant-wide (null location)
      expect(ids).toContain('h-2');
      expect(ids).toContain('h-1');
    });

    it('excludes holidays scoped to another location', () => {
      component.setLocationFilter('loc-OTHER');
      const ids = component.filteredHolidays().map((h) => h.id);
      expect(ids).not.toContain('h-2'); // belongs to loc-1
      expect(ids).toContain('h-1'); // tenant-wide
    });
  });

  describe('empty state', () => {
    it('reports no holidays when the list is empty', () => {
      configure([]);
      expect(component.hasHolidays()).toBeFalse();
      component.setView('list');
      fixture.detectChanges();
      const empty = fixture.nativeElement.querySelector('[data-test="empty-state"]');
      expect(empty).toBeTruthy();
    });
  });

  describe('add/edit form validation (AC-1)', () => {
    beforeEach(() => configure());

    it('opens the create form prefilled when a calendar cell is clicked', () => {
      component.openCreateForCell({ date: '2026-03-17', day: 17, inMonth: true, holidays: [] });
      expect(component.formOpen()).toBeTrue();
      expect(component.form.controls['date'].value).toBe('2026-03-17');
      expect(component.editingHoliday()).toBeNull();
    });

    it('does not submit an invalid form', () => {
      component.openCreate();
      component.form.controls['name'].setValue('');
      component.submitForm();
      expect(holidaySpy.createHoliday).not.toHaveBeenCalled();
      expect(component.form.controls['name'].touched).toBeTrue();
    });

    it('creates a holiday with normalized payload (AC-1)', () => {
      const created = makeHoliday({ id: 'h-new', name: 'Labour Day', date: '2026-05-01' });
      holidaySpy.createHoliday.and.returnValue(of(created));

      component.openCreate();
      component.form.setValue({
        name: '  Labour Day  ',
        date: '2026-05-01',
        type: 'public',
        locationId: '',
        description: '',
        isRecurring: true,
      });
      component.submitForm();

      const payload = holidaySpy.createHoliday.calls.mostRecent().args[0];
      expect(payload.name).toBe('Labour Day'); // trimmed
      expect(payload.locationId).toBeNull(); // empty -> null
      expect(payload.description).toBeNull();
      expect(component.formOpen()).toBeFalse();
      expect(toastrSpy.success).toHaveBeenCalled();
    });

    it('loads an existing holiday into the form on edit', () => {
      component.openEdit(holidays[1]);
      expect(component.editingHoliday()!.id).toBe('h-2');
      expect(component.form.controls['type'].value).toBe('restricted');
      expect(component.form.controls['locationId'].value).toBe('loc-1');
    });

    it('surfaces a backend error via toast on save failure', () => {
      holidaySpy.createHoliday.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'Duplicate date', code: 'duplicate_date' } }))
      );
      component.openCreate();
      component.form.patchValue({ name: 'Dup', date: '2026-01-01', type: 'public' });
      component.submitForm();
      expect(toastrSpy.error).toHaveBeenCalledWith('Duplicate date');
      expect(component.formOpen()).toBeTrue(); // stays open on error
    });
  });

  describe('deactivate / reactivate (BR-4)', () => {
    beforeEach(() => configure());

    it('deactivates an active holiday', () => {
      holidaySpy.deactivateHoliday.and.returnValue(of(makeHoliday({ id: 'h-1', isActive: false })));
      component.toggleActive(holidays[0]);
      expect(holidaySpy.deactivateHoliday).toHaveBeenCalledWith('h-1');
      expect(component.holidays().find((h) => h.id === 'h-1')!.isActive).toBeFalse();
    });

    it('surfaces a payroll-lock error verbatim (BR-4)', () => {
      holidaySpy.deactivateHoliday.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 409, error: { message: 'Within a locked payroll period', code: 'payroll_locked' } }))
      );
      component.toggleActive(holidays[0]);
      expect(toastrSpy.error).toHaveBeenCalledWith('Within a locked payroll period');
    });
  });

  describe('CSV import (AC-3)', () => {
    beforeEach(() => configure());

    function csvFile(content: string): File {
      const file = new File([content], 'holidays.csv', { type: 'text/csv' });
      // Override the async FileReader seam to resolve deterministically with the
      // exact content (File.text() is unreliable under fakeAsync).
      (component as unknown as { readFileText: (f: File) => Promise<string> }).readFileText =
        () => Promise.resolve(content);
      return file;
    }

    function selectFile(content: string): void {
      const file = csvFile(content);
      const input = { target: { files: [file], value: '' } } as unknown as Event;
      component.onImportFileSelected(input);
      flushMicrotasks();
    }

    it('rejects a non-CSV file', () => {
      component.openImport();
      const input = { target: { files: [new File(['x'], 'bad.txt')], value: '' } } as unknown as Event;
      component.onImportFileSelected(input);
      expect(component.importError()).toContain('Only .csv');
      expect(component.importFile()).toBeNull();
    });

    it('builds a preview with valid + duplicate + invalid counts', fakeAsync(() => {
      component.openImport();
      selectFile(
        [
          'name,date,type',
          'New Year,2026-01-01,public', // duplicate of existing h-1
          'Good Day,2026-07-04,public', // valid
          'Bad,not-a-date,public', // invalid
        ].join('\n')
      );
      const preview = component.importPreview();
      expect(preview).toBeTruthy();
      expect(preview!.validCount).toBe(1);
      expect(preview!.duplicateCount).toBe(1);
      expect(preview!.invalidCount).toBe(1);
      expect(component.canConfirmImport()).toBeTrue();
    }));

    it('blocks confirm when there are no valid rows', fakeAsync(() => {
      component.openImport();
      selectFile('name,date,type\nBad,not-a-date,public');
      expect(component.canConfirmImport()).toBeFalse();
    }));

    it('confirms the import, reloads, and toasts the result', fakeAsync(() => {
      holidaySpy.importHolidays.and.returnValue(
        of({ total: 2, imported: 2, skipped: 0, errors: [] })
      );
      holidaySpy.getHolidaysForYear.calls.reset();
      holidaySpy.getHolidaysForYear.and.returnValue(of([]));

      component.openImport();
      selectFile('name,date,type\nGood Day,2026-07-04,public');
      component.confirmImport();

      expect(holidaySpy.importHolidays).toHaveBeenCalled();
      expect(toastrSpy.success).toHaveBeenCalled();
      expect(component.importOpen()).toBeFalse();
      expect(holidaySpy.getHolidaysForYear).toHaveBeenCalled();
    }));

    it('surfaces a backend import error in the panel', fakeAsync(() => {
      holidaySpy.importHolidays.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: { message: 'Import rejected' } }))
      );
      component.openImport();
      selectFile('name,date,type\nGood Day,2026-07-04,public');
      component.confirmImport();
      expect(component.importError()).toBe('Import rejected');
      expect(component.importOpen()).toBeTrue();
    }));
  });
});
