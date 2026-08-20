import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { GoalProgressService } from './goal-progress.service';
import { environment } from '../../../../environments/environment';
import {
  GoalTimelineWire,
  IGoalComment,
  IGoalUpdate,
  IMyGoals,
  ITeamGoalProgressRow,
  MyGoalProgressWire,
  TeamGoalRowWire,
} from '../models/goal-progress.models';

describe('GoalProgressService (US-PRF-009)', () => {
  let service: GoalProgressService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance`;

  // ─── WIRE fixtures (the real Performance* DTO shapes the API sends) ──────────
  const goalWire: MyGoalProgressWire = {
    goalId: 'g-1',
    title: 'Ship the mobile app',
    description: 'Deliver v1 to the store',
    targetValue: 'Store approval',
    measurementUnit: 'release',
    cycleId: 'c-1',
    currentProgressPct: 60,
    currentStatus: 'InProgress',
    currentStatusName: 'InProgress',
    lastUpdatedAt: '2026-06-10T10:00:00Z',
    needsAttention: false,
    weight: 100,
    updateCount: 2,
  };

  // A single timeline object with two updates and one comment keyed by update id.
  const timelineWire: GoalTimelineWire = {
    goalId: 'g-1',
    title: 'Ship the mobile app',
    currentProgressPct: 60,
    currentStatus: 'InProgress',
    currentStatusName: 'InProgress',
    updates: [
      {
        id: 'u-1',
        goalId: 'g-1',
        employeeId: 'e-1',
        progressPct: 40,
        status: 'InProgress',
        statusName: 'InProgress',
        notes: 'kicked off',
        createdAt: '2026-06-01T09:00:00Z',
        attachments: [],
      },
      {
        id: 'u-2',
        goalId: 'g-1',
        employeeId: 'e-1',
        progressPct: 60,
        status: 'InProgress',
        statusName: 'InProgress',
        notes: 'halfway',
        createdAt: '2026-06-10T10:00:00Z',
        attachments: [
          {
            id: 'a-1',
            fileName: 'evidence.pdf',
            contentType: 'application/pdf',
            sizeBytes: 1024,
          },
        ],
      },
    ],
    comments: [
      {
        id: 'c-1',
        goalId: 'g-1',
        progressUpdateId: 'u-2',
        authorName: 'Sam Lead',
        body: 'great progress',
        createdAt: '2026-06-11T09:00:00Z',
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        GoalProgressService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(GoalProgressService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getMyGoals() maps the FLAT wire goal list into the IMyGoals envelope', () => {
    // MUTATION ARM: fails against the un-migrated code, which cast the raw ARRAY to
    // IMyGoals so `.goals` was undefined and the screen threw on `d.goals.length`.
    let result: IMyGoals | undefined;
    service.getMyGoals().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/my-goals`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([goalWire]);

    expect(result?.cycleId).toBe('c-1');
    expect(result?.windowOpen).toBeTrue();
    expect(result?.goals.length).toBe(1);
    expect(result?.goals[0].goalId).toBe('g-1');
    expect(result?.goals[0].progressPercent).toBe(60); // ← currentProgressPct
    expect(result?.goals[0].status).toBe('InProgress'); // ← currentStatusName
    expect(result?.goals[0].target).toBe('Store approval'); // ← targetValue
    // overall completion is FE-derived from the (single, weight-100) goal.
    expect(result?.overallCompletionPercent).toBe(60);
  });

  it('getMyGoals() unwraps a { data } page', () => {
    let result: IMyGoals | undefined;
    service.getMyGoals().subscribe((r) => (result = r));

    httpMock.expectOne(`${baseUrl}/my-goals`).flush({ data: [] });

    expect(result?.goals).toEqual([]);
  });

  it('getGoalUpdates() unpacks the single timeline into a newest-first update list', () => {
    // MUTATION ARM: fails against the un-migrated code, which treated the response as
    // an array (or { data }) — a single timeline OBJECT collapsed to [] there.
    let result: IGoalUpdate[] | undefined;
    service.getGoalUpdates('g-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/goals/g-1/timeline`);
    expect(req.request.method).toBe('GET');
    req.flush(timelineWire);

    expect(result?.length).toBe(2);
    // newest first
    expect(result?.[0].updateId).toBe('u-2');
    expect(result?.[0].progressPercent).toBe(60); // ← progressPct
    // previous-% is FE-derived from the prior chronological update (40 → 60)
    expect(result?.[0].previousProgressPercent).toBe(40);
    // timeline-level comments are regrouped onto their update by progressUpdateId
    expect(result?.[0].comments.length).toBe(1);
    expect(result?.[0].comments[0].comment).toBe('great progress'); // ← body
    expect(result?.[0].attachments[0].fileName).toBe('evidence.pdf');
    // oldest update has no predecessor
    expect(result?.[1].updateId).toBe('u-1');
    expect(result?.[1].previousProgressPercent).toBeNull();
  });

  it('addGoalUpdate() POSTs JSON when no files are attached and returns the appended update', () => {
    const body = { progressPercent: 60, status: 'InProgress' as const, notes: 'n' };
    let result: IGoalUpdate | undefined;
    service.addGoalUpdate('g-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/goals/g-1/progress`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    expect(req.request.body instanceof FormData).toBeFalse();
    req.flush(timelineWire);

    expect(result?.updateId).toBe('u-2'); // newest update from the returned timeline
    expect(result?.progressPercent).toBe(60);
  });

  it('addGoalUpdate() POSTs multipart with repeated files field when attached', () => {
    const body = { progressPercent: 100, status: 'Completed' as const, notes: 'done' };
    const f1 = new File(['a'], 'a.pdf', { type: 'application/pdf' });
    const f2 = new File(['b'], 'b.png', { type: 'image/png' });
    service.addGoalUpdate('g-1', body, [f1, f2]).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/goals/g-1/progress`);
    expect(req.request.method).toBe('POST');
    const form = req.request.body as FormData;
    expect(form instanceof FormData).toBeTrue();
    expect(form.get('progressPercent')).toBe('100');
    expect(form.get('status')).toBe('Completed');
    expect(form.get('notes')).toBe('done');
    expect(form.getAll('files').length).toBe(2);
    req.flush(timelineWire);
  });

  it('addComment() POSTs a comment and maps the appended comment from the timeline', () => {
    let result: IGoalComment | undefined;
    service.addComment('g-1', 'u-2', 'great progress').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/goals/g-1/comments`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      progressUpdateId: 'u-2',
      body: 'great progress',
    });
    req.flush(timelineWire);

    expect(result?.commentId).toBe('c-1'); // ← id
    expect(result?.comment).toBe('great progress'); // ← body
    expect(result?.authorName).toBe('Sam Lead');
  });

  it('getTeamProgress() GETs the team table and maps the renamed row fields', () => {
    const rowsWire: TeamGoalRowWire[] = [
      {
        employeeId: 'e-1',
        employeeName: 'Alex Doe',
        overallCompletionPct: 55,
        goalCount: 3,
        atRiskGoalCount: 1,
        needsAttentionGoalCount: 0,
        lastUpdatedAt: '2026-06-10T10:00:00Z',
      },
    ];
    let result: ITeamGoalProgressRow[] | undefined;
    service.getTeamProgress().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/team-goals`);
    expect(req.request.method).toBe('GET');
    req.flush(rowsWire);

    expect(result?.length).toBe(1);
    expect(result?.[0].overallCompletionPercent).toBe(55); // ← overallCompletionPct
    expect(result?.[0].goalsAtRisk).toBe(1); // ← atRiskGoalCount
    // jobTitle has no wire source — defaulted + reported.
    expect(result?.[0].jobTitle).toBeNull();
  });

  it('getTeamProgress() unwraps a { data } page', () => {
    let result: ITeamGoalProgressRow[] | undefined;
    service.getTeamProgress().subscribe((r) => (result = r));

    httpMock.expectOne(`${baseUrl}/team-goals`).flush({ data: [] });

    expect(result).toEqual([]);
  });

  it('getEmployeeProgress() maps the FLAT wire list and threads the employee id', () => {
    let result: import('../models/goal-progress.models').IEmployeeGoalProgress | undefined;
    service.getEmployeeProgress('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/team-goals/employees/e-1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([goalWire]);

    expect(result?.employeeId).toBe('e-1'); // threaded from the caller
    expect(result?.goals.length).toBe(1);
    expect(result?.goals[0].progressPercent).toBe(60);
    expect(result?.overallCompletionPercent).toBe(60); // FE-derived
  });
});
