import { TestBed, ComponentFixture, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { signal } from '@angular/core';
import { MyTeamComponent } from './my-team.component';
import { AuthService } from '@core/auth/auth.service';
import { IDirectReport } from '../../models/employee.models';

describe('MyTeamComponent', () => {
  let component: MyTeamComponent;
  let fixture: ComponentFixture<MyTeamComponent>;
  let httpMock: HttpTestingController;
  let router: Router;

  const mockReports: IDirectReport[] = [
    {
      employeeId: 'emp-2',
      firstName: 'Jane',
      lastName: 'Smith',
      jobTitleName: 'QA Engineer',
      departmentName: 'Engineering',
      status: 'active',
      profilePhotoUrl: null,
      email: 'jane@company.com',
      employeeNo: 'EMP-0002',
    },
    {
      employeeId: 'emp-3',
      firstName: 'Bob',
      lastName: 'Johnson',
      jobTitleName: 'Designer',
      departmentName: 'Design',
      status: 'probation',
      profilePhotoUrl: null,
      email: 'bob@company.com',
      employeeNo: 'EMP-0003',
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyTeamComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideAnimationsAsync(),
        {
          provide: AuthService,
          useValue: {
            currentUser: signal({ userId: 'u-1', email: 'manager@test.com', displayName: 'Manager', mfaEnabled: false, employeeId: 'emp-1' }),
            permissions: signal(['Employee.View.All', 'Employee.View.Team']),
            roles: signal(['Manager']),
            hasRole: (r: string) => r === 'Manager',
          },
        },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate');

    fixture = TestBed.createComponent(MyTeamComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    httpMock.verify();
  });

  function flushDirectReports(reports: IDirectReport[] = mockReports): void {
    const req = httpMock.expectOne((r) => r.url.includes('/direct-reports'));
    req.flush(reports);
  }

  it('should create the component', () => {
    fixture.detectChanges();
    flushDirectReports();
    expect(component).toBeTruthy();
  });

  it('should load direct reports on init', fakeAsync(() => {
    fixture.detectChanges();
    flushDirectReports();
    tick();

    expect(component.directReports().length).toBe(2);
    expect(component.directReports()[0].firstName).toBe('Jane');
    expect(component.isLoading()).toBeFalse();
  }));

  it('should show empty state when no direct reports', fakeAsync(() => {
    fixture.detectChanges();
    flushDirectReports([]);
    tick();

    expect(component.directReports().length).toBe(0);
    expect(component.isLoading()).toBeFalse();
  }));

  it('should show error state on API failure', fakeAsync(() => {
    fixture.detectChanges();
    const req = httpMock.expectOne((r) => r.url.includes('/direct-reports'));
    req.error(new ProgressEvent('error'), { status: 500 });
    tick();

    expect(component.loadError()).toBeTruthy();
    expect(component.isLoading()).toBeFalse();
  }));

  it('should treat 404 as empty (no direct reports) rather than error', fakeAsync(() => {
    fixture.detectChanges();
    const req = httpMock.expectOne((r) => r.url.includes('/direct-reports'));
    req.flush(null, { status: 404, statusText: 'Not Found' });
    tick();

    expect(component.directReports().length).toBe(0);
    expect(component.loadError()).toBeNull();
  }));

  it('should navigate to employee profile on viewProfile', fakeAsync(() => {
    fixture.detectChanges();
    flushDirectReports();
    tick();

    component.viewProfile('emp-2');
    expect(router.navigate).toHaveBeenCalledWith(['/employees', 'emp-2']);
  }));

  it('should render initials for reports without photo', () => {
    fixture.detectChanges();
    flushDirectReports();

    expect(component.getInitials('Jane', 'Smith')).toBe('JS');
    expect(component.getInitials('Bob', 'Johnson')).toBe('BJ');
  });

  it('should return correct status badge classes', () => {
    fixture.detectChanges();
    flushDirectReports();

    expect(component.getStatusClasses('active')).toContain('green');
    expect(component.getStatusClasses('probation')).toContain('amber');
    expect(component.getStatusClasses('terminated')).toContain('red');
  });

  it('should display correct count of direct reports', fakeAsync(() => {
    fixture.detectChanges();
    flushDirectReports();
    tick();

    expect(component.directReports().length).toBe(2);
  }));
});
