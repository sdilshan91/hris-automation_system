import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { MyPayslipService } from './my-payslip.service';
import { environment } from '../../../../environments/environment';
import {
  IMyPayslipDetail,
  IMyPayslipListItem,
  IMyPayslipPage,
  MyPayslipDetailWire,
  MyPayslipListItemWire,
  MyPayslipListWire,
} from '../models/my-payslip.models';

describe('MyPayslipService', () => {
  let service: MyPayslipService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/payroll/my-payslips`;

  // WIRE shapes (PayrollMyPayslipListDto / PayrollMyPayslipDetailDto) — flushing the
  // view-model instead would assert nothing about what the server actually sends.
  const items: MyPayslipListItemWire[] = [
    {
      payslipId: 'p-1',
      payMonth: 5,
      payYear: 2026,
      grossEarnings: 50000,
      totalDeductions: 8500,
      netSalary: 41500,
      paidDays: 22,
      lopDays: 0,
      pdfAvailable: true,
    },
  ];

  const page: MyPayslipListWire = {
    items,
    totalCount: 24,
    page: 1,
    pageSize: 12,
  };

  const detail: MyPayslipDetailWire = {
    payslipId: 'p-1',
    payMonth: 5,
    payYear: 2026,
    employee: {
      name: 'Alex Doe',
      employeeNo: 'EMP001',
      department: 'People',
      designation: 'Engineer',
    },
    earnings: [{ componentName: 'Basic Salary', amount: 25000, ytdAmount: 125000 }],
    deductions: [{ componentName: 'EPF (Employee)', amount: 3000, ytdAmount: 15000 }],
    grossEarnings: 50000,
    totalDeductions: 8500,
    netSalary: 41500,
    workingDays: 22,
    paidDays: 22,
    lopDays: 0,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        MyPayslipService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(MyPayslipService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listMyPayslips GETs the my-payslips endpoint with default paging params', () => {
    let result: IMyPayslipPage | undefined;
    service.listMyPayslips().subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('page') === '1',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('pageSize')).toBe('12');
    expect(req.request.params.has('year')).toBeFalse();
    expect(req.request.params.has('month')).toBeFalse();
    expect(req.request.withCredentials).toBeTrue();
    req.flush(page);

    expect(result).toEqual(page as IMyPayslipPage);
  });

  it('listMyPayslips appends the year filter when provided (FR-6)', () => {
    service.listMyPayslips(2025, null, 2, 6).subscribe();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.get('year')).toBe('2025');
    expect(req.request.params.has('month')).toBeFalse();
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('6');
    req.flush(page);
  });

  it('listMyPayslips appends the month filter when provided (FR-6)', () => {
    service.listMyPayslips(2026, 5).subscribe();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.get('year')).toBe('2026');
    expect(req.request.params.get('month')).toBe('5');
    req.flush(page);
  });

  it('listMyPayslips omits the month filter when null', () => {
    service.listMyPayslips(2026, null).subscribe();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.get('year')).toBe('2026');
    expect(req.request.params.has('month')).toBeFalse();
    req.flush(page);
  });

  it('listMyPayslips normalizes a bare array into a page', () => {
    let result: IMyPayslipPage | undefined;
    service.listMyPayslips().subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.flush(items);

    expect(result?.items).toEqual(items as IMyPayslipListItem[]);
    expect(result?.totalCount).toBe(items.length);
    expect(result?.page).toBe(1);
    expect(result?.pageSize).toBe(12);
  });

  it('listMyPayslips returns an empty page for a null/garbage body', () => {
    let result: IMyPayslipPage | undefined;
    service.listMyPayslips().subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.flush(null);

    expect(result?.items).toEqual([]);
    expect(result?.totalCount).toBe(0);
  });

  it('listMyPayslips defaults an ABSENT pdfAvailable to false', () => {
    let result: IMyPayslipPage | undefined;
    service.listMyPayslips().subscribe((r) => (result = r));

    // No `pdfAvailable` on the wire ⇒ do NOT offer a Download button for a PDF that
    // may never have been rendered.
    httpMock
      .expectOne((r) => r.url === baseUrl)
      .flush({ items: [{ payslipId: 'p-9' }], totalCount: 1, page: 1, pageSize: 12 });

    expect(result?.items[0].pdfAvailable).toBeFalse();
    expect(result?.items[0].netSalary).toBe(0);
  });

  it('getMyPayslip maps an absent ytdAmount to null, not 0 (FR-7 column gating)', () => {
    let result: IMyPayslipDetail | undefined;
    service.getMyPayslip('p-1').subscribe((r) => (result = r));

    httpMock.expectOne(`${baseUrl}/p-1`).flush({
      payslipId: 'p-1',
      earnings: [{ componentName: 'Basic Salary', amount: 25000 }],
    });

    // null (YTD disabled) must stay distinguishable from a real 0.00 YTD.
    expect(result?.earnings[0].ytdAmount).toBeNull();
    expect(result?.deductions).toEqual([]);
    expect(result?.employee).toEqual({
      name: '',
      employeeNo: '',
      department: null,
      designation: null,
    });
  });

  it('getMyPayslip GETs the detail by id', () => {
    let result: IMyPayslipDetail | undefined;
    service.getMyPayslip('p-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/p-1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(detail);

    expect(result).toEqual(detail as IMyPayslipDetail);
  });

  it('downloadMyPayslipPdf requests a blob with the full response (FR-4)', () => {
    let resp: import('@angular/common/http').HttpResponse<Blob> | undefined;
    service.downloadMyPayslipPdf('p-1').subscribe((r) => (resp = r));

    const req = httpMock.expectOne(`${baseUrl}/p-1/pdf`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.withCredentials).toBeTrue();

    const blob = new Blob(['%PDF-1.7'], { type: 'application/pdf' });
    req.flush(blob, {
      headers: { 'Content-Disposition': 'attachment; filename="EMP001_5_2026.pdf"' },
    });

    expect(resp?.body?.size).toBe(blob.size);
    expect(resp?.headers.get('Content-Disposition')).toContain('EMP001_5_2026.pdf');
  });
});
