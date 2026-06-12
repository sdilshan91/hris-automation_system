import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { JobTitleListComponent } from './job-title-list.component';
import { JobTitleService } from '../../services/job-title.service';
import { IJobTitle } from '../../models/job-title.models';

describe('JobTitleListComponent', () => {
  let component: JobTitleListComponent;
  let fixture: ComponentFixture<JobTitleListComponent>;
  let jobTitleServiceSpy: jasmine.SpyObj<JobTitleService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockJobTitles: IJobTitle[] = [
    {
      jobTitleId: 'jt-1',
      tenantId: 'tenant-1',
      titleName: 'Software Engineer',
      description: 'Develops software applications',
      gradeId: null,
      gradeName: null,
      isActive: true,
      employeeCount: 10,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
    {
      jobTitleId: 'jt-2',
      tenantId: 'tenant-1',
      titleName: 'Product Manager',
      description: 'Manages product development',
      gradeId: null,
      gradeName: null,
      isActive: true,
      employeeCount: 0,
      createdAt: '2026-01-15T00:00:00Z',
      updatedAt: '2026-01-15T00:00:00Z',
    },
    {
      jobTitleId: 'jt-3',
      tenantId: 'tenant-1',
      titleName: 'Data Analyst',
      description: null,
      gradeId: null,
      gradeName: null,
      isActive: false,
      employeeCount: 0,
      createdAt: '2026-02-01T00:00:00Z',
      updatedAt: '2026-02-01T00:00:00Z',
    },
  ];

  beforeEach(async () => {
    jobTitleServiceSpy = jasmine.createSpyObj('JobTitleService', [
      'getJobTitles',
      'deactivateJobTitle',
    ]);
    jobTitleServiceSpy.getJobTitles.and.returnValue(of(mockJobTitles));
    jobTitleServiceSpy.deactivateJobTitle.and.returnValue(of(undefined));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [JobTitleListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: JobTitleService, useValue: jobTitleServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(JobTitleListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load job titles on init', () => {
    fixture.detectChanges();
    expect(jobTitleServiceSpy.getJobTitles).toHaveBeenCalled();
    expect(component.jobTitles().length).toBe(3);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error state when loading fails', () => {
    jobTitleServiceSpy.getJobTitles.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Internal server error' },
      }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Internal server error');
    expect(component.isLoading()).toBeFalse();
  });

  it('should use default error message when backend message is missing', () => {
    jobTitleServiceSpy.getJobTitles.and.returnValue(
      throwError(() => ({ status: 0 }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe(
      'Failed to load job titles. Please try again.'
    );
  });

  // --- Search / Filter ----------------------------------------

  it('should filter job titles by search query', () => {
    fixture.detectChanges();
    expect(component.filteredJobTitles().length).toBe(3);

    component.searchQuery.set('Software');
    expect(component.filteredJobTitles().length).toBe(1);
    expect(component.filteredJobTitles()[0].titleName).toBe('Software Engineer');
  });

  it('should filter job titles by description', () => {
    fixture.detectChanges();
    component.searchQuery.set('product');
    expect(component.filteredJobTitles().length).toBe(1);
    expect(component.filteredJobTitles()[0].titleName).toBe('Product Manager');
  });

  it('should return all job titles when search query is empty', () => {
    fixture.detectChanges();
    component.searchQuery.set('');
    expect(component.filteredJobTitles().length).toBe(3);
  });

  it('should return no results for non-matching query', () => {
    fixture.detectChanges();
    component.searchQuery.set('nonexistent');
    expect(component.filteredJobTitles().length).toBe(0);
  });

  // --- Form slide-over ----------------------------------------

  it('should open create form with null job title', () => {
    fixture.detectChanges();
    component.openCreate();
    expect(component.formOpen()).toBeTrue();
    expect(component.editingJobTitle()).toBeNull();
  });

  it('should open edit form with the selected job title', () => {
    fixture.detectChanges();
    const jt = mockJobTitles[0];
    component.openEdit(jt);
    expect(component.formOpen()).toBeTrue();
    expect(component.editingJobTitle()).toBe(jt);
  });

  it('should close form and clear editing state', () => {
    fixture.detectChanges();
    component.openEdit(mockJobTitles[0]);
    component.closeForm();
    expect(component.formOpen()).toBeFalse();
    expect(component.editingJobTitle()).toBeNull();
  });

  it('should reload job titles when form saved', () => {
    fixture.detectChanges();
    jobTitleServiceSpy.getJobTitles.calls.reset();

    component.onFormSaved();
    expect(component.formOpen()).toBeFalse();
    expect(jobTitleServiceSpy.getJobTitles).toHaveBeenCalled();
  });

  // --- Deactivation -------------------------------------------

  it('should open deactivation dialog', () => {
    fixture.detectChanges();
    const jt = mockJobTitles[1]; // Product Manager
    component.confirmDeactivate(jt);
    expect(component.jobTitleToDeactivate()).toBe(jt);
  });

  it('should cancel deactivation', () => {
    fixture.detectChanges();
    component.confirmDeactivate(mockJobTitles[1]);
    component.cancelDeactivate();
    expect(component.jobTitleToDeactivate()).toBeNull();
  });

  it('should deactivate a job title', () => {
    fixture.detectChanges();
    const jt = mockJobTitles[1]; // Product Manager, 0 employees
    component.confirmDeactivate(jt);
    component.deactivateJobTitle();

    expect(jobTitleServiceSpy.deactivateJobTitle).toHaveBeenCalledWith(
      jt.jobTitleId
    );
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.jobTitleToDeactivate()).toBeNull();
  });

  it('should handle deactivation error with has_active_employees code (AC-5)', () => {
    fixture.detectChanges();
    jobTitleServiceSpy.deactivateJobTitle.and.returnValue(
      throwError(() => ({
        status: 422,
        error: {
          message: 'This job title is assigned to 5 active employees. Reassign them before deactivating.',
          code: 'has_active_employees',
          employeeCount: 5,
        },
      }))
    );

    const jt = mockJobTitles[1];
    component.confirmDeactivate(jt);
    component.deactivateJobTitle();

    expect(toastrSpy.warning).toHaveBeenCalled();
    expect(component.isDeactivating()).toBeFalse();
  });

  it('should show generic error toast on unexpected deactivation failure', () => {
    fixture.detectChanges();
    jobTitleServiceSpy.deactivateJobTitle.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Unexpected error' },
      }))
    );

    const jt = mockJobTitles[1];
    component.confirmDeactivate(jt);
    component.deactivateJobTitle();

    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('should do nothing if no job title is selected for deactivation', () => {
    fixture.detectChanges();
    component.deactivateJobTitle();
    expect(jobTitleServiceSpy.deactivateJobTitle).not.toHaveBeenCalled();
  });
});
