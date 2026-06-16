import {
  TestBed,
  fakeAsync,
  tick,
  discardPeriodicTasks,
} from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PayslipEmailService } from './payslip-email.service';
import { environment } from '../../../../environments/environment';
import {
  IPayslipDistributionAccepted,
  IPayslipDistributionStatus,
} from '../models/payslip-email.models';

describe('PayslipEmailService', () => {
  let service: PayslipEmailService;
  let httpMock: HttpTestingController;
  const runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  const accepted: IPayslipDistributionAccepted = {
    runId: 'r-1',
    queuedCount: 250,
    resend: false,
  };

  const sample: IPayslipDistributionStatus = {
    runId: 'r-1',
    isSending: false,
    hasSent: true,
    totalEmployees: 250,
    emailsSent: 240,
    emailsFailed: 7,
    emailsSkipped: 3,
    emailsQueued: 0,
    startedAt: '2026-06-15T10:00:00Z',
    completedAt: '2026-06-15T10:08:00Z',
    recipients: [
      {
        employeeId: 'e-1',
        employeeName: 'Alex HR',
        employeeNo: 'EMP001',
        recipientEmail: 'alex@acme.test',
        status: 'Sent',
        failureReason: null,
        sentAt: '2026-06-15T10:01:00Z',
        retryCount: 0,
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PayslipEmailService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PayslipEmailService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sendPayslips POSTs to send-emails with the confirm flag, returns accepted dto (AC-1/FR-7)', () => {
    let result: IPayslipDistributionAccepted | undefined;
    service.sendPayslips('r-1', false).subscribe((s) => (result = s));

    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/send-emails`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual({ confirm: false });
    req.flush(accepted);

    expect(result).toEqual(accepted);
  });

  it('sendPayslips passes confirm:true for a re-send (BR-5)', () => {
    service.sendPayslips('r-1', true).subscribe();
    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/send-emails`);
    expect(req.request.body).toEqual({ confirm: true });
    req.flush({ ...accepted, resend: true });
  });

  it('getDistributionStatus GETs the distribution-summary endpoint', () => {
    let result: IPayslipDistributionStatus | undefined;
    service.getDistributionStatus('r-1').subscribe((s) => (result = s));

    const req = httpMock.expectOne(
      `${runsUrl}/r-1/payslips/distribution-summary`,
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(sample);

    expect(result).toEqual(sample);
  });

  it('resendAllFailed POSTs { onlyFailed: true } to resend-emails (FR-4)', () => {
    let result: IPayslipDistributionAccepted | undefined;
    service.resendAllFailed('r-1').subscribe((s) => (result = s));
    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/resend-emails`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ onlyFailed: true });
    req.flush({ ...accepted, queuedCount: 7, resend: true });

    expect(result!.queuedCount).toBe(7);
  });

  it('resendSelective POSTs the employeeIds to resend-emails (FR-4)', () => {
    service.resendSelective('r-1', ['e-1', 'e-2']).subscribe();
    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/resend-emails`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ employeeIds: ['e-1', 'e-2'] });
    req.flush({ ...accepted, queuedCount: 2, resend: true });
  });

  describe('streamDistributionStatus (polling, §8)', () => {
    it('polls while sending and completes on the first settled snapshot', fakeAsync(() => {
      const emitted: IPayslipDistributionStatus[] = [];
      let completed = false;
      service.streamDistributionStatus('r-1').subscribe({
        next: (s) => emitted.push(s),
        complete: () => (completed = true),
      });

      // timer(0, …) does NOT fire synchronously on subscribe — tick(0) first.
      tick(0);
      httpMock
        .expectOne(`${runsUrl}/r-1/payslips/distribution-summary`)
        .flush({ ...sample, isSending: true, emailsSent: 100 });
      expect(emitted.length).toBe(1);
      expect(completed).toBeFalse();

      tick(PayslipEmailService.POLL_INTERVAL_MS);
      httpMock
        .expectOne(`${runsUrl}/r-1/payslips/distribution-summary`)
        .flush({ ...sample, isSending: false, emailsSent: 240 });

      // Settled snapshot is emitted (inclusive takeWhile) then the stream completes.
      expect(emitted.length).toBe(2);
      expect(completed).toBeTrue();

      discardPeriodicTasks();
    }));
  });
});
