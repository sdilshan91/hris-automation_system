import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { CourseListComponent } from './course-list.component';
import { TrainingService } from '../../services/training.service';
import { ICourse, IEnrollment } from '../../models/training.models';

describe('CourseListComponent', () => {
  let component: CourseListComponent;
  let fixture: ComponentFixture<CourseListComponent>;
  let trainingServiceSpy: jasmine.SpyObj<TrainingService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockCourses: ICourse[] = [
    {
      id: 'c-1',
      title: 'Workplace Safety',
      description: 'Safety fundamentals',
      mode: 'InPerson',
      capacity: 20,
      location: 'Room B',
      instructor: 'Jane Doe',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
      durationHours: 8,
      status: 'Open',
      enrolledCount: 5,
      waitlistCount: 0,
      createdAt: '2026-07-01T00:00:00Z',
      updatedAt: null,
    },
    {
      id: 'c-2',
      title: 'First Aid Basics',
      description: null,
      mode: 'Online',
      capacity: null,
      location: null,
      instructor: 'Tom Ford',
      startDate: null,
      endDate: null,
      durationHours: 4,
      status: 'Draft',
      enrolledCount: 0,
      waitlistCount: 0,
      createdAt: '2026-07-02T00:00:00Z',
      updatedAt: null,
    },
    {
      id: 'c-3',
      title: 'Leadership 101',
      description: null,
      mode: 'Hybrid',
      capacity: 10,
      location: null,
      instructor: null,
      startDate: null,
      endDate: null,
      durationHours: null,
      status: 'Completed',
      enrolledCount: 10,
      waitlistCount: 2,
      createdAt: '2026-06-01T00:00:00Z',
      updatedAt: null,
    },
  ];

  const enrolled: IEnrollment = {
    id: 'e-1',
    courseId: 'c-1',
    courseTitle: 'Workplace Safety',
    employeeId: 'emp-1',
    employeeName: 'John Smith',
    status: 'Enrolled',
    waitlistPosition: null,
    enrolledAt: '2026-07-05T00:00:00Z',
    completedAt: null,
    certificateReference: null,
    score: null,
  };

  const waitlisted: IEnrollment = {
    ...enrolled,
    id: 'e-2',
    status: 'Waitlisted',
    waitlistPosition: 1,
  };

  beforeEach(async () => {
    trainingServiceSpy = jasmine.createSpyObj('TrainingService', [
      'getCourses',
      'changeCourseStatus',
      'enroll',
    ]);
    trainingServiceSpy.getCourses.and.returnValue(of(mockCourses));
    trainingServiceSpy.changeCourseStatus.and.returnValue(of(mockCourses[0]));
    trainingServiceSpy.enroll.and.returnValue(of(enrolled));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [CourseListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: TrainingService, useValue: trainingServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CourseListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load courses on init', () => {
    fixture.detectChanges();
    expect(trainingServiceSpy.getCourses).toHaveBeenCalled();
    expect(component.courses().length).toBe(3);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error state when loading fails', () => {
    trainingServiceSpy.getCourses.and.returnValue(
      throwError(() => ({ status: 500, error: { message: 'Internal server error' } }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Internal server error');
    expect(component.isLoading()).toBeFalse();
  });

  it('should use default error message when backend message is missing', () => {
    trainingServiceSpy.getCourses.and.returnValue(throwError(() => ({ status: 0 })));
    fixture.detectChanges();
    expect(component.loadError()).toBe('Failed to load courses. Please try again.');
  });

  // --- Search / filter ----------------------------------------

  it('should filter courses by title', () => {
    fixture.detectChanges();
    component.searchQuery.set('safety');
    expect(component.filteredCourses().length).toBe(1);
    expect(component.filteredCourses()[0].title).toBe('Workplace Safety');
  });

  it('should filter courses by instructor', () => {
    fixture.detectChanges();
    component.searchQuery.set('tom ford');
    expect(component.filteredCourses().length).toBe(1);
    expect(component.filteredCourses()[0].title).toBe('First Aid Basics');
  });

  it('should return all courses for an empty query', () => {
    fixture.detectChanges();
    component.searchQuery.set('');
    expect(component.filteredCourses().length).toBe(3);
  });

  // --- Status transition rules (AC-2) -------------------------

  it('should expose the legal transitions per status', () => {
    expect(component.allowedTransitions('Draft')).toEqual(['Open', 'Cancelled']);
    expect(component.allowedTransitions('Open')).toEqual([
      'Closed',
      'Cancelled',
      'Completed',
    ]);
    expect(component.allowedTransitions('Closed')).toEqual(['Completed', 'Cancelled']);
    expect(component.allowedTransitions('Cancelled')).toEqual([]);
    expect(component.allowedTransitions('Completed')).toEqual([]);
  });

  // --- Form slide-over ----------------------------------------

  it('should open create form with a null course', () => {
    fixture.detectChanges();
    component.openCreate();
    expect(component.formOpen()).toBeTrue();
    expect(component.editingCourse()).toBeNull();
  });

  it('should open edit form with the selected course', () => {
    fixture.detectChanges();
    component.openEdit(mockCourses[0]);
    expect(component.formOpen()).toBeTrue();
    expect(component.editingCourse()).toBe(mockCourses[0]);
  });

  it('should reload courses when form saved', () => {
    fixture.detectChanges();
    trainingServiceSpy.getCourses.calls.reset();
    component.onFormSaved();
    expect(component.formOpen()).toBeFalse();
    expect(trainingServiceSpy.getCourses).toHaveBeenCalled();
  });

  // --- Status change flow -------------------------------------

  it('should open the status dialog defaulting to the first legal transition', () => {
    fixture.detectChanges();
    component.openStatusChange(mockCourses[1]); // Draft
    expect(component.courseToChangeStatus()).toBe(mockCourses[1]);
    expect(component.targetStatus()).toBe('Open');
  });

  it('should not open the status dialog for a terminal status', () => {
    fixture.detectChanges();
    component.openStatusChange(mockCourses[2]); // Completed
    expect(component.courseToChangeStatus()).toBeNull();
  });

  it('should change status and reload on confirm', () => {
    fixture.detectChanges();
    component.openStatusChange(mockCourses[1]); // Draft → Open
    trainingServiceSpy.getCourses.calls.reset();
    component.confirmStatusChange();

    expect(trainingServiceSpy.changeCourseStatus).toHaveBeenCalledWith('c-2', {
      status: 'Open',
    });
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.courseToChangeStatus()).toBeNull();
    expect(trainingServiceSpy.getCourses).toHaveBeenCalled();
  });

  it('should show an error toast when status change fails', () => {
    fixture.detectChanges();
    trainingServiceSpy.changeCourseStatus.and.returnValue(
      throwError(() => ({
        status: 409,
        error: { message: 'Illegal transition', code: 'invalid_status_transition' },
      }))
    );
    component.openStatusChange(mockCourses[1]);
    component.confirmStatusChange();

    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isChangingStatus()).toBeFalse();
  });

  // --- Enrollment ---------------------------------------------

  it('should enroll (self) on an Open course and show success', () => {
    fixture.detectChanges();
    component.enroll(mockCourses[0]);

    expect(trainingServiceSpy.enroll).toHaveBeenCalledWith('c-1', {
      employeeId: null,
    });
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.enrollingCourseId()).toBeNull();
  });

  it('should show an info toast when the enrollment is waitlisted (AC-5)', () => {
    fixture.detectChanges();
    trainingServiceSpy.enroll.and.returnValue(of(waitlisted));
    component.enroll(mockCourses[0]);

    expect(toastrSpy.info).toHaveBeenCalled();
    expect(toastrSpy.success).not.toHaveBeenCalled();
  });

  it('should warn on a duplicate enrollment (AC-6)', () => {
    fixture.detectChanges();
    trainingServiceSpy.enroll.and.returnValue(
      throwError(() => ({
        status: 409,
        error: { message: 'Already enrolled', code: 'already_enrolled' },
      }))
    );
    component.enroll(mockCourses[0]);

    expect(toastrSpy.warning).toHaveBeenCalled();
    expect(component.enrollingCourseId()).toBeNull();
  });

  it('should show a generic error toast on unexpected enroll failure', () => {
    fixture.detectChanges();
    trainingServiceSpy.enroll.and.returnValue(
      throwError(() => ({ status: 500, error: { message: 'Boom' } }))
    );
    component.enroll(mockCourses[0]);

    expect(toastrSpy.error).toHaveBeenCalled();
  });
});
