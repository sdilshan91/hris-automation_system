import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { AdjustmentBulkUploadComponent } from './adjustment-bulk-upload.component';
import { AdjustmentService } from '../../services/adjustment.service';
import { IBulkAdjustmentResult } from '../../models/adjustment.models';

/** The committed result the BE returns from the bulk endpoint. */
const bulkResult: IBulkAdjustmentResult = {
  totalRows: 2,
  succeededCount: 1,
  failedCount: 1,
  results: [
    {
      rowNumber: 1,
      employeeNo: 'EMP001',
      success: true,
      adjustmentId: 'a-1',
      error: null,
      errorCode: null,
    },
    {
      rowNumber: 2,
      employeeNo: 'BAD',
      success: false,
      adjustmentId: null,
      error: 'Unknown employee',
      errorCode: 'EMP_NOT_FOUND',
    },
  ],
};

/**
 * A CSV body with one valid row (EMP001/Bonus/1000) and one invalid row
 * (blank employee_no) — the client-side parser builds a 1-valid/1-invalid preview.
 */
const CSV_BODY = [
  'employee_no,adjustment_type,amount,description,is_taxable',
  'EMP001,Bonus,1000,ok,true',
  ',Bonus,500,missing emp,false',
].join('\n');

describe('AdjustmentBulkUploadComponent', () => {
  let fixture: ComponentFixture<AdjustmentBulkUploadComponent>;
  let component: AdjustmentBulkUploadComponent;
  let service: jasmine.SpyObj<AdjustmentService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  function csvFile(body = CSV_BODY, name = 'adj.csv'): File {
    return new File([body], name, { type: 'text/csv' });
  }

  /** Pick a file and wait for the async FileReader-based client-side parse to land. */
  async function pickFile(file: File): Promise<void> {
    component.onFileSelected({
      target: { files: [file], value: 'x' },
    } as unknown as Event);
    // FileReader.onload resolves on a macrotask; wait until the preview is set.
    await waitFor(() => component.preview() !== null || component.fileError() !== null);
  }

  function waitFor(cond: () => boolean, timeoutMs = 2000): Promise<void> {
    return new Promise((resolve, reject) => {
      const start = Date.now();
      const tick = () => {
        if (cond()) {
          resolve();
        } else if (Date.now() - start > timeoutMs) {
          reject(new Error('waitFor timed out'));
        } else {
          setTimeout(tick, 5);
        }
      };
      tick();
    });
  }

  beforeEach(async () => {
    service = jasmine.createSpyObj('AdjustmentService', [
      'bulkUpload',
      'bulkTemplateCsv',
    ]);
    service.bulkTemplateCsv.and.returnValue(
      'employee_no,adjustment_type,amount,description,is_taxable\nEMP001,Bonus,10000,bonus,true',
    );
    toastr = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [AdjustmentBulkUploadComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AdjustmentService, useValue: service },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdjustmentBulkUploadComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('creates', () => {
    expect(component).toBeTruthy();
  });

  it('defaults to a sensible period (no Date.now)', () => {
    expect(component.payMonth()).toBe(6);
    expect(component.payYear()).toBe(2026);
  });

  it('rejects a non-CSV file and does not parse a preview', () => {
    const file = new File(['x'], 'notes.txt', { type: 'text/plain' });
    component.onFileSelected({
      target: { files: [file], value: 'x' },
    } as unknown as Event);
    expect(component.fileError()).toContain('.csv');
    expect(component.preview()).toBeNull();
  });

  it('parses the CSV client-side to build the preview (FR-2)', async () => {
    await pickFile(csvFile());

    const p = component.preview();
    expect(p).not.toBeNull();
    expect(p!.totalRows).toBe(2);
    expect(p!.validCount).toBe(1);
    expect(p!.invalidCount).toBe(1);
    expect(component.hasValidRows()).toBeTrue();
    expect(component.fileName()).toBe('adj.csv');
    // No server preview call exists anymore.
    expect((service as unknown as Record<string, unknown>)['previewBulkUpload']).toBeUndefined();
  });

  it('marks a row invalid when the adjustment_type is not allowed', async () => {
    await pickFile(
      csvFile(
        [
          'employee_no,adjustment_type,amount,description,is_taxable',
          'EMP001,Nonsense,1000,bad type,true',
        ].join('\n'),
      ),
    );
    const p = component.preview();
    expect(p!.validCount).toBe(0);
    expect(p!.rows[0].error).toContain('adjustment_type');
  });

  it('marks a row invalid when the amount is not a positive number', async () => {
    await pickFile(
      csvFile(
        [
          'employee_no,adjustment_type,amount,description,is_taxable',
          'EMP001,Bonus,-5,negative,true',
        ].join('\n'),
      ),
    );
    const p = component.preview();
    expect(p!.validCount).toBe(0);
    expect(p!.rows[0].error).toContain('positive');
  });

  it('accepts a dropped CSV via the drop handler', async () => {
    const event = {
      preventDefault: () => {},
      dataTransfer: { files: [csvFile()] },
    } as unknown as DragEvent;
    component.onDrop(event);
    await waitFor(() => component.preview() !== null);
    expect(component.dragging()).toBeFalse();
    expect(component.preview()!.validCount).toBe(1);
  });

  it('toggles the drag highlight on dragover/dragleave', () => {
    const ev = { preventDefault: () => {} } as unknown as DragEvent;
    component.onDragOver(ev);
    expect(component.dragging()).toBeTrue();
    component.onDragLeave(ev);
    expect(component.dragging()).toBeFalse();
  });

  it('does not commit without a file or with zero valid rows', () => {
    component.commit();
    expect(service.bulkUpload).not.toHaveBeenCalled();
  });

  it('commits with (file, payMonth, payYear), toasts the counts, resets and emits (FR-2)', async () => {
    service.bulkUpload.and.returnValue(of(bulkResult));
    const committedSpy = jasmine.createSpy('committed');
    component.committed.subscribe(committedSpy);

    const file = csvFile();
    await pickFile(file);
    component.payMonth.set(7);
    component.payYear.set(2027);
    expect(component.canCommit()).toBeTrue();

    component.commit();

    expect(service.bulkUpload).toHaveBeenCalledWith(file, 7, 2027);
    expect(toastr.success).toHaveBeenCalled();
    expect(committedSpy).toHaveBeenCalled();
    expect(component.preview()).toBeNull();
  });

  it('shows an error toast when the commit fails', async () => {
    service.bulkUpload.and.returnValue(throwError(() => new Error('fail')));
    await pickFile(csvFile());
    component.commit();

    expect(toastr.error).toHaveBeenCalled();
    expect(component.committing()).toBeFalse();
  });

  it('downloads a client-side template Blob and triggers an anchor click (§8)', () => {
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');
    spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    spyOn(URL, 'revokeObjectURL');

    component.downloadTemplate();

    expect(service.bulkTemplateCsv).toHaveBeenCalled();
    expect(clickSpy).toHaveBeenCalled();
  });
});
