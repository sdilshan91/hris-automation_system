import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { PayrollRunsComponent } from './payroll-runs.component';
import { PayrollRunService } from '../../services/payroll-run.service';
import { IPayrollRun } from '../../models/payroll-run.models';

describe('PayrollRunsComponent', () => {
  let fixture: ComponentFixture<PayrollRunsComponent>;
  let component: PayrollRunsComponent;
  let runs: jasmine.SpyObj<PayrollRunService>;

  const finalized: IPayrollRun = {
    id: 'r-1',
    payMonth: 5,
    payYear: 2026,
    status: 'Finalized',
    totalEmployees: 250,
    processedEmployees: 247,
    skippedEmployees: 3,
    totalGross: 1000000,
    totalDeductions: 200000,
    totalNet: 800000,
    initiatedByName: 'Alex HR',
    initiatedAt: '2026-05-31T10:00:00Z',
    completedAt: '2026-05-31T10:05:00Z',
  };
  const processing: IPayrollRun = {
    ...finalized,
    id: 'r-2',
    payMonth: 6,
    payYear: 2026,
    status: 'Processing',
    totalNet: 0,
    initiatedAt: '2026-06-30T10:00:00Z',
  };

  beforeEach(async () => {
    runs = jasmine.createSpyObj<PayrollRunService>('PayrollRunService', [
      'listRuns',
    ]);
    runs.listRuns.and.returnValue(of([finalized, processing]));

    await TestBed.configureTestingModule({
      imports: [PayrollRunsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: PayrollRunService, useValue: runs },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PayrollRunsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('creates and loads runs on init', () => {
    expect(component).toBeTruthy();
    expect(runs.listRuns).toHaveBeenCalled();
    expect(component.runs().length).toBe(2);
    expect(component.loading()).toBeFalse();
  });

  it('renders rows for each run', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('May 2026');
    expect(text).toContain('June 2026');
    expect(text).toContain('Finalized');
  });

  it('sets an error when the load fails', () => {
    runs.listRuns.and.returnValue(throwError(() => new Error('boom')));
    component.load();
    expect(component.error()).toBe('Could not load payroll runs.');
    expect(component.loading()).toBeFalse();
  });

  describe('filtering', () => {
    it('filters by status', () => {
      component.setFilter('Finalized');
      expect(component.visibleRuns().length).toBe(1);
      expect(component.visibleRuns()[0].id).toBe('r-1');
    });

    it('shows all when the filter is cleared', () => {
      component.setFilter('Finalized');
      component.setFilter(null);
      expect(component.visibleRuns().length).toBe(2);
    });
  });

  describe('sorting', () => {
    it('toggles direction when the same key is clicked twice', () => {
      component.sortBy('net'); // ascending
      expect(component.sortAsc()).toBeTrue();
      expect(component.visibleRuns()[0].id).toBe('r-2'); // 0 net first asc

      component.sortBy('net'); // descending
      expect(component.sortAsc()).toBeFalse();
      expect(component.visibleRuns()[0].id).toBe('r-1'); // 800000 net first desc
    });

    it('switches key and resets to ascending', () => {
      component.sortBy('period');
      expect(component.sortKey()).toBe('period');
      expect(component.sortAsc()).toBeTrue();
      expect(component.visibleRuns()[0].id).toBe('r-1'); // May before June asc
    });

    it('exposes a sort indicator only for the active key', () => {
      component.sortBy('employees');
      expect(component.sortIndicator('employees')).toBe('↑');
      expect(component.sortIndicator('net')).toBe('');
    });
  });

  describe('period label', () => {
    it('maps the pay month to a name', () => {
      expect(component.periodLabel(finalized)).toBe('May 2026');
    });
  });

  describe('new run modal', () => {
    it('opens and closes the modal', () => {
      expect(component.newOpen()).toBeFalse();
      component.openNew();
      expect(component.newOpen()).toBeTrue();
    });

    it('routes to the run detail when a run is created', () => {
      const router = TestBed.inject(Router);
      const navigate = spyOn(router, 'navigate');
      component.openNew();
      component.onCreated(processing);
      expect(component.newOpen()).toBeFalse();
      expect(navigate).toHaveBeenCalledWith(['/payroll', 'runs', 'r-2']);
    });
  });

  it('renders the empty state when there are no runs', () => {
    runs.listRuns.and.returnValue(of([]));
    component.load();
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No payroll runs yet');
  });
});
