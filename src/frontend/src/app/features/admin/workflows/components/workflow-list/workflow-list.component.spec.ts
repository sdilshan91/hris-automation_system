import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { provideTranslateService } from '@ngx-translate/core';
import { WorkflowListComponent } from './workflow-list.component';
import { IWorkflowSummary } from '../../models/workflow.models';
import { environment } from '../../../../../../environments/environment';

describe('WorkflowListComponent', () => {
  let component: WorkflowListComponent;
  let fixture: ComponentFixture<WorkflowListComponent>;
  let httpMock: HttpTestingController;
  let toastr: jasmine.SpyObj<ToastrService>;
  let router: Router;

  const baseUrl = `${environment.apiBaseUrl}/tenant/workflows`;

  const workflows: IWorkflowSummary[] = [
    {
      id: 'wf-1',
      lineageId: 'lin-1',
      name: 'Standard Leave',
      entityType: 'Leave',
      version: 2,
      status: 'Active',
      isDefault: true,
      stepCount: 3,
      updatedAt: '2026-06-01T00:00:00Z',
    },
    {
      id: 'wf-2',
      lineageId: 'lin-2',
      name: 'Expense Approval',
      entityType: 'Expense',
      version: 1,
      status: 'Archived',
      isDefault: false,
      stepCount: 1,
      updatedAt: '2026-05-20T00:00:00Z',
    },
  ];

  function flushInit(list: IWorkflowSummary[] = workflows): void {
    fixture.detectChanges();
    httpMock.expectOne(baseUrl).flush(list);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);

    await TestBed.configureTestingModule({
      imports: [WorkflowListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        provideTranslateService(),
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WorkflowListComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => httpMock.verify());

  it('loads and groups workflows by entity type (AC-1)', () => {
    flushInit();

    const groups = component.groups();
    expect(groups.length).toBe(2);
    expect(groups[0].entityType).toBe('Leave');
    expect(groups[1].entityType).toBe('Expense');

    const groupEls = fixture.nativeElement.querySelectorAll(
      '[data-testid="entity-group"]'
    );
    expect(groupEls.length).toBe(2);
  });

  it('renders status badges and a default badge (AC-1)', () => {
    flushInit();
    const el: HTMLElement = fixture.nativeElement;
    const statusBadges = el.querySelectorAll('[data-testid="status-badge"]');
    expect(statusBadges.length).toBe(2);
    const defaultBadges = el.querySelectorAll('[data-testid="default-badge"]');
    expect(defaultBadges.length).toBe(1);
  });

  it('applies the correct status badge class', () => {
    flushInit([]);
    expect(component.statusBadgeClass('Active')).toContain('green');
    expect(component.statusBadgeClass('Draft')).toContain('amber');
    expect(component.statusBadgeClass('Archived')).toContain('gray');
  });

  it('navigates to the editor on create (AC-4 surfaced there)', () => {
    flushInit();
    const navSpy = spyOn(router, 'navigate');
    component.createWorkflow();
    expect(navSpy).toHaveBeenCalledWith(['/admin/workflows/new']);
  });

  it('shows an error state when the list fails to load', () => {
    fixture.detectChanges();
    httpMock
      .expectOne(baseUrl)
      .flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).toBeTrue();
    const errorEl = fixture.nativeElement.querySelector(
      '[data-testid="list-error"]'
    );
    expect(errorEl).toBeTruthy();
  });

  it('archive: confirm → POSTs the archive endpoint', () => {
    flushInit();
    component.requestArchive(workflows[0]);
    expect(component.pendingAction()?.kind).toBe('archive');

    component.confirmAction();
    const req = httpMock.expectOne(`${baseUrl}/wf-1/archive`);
    expect(req.request.method).toBe('POST');
    req.flush(null);

    // reload after success
    httpMock.expectOne(baseUrl).flush(workflows);
    expect(toastr.success).toHaveBeenCalled();
    expect(component.pendingAction()).toBeNull();
  });

  it('restore: confirm → POSTs the restore endpoint', () => {
    flushInit();
    component.requestRestore(workflows[1]);
    component.confirmAction();

    const req = httpMock.expectOne(`${baseUrl}/wf-2/restore`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
    httpMock.expectOne(baseUrl).flush(workflows);

    expect(toastr.success).toHaveBeenCalled();
  });

  it('delete: confirm → DELETEs the workflow', () => {
    flushInit();
    component.requestDelete(workflows[0]);
    component.confirmAction();

    const req = httpMock.expectOne(`${baseUrl}/wf-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    httpMock.expectOne(baseUrl).flush(workflows);

    expect(toastr.success).toHaveBeenCalled();
  });

  it('cancelling an action clears the state without a request', () => {
    flushInit();
    component.requestDelete(workflows[0]);
    component.cancelAction();
    expect(component.pendingAction()).toBeNull();
    httpMock.verify(); // no DELETE issued
  });
});
