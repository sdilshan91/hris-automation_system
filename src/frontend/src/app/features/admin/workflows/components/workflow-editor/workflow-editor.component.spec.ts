import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { provideTranslateService } from '@ngx-translate/core';
import { convertToParamMap, ParamMap } from '@angular/router';
import { WorkflowEditorComponent } from './workflow-editor.component';
import { IWorkflowDefinition } from '../../models/workflow.models';
import { environment } from '../../../../../../environments/environment';

const baseUrl = `${environment.apiBaseUrl}/tenant/workflows`;
const rolesUrl = `${environment.apiBaseUrl}/tenant/roles`;
const usersUrl = `${environment.apiBaseUrl}/tenant/users`;

const roles = [{ id: 'r-1', name: 'HR Officer', description: 'x' }];
const users = {
  items: [
    { userTenantId: 'u-1', displayName: 'Jane Doe', email: 'jane@acme.com' },
    { userTenantId: 'u-2', displayName: 'Bob Smith', email: 'bob@acme.com' },
  ],
};

/** Build an ActivatedRoute stub with the given route + query params. */
function routeStub(
  params: Record<string, string>,
  query: Record<string, string> = {}
): Partial<ActivatedRoute> {
  return {
    snapshot: {
      paramMap: convertToParamMap(params) as ParamMap,
      queryParamMap: convertToParamMap(query) as ParamMap,
    } as ActivatedRoute['snapshot'],
  };
}

function setup(
  route: Partial<ActivatedRoute>,
  toastr: jasmine.SpyObj<ToastrService>
): {
  fixture: ComponentFixture<WorkflowEditorComponent>;
  component: WorkflowEditorComponent;
  httpMock: HttpTestingController;
  router: Router;
} {
  TestBed.configureTestingModule({
    imports: [WorkflowEditorComponent],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideAnimationsAsync(),
      provideToastr(),
      provideTranslateService(),
      { provide: ToastrService, useValue: toastr },
      { provide: ActivatedRoute, useValue: route },
    ],
  });
  const fixture = TestBed.createComponent(WorkflowEditorComponent);
  const component = fixture.componentInstance;
  const httpMock = TestBed.inject(HttpTestingController);
  const router = TestBed.inject(Router);
  return { fixture, component, httpMock, router };
}

/** Flush the picker requests that fire in the constructor. */
function flushPickers(httpMock: HttpTestingController): void {
  httpMock.expectOne(rolesUrl).flush(roles);
  httpMock
    .expectOne((r) => r.url === usersUrl && r.method === 'GET')
    .flush(users);
}

describe('WorkflowEditorComponent', () => {
  let toastr: jasmine.SpyObj<ToastrService>;

  beforeEach(() => {
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
  });

  describe('create mode', () => {
    let fixture: ComponentFixture<WorkflowEditorComponent>;
    let component: WorkflowEditorComponent;
    let httpMock: HttpTestingController;

    beforeEach(() => {
      ({ fixture, component, httpMock } = setup(
        routeStub({}, { entityType: 'Leave' }),
        toastr
      ));
      flushPickers(httpMock);
      fixture.detectChanges();
    });

    afterEach(() => httpMock.verify());

    it('starts with one step in create mode and no version warning is an edit', () => {
      expect(component.isEdit()).toBeFalse();
      expect(component.steps().length).toBe(1);
      expect(component.willCreateNewVersion()).toBeFalse();
    });

    it('adds and removes steps', () => {
      component.addStep();
      expect(component.steps().length).toBe(2);
      const uid = component.steps()[0].uid;
      component.removeStep(uid);
      expect(component.steps().length).toBe(1);
    });

    it('reorders steps via drag-drop (moves item in array)', () => {
      component.addStep();
      const firstUid = component.steps()[0].uid;
      component.dropStep({
        previousIndex: 0,
        currentIndex: 1,
      } as never);
      expect(component.steps()[1].uid).toBe(firstUid);
    });

    it('condition builder produces the expected condition object on save', () => {
      const uid = component.steps()[0].uid;
      component.name.set('Leave WF');
      component.toggleCondition(uid, true);
      component.setConditionField(uid, 'days_requested');
      component.setConditionOperator(uid, '>');
      component.setConditionValue(uid, '5');

      const steps = component.buildSteps();
      expect(steps[0].conditionJson).toBe(
        JSON.stringify({
          field: 'days_requested',
          operator: '>',
          value: 5,
        })
      );
    });

    it('parallel toggle reveals the multi-approver section and serializes them', () => {
      const uid = component.steps()[0].uid;
      component.toggleParallel(uid, true);
      component.addParallelApprover(uid);
      component.setParallelApprover(uid, 0, 'u-1');
      component.addParallelApprover(uid);
      component.setParallelApprover(uid, 1, 'u-2');
      fixture.detectChanges();

      expect(
        fixture.nativeElement.querySelector('[data-testid="parallel-section"]')
      ).toBeTruthy();

      const steps = component.buildSteps();
      expect(steps[0].isParallel).toBeTrue();
      expect(steps[0].parallelApproverIdentifiers).toEqual(['u-1', 'u-2']);
    });

    it('delegation toggle reveals the backup selector and serializes it', () => {
      const uid = component.steps()[0].uid;
      component.toggleDelegation(uid, true);
      component.setDelegationBackup(uid, 'u-2');
      fixture.detectChanges();

      expect(
        fixture.nativeElement.querySelector(
          '[data-testid="delegation-section"]'
        )
      ).toBeTruthy();

      const steps = component.buildSteps();
      expect(steps[0].delegationEnabled).toBeTrue();
      expect(steps[0].delegationBackupUserId).toBe('u-2');
    });

    it('save POSTs the create body with the correct steps array', () => {
      const uid = component.steps()[0].uid;
      component.name.set('Standard Leave');
      component.setApproverType(uid, 'Role');
      component.setApproverIdentifier(uid, 'r-1');
      component.setSlaHours(uid, 24);

      component.save();
      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.name).toBe('Standard Leave');
      expect(req.request.body.entityType).toBe('Leave');
      expect(req.request.body.steps.length).toBe(1);
      expect(req.request.body.steps[0].approverType).toBe('Role');
      expect(req.request.body.steps[0].approverIdentifier).toBe('r-1');
      expect(req.request.body.steps[0].stepOrder).toBe(1);
      req.flush({});

      expect(toastr.success).toHaveBeenCalled();
    });

    it('surfaces the plan-limit error message verbatim (AC-4)', () => {
      const limitMessage =
        'You have reached the maximum number of workflows (2) for your plan. Please upgrade or archive an existing workflow.';
      component.name.set('Over');
      component.save();
      httpMock
        .expectOne(baseUrl)
        .flush({ message: limitMessage }, { status: 409, statusText: 'Conflict' });

      expect(toastr.error).toHaveBeenCalledWith(limitMessage);
    });

    it('does not save when name or steps are missing (FR-5)', () => {
      // name empty by default → canSave false
      expect(component.canSave()).toBeFalse();
      component.save();
      httpMock.verify(); // no POST
    });
  });

  describe('edit mode', () => {
    let fixture: ComponentFixture<WorkflowEditorComponent>;
    let component: WorkflowEditorComponent;
    let httpMock: HttpTestingController;

    const definition: IWorkflowDefinition = {
      id: 'wf-1',
      lineageId: 'lin-1',
      name: 'Standard Leave',
      entityType: 'Leave',
      version: 1,
      status: 'Active',
      isDefault: true,
      steps: [
        {
          stepOrder: 1,
          approverType: 'LineManager',
          approverIdentifier: null,
          isParallel: false,
          slaHours: 24,
          conditionJson: null,
          delegationEnabled: false,
        },
        {
          stepOrder: 2,
          approverType: 'Role',
          approverIdentifier: 'r-1',
          isParallel: false,
          slaHours: 48,
          conditionJson: JSON.stringify({
            field: 'days_requested',
            operator: '>',
            value: 5,
          }),
          escalationApproverType: 'Role',
          escalationApproverIdentifier: 'r-1',
          delegationEnabled: false,
        },
      ],
    };

    beforeEach(() => {
      ({ fixture, component, httpMock } = setup(
        routeStub({ id: 'wf-1' }),
        toastr
      ));
      flushPickers(httpMock);
      httpMock.expectOne(`${baseUrl}/wf-1`).flush(definition);
      fixture.detectChanges();
    });

    afterEach(() => httpMock.verify());

    it('loads the definition into editable steps and parses the condition', () => {
      expect(component.isEdit()).toBeTrue();
      expect(component.version()).toBe(1);
      expect(component.steps().length).toBe(2);
      expect(component.steps()[1].conditionEnabled).toBeTrue();
      expect(component.steps()[1].conditionField).toBe('days_requested');
      expect(component.steps()[1].conditionValue).toBe('5');
      expect(component.steps()[1].escalationEnabled).toBeTrue();
    });

    it('warns that editing creates a new version (AC-3)', () => {
      expect(component.willCreateNewVersion()).toBeTrue();
      const warning = fixture.nativeElement.querySelector(
        '[data-testid="new-version-warning"]'
      );
      expect(warning).toBeTruthy();
    });

    it('save PUTs to the workflow id (→ new version, AC-3)', () => {
      component.save();
      const req = httpMock.expectOne(`${baseUrl}/wf-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body.name).toBe('Standard Leave');
      expect(req.request.body.steps.length).toBe(2);
      req.flush({ ...definition, version: 2 });

      expect(toastr.success).toHaveBeenCalled();
    });
  });
});
