import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ActivatedRoute } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { CdkDragDrop } from '@angular/cdk/drag-drop';

import { ApplicantPipelineComponent } from './applicant-pipeline.component';
import { PipelineService } from '../../services/pipeline.service';
import {
  IApplicantCard,
  IPipelineBoard,
  IPipelineStage,
  IStageMoveResult,
} from '../../models/pipeline.models';
import { ApplicantStage } from '../../models/applicant.models';

describe('ApplicantPipelineComponent', () => {
  let component: ApplicantPipelineComponent;
  let fixture: ComponentFixture<ApplicantPipelineComponent>;
  let serviceSpy: jasmine.SpyObj<PipelineService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const card = (over: Partial<IApplicantCard> = {}): IApplicantCard => ({
    id: 'app-1',
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    source: 'Public',
    isInternal: false,
    appliedAt: '2026-06-10T00:00:00Z',
    stage: 'Applied',
    ...over,
  });

  const moveResult = (
    stage: ApplicantStage,
    warnings: string[] = [],
  ): IStageMoveResult => ({
    id: 'app-1',
    stage,
    enteredStageAt: '2026-06-15T00:00:00Z',
    warnings,
  });

  const fullBoard = (): IPipelineBoard => ({
    vacancyId: 'vac-1',
    vacancyTitle: 'Senior Engineer',
    stages: [
      { stage: 'Applied', count: 1, applicants: [card()] },
      { stage: 'Screening', count: 0, applicants: [] },
      { stage: 'Interview', count: 0, applicants: [] },
      { stage: 'Offer', count: 0, applicants: [] },
      { stage: 'Hired', count: 0, applicants: [] },
      { stage: 'Rejected', count: 0, applicants: [] },
    ],
    total: 1,
  });

  /** Build a CDK drop event moving the first card of `from` into `to`. */
  const dropEvent = (
    from: IPipelineStage,
    to: IPipelineStage,
    movedCard: IApplicantCard,
  ): CdkDragDrop<IPipelineStage> =>
    ({
      previousContainer: { data: from },
      container: { data: to },
      previousIndex: 0,
      currentIndex: 0,
      item: { data: movedCard },
    }) as unknown as CdkDragDrop<IPipelineStage>;

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('PipelineService', [
      'getPipeline',
      'getApplicant',
      'changeStage',
      'downloadResume',
    ]);
    serviceSpy.getPipeline.and.returnValue(of(fullBoard()));
    serviceSpy.changeStage.and.returnValue(of(moveResult('Screening')));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [ApplicantPipelineComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: PipelineService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => 'vac-1' } },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ApplicantPipelineComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads the board on init', () => {
    expect(serviceSpy.getPipeline).toHaveBeenCalledWith('vac-1', {});
    expect(component.board().length).toBe(6);
    expect(component.total()).toBe(1);
    expect(component.boardTitle()).toBe('Senior Engineer');
  });

  it('shows an error toast when the board fails to load', () => {
    serviceSpy.getPipeline.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    component.load();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.loading()).toBeFalse();
  });

  // ─── Filters (AC-4 / FR-6) ─────────────────────────────────

  it('applies a stage filter and reloads', () => {
    serviceSpy.getPipeline.calls.reset();
    component.updateFilter({ stage: 'Screening' });
    expect(serviceSpy.getPipeline).toHaveBeenCalledWith('vac-1', {
      stage: 'Screening',
    });
    expect(component.hasActiveFilters()).toBeTrue();
  });

  it('debounces free-text search', fakeAsync(() => {
    serviceSpy.getPipeline.calls.reset();
    component.onSearchChange('ada');
    expect(serviceSpy.getPipeline).not.toHaveBeenCalled();
    tick(300);
    expect(serviceSpy.getPipeline).toHaveBeenCalledWith('vac-1', {
      search: 'ada',
    });
  }));

  it('clears filters and reloads', () => {
    component.updateFilter({ stage: 'Offer' });
    serviceSpy.getPipeline.calls.reset();
    component.clearFilters();
    expect(component.filters()).toEqual({});
    expect(serviceSpy.getPipeline).toHaveBeenCalledWith('vac-1', {});
  });

  // ─── Drag-and-drop (AC-2 / FR-3) ───────────────────────────

  it('persists a forward move immediately and optimistically updates the board', () => {
    const cols = component.board();
    const applied = cols.find((c) => c.stage === 'Applied')!;
    const screening = cols.find((c) => c.stage === 'Screening')!;
    const moving = applied.applicants[0];

    component.onDrop(dropEvent(applied, screening, moving));

    // Optimistic: card removed from Applied, added to Screening.
    expect(component.board().find((c) => c.stage === 'Applied')!.applicants.length).toBe(0);
    expect(component.board().find((c) => c.stage === 'Screening')!.applicants.length).toBe(1);
    expect(serviceSpy.changeStage).toHaveBeenCalledWith('app-1', {
      toStage: 'Screening',
      reason: undefined,
      rejectionReason: undefined,
      notes: undefined,
    });
    expect(component.pendingMove()).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('surfaces soft-gate warnings as toasts after a successful move (BR-4/FR-1)', () => {
    serviceSpy.changeStage.and.returnValue(
      of(moveResult('Screening', ['Headcount already filled'])),
    );
    const cols = component.board();
    const applied = cols.find((c) => c.stage === 'Applied')!;
    const screening = cols.find((c) => c.stage === 'Screening')!;
    const moving = applied.applicants[0];

    component.onDrop(dropEvent(applied, screening, moving));

    expect(toastrSpy.success).toHaveBeenCalled();
    expect(toastrSpy.warning).toHaveBeenCalledWith(
      'Headcount already filled',
      'Heads up',
    );
  });

  it('rolls back the board when the server rejects a move', () => {
    serviceSpy.changeStage.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    const cols = component.board();
    const applied = cols.find((c) => c.stage === 'Applied')!;
    const screening = cols.find((c) => c.stage === 'Screening')!;
    const moving = applied.applicants[0];

    component.onDrop(dropEvent(applied, screening, moving));

    // Rolled back: card is back in Applied.
    expect(component.board().find((c) => c.stage === 'Applied')!.applicants.length).toBe(1);
    expect(component.board().find((c) => c.stage === 'Screening')!.applicants.length).toBe(0);
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('defers a move to Rejected until a reason is supplied (BR-3)', () => {
    const cols = component.board();
    const applied = cols.find((c) => c.stage === 'Applied')!;
    const rejected = cols.find((c) => c.stage === 'Rejected')!;
    const moving = applied.applicants[0];

    component.onDrop(dropEvent(applied, rejected, moving));

    // Optimistic move done, but no persist yet — dialog pending.
    expect(component.pendingMove()).not.toBeNull();
    expect(component.pendingMove()!.to).toBe('Rejected');
    expect(serviceSpy.changeStage).not.toHaveBeenCalled();
  });

  it('persists the deferred move with the structured rejection reason on confirm (AC-4)', () => {
    serviceSpy.changeStage.and.returnValue(of(moveResult('Rejected')));
    const cols = component.board();
    const applied = cols.find((c) => c.stage === 'Applied')!;
    const rejected = cols.find((c) => c.stage === 'Rejected')!;
    const moving = applied.applicants[0];

    component.onDrop(dropEvent(applied, rejected, moving));
    component.confirmMove({
      reason: 'Not qualified',
      rejectionReason: 'NotQualified',
      notes: 'n/a',
    });

    expect(serviceSpy.changeStage).toHaveBeenCalledWith('app-1', {
      toStage: 'Rejected',
      reason: 'Not qualified',
      rejectionReason: 'NotQualified',
      notes: 'n/a',
    });
    expect(component.pendingMove()).toBeNull();
  });

  it('rolls back the optimistic move when the reason dialog is cancelled', () => {
    const cols = component.board();
    const applied = cols.find((c) => c.stage === 'Applied')!;
    const rejected = cols.find((c) => c.stage === 'Rejected')!;
    const moving = applied.applicants[0];

    component.onDrop(dropEvent(applied, rejected, moving));
    expect(component.board().find((c) => c.stage === 'Rejected')!.applicants.length).toBe(1);

    component.cancelMove();

    expect(component.pendingMove()).toBeNull();
    expect(component.board().find((c) => c.stage === 'Applied')!.applicants.length).toBe(1);
    expect(component.board().find((c) => c.stage === 'Rejected')!.applicants.length).toBe(0);
    expect(serviceSpy.changeStage).not.toHaveBeenCalled();
  });

  // ─── Detail (AC-3) ─────────────────────────────────────────

  it('opens and closes the detail slide-over', () => {
    component.openDetail(card());
    expect(component.selectedId()).toBe('app-1');
    component.closeDetail();
    expect(component.selectedId()).toBeNull();
  });

  // ─── Table view (FR-4) ─────────────────────────────────────

  it('toggles to the table view and sorts rows', () => {
    serviceSpy.getPipeline.and.returnValue(
      of({
        vacancyId: 'vac-1',
        vacancyTitle: 'V',
        stages: [
          {
            stage: 'Applied',
            count: 2,
            applicants: [
              card({ id: 'b', lastName: 'Zeta' }),
              card({ id: 'a', lastName: 'Alpha' }),
            ],
          },
        ],
        total: 2,
      }),
    );
    component.load();
    component.viewMode.set('table');

    component.sortBy('name');
    expect(component.sortKey()).toBe('name');
    expect(component.sortDir()).toBe('asc');
    // Alpha sorts before Zeta ascending.
    expect(component.tableRows()[0].id).toBe('a');

    component.sortBy('name'); // toggle direction
    expect(component.sortDir()).toBe('desc');
    expect(component.tableRows()[0].id).toBe('b');
  });

  it('reorders within a column without calling the server', () => {
    serviceSpy.getPipeline.and.returnValue(
      of({
        vacancyId: 'vac-1',
        vacancyTitle: 'V',
        stages: [
          {
            stage: 'Applied',
            count: 2,
            applicants: [card({ id: 'a' }), card({ id: 'b' })],
          },
          { stage: 'Screening', count: 0, applicants: [] },
          { stage: 'Interview', count: 0, applicants: [] },
          { stage: 'Offer', count: 0, applicants: [] },
          { stage: 'Hired', count: 0, applicants: [] },
          { stage: 'Rejected', count: 0, applicants: [] },
        ],
        total: 2,
      }),
    );
    component.load();
    serviceSpy.changeStage.calls.reset();

    const applied = component.board().find((c) => c.stage === 'Applied')!;
    // Same-column reorder: CDK passes the SAME drop-list instance for both refs.
    const sameContainer = { data: applied };
    const event = {
      previousContainer: sameContainer,
      container: sameContainer,
      previousIndex: 0,
      currentIndex: 1,
      item: { data: applied.applicants[0] },
    } as unknown as CdkDragDrop<IPipelineStage>;

    component.onDrop(event);

    expect(serviceSpy.changeStage).not.toHaveBeenCalled();
    const after = component.board().find((c) => c.stage === 'Applied')!;
    expect(after.applicants.map((a) => a.id)).toEqual(['b', 'a']);
  });
});
