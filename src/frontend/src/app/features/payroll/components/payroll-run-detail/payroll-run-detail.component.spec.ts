import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of, throwError, Subject } from 'rxjs';

import { PayrollRunDetailComponent } from './payroll-run-detail.component';
import { PayrollRunService } from '../../services/payroll-run.service';
import { PayrollApprovalService } from '../../services/payroll-approval.service';
import { PayslipEmailService } from '../../services/payslip-email.service';
import { PayslipService } from '../../services/payslip.service';
import { AuditService } from '../../services/audit.service';
import { AuthService } from '../../../../core/auth/auth.service';
import { IPayslipDistributionStatus } from '../../models/payslip-email.models';
import { IPayslipGenerationStatus } from '../../models/payslip.models';
import {
  IPayrollRun,
  IPayrollRunProgress,
} from '../../models/payroll-run.models';
import {
  IApprovalHistoryEntry,
  IApprovalSummary,
} from '../../models/approval.models';

describe('PayrollRunDetailComponent', () => {
  let fixture: ComponentFixture<PayrollRunDetailComponent>;
  let component: PayrollRunDetailComponent;
  let runs: jasmine.SpyObj<PayrollRunService>;
  let approval: jasmine.SpyObj<PayrollApprovalService>;
  let email: jasmine.SpyObj<PayslipEmailService>;
  let payslips: jasmine.SpyObj<PayslipService>;
  let audit: jasmine.SpyObj<AuditService>;
  let auth: jasmine.SpyObj<AuthService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const emptyDistribution: IPayslipDistributionStatus = {
    runId: 'r-1',
    isSending: false,
    hasSent: false,
    totalEmployees: 0,
    emailsSent: 0,
    emailsFailed: 0,
    emailsSkipped: 0,
    emailsQueued: 0,
    startedAt: null,
    completedAt: null,
    recipients: [],
  };

  const emptyGeneration: IPayslipGenerationStatus = {
    runId: 'r-1',
    isGenerating: false,
    totalCount: 0,
    queuedCount: 0,
    failedCount: 0,
    pendingCount: 0,
  };

  /**
   * The embedded US-PAY-011 distribution child injects PayslipEmailService +
   * PayslipService and fires status reads on init — stub both so the run-detail
   * spec stays focused on US-PAY-003/008 behaviour.
   */
  function makeChildSpies(): void {
    email = jasmine.createSpyObj<PayslipEmailService>('PayslipEmailService', [
      'sendPayslips',
      'getDistributionStatus',
      'streamDistributionStatus',
      'resendAllFailed',
      'resendSelective',
    ]);
    email.getDistributionStatus.and.returnValue(of(emptyDistribution));
    email.streamDistributionStatus.and.returnValue(of());
    payslips = jasmine.createSpyObj<PayslipService>('PayslipService', [
      'getGenerationStatus',
    ]);
    payslips.getGenerationStatus.and.returnValue(of(emptyGeneration));
    // The embedded US-PAY-012 audit-trail child fires getRunAuditTrail on init.
    audit = jasmine.createSpyObj<AuditService>('AuditService', [
      'getHistory',
      'getAuditTrail',
      'getRunAuditTrail',
      'exportAuditTrail',
    ]);
    audit.getRunAuditTrail.and.returnValue(of([]));
  }

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

  const summary: IApprovalSummary = {
    runId: 'r-1',
    totalEmployees: 250,
    totalGross: 1000000,
    totalDeductions: 200000,
    totalStatutory: 120000,
    totalNet: 800000,
    previousMonthTotalNet: 600000,
    variancePercentage: 33.3,
    exceptions: [{ severity: 'Error', message: 'Negative net', employeeName: 'Sam' }],
  };

  const history: IApprovalHistoryEntry[] = [
    {
      id: 'h-1',
      stepNumber: 1,
      action: 'Submitted',
      actorName: 'Alex HR',
      comments: 'Initial submit',
      actedAt: '2026-05-31T11:00:00Z',
      ipAddress: '10.0.0.1',
    },
  ];

  function makeApproval(): void {
    approval = jasmine.createSpyObj<PayrollApprovalService>(
      'PayrollApprovalService',
      [
        'submit',
        'approve',
        'reject',
        'return',
        'finalize',
        'getApprovalSummary',
        'getApprovalHistory',
        'listPendingApprovals',
      ],
    );
    approval.getApprovalSummary.and.returnValue(of(summary));
    approval.getApprovalHistory.and.returnValue(of(history));
  }

  function makeAuth(canApprove: boolean, canRun = true): void {
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['hasPermission']);
    auth.hasPermission.and.callFake((p: string) => {
      if (p === 'Payroll.Approve') {
        return canApprove;
      }
      if (p === 'Payroll.Run') {
        return canRun;
      }
      return false;
    });
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
  }

  function setup(
    run: IPayrollRun,
    canApprove = true,
    canRun = true,
  ): void {
    runs = jasmine.createSpyObj<PayrollRunService>('PayrollRunService', [
      'getRun',
      'streamProgress',
      'cancelRun',
      'rerunRun',
    ]);
    runs.getRun.and.returnValue(of(run));
    runs.streamProgress.and.returnValue(of());
    makeApproval();
    makeAuth(canApprove, canRun);
    makeChildSpies();

    TestBed.configureTestingModule({
      imports: [PayrollRunDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: PayrollRunService, useValue: runs },
        { provide: PayrollApprovalService, useValue: approval },
        { provide: PayslipEmailService, useValue: email },
        { provide: PayslipService, useValue: payslips },
        { provide: AuditService, useValue: audit },
        { provide: AuthService, useValue: auth },
        { provide: ToastrService, useValue: toastr },
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
        'cancelRun',
        'rerunRun',
      ]);
      runs.getRun.and.returnValue(of(run));
      runs.streamProgress.and.returnValue(stream.asObservable());
      makeApproval();
      makeAuth(true);
      makeChildSpies();

      TestBed.configureTestingModule({
        imports: [PayrollRunDetailComponent],
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          provideNoopAnimations(),
          { provide: PayrollRunService, useValue: runs },
          { provide: PayrollApprovalService, useValue: approval },
          { provide: PayslipEmailService, useValue: email },
          { provide: PayslipService, useValue: payslips },
          { provide: AuditService, useValue: audit },
          { provide: AuthService, useValue: auth },
          { provide: ToastrService, useValue: toastr },
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

  // ─── US-PAY-008 approval workflow ──────────────────────────

  describe('approval workflow (US-PAY-008)', () => {
    it('loads approval history + summary once the run is in review', () => {
      setup(baseRun); // ReviewPending
      expect(approval.getApprovalHistory).toHaveBeenCalledWith('r-1');
      expect(approval.getApprovalSummary).toHaveBeenCalledWith('r-1');
      expect(component.history().length).toBe(1);
      expect(component.exceptions().length).toBe(1);
    });

    it('shows "Submit for Approval" action only on a ReviewPending run', () => {
      setup(baseRun);
      expect(component.hasActions()).toBeTrue();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Submit for Approval');
    });

    it('submits for approval and updates the run + toasts', () => {
      setup(baseRun);
      approval.submit.and.returnValue(
        of({ ...baseRun, status: 'AwaitingApproval' }),
      );
      // The action returns a result DTO; the component refetches the full run.
      runs.getRun.and.returnValue(of({ ...baseRun, status: 'AwaitingApproval' }));
      component.submitForApproval();
      expect(approval.submit).toHaveBeenCalledWith('r-1');
      expect(component.run()?.status).toBe('AwaitingApproval');
      expect(component.acting()).toBeFalse();
      expect(toastr.success).toHaveBeenCalled();
    });

    it('AwaitingApproval shows approve/reject/return for an approver', () => {
      setup({ ...baseRun, status: 'AwaitingApproval' }, true);
      expect(component.canApprove()).toBeTrue();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Approve');
      expect(text).toContain('Reject');
      expect(text).toContain('Return to HR');
    });

    it('AwaitingApproval hides approver controls without Payroll.Approve', () => {
      setup({ ...baseRun, status: 'AwaitingApproval' }, false);
      expect(component.canApprove()).toBeFalse();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('do not have permission');
    });

    it('approves an AwaitingApproval run', () => {
      setup({ ...baseRun, status: 'AwaitingApproval' }, true);
      approval.approve.and.returnValue(of({ ...baseRun, status: 'Approved' }));
      runs.getRun.and.returnValue(of({ ...baseRun, status: 'Approved' }));
      component.approve();
      expect(approval.approve).toHaveBeenCalledWith('r-1');
      expect(component.run()?.status).toBe('Approved');
    });

    it('requires a comment before confirming a rejection (AC-3)', () => {
      setup({ ...baseRun, status: 'AwaitingApproval' }, true);
      component.startReject();
      expect(component.commentAction()).toBe('reject');

      // No comment → action is NOT sent.
      component.confirmReject();
      expect(approval.reject).not.toHaveBeenCalled();
      expect(component.comments.touched).toBeTrue();

      // With a comment → sent with the reason.
      component.comments.setValue('Wrong statutory rates');
      approval.reject.and.returnValue(of({ ...baseRun, status: 'Rejected' }));
      runs.getRun.and.returnValue(of({ ...baseRun, status: 'Rejected' }));
      component.confirmReject();
      expect(approval.reject).toHaveBeenCalledWith('r-1', {
        comments: 'Wrong statutory rates',
      });
      expect(component.run()?.status).toBe('Rejected');
      expect(component.commentAction()).toBeNull();
    });

    it('requires a comment before confirming a return (FR-9)', () => {
      setup({ ...baseRun, status: 'AwaitingApproval' }, true);
      component.startReturn();

      component.confirmReturn();
      expect(approval.return).not.toHaveBeenCalled();

      component.comments.setValue('Re-check OT hours');
      approval.return.and.returnValue(
        of({ ...baseRun, status: 'ReviewPending' }),
      );
      runs.getRun.and.returnValue(of({ ...baseRun, status: 'ReviewPending' }));
      component.confirmReturn();
      expect(approval.return).toHaveBeenCalledWith('r-1', {
        comments: 'Re-check OT hours',
      });
      expect(component.run()?.status).toBe('ReviewPending');
    });

    it('finalizes an Approved run (AC-5)', () => {
      setup({ ...baseRun, status: 'Approved' }, true);
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Finalize');

      approval.finalize.and.returnValue(of({ ...baseRun, status: 'Finalized' }));
      runs.getRun.and.returnValue(of({ ...baseRun, status: 'Finalized' }));
      component.finalize();
      expect(approval.finalize).toHaveBeenCalledWith('r-1');
      expect(component.run()?.status).toBe('Finalized');
    });

    it('toasts an error and re-enables the bar when an action fails', () => {
      setup(baseRun);
      approval.submit.and.returnValue(throwError(() => new Error('boom')));
      component.submitForApproval();
      expect(component.acting()).toBeFalse();
      expect(toastr.error).toHaveBeenCalled();
    });

    it('classifies a >15% net variance as the high (red) band', () => {
      setup({ ...baseRun, status: 'AwaitingApproval' }, true);
      // summary.variancePercentage is 33.3 in the fixture.
      expect(component.varianceBandValue()).toBe('high');
      expect(component.varianceLabel()).toBe('+33.3%');
    });

    it('shows the Rejected off-path banner with the last comment', () => {
      setup({ ...baseRun, status: 'Rejected' }, true);
      expect(component.isComplete()).toBeFalse();
      expect(component.stepIndex()).toBe(-1);
      expect(component.lastComment()).toBe('Initial submit');
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('rejected');
    });

    it('does not render the action bar on a Finalized run', () => {
      setup({ ...baseRun, status: 'Finalized' }, true);
      expect(component.hasActions()).toBeFalse();
    });
  });

  // ─── ISSUE-154 cancel / re-run ─────────────────────────────

  describe('cancel / re-run (ISSUE-154)', () => {
    it('offers Cancel + Re-run on a ReviewPending run with Payroll.Run', () => {
      setup(baseRun); // ReviewPending, canRun defaults true
      expect(component.canCancel()).toBeTrue();
      expect(component.canRerun()).toBeTrue();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Cancel run');
      expect(text).toContain('Re-run');
    });

    it('hides both actions without the Payroll.Run permission', () => {
      setup(baseRun, true, false);
      expect(component.canCancel()).toBeFalse();
      expect(component.canRerun()).toBeFalse();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).not.toContain('Cancel run');
    });

    it('hides Cancel on a Finalized run', () => {
      setup({ ...baseRun, status: 'Finalized' });
      expect(component.canCancel()).toBeFalse();
      expect(component.canRerun()).toBeFalse();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).not.toContain('Cancel run');
    });

    it('hides Cancel on an already Cancelled run', () => {
      setup({ ...baseRun, status: 'Cancelled' });
      expect(component.canCancel()).toBeFalse();
      expect(component.canRerun()).toBeFalse();
    });

    it('offers Cancel but not Re-run on a Processing run', () => {
      setup({ ...baseRun, status: 'Processing' });
      expect(component.canCancel()).toBeTrue();
      expect(component.canRerun()).toBeFalse();
    });

    it('offers Cancel but not Re-run on an AwaitingApproval run', () => {
      setup({ ...baseRun, status: 'AwaitingApproval' });
      expect(component.canCancel()).toBeTrue();
      expect(component.canRerun()).toBeFalse();
    });

    it('offers Cancel on a Rejected run (still pre-Finalized)', () => {
      setup({ ...baseRun, status: 'Rejected' });
      expect(component.canCancel()).toBeTrue();
      expect(component.canRerun()).toBeFalse();
    });

    it('confirms before cancelling, then calls cancelRun + refetches + toasts', () => {
      setup(baseRun);
      // The action itself is not sent until the inline confirm is shown + confirmed.
      component.startCancel();
      expect(component.confirmAction()).toBe('cancel');
      expect(runs.cancelRun).not.toHaveBeenCalled();

      runs.cancelRun.and.returnValue(of({ ...baseRun, status: 'Cancelled' }));
      runs.getRun.and.returnValue(of({ ...baseRun, status: 'Cancelled' }));
      component.confirmCancel();

      expect(runs.cancelRun).toHaveBeenCalledWith('r-1');
      expect(component.confirmAction()).toBeNull();
      expect(component.acting()).toBeFalse();
      expect(component.run()?.status).toBe('Cancelled');
      expect(toastr.success).toHaveBeenCalled();
    });

    it('dismisses the cancel confirm without acting', () => {
      setup(baseRun);
      component.startCancel();
      component.dismissConfirm();
      expect(component.confirmAction()).toBeNull();
      expect(runs.cancelRun).not.toHaveBeenCalled();
    });

    it('confirms before re-running, then calls rerunRun + refetches + toasts', () => {
      setup(baseRun);
      component.startRerun();
      expect(component.confirmAction()).toBe('rerun');
      expect(runs.rerunRun).not.toHaveBeenCalled();

      runs.rerunRun.and.returnValue(of({ ...baseRun, status: 'Queued' }));
      runs.getRun.and.returnValue(of({ ...baseRun, status: 'Queued' }));
      component.confirmRerun();

      expect(runs.rerunRun).toHaveBeenCalledWith('r-1');
      expect(component.confirmAction()).toBeNull();
      expect(component.acting()).toBeFalse();
      expect(toastr.success).toHaveBeenCalled();
    });

    it('maps a 409 error code to a friendly message on cancel failure', () => {
      setup(baseRun);
      component.startCancel();
      runs.cancelRun.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: { code: 'run_finalized', message: 'x' },
            }),
        ),
      );
      component.confirmCancel();

      expect(component.acting()).toBeFalse();
      expect(toastr.error).toHaveBeenCalledWith(
        'This run is finalized and can no longer be cancelled or re-run.',
      );
    });

    it('falls back to a generic message for an unknown error code', () => {
      setup(baseRun);
      component.startRerun();
      runs.rerunRun.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500 })),
      );
      component.confirmRerun();

      expect(component.acting()).toBeFalse();
      expect(toastr.error).toHaveBeenCalledWith(
        'That action could not be completed. Please refresh and try again.',
      );
    });
  });
});
