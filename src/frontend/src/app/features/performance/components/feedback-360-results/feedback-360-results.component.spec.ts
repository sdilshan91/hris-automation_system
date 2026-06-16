import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { HttpResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';

import { Feedback360ResultsComponent } from './feedback-360-results.component';
import { Feedback360Service } from '../../services/feedback-360.service';
import { IFeedback360Results } from '../../models/feedback-360.models';

function makeResults(
  overrides: Partial<IFeedback360Results> = {},
): IFeedback360Results {
  return {
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    ratingScaleMax: 5,
    anonymous: true,
    released: true,
    compositeScore: 82,
    competencies: [
      {
        questionId: 'q-1',
        title: 'Communication',
        kind: 'Competency',
        overallAverage: 4.2,
        byCategory: [
          { category: 'Self', average: 4, responseCount: 1 },
          { category: 'Manager', average: 4.5, responseCount: 1 },
          { category: 'Peer', average: 4.1, responseCount: 3 },
        ],
      },
    ],
    categoryAverages: [
      { category: 'Self', average: 4, responseCount: 1 },
      { category: 'Manager', average: 4.5, responseCount: 1 },
      { category: 'Peer', average: 4.1, responseCount: 3 },
      { category: 'DirectReport', average: null, responseCount: 0 },
    ],
    comments: [
      {
        questionTitle: 'Communication',
        category: 'Peer',
        comment: 'Communicates clearly in standups.',
        // no reviewerName → anonymous
      },
    ],
    exportAvailable: true,
    ...overrides,
  };
}

describe('Feedback360ResultsComponent', () => {
  let fixture: ComponentFixture<Feedback360ResultsComponent>;
  let component: Feedback360ResultsComponent;
  let serviceSpy: jasmine.SpyObj<Feedback360Service>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(
    results: IFeedback360Results = makeResults(),
  ): Promise<void> {
    serviceSpy = jasmine.createSpyObj<Feedback360Service>(
      'Feedback360Service',
      ['getResults', 'exportResultsPdf'],
    );
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getResults.and.returnValue(of(results));

    await TestBed.configureTestingModule({
      imports: [Feedback360ResultsComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: Feedback360Service, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'e-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Feedback360ResultsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders the composite score (FR-6)', async () => {
    await setup();
    const el = fixture.nativeElement.querySelector(
      '[data-testid="composite-score"]',
    );
    expect(el.textContent).toContain('82');
  });

  it('renders a per-competency average row (AC-4)', async () => {
    await setup();
    const rows = fixture.nativeElement.querySelectorAll(
      '[data-testid="competency-row"]',
    );
    expect(rows.length).toBe(1);
    expect((rows[0] as HTMLElement).textContent).toContain('Communication');
  });

  it('orders the perspective comparison Self/Manager/Peer/DirectReport (AC-4)', async () => {
    await setup();
    const cats = component
      .orderedCategoryAverages()
      .map((c) => c.category);
    expect(cats).toEqual(['Self', 'Manager', 'Peer', 'DirectReport']);
  });

  it('NEVER shows a reviewer identity when anonymity is on (FR-5/NFR-3)', async () => {
    await setup(makeResults({ anonymous: true }));
    const authors = fixture.nativeElement.querySelectorAll(
      '[data-testid="comment-author"]',
    );
    const anon = fixture.nativeElement.querySelectorAll(
      '[data-testid="comment-anonymous"]',
    );
    expect(authors.length).toBe(0);
    expect(anon.length).toBe(1);
    // and the comment text still renders
    const commentRow = fixture.nativeElement.querySelector(
      '[data-testid="comment-row"]',
    );
    expect(commentRow.textContent).toContain('Communicates clearly');
  });

  it('shows the reviewer name only when anonymity is off and the API returns it', async () => {
    await setup(
      makeResults({
        anonymous: false,
        comments: [
          {
            questionTitle: 'Communication',
            category: 'Manager',
            comment: 'Great clarity.',
            reviewerName: 'Morgan Manager',
          },
        ],
      }),
    );
    const author = fixture.nativeElement.querySelector(
      '[data-testid="comment-author"]',
    );
    expect(author.textContent).toContain('Morgan Manager');
  });

  it('does NOT reconstruct identity if anonymity is on even when a name leaks in', async () => {
    // Defensive: if the backend mistakenly includes a name under anonymity, the
    // template still renders "Anonymous" (gated on r.anonymous, not on the field).
    await setup(
      makeResults({
        anonymous: true,
        comments: [
          {
            questionTitle: 'Communication',
            category: 'Peer',
            comment: 'x',
            reviewerName: 'Leaked Name',
          },
        ],
      }),
    );
    const author = fixture.nativeElement.querySelector(
      '[data-testid="comment-author"]',
    );
    const anon = fixture.nativeElement.querySelector(
      '[data-testid="comment-anonymous"]',
    );
    expect(author).toBeNull();
    expect(anon).toBeTruthy();
    expect(fixture.nativeElement.textContent).not.toContain('Leaked Name');
  });

  it('shows a not-released warning when the threshold is unmet (BR-4)', async () => {
    await setup(makeResults({ released: false }));
    const warn = fixture.nativeElement.querySelector(
      '[data-testid="not-released"]',
    );
    expect(warn).toBeTruthy();
  });

  it('hides the export button when exportAvailable is false (FR-7)', async () => {
    await setup(makeResults({ exportAvailable: false }));
    const btn = fixture.nativeElement.querySelector(
      '[data-testid="export-pdf"]',
    );
    expect(btn).toBeNull();
  });

  it('exportPdf() downloads the blob with the disposition filename', async () => {
    await setup();
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    const response = new HttpResponse({
      body: blob,
      status: 200,
      headers: undefined,
    });
    serviceSpy.exportResultsPdf.and.returnValue(of(response));
    const clickSpy = jasmine.createSpy('click');
    spyOn(document, 'createElement').and.returnValue({
      href: '',
      download: '',
      click: clickSpy,
    } as unknown as HTMLAnchorElement);
    spyOn(URL, 'createObjectURL').and.returnValue('blob:url');
    spyOn(URL, 'revokeObjectURL');

    component.exportPdf();

    expect(serviceSpy.exportResultsPdf).toHaveBeenCalledWith('e-1');
    expect(clickSpy).toHaveBeenCalled();
    expect(component.exporting()).toBeFalse();
  });
});
