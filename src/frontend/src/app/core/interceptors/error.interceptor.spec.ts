import { TestBed } from '@angular/core/testing';
import {
  HttpClient,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let router: jasmine.SpyObj<Router>;
  let toastr: jasmine.SpyObj<ToastrService>;

  beforeEach(() => {
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    toastr = jasmine.createSpyObj('ToastrService', ['error', 'warning', 'success']);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: ToastrService, useValue: toastr },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('routes blocked tenant users to the suspension notice on HTTP 451 (US-ADM-004 AC-2)', () => {
    let errored = false;
    http.get('/api/v1/employees').subscribe({
      next: () => fail('should not succeed'),
      error: () => (errored = true),
    });

    httpMock
      .expectOne('/api/v1/employees')
      .flush(
        { message: 'Workspace suspended' },
        { status: 451, statusText: 'Unavailable For Legal Reasons' },
      );

    expect(router.navigate).toHaveBeenCalledWith(['/tenant-suspended']);
    // The error still propagates to the caller.
    expect(errored).toBeTrue();
  });

  it('does not route to the suspension notice for non-451 errors', () => {
    http.get('/api/v1/employees').subscribe({
      next: () => fail('should not succeed'),
      error: () => undefined,
    });

    httpMock
      .expectOne('/api/v1/employees')
      .flush({ message: 'nope' }, { status: 404, statusText: 'Not Found' });

    expect(router.navigate).not.toHaveBeenCalledWith(['/tenant-suspended']);
  });

  it('shows a "not in your plan" message and does NOT redirect on 403 code=module_not_entitled (US-ADM-012 AC-2)', () => {
    http.get('/api/v1/payroll').subscribe({
      next: () => fail('should not succeed'),
      error: () => undefined,
    });

    httpMock.expectOne('/api/v1/payroll').flush(
      {
        success: false,
        message: 'The Payroll module is not included in your plan.',
        code: 'module_not_entitled',
        errors: [],
        timestamp: '2026-07-30T00:00:00Z',
      },
      { status: 403, statusText: 'Forbidden' }
    );

    // Discriminates from the generic-403 path: the plan-specific toast fires with the
    // backend message, and there is NO /forbidden redirect (which reads as "you lack
    // permission", the wrong signal for a plan gate).
    expect(toastr.warning).toHaveBeenCalledWith(
      'The Payroll module is not included in your plan.',
      'Not in your plan'
    );
    expect(router.navigate).not.toHaveBeenCalledWith(['/forbidden']);
  });

  it('keeps the generic /forbidden behaviour for a 403 without the module_not_entitled code (US-ADM-012 AC-2)', () => {
    http.get('/api/v1/employees').subscribe({
      next: () => fail('should not succeed'),
      error: () => undefined,
    });

    httpMock
      .expectOne('/api/v1/employees')
      .flush(
        { message: "You don't have permission to perform this action." },
        { status: 403, statusText: 'Forbidden' }
      );

    // Proves the new branch is inert for ordinary authorization 403s: still redirects
    // to /forbidden and does NOT emit the plan-specific "Not in your plan" toast.
    expect(router.navigate).toHaveBeenCalledWith(['/forbidden']);
    expect(toastr.warning).not.toHaveBeenCalledWith(
      jasmine.any(String),
      'Not in your plan'
    );
  });
});
