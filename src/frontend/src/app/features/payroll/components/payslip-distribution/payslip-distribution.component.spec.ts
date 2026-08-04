import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { ToastrService } from 'ngx-toastr';
import { of, throwError, Subject } from 'rxjs';

import { PayslipDistributionComponent } from './payslip-distribution.component';
import { TrappedDialogDirective } from '../../../../shared/directives';
import { PayslipEmailService } from '../../services/payslip-email.service';
import { PayslipService } from '../../services/payslip.service';
import { PayrollRunStatus } from '../../models/payroll-run.models';
import {
  IPayslipDistributionStatus,
  distributionPercent,
  recipientsByStatus,
} from '../../models/payslip-email.models';
import { IPayslipGenerationStatus } from '../../models/payslip.models';

describe('PayslipDistributionComponent', () => {
  let fixture: ComponentFixture<PayslipDistributionComponent>;
  let component: PayslipDistributionComponent;
  let email: jasmine.SpyObj<PayslipEmailService>;
  let payslips: jasmine.SpyObj<PayslipService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const notSent: IPayslipDistributionStatus = {
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

  const distributed: IPayslipDistributionStatus = {
    runId: 'r-1',
    isSending: false,
    hasSent: true,
    totalEmployees: 5,
    emailsSent: 3,
    emailsFailed: 1,
    emailsSkipped: 1,
    emailsQueued: 0,
    startedAt: '2026-06-15T10:00:00Z',
    completedAt: '2026-06-15T10:05:00Z',
    recipients: [
      {
        employeeId: 'e-1',
        employeeName: 'Alex',
        employeeNo: 'EMP001',
        recipientEmail: 'alex@acme.test',
        status: 'Sent',
        failureReason: null,
        sentAt: '2026-06-15T10:01:00Z',
        retryCount: 0,
      },
      {
        employeeId: 'e-2',
        employeeName: 'Beth',
        employeeNo: 'EMP002',
        recipientEmail: 'beth@acme.test',
        status: 'Failed',
        failureReason: 'SMTP 550 mailbox unavailable',
        sentAt: null,
        retryCount: 3,
      },
      {
        employeeId: 'e-3',
        employeeName: 'Cory',
        employeeNo: 'EMP003',
        recipientEmail: null,
        status: 'Skipped',
        failureReason: null,
        sentAt: null,
        retryCount: 0,
      },
    ],
  };

  const acceptedDto = { runId: 'r-1', queuedCount: 5, resend: false };

  const generated: IPayslipGenerationStatus = {
    runId: 'r-1',
    isGenerating: false,
    totalCount: 5,
    generatedCount: 5,
    failedCount: 0,
    pendingCount: 0,
  };

  const notGenerated: IPayslipGenerationStatus = {
    ...generated,
    generatedCount: 0,
    pendingCount: 5,
  };

  function setup(
    runStatus: PayrollRunStatus,
    opts: {
      distribution?: IPayslipDistributionStatus;
      generation?: IPayslipGenerationStatus;
      employeeCount?: number;
    } = {},
  ): void {
    email = jasmine.createSpyObj<PayslipEmailService>('PayslipEmailService', [
      'sendPayslips',
      'getDistributionStatus',
      'streamDistributionStatus',
      'resendAllFailed',
      'resendSelective',
    ]);
    email.getDistributionStatus.and.returnValue(
      of(opts.distribution ?? notSent),
    );
    email.streamDistributionStatus.and.returnValue(of());

    payslips = jasmine.createSpyObj<PayslipService>('PayslipService', [
      'getGenerationStatus',
    ]);
    payslips.getGenerationStatus.and.returnValue(
      of(opts.generation ?? generated),
    );

    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);

    TestBed.configureTestingModule({
      imports: [PayslipDistributionComponent],
      providers: [
        provideNoopAnimations(),
        { provide: PayslipEmailService, useValue: email },
        { provide: PayslipService, useValue: payslips },
        { provide: ToastrService, useValue: toastr },
      ],
    });

    fixture = TestBed.createComponent(PayslipDistributionComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('runId', 'r-1');
    fixture.componentRef.setInput('runStatus', runStatus);
    fixture.componentRef.setInput('employeeCount', opts.employeeCount ?? 5);
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('loads distribution + PDF readiness on init', () => {
    setup('Finalized');
    expect(email.getDistributionStatus).toHaveBeenCalledWith('r-1');
    expect(payslips.getGenerationStatus).toHaveBeenCalledWith('r-1');
    expect(component.hasGeneratedPdfs()).toBeTrue();
  });

  describe('Send enablement (AC-1)', () => {
    it('enables Send for a Finalized run with generated PDFs', () => {
      setup('Finalized');
      expect(component.canSend()).toBeTrue();
    });

    it('disables Send for a non-Finalized run and explains why', () => {
      setup('Approved');
      expect(component.canSend()).toBeFalse();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('once this payroll run is finalized');
    });

    it('disables Send when no PDFs have been generated', () => {
      setup('Finalized', { generation: notGenerated });
      expect(component.canSend()).toBeFalse();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Generate payslip PDFs');
    });

    // ENH-024: the button already exposed disabled + aria-disabled, but a screen-reader user heard no
    // REASON. These arms pin the programmatic association, not just the presence of the hint text.
    it('points the disabled Send button at the reason hint via aria-describedby (ENH-024)', () => {
      setup('Finalized', { generation: notGenerated });

      const button = fixture.debugElement.query(
        By.css('button[aria-describedby]'),
      ).nativeElement as HTMLButtonElement;
      const describedBy = button.getAttribute('aria-describedby')!;

      // The referenced element must actually exist, and carry the reason the button is dimmed —
      // a dangling aria-describedby is worse than none.
      const hint = (fixture.nativeElement as HTMLElement).querySelector(`#${describedBy}`);
      expect(hint).not.toBeNull();
      expect(hint!.textContent).toContain('Generate payslip PDFs');
    });

    it('drops aria-describedby when Send is enabled and no hint is rendered (ENH-024)', () => {
      setup('Finalized');

      expect(component.canSend()).toBeTrue();
      const button = fixture.debugElement.query(
        By.css('button[aria-describedby]'),
      );
      expect(button)
        .withContext('no hint is rendered, so the button must not reference a missing element')
        .toBeNull();
    });

    it('leaves Send disabled when PDF readiness lookup errors', () => {
      email = jasmine.createSpyObj<PayslipEmailService>('PayslipEmailService', [
        'sendPayslips',
        'getDistributionStatus',
        'streamDistributionStatus',
        'resendAllFailed',
        'resendSelective',
      ]);
      email.getDistributionStatus.and.returnValue(of(notSent));
      email.streamDistributionStatus.and.returnValue(of());
      payslips = jasmine.createSpyObj<PayslipService>('PayslipService', [
        'getGenerationStatus',
      ]);
      payslips.getGenerationStatus.and.returnValue(
        throwError(() => new Error('boom')),
      );
      toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
        'success',
        'error',
      ]);
      TestBed.configureTestingModule({
        imports: [PayslipDistributionComponent],
        providers: [
          provideNoopAnimations(),
          { provide: PayslipEmailService, useValue: email },
          { provide: PayslipService, useValue: payslips },
          { provide: ToastrService, useValue: toastr },
        ],
      });
      fixture = TestBed.createComponent(PayslipDistributionComponent);
      component = fixture.componentInstance;
      fixture.componentRef.setInput('runId', 'r-1');
      fixture.componentRef.setInput('runStatus', 'Finalized');
      fixture.componentRef.setInput('employeeCount', 5);
      fixture.detectChanges();

      expect(component.hasGeneratedPdfs()).toBeFalse();
      expect(component.canSend()).toBeFalse();
    });
  });

  describe('confirmation dialog (§8 / FR-7 / BR-5)', () => {
    it('traps focus in the dialog (ISSUE-296)', () => {
      setup('Finalized');
      component.openConfirm();
      fixture.detectChanges();
      const dialog = fixture.debugElement.query(
        By.directive(TrappedDialogDirective),
      );
      expect(dialog).toBeTruthy();
    });

    it('opens the dialog with the recipient count', () => {
      setup('Finalized', { employeeCount: 42 });
      component.openConfirm();
      fixture.detectChanges();
      expect(component.confirmOpen()).toBeTrue();
      // recipientCount prefers the live total (0 here) → falls back to employeeCount.
      expect(component.recipientCount()).toBe(42);
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('42 employees');
    });

    it('does not open the dialog when Send is disabled', () => {
      setup('Approved');
      component.openConfirm();
      expect(component.confirmOpen()).toBeFalse();
    });

    it('sends with confirm:false on a first-time distribution', () => {
      setup('Finalized');
      email.sendPayslips.and.returnValue(of(acceptedDto));
      // Keep the progress stream open so `sending` stays true after dispatch.
      email.streamDistributionStatus.and.returnValue(
        new Subject<IPayslipDistributionStatus>().asObservable(),
      );
      component.openConfirm();
      component.confirmSend();
      expect(email.sendPayslips).toHaveBeenCalledWith('r-1', false);
      expect(component.confirmOpen()).toBeFalse();
      expect(component.sending()).toBeTrue();
    });

    it('blocks confirm on a re-send until the ack is ticked (BR-5)', () => {
      setup('Finalized', { distribution: distributed });
      component.openConfirm();
      expect(component.canConfirm()).toBeFalse(); // hasSent, ack not given

      component.confirmSend();
      expect(email.sendPayslips).not.toHaveBeenCalled();

      component.resendAck.set(true);
      expect(component.canConfirm()).toBeTrue();
      email.sendPayslips.and.returnValue(of({ ...acceptedDto, resend: true }));
      component.confirmSend();
      expect(email.sendPayslips).toHaveBeenCalledWith('r-1', true);
    });

    it('toasts an error and re-enables when the send request fails', () => {
      setup('Finalized');
      email.sendPayslips.and.returnValue(throwError(() => new Error('boom')));
      component.openConfirm();
      component.confirmSend();
      expect(component.sending()).toBeFalse();
      expect(toastr.error).toHaveBeenCalled();
    });
  });

  describe('progress streaming (§8)', () => {
    it('streams status while sending and clears the flag on completion', () => {
      setup('Finalized');
      const stream = new Subject<IPayslipDistributionStatus>();
      email.sendPayslips.and.returnValue(of(acceptedDto));
      email.streamDistributionStatus.and.returnValue(stream.asObservable());

      component.openConfirm();
      component.confirmSend();
      expect(component.sending()).toBeTrue();

      stream.next({ ...distributed, isSending: true, emailsSent: 2 });
      expect(component.status()?.emailsSent).toBe(2);
      expect(component.percent()).toBe(distributionPercent(component.status()));

      stream.next(distributed);
      stream.complete();
      expect(component.sending()).toBeFalse();
      expect(toastr.success).toHaveBeenCalled();
    });

    it('resumes streaming if a job is already in flight on load', () => {
      setup('Finalized', {
        distribution: { ...distributed, isSending: true },
      });
      expect(email.streamDistributionStatus).toHaveBeenCalledWith('r-1');
    });
  });

  describe('summary card + re-send (BR-6 / FR-4)', () => {
    beforeEach(() => setup('Finalized', { distribution: distributed }));

    it('shows the distribution summary card with counts', () => {
      expect(component.isDistributed()).toBeTrue();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Sent');
      expect(text).toContain('Failed');
      expect(text).toContain('Skipped');
    });

    it('expands a status list and filters its rows', () => {
      component.toggle('Failed');
      expect(component.expanded()).toBe('Failed');
      expect(component.expandedRows().length).toBe(1);
      expect(component.expandedRows()[0].employeeId).toBe('e-2');

      component.toggle('Failed'); // toggle off
      expect(component.expanded()).toBeNull();
    });

    it('re-sends all failed deliveries (FR-4)', () => {
      email.resendAllFailed.and.returnValue(
        of({ ...acceptedDto, queuedCount: 1, resend: true }),
      );
      email.streamDistributionStatus.and.returnValue(of());
      component.resendAllFailed();
      expect(email.resendAllFailed).toHaveBeenCalledWith('r-1');
    });

    it('re-sends a single failed delivery (FR-4)', () => {
      email.resendSelective.and.returnValue(
        of({ ...acceptedDto, queuedCount: 1, resend: true }),
      );
      email.streamDistributionStatus.and.returnValue(of());
      const failed = distributed.recipients[1];
      component.resendOne(failed);
      expect(email.resendSelective).toHaveBeenCalledWith('r-1', ['e-2']);
    });

    it('toasts an error when re-send all failed errors', () => {
      email.resendAllFailed.and.returnValue(throwError(() => new Error('x')));
      component.resendAllFailed();
      expect(component.resendingAll()).toBeFalse();
      expect(toastr.error).toHaveBeenCalled();
    });
  });

  describe('teardown', () => {
    it('completes the destroy subject on destroy', () => {
      setup('Finalized');
      expect(() => fixture.destroy()).not.toThrow();
    });
  });

  describe('pure helpers', () => {
    it('distributionPercent counts terminal deliveries against total', () => {
      expect(distributionPercent(null)).toBe(0);
      expect(distributionPercent({ ...notSent, totalEmployees: 0 })).toBe(0);
      expect(distributionPercent(distributed)).toBe(100); // 3+1+1 / 5
      expect(
        distributionPercent({
          ...distributed,
          emailsSent: 1,
          emailsFailed: 0,
          emailsSkipped: 0,
        }),
      ).toBe(20);
    });

    it('recipientsByStatus filters per delivery status', () => {
      expect(recipientsByStatus(null, 'Sent')).toEqual([]);
      expect(recipientsByStatus(distributed, 'Sent').length).toBe(1);
      expect(recipientsByStatus(distributed, 'Failed').length).toBe(1);
      expect(recipientsByStatus(distributed, 'Skipped').length).toBe(1);
      expect(recipientsByStatus(distributed, 'Queued').length).toBe(0);
    });
  });
});
