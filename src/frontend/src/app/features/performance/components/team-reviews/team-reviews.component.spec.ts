import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { TeamReviewsComponent } from './team-reviews.component';
import { ManagerReviewService } from '../../services/manager-review.service';
import { IManagerTeamRow } from '../../models/manager-review.models';

function makeRows(): IManagerTeamRow[] {
  return [
    {
      reviewId: 'rv-1',
      employeeId: 'e-1',
      employeeName: 'Zoe Carter',
      jobTitle: 'Engineer',
      status: 'SelfAssessmentSubmitted',
      goalCount: 2,
      selfSubmittedOn: '2026-06-01T10:00:00Z',
    },
    {
      reviewId: null,
      employeeId: 'e-2',
      employeeName: 'Alex Doe',
      jobTitle: 'Designer',
      status: 'PendingSelfAssessment',
      goalCount: 3,
      selfSubmittedOn: null,
    },
    {
      reviewId: 'rv-3',
      employeeId: 'e-3',
      employeeName: 'Mia Brown',
      jobTitle: 'Analyst',
      status: 'Completed',
      goalCount: 1,
      selfSubmittedOn: '2026-05-20T10:00:00Z',
    },
  ];
}

describe('TeamReviewsComponent (AC-4)', () => {
  let fixture: ComponentFixture<TeamReviewsComponent>;
  let component: TeamReviewsComponent;
  let serviceSpy: jasmine.SpyObj<ManagerReviewService>;

  async function setup(rows: IManagerTeamRow[]): Promise<void> {
    serviceSpy = jasmine.createSpyObj<ManagerReviewService>(
      'ManagerReviewService',
      ['getTeam', 'getEmployeeReview', 'saveDraft', 'submit'],
    );
    serviceSpy.getTeam.and.returnValue(of(rows));

    await TestBed.configureTestingModule({
      imports: [TeamReviewsComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: ManagerReviewService, useValue: serviceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TeamReviewsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function rowTexts(): string[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="team-row"]'),
    ).map((el) => (el as HTMLElement).textContent ?? '');
  }

  it('renders every team member with a color-coded status chip', async () => {
    await setup(makeRows());

    const chips = fixture.nativeElement.querySelectorAll(
      '[data-testid="status-chip"]',
    );
    expect(chips.length).toBe(3);
    // Completed => emerald, Pending => neutral, SelfAssessmentSubmitted => sky.
    const classes = Array.from(chips).map((c) => (c as HTMLElement).className);
    expect(classes.join(' ')).toContain('emerald');
    expect(classes.join(' ')).toContain('sky');
  });

  it('sorts by employee name ascending by default', async () => {
    await setup(makeRows());
    const texts = rowTexts();
    expect(texts[0]).toContain('Alex Doe');
    expect(texts[1]).toContain('Mia Brown');
    expect(texts[2]).toContain('Zoe Carter');
  });

  it('reverses name order when the sort header is toggled', async () => {
    await setup(makeRows());
    component.toggleSort('employeeName');
    fixture.detectChanges();
    const texts = rowTexts();
    expect(texts[0]).toContain('Zoe Carter');
    expect(texts[2]).toContain('Alex Doe');
  });

  it('filters rows by the search term', async () => {
    await setup(makeRows());
    component.search.set('mia');
    fixture.detectChanges();
    const texts = rowTexts();
    expect(texts.length).toBe(1);
    expect(texts[0]).toContain('Mia Brown');
  });

  it('filters rows by status', async () => {
    await setup(makeRows());
    component.statusFilter.set('Completed');
    fixture.detectChanges();
    const texts = rowTexts();
    expect(texts.length).toBe(1);
    expect(texts[0]).toContain('Mia Brown');
  });

  it('does not offer a review link for pending self-assessment rows', async () => {
    await setup(makeRows());
    // Only the two non-pending rows are reviewable.
    const links = fixture.nativeElement.querySelectorAll(
      '[data-testid="review-link"]',
    );
    expect(links.length).toBe(2);
  });

  it('shows an empty state when the manager has no reports', async () => {
    await setup([]);
    expect(fixture.nativeElement.textContent).toContain('No team members yet');
  });

  it('shows an error with retry when loading fails', async () => {
    serviceSpy = jasmine.createSpyObj<ManagerReviewService>(
      'ManagerReviewService',
      ['getTeam', 'getEmployeeReview', 'saveDraft', 'submit'],
    );
    serviceSpy.getTeam.and.returnValue(throwError(() => new Error('boom')));

    await TestBed.configureTestingModule({
      imports: [TeamReviewsComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: ManagerReviewService, useValue: serviceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TeamReviewsComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'Unable to load your team reviews.',
    );
  });
});
