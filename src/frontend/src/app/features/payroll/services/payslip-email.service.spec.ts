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
  PayslipDistributionAcceptedWire,
  PayslipDistributionStatusWire,
} from '../models/payslip-email.models';

describe('PayslipEmailService', () => {
  let service: PayslipEmailService;
  let httpMock: HttpTestingController;
  const runsUrl = `${environment.apiBaseUrl}/payroll/runs`;

  // Typed as the WIRE shape. Unlike the payslip/adjustment surfaces, this one's hand-written
  // interfaces agree with the contract field-for-field, so the same literal satisfies both — the
  // wire typing is what makes a future backend rename fail HERE instead of in production.
  const acceptedWire: PayslipDistributionAcceptedWire = {
    runId: 'r-1',
    queuedCount: 250,
    resend: false,
  };

  const accepted: IPayslipDistributionAccepted = {
    runId: 'r-1',
    queuedCount: 250,
    resend: false,
  };

  const sampleWire: PayslipDistributionStatusWire = {
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
    req.flush(acceptedWire, { status: 202, statusText: 'Accepted' });

    expect(result).toEqual(accepted);
    // The service now MAPS, so it must be a new object rather than the flushed body.
    expect(result as unknown).not.toBe(acceptedWire);
  });

  it('sendPayslips defaults an absent queuedCount/resend rather than leaking undefined', () => {
    let result: IPayslipDistributionAccepted | undefined;
    service.sendPayslips('r-1', false).subscribe((s) => (result = s));

    httpMock
      .expectOne(`${runsUrl}/r-1/payslips/send-emails`)
      .flush({ runId: 'r-1' }, { status: 202, statusText: 'Accepted' });

    // Server-computed count → 0; `resend` is the least-claiming FALSE.
    expect(result!.queuedCount).toBe(0);
    expect(result!.resend).toBeFalse();
  });

  it('sendPayslips passes confirm:true for a re-send (BR-5)', () => {
    service.sendPayslips('r-1', true).subscribe();
    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/send-emails`);
    expect(req.request.body).toEqual({ confirm: true });
    req.flush({ ...acceptedWire, resend: true });
  });

  it('getDistributionStatus GETs the distribution-summary endpoint', () => {
    let result: IPayslipDistributionStatus | undefined;
    service.getDistributionStatus('r-1').subscribe((s) => (result = s));

    const req = httpMock.expectOne(
      `${runsUrl}/r-1/payslips/distribution-summary`,
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(sampleWire);

    expect(result).toEqual(sample);
    expect(result as unknown).not.toBe(sampleWire);
  });

  it('getDistributionStatus treats an ABSENT isSending as STILL SENDING', () => {
    let result: IPayslipDistributionStatus | undefined;
    service.getDistributionStatus('r-1').subscribe((s) => (result = s));

    // `isSending` is REQUIRED in the schema, so this is a degraded payload — the point is which way
    // the mapper falls when the flag simply is not there.
    const { isSending, ...noFlag } = sampleWire;
    expect(isSending).toBeFalse();
    httpMock
      .expectOne(`${runsUrl}/r-1/payslips/distribution-summary`)
      .flush(noFlag);

    // `isSending === false` ends the poll and presents the run as distributed. A missing flag must
    // never tell an operator that a payslip mailing finished.
    expect(result!.isSending).toBeTrue();
  });

  it('getDistributionStatus treats an ABSENT hasSent as NOT-YET-SENT (no implied consent)', () => {
    let result: IPayslipDistributionStatus | undefined;
    service.getDistributionStatus('r-1').subscribe((s) => (result = s));

    const { hasSent, ...noFlag } = sampleWire;
    expect(hasSent).toBeTrue();
    httpMock
      .expectOne(`${runsUrl}/r-1/payslips/distribution-summary`)
      .flush(noFlag);

    // The component sends `confirm: !!status.hasSent`. Defaulting this to TRUE would assert the
    // BR-5 duplicate-send consent on a payload that never claimed a send had happened.
    expect(result!.hasSent).toBeFalse();
  });

  it('getDistributionStatus does NOT coerce an absent recipient status', () => {
    let result: IPayslipDistributionStatus | undefined;
    service.getDistributionStatus('r-1').subscribe((s) => (result = s));

    httpMock.expectOne(`${runsUrl}/r-1/payslips/distribution-summary`).flush({
      ...sampleWire,
      recipients: [{ employeeId: 'e-9', status: null }],
    });

    // `Sent` would claim an employee received their payslip; `Failed` would drop the row into the
    // "Re-send All Failed" set and mail them again. Neither is guessed.
    expect(result!.recipients[0].status as string).toBe('');
    expect(result!.recipients[0].recipientEmail).toBeNull();
    expect(result!.recipients[0].failureReason).toBeNull();
    expect(result!.recipients[0].sentAt).toBeNull();
    expect(result!.recipients[0].retryCount).toBe(0);
  });

  it('getDistributionStatus defaults a missing recipients array to [] (never undefined)', () => {
    let result: IPayslipDistributionStatus | undefined;
    service.getDistributionStatus('r-1').subscribe((s) => (result = s));

    const { recipients, ...noRecipients } = sampleWire;
    expect(recipients?.length).toBe(1);
    httpMock
      .expectOne(`${runsUrl}/r-1/payslips/distribution-summary`)
      .flush(noRecipients);

    expect(result!.recipients).toEqual([]);
  });

  it('resendAllFailed POSTs { onlyFailed: true } to resend-emails (FR-4)', () => {
    let result: IPayslipDistributionAccepted | undefined;
    service.resendAllFailed('r-1').subscribe((s) => (result = s));
    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/resend-emails`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ onlyFailed: true });
    req.flush({ ...acceptedWire, queuedCount: 7, resend: true });

    expect(result!.queuedCount).toBe(7);
    expect(result!.resend).toBeTrue();
  });

  it('resendSelective POSTs the employeeIds to resend-emails (FR-4)', () => {
    service.resendSelective('r-1', ['e-1', 'e-2']).subscribe();
    const req = httpMock.expectOne(`${runsUrl}/r-1/payslips/resend-emails`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ employeeIds: ['e-1', 'e-2'] });
    req.flush({ ...acceptedWire, queuedCount: 2, resend: true });
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
        .flush({ ...sampleWire, isSending: true, emailsSent: 100 });
      expect(emitted.length).toBe(1);
      expect(completed).toBeFalse();

      tick(PayslipEmailService.POLL_INTERVAL_MS);
      httpMock
        .expectOne(`${runsUrl}/r-1/payslips/distribution-summary`)
        .flush({ ...sampleWire, isSending: false, emailsSent: 240 });

      // Settled snapshot is emitted (inclusive takeWhile) then the stream completes.
      expect(emitted.length).toBe(2);
      expect(completed).toBeTrue();

      discardPeriodicTasks();
    }));
  });
});
