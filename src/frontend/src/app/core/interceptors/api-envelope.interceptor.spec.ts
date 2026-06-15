import { TestBed } from '@angular/core/testing';
import {
  HttpClient,
  HttpResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { apiEnvelopeInterceptor } from './api-envelope.interceptor';

/**
 * US-PLT-001 — the interceptor unwraps the `{ success, data }` envelope for
 * successful JSON responses and passes everything else through untouched.
 * These specs register the interceptor in the TestBed (the only place we do so;
 * service specs deliberately do NOT, since they test behaviour downstream of it).
 */
describe('apiEnvelopeInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiEnvelopeInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('unwraps an enveloped 2xx object to its data payload', () => {
    let body: unknown;
    http.get('/api/v1/x').subscribe((b) => (body = b));
    httpMock
      .expectOne('/api/v1/x')
      .flush({ success: true, data: { id: 1, name: 'A' }, message: null });
    expect(body).toEqual({ id: 1, name: 'A' });
  });

  it('unwraps an enveloped array payload', () => {
    let body: unknown;
    http.get('/api/v1/x').subscribe((b) => (body = b));
    httpMock.expectOne('/api/v1/x').flush({ success: true, data: [1, 2, 3] });
    expect(body).toEqual([1, 2, 3]);
  });

  it('unwraps a null data payload', () => {
    let body: unknown = 'sentinel';
    http.get('/api/v1/x').subscribe((b) => (body = b));
    httpMock.expectOne('/api/v1/x').flush({ success: true, data: null });
    expect(body).toBeNull();
  });

  it('passes through a bare array (no envelope)', () => {
    let body: unknown;
    http.get('/api/v1/x').subscribe((b) => (body = b));
    httpMock.expectOne('/api/v1/x').flush([{ id: 1 }]);
    expect(body).toEqual([{ id: 1 }]);
  });

  it('passes through a non-enveloped object (no success/data keys)', () => {
    let body: unknown;
    http.get('/api/v1/x').subscribe((b) => (body = b));
    httpMock.expectOne('/api/v1/x').flush({ id: 1, name: 'A' });
    expect(body).toEqual({ id: 1, name: 'A' });
  });

  it('passes through an object that has success but no data (e.g. IMfaVerifyResponse)', () => {
    let body: unknown;
    http.get('/api/v1/x').subscribe((b) => (body = b));
    httpMock.expectOne('/api/v1/x').flush({ success: true, recoveryCodes: ['a'] });
    expect(body).toEqual({ success: true, recoveryCodes: ['a'] });
  });

  it('does not unwrap a paginated page envelope ({ data, total } with no boolean success)', () => {
    let body: unknown;
    http.get('/api/v1/x').subscribe((b) => (body = b));
    const page = { data: [{ id: 1 }], total: 1, page: 1, pageSize: 20 };
    httpMock.expectOne('/api/v1/x').flush(page);
    expect(body).toEqual(page);
  });

  it('passes through a 204 No Content response', () => {
    let response: HttpResponse<unknown> | undefined;
    http
      .get('/api/v1/x', { observe: 'response' })
      .subscribe((r) => (response = r));
    httpMock
      .expectOne('/api/v1/x')
      .flush(null, { status: 204, statusText: 'No Content' });
    expect(response!.status).toBe(204);
    expect(response!.body).toBeNull();
  });

  it('passes through a blob download untouched', () => {
    let body: Blob | undefined;
    http
      .get('/api/v1/x', { responseType: 'blob' })
      .subscribe((b) => (body = b));
    const blob = new Blob(['data'], { type: 'application/octet-stream' });
    httpMock.expectOne('/api/v1/x').flush(blob);
    expect(body instanceof Blob).toBeTrue();
  });

  it('does not unwrap error (non-2xx) responses — they surface on the error channel', () => {
    let errorBody: unknown;
    http.get('/api/v1/x').subscribe({
      next: () => fail('should not emit next'),
      error: (e) => (errorBody = e.error),
    });
    httpMock
      .expectOne('/api/v1/x')
      .flush(
        { success: false, data: null, message: 'nope' },
        { status: 400, statusText: 'Bad Request' },
      );
    expect(errorBody).toEqual({ success: false, data: null, message: 'nope' });
  });
});
