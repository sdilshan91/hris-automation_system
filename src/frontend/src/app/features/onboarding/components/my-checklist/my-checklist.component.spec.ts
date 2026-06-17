import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpEventType } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';

import { MyChecklistComponent } from './my-checklist.component';
import { environment } from '../../../../../environments/environment';
import {
  IMyChecklist,
  ICompleteTaskResponse,
} from '../../models/my-onboarding.models';

describe('MyChecklistComponent', () => {
  let fixture: ComponentFixture<MyChecklistComponent>;
  let component: MyChecklistComponent;
  let httpMock: HttpTestingController;
  let toastr: jasmine.SpyObj<ToastrService>;
  const base = `${environment.apiBaseUrl}/onboarding/checklists`;
  const meUrl = `${base}/me`;

  const checklist = (over: Partial<IMyChecklist> = {}): IMyChecklist => ({
    checklistInstanceId: 'ci-1',
    status: 'in_progress',
    progressPercent: 40,
    totalTasks: 5,
    completedTasks: 2,
    pendingTasks: 3,
    overdueTasks: 0,
    categories: [
      {
        category: 'Documentation',
        tasks: [
          {
            taskInstanceId: 'ti-1',
            title: 'Submit ID proof',
            description: 'Upload your national ID',
            category: 'Documentation',
            responsibleRole: 'Employee',
            dueDate: '2026-07-10',
            status: 'pending',
            isMandatory: true,
            requiresDocument: true,
            completedAt: null,
            completedBy: null,
            comment: null,
            attachmentUrl: null,
          },
          {
            taskInstanceId: 'ti-2',
            title: 'Read employee handbook',
            description: null,
            category: 'Documentation',
            responsibleRole: 'Employee',
            dueDate: '2026-07-12',
            status: 'pending',
            isMandatory: false,
            requiresDocument: false,
            completedAt: null,
            completedBy: null,
            comment: null,
            attachmentUrl: null,
          },
        ],
      },
      {
        category: 'IT Setup',
        tasks: [
          {
            taskInstanceId: 'ti-3',
            title: 'Provision laptop',
            description: null,
            category: 'IT Setup',
            responsibleRole: 'IT',
            dueDate: '2026-07-08',
            status: 'pending',
            isMandatory: true,
            requiresDocument: false,
            completedAt: null,
            completedBy: null,
            comment: null,
            attachmentUrl: null,
          },
        ],
      },
    ],
    ...over,
  });

  function loadWith(data: IMyChecklist): void {
    fixture.detectChanges(); // ngOnInit -> GET /me
    httpMock.expectOne(meUrl).flush(data);
    fixture.detectChanges();
  }

  beforeEach(() => {
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', ['success', 'error']);
    TestBed.configureTestingModule({
      imports: [MyChecklistComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        { provide: ToastrService, useValue: toastr },
      ],
    });
    fixture = TestBed.createComponent(MyChecklistComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads the checklist on init and expands all categories (AC-2)', () => {
    loadWith(checklist());
    expect(component.checklist()?.categories.length).toBe(2);
    expect(component.isExpanded('Documentation')).toBeTrue();
    expect(component.isExpanded('IT Setup')).toBeTrue();
  });

  it('renders category count badges and task titles', () => {
    loadWith(checklist());
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Submit ID proof');
    expect(text).toContain('0/2'); // Documentation: 0 of 2 complete
    expect(text).toContain('40%');
  });

  it('treats a 404 as an empty checklist (no error banner)', () => {
    fixture.detectChanges();
    httpMock.expectOne(meUrl).flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(component.loadError()).toBeNull();
    expect(component.totalTasks()).toBe(0);
  });

  it('surfaces a non-404 load error with retry', () => {
    fixture.detectChanges();
    httpMock.expectOne(meUrl).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(component.loadError()).toBeTruthy();
  });

  it('only allows completing Employee-role pending tasks (BR-1 / FR-7)', () => {
    loadWith(checklist());
    const employeeTask = component.checklist()!.categories[0].tasks[1]; // handbook
    const itTask = component.checklist()!.categories[1].tasks[0]; // laptop (IT)
    expect(component.canComplete(employeeTask)).toBeTrue();
    expect(component.canComplete(itTask)).toBeFalse();
  });

  it('flags an overdue still-pending task and computes days overdue (AC-5)', () => {
    const data = checklist();
    data.categories[0].tasks[1].dueDate = '2000-01-01'; // far past
    loadWith(data);
    const task = component.checklist()!.categories[0].tasks[1];
    expect(component.overdue(task)).toBeTrue();
    expect(component.overdueDays(task)).toBeGreaterThan(0);
    expect(component.chipClass(task)).toBe('chip-overdue');
  });

  it('completes a no-document task and updates progress from the response (AC-3)', () => {
    loadWith(checklist());
    const handbook = component.checklist()!.categories[0].tasks[1];

    component.openComplete(handbook);
    component.completeForm.setValue({ comment: 'Read it' });
    fixture.detectChanges();
    component.submit(handbook);

    const req = httpMock.expectOne(`${base}/tasks/ti-2/complete`);
    expect(req.request.body instanceof FormData).toBeFalse();
    expect(req.request.body).toEqual({ comment: 'Read it' });

    const response: ICompleteTaskResponse = {
      task: { ...handbook, status: 'completed', completedBy: 'Grace', completedAt: '2026-07-01T10:00:00Z' },
      progress: { progressPercent: 60, totalTasks: 5, completedTasks: 3, pendingTasks: 2, overdueTasks: 0 },
    };
    req.flush(response);
    fixture.detectChanges();

    expect(component.checklist()!.progressPercent).toBe(60);
    expect(component.checklist()!.categories[0].tasks[1].status).toBe('completed');
    expect(component.openTaskId()).toBeNull();
    expect(toastr.success).toHaveBeenCalled();
  });

  it('blocks submit for a document-required task with no file and shows an error (AC-4)', () => {
    loadWith(checklist());
    const idTask = component.checklist()!.categories[0].tasks[0]; // requiresDocument
    component.openComplete(idTask);
    component.submit(idTask);

    httpMock.expectNone(`${base}/tasks/ti-1/complete`);
    expect(component.fileError()).toBeTruthy();
    expect(component.submitting()).toBeFalse();
  });

  it('uploads a valid document and reports progress for a document task (AC-4)', () => {
    loadWith(checklist());
    const idTask = component.checklist()!.categories[0].tasks[0];
    component.openComplete(idTask);

    const file = new File(['x'], 'id.pdf', { type: 'application/pdf' });
    Object.defineProperty(file, 'size', { value: 4096 });
    component.onDrop({
      preventDefault: () => {},
      stopPropagation: () => {},
      dataTransfer: { files: [file] },
    } as unknown as DragEvent);
    expect(component.selectedFile()).toBe(file);
    expect(component.fileError()).toBeNull();

    component.submit(idTask);
    const req = httpMock.expectOne(`${base}/tasks/ti-1/complete`);
    expect(req.request.body instanceof FormData).toBeTrue();

    req.event({ type: HttpEventType.UploadProgress, loaded: 2048, total: 4096 });
    expect(component.uploadProgress()).toBe(50);

    req.flush({
      task: { ...idTask, status: 'completed', attachmentUrl: 'http://files/id.pdf' },
      progress: { progressPercent: 60, totalTasks: 5, completedTasks: 3, pendingTasks: 2, overdueTasks: 0 },
    });
    fixture.detectChanges();
    expect(component.checklist()!.categories[0].tasks[0].attachmentUrl).toBe('http://files/id.pdf');
  });

  it('rejects an oversized file on select (NFR-6)', () => {
    loadWith(checklist());
    const idTask = component.checklist()!.categories[0].tasks[0];
    component.openComplete(idTask);

    const big = new File(['x'], 'big.pdf', { type: 'application/pdf' });
    Object.defineProperty(big, 'size', { value: 11 * 1024 * 1024 });
    component.onDrop({
      preventDefault: () => {},
      stopPropagation: () => {},
      dataTransfer: { files: [big] },
    } as unknown as DragEvent);

    expect(component.selectedFile()).toBeNull();
    expect(component.fileError()).toContain('10 MB');
  });

  it('toggles a category accordion open/closed', () => {
    loadWith(checklist());
    expect(component.isExpanded('Documentation')).toBeTrue();
    component.toggleCategory('Documentation');
    expect(component.isExpanded('Documentation')).toBeFalse();
    component.toggleCategory('Documentation');
    expect(component.isExpanded('Documentation')).toBeTrue();
  });
});
