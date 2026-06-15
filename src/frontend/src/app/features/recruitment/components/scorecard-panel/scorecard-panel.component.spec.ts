import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastrService } from 'ngx-toastr';
import { of } from 'rxjs';

import { ScorecardPanelComponent } from './scorecard-panel.component';
import { ScorecardService } from '../../services/scorecard.service';
import {
  IScorecard,
  IScorecardBoard,
} from '../../models/scorecard.models';

describe('ScorecardPanelComponent (US-REC-006)', () => {
  let component: ScorecardPanelComponent;
  let fixture: ComponentFixture<ScorecardPanelComponent>;
  let serviceSpy: jasmine.SpyObj<ScorecardService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const card = (over: Partial<IScorecard> = {}): IScorecard => ({
    id: 'sc-1',
    interviewId: 'iv-1',
    interviewerEmployeeId: 'e1',
    interviewerName: 'Ada Lovelace',
    overallRecommendation: 'Hire',
    ratings: [
      { criterionKey: 'tech', score: 4, comment: null },
      { criterionKey: 'comm', score: 5, comment: null },
    ],
    generalNotes: 'Great communicator',
    averageScore: 4.5,
    submittedAt: '2026-06-15T10:00:00Z',
    ...over,
  });

  const board = (over: Partial<IScorecardBoard> = {}): IScorecardBoard => ({
    scorecards: [card()],
    aggregateAverageScore: 4.5,
    hiddenCount: 0,
    antiBiasApplies: false,
    ...over,
  });

  function setup(b: IScorecardBoard): void {
    serviceSpy.listForInterview.and.returnValue(of(b));
    fixture = TestBed.createComponent(ScorecardPanelComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('interviewId', 'iv-1');
    fixture.componentRef.setInput('criteria', [
      { key: 'tech', label: 'Technical Skills' },
      { key: 'comm', label: 'Communication' },
    ]);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('ScorecardService', ['listForInterview']);
    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [ScorecardPanelComponent],
      providers: [
        { provide: ScorecardService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  it('loads the board for the bound interview id', () => {
    setup(board());
    expect(serviceSpy.listForInterview).toHaveBeenCalledWith('iv-1');
    expect(component.board()?.scorecards.length).toBe(1);
  });

  // ─── consolidated comparison table (AC-3 / FR-8) ──────────

  it('renders a criterion row per provided criterion with its scores', () => {
    setup(
      board({
        scorecards: [
          card({ id: 'a', interviewerName: 'Ada', averageScore: 4.5 }),
          card({
            id: 'b',
            interviewerName: 'Bob',
            averageScore: 3,
            ratings: [
              { criterionKey: 'tech', score: 2, comment: null },
              { criterionKey: 'comm', score: 4, comment: null },
            ],
          }),
        ],
        aggregateAverageScore: 3.8,
      }),
    );
    const rows = component.criterionRows();
    expect(rows.map((r) => r.name)).toEqual([
      'Technical Skills',
      'Communication',
    ]);
    // Tech scores: Ada=4, Bob=2 (index-aligned to scorecards).
    expect(rows[0].scores).toEqual([4, 2]);
  });

  it('highlights the aggregate average (FR-3)', () => {
    setup(board({ aggregateAverageScore: 3.8 }));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Aggregate average');
    expect(text).toContain('3.8');
  });

  it('shows an empty state when there are no scorecards and viewing is allowed', () => {
    setup(board({ scorecards: [], aggregateAverageScore: null }));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No scorecards yet');
  });

  // ─── anti-bias overlay (FR-6 / BR-5) ──────────────────────

  it('hides other scorecards behind the overlay when antiBiasApplies is true', () => {
    setup(
      board({
        antiBiasApplies: true,
        hiddenCount: 1,
      }),
    );
    expect(component.canView()).toBeFalse();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Submit your scorecard first');
  });

  it('allows viewing when antiBiasApplies is false (recruiter)', () => {
    setup(board({ antiBiasApplies: false }));
    expect(component.canView()).toBeTrue();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Submit your scorecard first');
  });

  // ─── interviewer CTA ──────────────────────────────────────

  it('emits submitOwn when the assigned interviewer clicks the CTA', () => {
    setup(
      board({
        antiBiasApplies: true,
        hiddenCount: 1,
      }),
    );
    let emitted = false;
    component.submitOwn.subscribe(() => (emitted = true));
    // The overlay's submit button.
    const overlayBtn = (
      fixture.nativeElement as HTMLElement
    ).querySelector('.absolute button') as HTMLButtonElement;
    overlayBtn.click();
    expect(emitted).toBeTrue();
  });

  it('derives criterion rows from scorecard keys when no criteria are provided', () => {
    serviceSpy.listForInterview.and.returnValue(of(board()));
    fixture = TestBed.createComponent(ScorecardPanelComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('interviewId', 'iv-1');
    fixture.detectChanges();
    const rows = component.criterionRows();
    expect(rows.map((r) => r.key)).toEqual(['tech', 'comm']);
  });
});
