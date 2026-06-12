import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { EmployeeListComponent } from './employee-list.component';
import { environment } from '../../../../../../environments/environment';

describe('EmployeeListComponent', () => {
  let component: EmployeeListComponent;
  let fixture: ComponentFixture<EmployeeListComponent>;
  let httpMock: HttpTestingController;
  let router: Router;

  const empUrl = `${environment.apiBaseUrl}/employees`;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EmployeeListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'employees', children: [] },
          { path: 'employees/new', children: [] },
        ]),
        provideAnimationsAsync(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate');

    fixture = TestBed.createComponent(EmployeeListComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create the component', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(empUrl);
    req.flush([]);
    expect(component).toBeTruthy();
  });

  it('should load employees on init', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(empUrl);
    expect(req.request.method).toBe('GET');
    req.flush([
      {
        employeeId: 'emp-1',
        firstName: 'John',
        lastName: 'Doe',
        employeeNo: 'EMP-0001',
        email: 'john@test.com',
        status: 'active',
      },
    ]);
    fixture.detectChanges();

    expect(component.employees().length).toBe(1);
    expect(component.isLoading()).toBeFalse();
  });

  it('should navigate to new employee wizard when addEmployee is called', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(empUrl);
    req.flush([]);

    component.addEmployee();
    expect(router.navigate).toHaveBeenCalledWith(['/employees/new']);
  });

  it('should display correct initials', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(empUrl);
    req.flush([]);

    const result = component.getInitials({
      firstName: 'John',
      lastName: 'Doe',
    } as any);
    expect(result).toBe('JD');
  });

  it('should handle empty employee list', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(empUrl);
    req.flush([]);
    fixture.detectChanges();

    expect(component.employees().length).toBe(0);
    expect(component.isLoading()).toBeFalse();
  });
});
