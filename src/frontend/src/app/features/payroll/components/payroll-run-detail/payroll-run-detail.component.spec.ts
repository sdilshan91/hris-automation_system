import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError, Subject } from 'rxjs';

import { PayrollRunDetailComponent } from './payroll-run-detail.component';
import { PayrollRunService } from '../../services/payroll-run.service';
import {
  IPayrollRun,
  IPayrollRunProgress,
} from '../../models/payroll-run.models';

describe('PayrollRunDetailComponent', () => {
  let fixture: ComponentFixture<PayrollRunDetailComponent>;
  let component: PayrollRunDetailComponent;
  let runs: jasmine.SpyObj<PayrollRunService>;

  const baseRun: IPayrollRun = {
    id: 'r-1',
    payMonth: 5,
    payYear: 2026,
    status: 'ReviewPending',
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

  function setup(run: IPayrollRun): void {
    runs = jasmine.createSpyObj<PayrollRunService>('PayrollRunService', [
      'getRun',
      'streamProgress',
    ]);
    runs.getRun.and.returnValue(of(run));
    runs.streamProgress.and.returnValue(of());

    TestBed.configureTestingModule({
      imports: [PayrollRunDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: PayrollRunService, useValue: runs },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => 'r-1' } },
          },
        },
      ],
    });

    fixture = TestBed.createComponent(PayrollRunDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('loads the run on init', () => {
    setup(baseRun);
    expect(component).toBeTruthy();
    expect(runs.getRun).toHaveBeenCalledWith('r-1');
    expect(component.run()).toEqual(baseRun);
    expect(component.loading()).toBeFalse();
  });

  it('sets an error when the run fails to load', () => {
    setup(baseRun);
    runs.getRun.and.returnValue(throwError(() => new Error('boom')));
    component.load();
    expect(component.error()).toBe('Could not load this payroll run.');
  });

  describe('completed run', () => {
    beforeEach(() => setup(baseRun));

    it('is in the complete state and not processing', () => {
      expect(component.isComplete()).toBeTrue();
      expect(component.isProcessing()).toBeFalse();
    });

    it('does not start a progress stream for a finished run', () => {
      expect(runs.streamProgress).not.toHaveBeenCalled();
    });

    it('renders the summary totals', () => {
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Run summary');
      expect(text).toContain('800,000.00'); // total net
      expect(text).toContain('Skipped');
    });

    it('computes the current step index for ReviewPending', () => {
      expect(component.stepIndex()).toBe(2); // Queued,Processing,ReviewPending
      expect(component.isCurrentStep('ReviewPending')).toBeTrue();
      expect(component.isCompleteStep('Queued')).toBeTrue();
      expect(component.isCompleteStep('Approved')).toBeFalse();
    });
  });

  describe('processing run', () => {
    const processing: IPayrollRun = {
      ...baseRun,
      status: 'Processing',
      processedEmployees: 100,
      totalNet: 0,
    };

    it('starts the progress stream and updates from emissions (FR-6)', () => {
      const stream = new Subject<IPayrollRunProgress>();
      setupProcessing(processing, stream);

      expect(runs.streamProgress).toHaveBeenCalledWith('r-1');
      expect(component.isProcessing()).toBeTrue();

      stream.next({
        runId: 'r-1',
        status: 'Processing',
        processedEmployees: 200,
        totalEmployees: 250,
        skippedEmployees: 1,
      });

      expect(component.processed()).toBe(200);
      expect(component.total()).toBe(250);
      expect(component.percent()).toBe(80);
      expect(component.skipped()).toBe(1);
    });

    it('refetches the run for the summary when the stream completes', () => {
      const stream = new Subject<IPayrollRunProgress>();
      setupProcessing(processing, stream);
      runs.getRun.calls.reset();
      runs.getRun.and.returnValue(of({ ...baseRun, status: 'ReviewPending' }));

      stream.complete();

      expect(runs.getRun).toHaveBeenCalledWith('r-1');
      expect(component.run()?.status).toBe('ReviewPending');
    });

    it('tears down the stream subscription on destroy', () => {
      const stream = new Subject<IPayrollRunProgress>();
      setupProcessing(processing, stream);
      expect(stream.observed).toBeTrue();
      fixture.destroy();
      expect(stream.observed).toBeFalse();
    });

    function setupProcessing(
      run: IPayrollRun,
      stream: Subject<IPayrollRunProgress>,
    ): void {
      runs = jasmine.createSpyObj<PayrollRunService>('PayrollRunService', [
        'getRun',
        'streamProgress',
      ]);
      runs.getRun.and.returnValue(of(run));
      runs.streamProgress.and.returnValue(stream.asObservable());

      TestBed.configureTestingModule({
        imports: [PayrollRunDetailComponent],
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          provideNoopAnimations(),
          { provide: PayrollRunService, useValue: runs },
          {
            provide: ActivatedRoute,
            useValue: { snapshot: { paramMap: { get: () => 'r-1' } } },
          },
        ],
      });
      fixture = TestBed.createComponent(PayrollRunDetailComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();
    }
  });

  describe('cancelled run', () => {
    it('shows the cancelled banner and a -1 step index', () => {
      setup({ ...baseRun, status: 'Cancelled' });
      expect(component.stepIndex()).toBe(-1);
      expect(component.isProcessing()).toBeFalse();
      expect(component.isComplete()).toBeFalse();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('cancelled');
    });
  });

  describe('percent', () => {
    it('is 0 when total is 0', () => {
      setup({ ...baseRun, status: 'Queued', totalEmployees: 0 });
      expect(component.percent()).toBe(0);
    });
  });
});
