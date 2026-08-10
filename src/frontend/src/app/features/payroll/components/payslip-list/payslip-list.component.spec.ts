import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { HttpHeaders, HttpResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { of, throwError, Subject } from 'rxjs';

import { PayslipListComponent } from './payslip-list.component';
import { TrappedDialogDirective } from '../../../../shared/directives';
import { PayslipService } from '../../services/payslip.service';
import {
  IPayslip,
  IPayslipGenerationStatus,
} from '../../models/payslip.models';

describe('PayslipListComponent', () => {
  let fixture: ComponentFixture<PayslipListComponent>;
  let component: PayslipListComponent;
  let payslip: jasmine.SpyObj<PayslipService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const rows: IPayslip[] = [
    {
      id: 's-1',
      employeeId: 'e-1',
      employeeName: 'Alex HR',
      employeeNo: 'EMP001',
      department: 'People',
      netSalary: 5000,
      pdfStatus: 'Generated',
      pdfGeneratedAt: '2026-05-31T10:05:00Z',
    },
    {
      id: 's-2',
      employeeId: 'e-2',
      employeeName: 'Bo Finance',
      employeeNo: 'EMP002',
      department: 'Finance',
      netSalary: 7000,
      pdfStatus: 'Pending',
      pdfGeneratedAt: null,
    },
    {
      id: 's-3',
      employeeId: 'e-3',
      employeeName: 'Cy Ops',
      employeeNo: 'EMP003',
      department: null,
      netSalary: 4000,
      pdfStatus: 'Failed',
      pdfGeneratedAt: null,
    },
  ];

  const status: IPayslipGenerationStatus = {
    runId: 'r-1',
    isGenerating: false,
    totalCount: 3,
    queuedCount: 3,
    failedCount: 0,
    pendingCount: 0,
  };

  function setup(initialRows: IPayslip[] = rows): void {
    payslip = jasmine.createSpyObj<PayslipService>('PayslipService', [
      'listPayslips',
      'generatePayslips',
      'regeneratePayslips',
      'streamGenerationStatus',
      'downloadPayslip',
      'downloadZip',
      'retryPayslip',
    ]);
    payslip.listPayslips.and.returnValue(of(initialRows));
    payslip.streamGenerationStatus.and.returnValue(of(status));
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);

    TestBed.configureTestingModule({
      imports: [PayslipListComponent],
      providers: [
        provideNoopAnimations(),
        { provide: PayslipService, useValue: payslip },
        { provide: ToastrService, useValue: toastr },
      ],
    });

    fixture = TestBed.createComponent(PayslipListComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('runId', 'r-1');
    fixture.componentRef.setInput('canGenerate', true);
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('loads payslips on init', () => {
    setup();
    expect(payslip.listPayslips).toHaveBeenCalledWith('r-1');
    expect(component.payslips()).toEqual(rows);
    expect(component.loading()).toBeFalse();
  });

  it('traps focus in the payslip preview dialog (ISSUE-296)', () => {
    setup();
    component.previewSlip.set(rows[0]);
    fixture.detectChanges();
    const dialog = fixture.debugElement.query(
      By.directive(TrappedDialogDirective),
    );
    expect(dialog).toBeTruthy();

    // ISSUE-296 behaviour: Escape routes through the directive's `dismiss` → the bound `closePreview()`,
    // so the preview actually closes. Catches a broken/removed `(dismiss)="closePreview()"` binding.
    (dialog.nativeElement as HTMLElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );
    expect(component.previewSlip()).toBeNull();
  });

  it('sets an error when the list fails to load', () => {
    setup();
    payslip.listPayslips.and.returnValue(throwError(() => new Error('boom')));
    component.loadPayslips();
    expect(component.error()).toBe('Could not load payslips for this run.');
  });

  it('renders the Notion table with employee rows and status badges', () => {
    setup();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Alex HR');
    expect(text).toContain('EMP001');
    expect(text).toContain('Generated');
    expect(text).toContain('Failed');
  });

  describe('search + filter', () => {
    beforeEach(() => setup());

    it('filters by free-text search across name/number/department', () => {
      component.search.set('finance');
      expect(component.filtered().map((p) => p.id)).toEqual(['s-2']);
    });

    it('filters by PDF status', () => {
      component.statusFilter.set('Failed');
      expect(component.filtered().map((p) => p.id)).toEqual(['s-3']);
    });

    it('combines search and status filter', () => {
      component.statusFilter.set('Generated');
      component.search.set('bo');
      expect(component.filtered().length).toBe(0);
    });
  });

  it('hasGenerated is true when any payslip is Generated', () => {
    setup();
    expect(component.hasGenerated()).toBeTrue();
  });

  it('hasGenerated is false when no payslip is Generated', () => {
    setup([rows[1], rows[2]]); // only Pending + Failed
    expect(component.hasGenerated()).toBeFalse();
  });

  describe('generation (AC-1 / AC-5)', () => {
    it('calls generatePayslips when nothing is generated yet, then streams + reloads', () => {
      setup([rows[1]]); // Pending only → hasGenerated false
      const stream = new Subject<IPayslipGenerationStatus>();
      payslip.generatePayslips.and.returnValue(
        of({ ...status, isGenerating: true, generatedCount: 0 }),
      );
      payslip.streamGenerationStatus.and.returnValue(stream.asObservable());
      payslip.listPayslips.calls.reset();
      payslip.listPayslips.and.returnValue(of(rows));

      component.generate();

      expect(payslip.generatePayslips).toHaveBeenCalledWith('r-1');
      expect(payslip.regeneratePayslips).not.toHaveBeenCalled();
      expect(component.generating()).toBeTrue();

      stream.next({ ...status, isGenerating: false });
      stream.complete();

      expect(component.generating()).toBeFalse();
      expect(payslip.listPayslips).toHaveBeenCalled();
      expect(toastr.success).toHaveBeenCalled();
    });

    it('calls regeneratePayslips when payslips already exist (AC-5)', () => {
      setup(); // has Generated → hasGenerated true
      payslip.regeneratePayslips.and.returnValue(of(status));

      component.generate();

      expect(payslip.regeneratePayslips).toHaveBeenCalledWith('r-1');
      expect(payslip.generatePayslips).not.toHaveBeenCalled();
    });

    it('toasts an error if generation fails to start', () => {
      setup([rows[1]]);
      payslip.generatePayslips.and.returnValue(
        throwError(() => new Error('boom')),
      );
      component.generate();
      expect(component.generating()).toBeFalse();
      expect(toastr.error).toHaveBeenCalled();
    });

    it('genPercent counts generated + failed against total', () => {
      setup();
      component.genStatus.set({
        ...status,
        totalCount: 200,
        queuedCount: 150,
        failedCount: 10,
      });
      expect(component.genPercent()).toBe(80);
    });
  });

  describe('preview (§8)', () => {
    it('downloads the PDF blob and exposes a sanitized URL', () => {
      setup();
      const blob = new Blob(['%PDF'], { type: 'application/pdf' });
      payslip.downloadPayslip.and.returnValue(
        of(new HttpResponse<Blob>({ body: blob })),
      );
      spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');

      component.preview(rows[0]);

      expect(payslip.downloadPayslip).toHaveBeenCalledWith('r-1', 'e-1');
      expect(component.previewSlip()).toEqual(rows[0]);
      expect(component.previewUrl()).toBeTruthy();
      expect(component.previewLoading()).toBeFalse();
    });

    it('closePreview clears state and revokes the object URL', () => {
      setup();
      const blob = new Blob(['%PDF'], { type: 'application/pdf' });
      payslip.downloadPayslip.and.returnValue(
        of(new HttpResponse<Blob>({ body: blob })),
      );
      spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
      const revoke = spyOn(URL, 'revokeObjectURL');

      component.preview(rows[0]);
      component.closePreview();

      expect(component.previewSlip()).toBeNull();
      expect(component.previewUrl()).toBeNull();
      expect(revoke).toHaveBeenCalledWith('blob:fake');
    });

    it('toasts an error if the preview download fails', () => {
      setup();
      payslip.downloadPayslip.and.returnValue(
        throwError(() => new Error('boom')),
      );
      component.preview(rows[0]);
      expect(component.previewLoading()).toBeFalse();
      expect(toastr.error).toHaveBeenCalled();
    });
  });

  describe('downloads (AC-3)', () => {
    it('download triggers an anchor click with the Content-Disposition filename', () => {
      setup();
      const blob = new Blob(['%PDF'], { type: 'application/pdf' });
      payslip.downloadPayslip.and.returnValue(
        of(
          new HttpResponse<Blob>({
            body: blob,
            headers: new HttpHeaders({
              'Content-Disposition':
                'attachment; filename="EMP001_May_2026.pdf"',
            }),
          }),
        ),
      );
      spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
      spyOn(URL, 'revokeObjectURL');
      const click = jasmine.createSpy('click');
      const anchor = { href: '', download: '', click } as unknown as HTMLAnchorElement;
      spyOn(document, 'createElement').and.returnValue(anchor);

      component.download(rows[0]);

      expect(payslip.downloadPayslip).toHaveBeenCalledWith('r-1', 'e-1');
      expect(anchor.download).toBe('EMP001_May_2026.pdf');
      expect(click).toHaveBeenCalled();
      expect(component.downloadingId()).toBeNull();
    });

    it('toasts an error when a single download fails', () => {
      setup();
      payslip.downloadPayslip.and.returnValue(
        throwError(() => new Error('boom')),
      );
      component.download(rows[0]);
      expect(component.downloadingId()).toBeNull();
      expect(toastr.error).toHaveBeenCalled();
    });

    it('downloadAll triggers a ZIP anchor download (desktop-only, AC-3)', () => {
      setup();
      const blob = new Blob(['PK'], { type: 'application/zip' });
      payslip.downloadZip.and.returnValue(
        of(new HttpResponse<Blob>({ body: blob })),
      );
      spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
      spyOn(URL, 'revokeObjectURL');
      const click = jasmine.createSpy('click');
      const anchor = { href: '', download: '', click } as unknown as HTMLAnchorElement;
      spyOn(document, 'createElement').and.returnValue(anchor);

      component.downloadAll();

      expect(payslip.downloadZip).toHaveBeenCalledWith('r-1');
      expect(anchor.download).toBe('payslips-r-1.zip');
      expect(click).toHaveBeenCalled();
      expect(component.zipDownloading()).toBeFalse();
    });

    it('toasts an error when the ZIP download fails', () => {
      setup();
      payslip.downloadZip.and.returnValue(throwError(() => new Error('boom')));
      component.downloadAll();
      expect(component.zipDownloading()).toBeFalse();
      expect(toastr.error).toHaveBeenCalled();
    });
  });

  describe('retry (DF-31 / ISSUE-162 FR-8)', () => {
    /** Retry buttons in the desktop table only, keyed by their text content. */
    function retryButtons(): HTMLButtonElement[] {
      const table = fixture.debugElement.query(By.css('table'));
      return (table.queryAll(By.css('button')) as { nativeElement: HTMLButtonElement }[])
        .map((d) => d.nativeElement)
        .filter((b) => (b.textContent ?? '').trim() === 'Retry');
    }

    it('renders a Retry button only for Failed rows', () => {
      setup(); // rows: s-1 Generated, s-2 not-failed, s-3 Failed
      const buttons = retryButtons();
      // Exactly one Failed row → exactly one Retry button.
      expect(buttons.length).toBe(1);
    });

    it('does not render a Retry button when no row is Failed', () => {
      setup([rows[0], rows[1]]); // no Failed row
      expect(retryButtons().length).toBe(0);
    });

    it('hides the Retry button when the user cannot generate', () => {
      setup();
      fixture.componentRef.setInput('canGenerate', false);
      fixture.detectChanges();
      expect(retryButtons().length).toBe(0);
    });

    it('retry() calls the service with the row employeeId, then reloads + toasts', () => {
      setup(); // Failed row is rows[2] → e-3
      payslip.retryPayslip.and.returnValue(of(undefined));
      payslip.streamGenerationStatus.and.returnValue(of(status));
      payslip.listPayslips.calls.reset();
      payslip.listPayslips.and.returnValue(of(rows));

      component.retry(rows[2]);

      expect(payslip.retryPayslip).toHaveBeenCalledWith('r-1', 'e-3');
      // streamStatus() completes synchronously (of(status)) → reload + success toast.
      expect(payslip.listPayslips).toHaveBeenCalled();
      expect(toastr.success).toHaveBeenCalled();
      expect(component.retryingId()).toBeNull();
      expect(component.generating()).toBeFalse();
    });

    it('clicking the row Retry button invokes retry()', () => {
      setup();
      payslip.retryPayslip.and.returnValue(of(undefined));
      payslip.streamGenerationStatus.and.returnValue(of(status));
      const spy = spyOn(component, 'retry').and.callThrough();

      retryButtons()[0].click();

      expect(spy).toHaveBeenCalled();
      expect(payslip.retryPayslip).toHaveBeenCalledWith('r-1', 'e-3');
    });

    it('toasts an error and clears retryingId when retry fails', () => {
      setup();
      payslip.retryPayslip.and.returnValue(throwError(() => new Error('boom')));

      component.retry(rows[2]);

      expect(component.retryingId()).toBeNull();
      expect(toastr.error).toHaveBeenCalled();
    });
  });

  it('shows the mobile note that bulk download is desktop-only (§8)', () => {
    setup();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Bulk ZIP download is available on desktop');
  });
});
