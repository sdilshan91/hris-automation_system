import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { GoalProgressService } from './goal-progress.service';
import { environment } from '../../../../environments/environment';
import {
  IGoalUpdate,
  IMyGoals,
  ITeamGoalProgressRow,
} from '../models/goal-progress.models';

describe('GoalProgressService (US-PRF-009)', () => {
  let service: GoalProgressService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/goal-progress`;

  const mockMyGoals: IMyGoals = {
    cycleId: 'c-1',
    cycleName: '2026 Annual',
    windowOpen: true,
    overallCompletionPercent: 55,
    goals: [],
  };

  const mockUpdate: IGoalUpdate = {
    updateId: 'u-1',
    progressPercent: 60,
    previousProgressPercent: 40,
    status: 'InProgress',
    notes: 'progress note',
    authorName: 'Alex Doe',
    createdOn: '2026-06-10T10:00:00Z',
    attachments: [],
    comments: [],
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

  it('getMyGoals() GETs my-goals with credentials', () => {
    let result: IMyGoals | undefined;
    service.getMyGoals().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/my-goals`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockMyGoals);

    expect(result).toEqual(mockMyGoals);
  });

  it('getGoalUpdates() GETs the timeline (bare array)', () => {
    let result: IGoalUpdate[] | undefined;
    service.getGoalUpdates('g-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/goals/g-1/updates`);
    expect(req.request.method).toBe('GET');
    req.flush([mockUpdate]);

    expect(result).toEqual([mockUpdate]);
  });

  it('getGoalUpdates() unwraps a { data } page', () => {
    let result: IGoalUpdate[] | undefined;
    service.getGoalUpdates('g-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/goals/g-1/updates`);
    req.flush({ data: [] });

    expect(result).toEqual([]);
  });

  it('addGoalUpdate() POSTs JSON when no files are attached', () => {
    const body = { progressPercent: 60, status: 'InProgress' as const, notes: 'n' };
    service.addGoalUpdate('g-1', body).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/goals/g-1/updates`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    expect(req.request.body instanceof FormData).toBeFalse();
    req.flush(mockUpdate);
  });

  it('addGoalUpdate() POSTs multipart with repeated files field when attached', () => {
    const body = { progressPercent: 100, status: 'Completed' as const, notes: 'done' };
    const f1 = new File(['a'], 'a.pdf', { type: 'application/pdf' });
    const f2 = new File(['b'], 'b.png', { type: 'image/png' });
    service.addGoalUpdate('g-1', body, [f1, f2]).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/goals/g-1/updates`);
    expect(req.request.method).toBe('POST');
    const form = req.request.body as FormData;
    expect(form instanceof FormData).toBeTrue();
    expect(form.get('progressPercent')).toBe('100');
    expect(form.get('status')).toBe('Completed');
    expect(form.get('notes')).toBe('done');
    expect(form.getAll('files').length).toBe(2);
    req.flush(mockUpdate);
  });

  it('addComment() POSTs a comment to the update', () => {
    service.addComment('u-1', 'nice work').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/updates/u-1/comments`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ comment: 'nice work' });
    req.flush({
      commentId: 'c-1',
      authorName: 'Sam Lead',
      comment: 'nice work',
      createdOn: '2026-06-11T09:00:00Z',
    });
  });

  it('getTeamProgress() GETs the team table (bare array)', () => {
    const rows: ITeamGoalProgressRow[] = [
      {
        employeeId: 'e-1',
        employeeName: 'Alex Doe',
        jobTitle: 'Engineer',
        overallCompletionPercent: 55,
        goalCount: 3,
        goalsAtRisk: 1,
        lastUpdatedOn: '2026-06-10',
      },
    ];
    let result: ITeamGoalProgressRow[] | undefined;
    service.getTeamProgress().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/team`);
    expect(req.request.method).toBe('GET');
    req.flush(rows);

    expect(result).toEqual(rows);
  });

  it('getTeamProgress() unwraps a { data } page', () => {
    let result: ITeamGoalProgressRow[] | undefined;
    service.getTeamProgress().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/team`);
    req.flush({ data: [] });

    expect(result).toEqual([]);
  });

  it('getEmployeeProgress() GETs one report’s goals', () => {
    service.getEmployeeProgress('e-1').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/team/e-1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush({
      employeeId: 'e-1',
      employeeName: 'Alex Doe',
      overallCompletionPercent: 55,
      goals: [],
    });
  });
});
