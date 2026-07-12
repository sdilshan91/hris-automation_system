import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpHeaders, HttpResponse } from '@angular/common/http';
import { of } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { ReviewRecordComponent } from './review-record.component';
import { ReviewSignoffService } from '../../services/review-signoff.service';
import { IReviewSignoff } from '../../models/review-signoff.models';

function makeRecord(overrides: Partial<IReviewSignoff> = {}): IReviewSignoff {
  return {
    reviewId: 'rv-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    managerName: 'Sam Lead',
    status: 'SignedOff',
    ratingScaleMax: 5,
    finalScore: 87,
    meetingNotesHtml: '<p>Great discussion.</p>',
    managerSignature: { name: 'Sam Lead', signedOn: '2026-06-10T09:00:00Z' },
    employeeSignature: { name: 'Alex Doe', signedOn: '2026-06-12T10:00:00Z' },
    disputeComments: null,
    disputedOn: null,
    employeeViewed: true,
    goals: [{ goalId: 'g-1', title: 'Improve NPS', weight: 100, managerRating: 4 }],
    exportAvailable: true,
    ...overrides,
  };
}

describe('ReviewRecordComponent', () => {
  let fixture: ComponentFixture<ReviewRecordComponent>;
  let component: ReviewRecordComponent;
  let serviceSpy: jasmine.SpyObj<ReviewSignoffService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(record: IReviewSignoff): Promise<void> {
    serviceSpy = jasmine.createSpyObj<ReviewSignoffService>(
      'ReviewSignoffService',
      ['exportPdf'],
    );
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);

    await TestBed.configureTestingModule({
      imports: [ReviewRecordComponent],
      providers: [
        { provide: ReviewSignoffService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ReviewRecordComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('record', record);
    fixture.detectChanges();
  }

  it('renders the full timeline, signatures, and sanitized notes (AC-4)', async () => {
    await setup(makeRecord());
    const el = fixture.nativeElement as HTMLElement;

    const steps = el.querySelectorAll('[data-testid="timeline-step"]');
    expect(steps.length).toBe(4);
    expect(el.querySelector('[data-testid="manager-signature"]')?.textContent)
      .toContain('Sam Lead');
    expect(el.querySelector('[data-testid="employee-signature"]')?.textContent)
      .toContain('Alex Doe');
    expect(el.querySelector('[data-testid="record-notes"]')?.innerHTML)
      .toContain('Great discussion.');
    expect(el.querySelector('[data-testid="record-final-score"]')?.textContent)
      .toContain('87%');
  });

  it('renders the dispute branch in the timeline when disputed', async () => {
    await setup(
      makeRecord({
        status: 'Disputed',
        employeeSignature: null,
        disputeComments: 'I disagree',
        disputedOn: '2026-06-11T00:00:00Z',
      }),
    );
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Disputed');
    expect(el.querySelector('[data-testid="record-dispute"]')?.textContent)
      .toContain('I disagree');
  });

  it('hides the export button when the backend has no export endpoint (FR-6)', async () => {
    await setup(makeRecord({ exportAvailable: false }));
    expect(
      fixture.nativeElement.querySelector('[data-testid="export-pdf-btn"]'),
    ).toBeFalsy();
  });

  it('exportPdf() downloads the blob using the Content-Disposition filename (FR-6)', async () => {
    await setup(makeRecord({ exportAvailable: true }));
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    serviceSpy.exportPdf.and.returnValue(
      of(
        new HttpResponse<Blob>({
          body: blob,
          headers: new HttpHeaders({
            'Content-Disposition': 'attachment; filename="review-rv-1.pdf"',
          }),
          status: 200,
        }),
      ),
    );

    const anchor = { href: '', download: '', click: jasmine.createSpy('click') };
    spyOn(document, 'createElement').and.returnValue(
      anchor as unknown as HTMLAnchorElement,
    );
    spyOn(URL, 'createObjectURL').and.returnValue('blob:url');
    const revoke = spyOn(URL, 'revokeObjectURL');

    component.exportPdf();

    expect(serviceSpy.exportPdf).toHaveBeenCalledWith('e-1');
    expect(anchor.download).toBe('review-rv-1.pdf');
    expect(anchor.click).toHaveBeenCalled();
    expect(revoke).toHaveBeenCalledWith('blob:url');
    expect(component.exporting()).toBeFalse();
  });
});
