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

  beforeEach(() => {
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        {
          provide: ToastrService,
          useValue: jasmine.createSpyObj('ToastrService', [
            'error',
            'warning',
            'success',
          ]),
        },
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
});
