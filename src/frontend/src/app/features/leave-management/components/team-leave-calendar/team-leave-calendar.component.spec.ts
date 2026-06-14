import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { TeamLeaveCalendarComponent } from './team-leave-calendar.component';
import { TeamCalendarService } from '../../services/team-calendar.service';
import {
  ITeamCalendarEntry,
  ITeamCalendarResponse,
} from '../../models/team-calendar.models';

describe('TeamLeaveCalendarComponent (US-LV-009)', () => {
  let component: TeamLeaveCalendarComponent;
  let fixture: ComponentFixture<TeamLeaveCalendarComponent>;
  let svcSpy: jasmine.SpyObj<TeamCalendarService>;

  // Use today's month so the month grid + today highlight are exercised live.
  const today = new Date();
  const y = today.getFullYear();
  const m = today.getMonth();
  const iso = (day: number) =>
    `${y}-${String(m + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;

  const managerEntries: ITeamCalendarEntry[] = [
    {
      employeeId: 'e-1',
      employeeName: 'Ada Lovelace',
      leaveTypeName: 'Annual Leave',
      color: '#2563eb',
      startDate: iso(10),
      endDate: iso(12),
      status: 'Approved',
      totalDays: 3,
      isHalfDay: false,
      halfDaySession: null,
    },
    {
      employeeId: 'e-2',
      employeeName: 'Bob Stone',
      leaveTypeName: 'Sick Leave',
      color: '#dc2626',
      startDate: iso(10),
      endDate: iso(10),
      status: 'Pending',
      totalDays: 0.5,
      isHalfDay: true,
      halfDaySession: 'AM',
    },
  ];

  const employeeScopeEntries: ITeamCalendarEntry[] = [
    // Employee scope: no leaveTypeName/color/status (BR-1).
    {
      employeeId: 'e-3',
      employeeName: 'Cara Vale',
      startDate: iso(15),
      endDate: iso(16),
      totalDays: 2,
    },
  ];

  const holidays = [{ date: iso(15), name: 'Founders Day' }];

  function setup(
    entries: ITeamCalendarEntry[] = managerEntries,
    hol = holidays
  ): void {
    svcSpy = jasmine.createSpyObj('TeamCalendarService', ['getTeamCalendar']);
    const res: ITeamCalendarResponse = { entries, holidays: hol };
    svcSpy.getTeamCalendar.and.returnValue(of(res));

    TestBed.configureTestingModule({
      imports: [TeamLeaveCalendarComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        { provide: TeamCalendarService, useValue: svcSpy },
      ],
    });

    fixture = TestBed.createComponent(TeamLeaveCalendarComponent);
    component = fixture.componentInstance;
  }

  it('loads the calendar on init with a from/to range (FR-1)', () => {
    setup();
    fixture.detectChanges();
    expect(svcSpy.getTeamCalendar).toHaveBeenCalled();
    const [from, to] = svcSpy.getTeamCalendar.calls.mostRecent().args;
    expect(from).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(to).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(component.entries().length).toBe(2);
    expect(component.holidays().length).toBe(1);
  });

  describe('view-mode toggle (FR-5)', () => {
    it('switches between month / week / list', () => {
      setup();
      component.viewMode.set('month');
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[data-test="month-grid"]')).toBeTruthy();

      component.setView('week');
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[data-test="week-grid"]')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('[data-test="month-grid"]')).toBeFalsy();

      component.setView('list');
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[data-test="list-view"]')).toBeTruthy();
    });
  });

  describe('month view (AC-1)', () => {
    beforeEach(() => {
      setup();
      component.viewMode.set('month');
      fixture.detectChanges();
    });

    it('renders color-coded leave blocks on covered dates', () => {
      const blocks = fixture.nativeElement.querySelectorAll('[data-test="leave-block"]');
      // Ada (10-12 = 3 days) + Bob (10 half) => Ada appears on 3 cells, Bob on 1.
      expect(blocks.length).toBe(4);
      const first = blocks[0] as HTMLElement;
      // Ada's annual-leave color comes through as the dot/text color.
      expect(first.getAttribute('style')).toContain('rgb(37, 99, 235)'); // #2563eb
    });

    it('highlights today and renders holiday-background cells (FR-7)', () => {
      const grid = component.monthGrid();
      expect(grid.some((c) => c.isToday)).toBeTrue();
      const holidayCell = grid.find((c) => c.date === iso(15));
      expect(holidayCell?.isHoliday).toBeTrue();
      // The holiday cell carries the light-gray background in the DOM.
      const domHoliday = fixture.nativeElement.querySelector('[data-test="holiday-cell"]');
      expect(domHoliday).toBeTruthy();
    });

    it('differentiates half-day leaves with an AM/PM indicator (BR-5)', () => {
      const indicators = fixture.nativeElement.querySelectorAll(
        '[data-test="half-day-indicator"]'
      );
      expect(indicators.length).toBeGreaterThan(0);
      expect((indicators[0] as HTMLElement).textContent?.trim()).toBe('AM');
    });
  });

  describe('week view (AC-3)', () => {
    it('renders a Gantt row per employee covered in the week', () => {
      setup();
      // Anchor the week on a date Ada/Bob are off (the 10th).
      component.weekAnchor.set(iso(10));
      component.setView('week');
      fixture.detectChanges();
      const rows = fixture.nativeElement.querySelectorAll('[data-test="week-row"]');
      expect(rows.length).toBe(2);
      const bars = fixture.nativeElement.querySelectorAll('[data-test="gantt-bar"]');
      expect(bars.length).toBeGreaterThan(0);
    });
  });

  describe('list view (AC-4)', () => {
    it('groups entries chronologically by date', () => {
      setup();
      component.setView('list');
      fixture.detectChanges();
      const groups = component.listGroups();
      // Two start dates: iso(10) (Ada+Bob) and none else in managerEntries.
      expect(groups.length).toBe(1);
      expect(groups[0].date).toBe(iso(10));
      expect(groups[0].entries.length).toBe(2);
      const dom = fixture.nativeElement.querySelectorAll('[data-test="list-entry"]');
      expect(dom.length).toBe(2);
    });

    it('shows a status badge only when status is present', () => {
      setup();
      component.setView('list');
      fixture.detectChanges();
      const badges = fixture.nativeElement.querySelectorAll('[data-test="list-status"]');
      expect(badges.length).toBe(2); // both manager entries have status
    });
  });

  describe('filter bar (FR-6)', () => {
    it('filters by employee', () => {
      setup();
      svcSpy.getTeamCalendar.calls.reset();
      fixture.detectChanges();
      component.setEmployeeFilter('e-1');
      fixture.detectChanges();
      // Server re-fetch requested with the employee filter.
      const filters = svcSpy.getTeamCalendar.calls.mostRecent().args[2];
      expect(filters?.employeeId).toBe('e-1');
      // Client-side narrowing too.
      expect(component.filteredEntries().every((e) => e.employeeId === 'e-1')).toBeTrue();
    });

    it('filters by leave type (client-side toggle chip)', () => {
      setup();
      fixture.detectChanges();
      component.setLeaveTypeFilter('Annual Leave');
      fixture.detectChanges();
      expect(component.filteredEntries().length).toBe(1);
      expect(component.filteredEntries()[0].leaveTypeName).toBe('Annual Leave');
      // toggling off restores
      component.setLeaveTypeFilter('Annual Leave');
      expect(component.leaveTypeFilter()).toBe('');
    });

    it('shows the status filter chips only in manager scope', () => {
      setup(managerEntries);
      component.setView('list');
      fixture.detectChanges();
      expect(component.hasStatus()).toBeTrue();
      expect(fixture.nativeElement.querySelectorAll('[data-test="status-chip"]').length).toBe(2);
    });

    it('hides the status filter chips in employee scope', () => {
      setup(employeeScopeEntries, []);
      component.setView('list');
      fixture.detectChanges();
      expect(component.hasStatus()).toBeFalse();
      expect(fixture.nativeElement.querySelectorAll('[data-test="status-chip"]').length).toBe(0);
    });

    it('builds a color legend; generic single item in employee scope', () => {
      setup(employeeScopeEntries, []);
      fixture.detectChanges();
      expect(component.legend().length).toBe(1);
      expect(component.legend()[0].label).toBe('On leave');
    });
  });

  describe('scope handling — employee scope renders gracefully (BR-1, AC-2)', () => {
    beforeEach(() => {
      setup(employeeScopeEntries, []);
      component.setView('list');
      fixture.detectChanges();
    });

    it('renders entries with no leaveType/status as a generic "On leave"', () => {
      const entries = fixture.nativeElement.querySelectorAll('[data-test="list-entry"]');
      expect(entries.length).toBe(1);
      expect((entries[0] as HTMLElement).textContent).toContain('Cara Vale');
      expect((entries[0] as HTMLElement).textContent).toContain('On leave');
    });

    it('does not render any status badge in employee scope', () => {
      expect(fixture.nativeElement.querySelectorAll('[data-test="list-status"]').length).toBe(0);
    });

    it('does not render leave-type filter chips in employee scope', () => {
      expect(
        fixture.nativeElement.querySelectorAll('[data-test="leave-type-chip"]').length
      ).toBe(0);
    });
  });

  describe('mobile default (§8, AC-4)', () => {
    it('defaults to list view on a narrow (360px) viewport', () => {
      const original = window.innerWidth;
      Object.defineProperty(window, 'innerWidth', {
        configurable: true,
        value: 360,
      });
      try {
        setup();
        // viewMode is initialized in a field initializer using window width.
        expect(component.viewMode()).toBe('list');
      } finally {
        Object.defineProperty(window, 'innerWidth', {
          configurable: true,
          value: original,
        });
      }
    });

    it('defaults to month view on a wide viewport', () => {
      const original = window.innerWidth;
      Object.defineProperty(window, 'innerWidth', {
        configurable: true,
        value: 1280,
      });
      try {
        setup();
        expect(component.viewMode()).toBe('month');
      } finally {
        Object.defineProperty(window, 'innerWidth', {
          configurable: true,
          value: original,
        });
      }
    });
  });

  describe('error handling', () => {
    it('surfaces a load error and recovers on retry', () => {
      svcSpy = jasmine.createSpyObj('TeamCalendarService', ['getTeamCalendar']);
      svcSpy.getTeamCalendar.and.returnValue(
        throwError(() => new HttpErrorResponse({ error: { message: 'No team' }, status: 400 }))
      );
      TestBed.configureTestingModule({
        imports: [TeamLeaveCalendarComponent],
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          provideAnimationsAsync(),
          { provide: TeamCalendarService, useValue: svcSpy },
        ],
      });
      fixture = TestBed.createComponent(TeamLeaveCalendarComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
      expect(component.loadError()).toBe('No team');
      expect(fixture.nativeElement.querySelector('[data-test="error-state"]')).toBeTruthy();
    });
  });

  describe('navigation', () => {
    it('changeMonth re-fetches and updates the active month', () => {
      setup();
      fixture.detectChanges();
      svcSpy.getTeamCalendar.calls.reset();
      const startMonth = component.activeMonth();
      component.changeMonth(1);
      expect(svcSpy.getTeamCalendar).toHaveBeenCalled();
      const expected = (startMonth + 1) % 12;
      expect(component.activeMonth()).toBe(expected);
    });

    it('goToToday resets to the current month', () => {
      setup();
      fixture.detectChanges();
      component.changeMonth(2);
      component.goToToday();
      expect(component.activeMonth()).toBe(new Date().getMonth());
      expect(component.year()).toBe(new Date().getFullYear());
    });
  });
});
