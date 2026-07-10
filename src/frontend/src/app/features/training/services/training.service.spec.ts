import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TrainingService } from './training.service';
import {
  ICourse,
  ICreateCourse,
  IUpdateCourse,
  IEnrollment,
} from '../models/training.models';
import { environment } from '../../../../environments/environment';

describe('TrainingService', () => {
  let service: TrainingService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/training`;

  const mockCourse: ICourse = {
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
  };

  const mockEnrollment: IEnrollment = {
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

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        TrainingService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(TrainingService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getCourses', () => {
    it('should return all courses for the tenant', () => {
      service.getCourses().subscribe((courses) => {
        expect(courses.length).toBe(1);
        expect(courses[0].title).toBe('Workplace Safety');
      });

      const req = httpMock.expectOne(`${baseUrl}/courses`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockCourse]);
    });

    it('should return an empty array when no courses exist', () => {
      service.getCourses().subscribe((courses) => {
        expect(courses.length).toBe(0);
      });

      const req = httpMock.expectOne(`${baseUrl}/courses`);
      req.flush([]);
    });
  });

  describe('getCourse', () => {
    it('should return a single course by ID', () => {
      service.getCourse('c-1').subscribe((course) => {
        expect(course.id).toBe('c-1');
        expect(course.status).toBe('Open');
      });

      const req = httpMock.expectOne(`${baseUrl}/courses/c-1`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockCourse);
    });
  });

  describe('createCourse', () => {
    it('should create a new course', () => {
      const request: ICreateCourse = {
        title: 'First Aid',
        description: 'Basic first aid',
        mode: 'Online',
        capacity: 30,
        location: null,
        instructor: null,
        startDate: null,
        endDate: null,
        durationHours: 4,
      };

      service.createCourse(request).subscribe((course) => {
        expect(course.title).toBe('First Aid');
      });

      const req = httpMock.expectOne(`${baseUrl}/courses`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockCourse, title: 'First Aid', status: 'Draft' });
    });
  });

  describe('updateCourse', () => {
    it('should update an existing course', () => {
      const request: IUpdateCourse = {
        title: 'Workplace Safety II',
        mode: 'Hybrid',
        capacity: 25,
      };

      service.updateCourse('c-1', request).subscribe((course) => {
        expect(course.title).toBe('Workplace Safety II');
      });

      const req = httpMock.expectOne(`${baseUrl}/courses/c-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockCourse, title: 'Workplace Safety II' });
    });
  });

  describe('changeCourseStatus', () => {
    it('should post the target status', () => {
      service.changeCourseStatus('c-1', { status: 'Open' }).subscribe((course) => {
        expect(course.status).toBe('Open');
      });

      const req = httpMock.expectOne(`${baseUrl}/courses/c-1/status`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ status: 'Open' });
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockCourse);
    });
  });

  describe('enroll', () => {
    it('should self-enroll with a null employeeId', () => {
      service.enroll('c-1', { employeeId: null }).subscribe((enrollment) => {
        expect(enrollment.status).toBe('Enrolled');
      });

      const req = httpMock.expectOne(`${baseUrl}/courses/c-1/enrollments`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ employeeId: null });
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockEnrollment);
    });

    it('should enroll another employee when employeeId is supplied', () => {
      service.enroll('c-1', { employeeId: 'emp-9' }).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/courses/c-1/enrollments`);
      expect(req.request.body).toEqual({ employeeId: 'emp-9' });
      req.flush({ ...mockEnrollment, employeeId: 'emp-9' });
    });
  });

  describe('cancelEnrollment', () => {
    it('should post to the cancel endpoint', () => {
      service.cancelEnrollment('e-1').subscribe((enrollment) => {
        expect(enrollment.id).toBe('e-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/enrollments/e-1/cancel`);
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockEnrollment, status: 'Cancelled' });
    });
  });

  describe('completeEnrollment', () => {
    it('should post cert/score to the complete endpoint', () => {
      const body = { certificateReference: 'CERT-123', score: 95 };

      service.completeEnrollment('e-1', body).subscribe((enrollment) => {
        expect(enrollment.status).toBe('Completed');
      });

      const req = httpMock.expectOne(`${baseUrl}/enrollments/e-1/complete`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({
        ...mockEnrollment,
        status: 'Completed',
        certificateReference: 'CERT-123',
        score: 95,
      });
    });
  });

  describe('getMyEnrollments', () => {
    it('should return the current user enrollments', () => {
      service.getMyEnrollments().subscribe((enrollments) => {
        expect(enrollments.length).toBe(1);
        expect(enrollments[0].courseTitle).toBe('Workplace Safety');
      });

      const req = httpMock.expectOne(`${baseUrl}/me/enrollments`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockEnrollment]);
    });
  });

  describe('getTrainingHistory', () => {
    it('should return an employee training history', () => {
      service.getTrainingHistory('emp-1').subscribe((enrollments) => {
        expect(enrollments.length).toBe(1);
      });

      const req = httpMock.expectOne(
        `${baseUrl}/employees/emp-1/training-history`
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockEnrollment]);
    });
  });
});
