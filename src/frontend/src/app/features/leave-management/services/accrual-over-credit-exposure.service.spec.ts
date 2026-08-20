import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpHeaders } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { AccrualOverCreditExposureService } from './accrual-over-credit-exposure.service';
import { IAccrualOverCreditExposureReport } from '../models/accrual-over-credit-exposure.models';
import { environment } from '../../../../environments/environment';

describe('AccrualOverCreditExposureService (BUG-291)', () => {
  let service: AccrualOverCreditExposureService;
  let httpMock: HttpTestingController;
  const resource = `${environment.apiBaseUrl}/tenant/leave-entitlements/accrual-over-credit-exposure`;

  const mockReport: IAccrualOverCreditExposureReport = {
    asOfDate: '2026-07-30',
    leaveYear: 2026,
    rows: [
      {
        employeeId: 'emp-1',
        employeeNo: 'E-001',
        employeeName: 'Alice Anderson',
        leaveTypeId: 'lt-1',
        leaveTypeName: 'Annual Leave',
        leaveYear: 2026,
        accrualFrequency: 'Monthly',
        creditedDays: 24,
        shouldHaveAccruedDays: 14,
        overCreditedDays: 10,
        isEmployeeActive: true,
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        AccrualOverCreditExposureService,
      ],
    });
    service = TestBed.inject(AccrualOverCreditExposureService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getExposure calls the endpoint with the asOfDate param and maps each row', () => {
    let result: IAccrualOverCreditExposureReport | undefined;
    service.getExposure('2026-07-30').subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) => r.url === resource && r.params.get('asOfDate') === '2026-07-30',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('format')).toBeFalse();
    expect(req.request.withCredentials).toBeTrue();
    // Flush the wire envelope + a row carrying a field the VM does not model.
    req.flush({
      ...mockReport,
      rows: [{ ...mockReport.rows[0], legacyBatchId: 'batch-9' }],
    });

    // mapAccrualExposureRow reconstructs each row from the generated contract, so the mapped result equals the
    // VM report and the wire-only field is dropped. A raw pass-through cast would leak it — fails pre-migration.
    expect(result).toEqual(mockReport);
    expect(result!.rows[0].overCreditedDays).toBe(10);
    expect(result!.rows[0].employeeName).toBe('Alice Anderson');
    expect('legacyBatchId' in result!.rows[0]).toBeFalse();
  });

  it('exportExposure passes format=csv through and reads the blob as a file', () => {
    let filename: string | undefined;
    let blob: Blob | undefined;
    service.exportExposure('2026-07-30', 'csv').subscribe((res) => {
      filename = res.filename;
      blob = res.blob;
    });

    const req = httpMock.expectOne(
      (r) =>
        r.url === resource &&
        r.params.get('asOfDate') === '2026-07-30' &&
        r.params.get('format') === 'csv',
    );
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['a,b,c'], { type: 'text/csv' }), {
      headers: new HttpHeaders({
        'Content-Disposition': 'attachment; filename="exposure-2026-07-30.csv"',
      }),
    });

    expect(blob).toBeTruthy();
    expect(filename).toBe('exposure-2026-07-30.csv');
  });

  it('exportExposure passes format=xlsx through and falls back to a constructed filename', () => {
    let filename: string | undefined;
    service.exportExposure('2026-07-30', 'xlsx').subscribe((res) => (filename = res.filename));

    const req = httpMock.expectOne(
      (r) => r.url === resource && r.params.get('format') === 'xlsx',
    );
    req.flush(new Blob(['x']), {}); // no Content-Disposition header

    expect(filename).toBe('accrual-over-credit-exposure-2026-07-30.xlsx');
  });
});
