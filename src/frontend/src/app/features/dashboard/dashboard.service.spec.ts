import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { DashboardService } from './dashboard.service';
import { IDashboardResponse } from './dashboard.models';
import { environment } from '../../../environments/environment';

describe('DashboardService', () => {
  let service: DashboardService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/dashboard`;

  const response: IDashboardResponse = {
    role: 'employee',
    greetingName: 'Lee',
    generatedAt: '2026-06-17T08:00:00Z',
    widgets: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        DashboardService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(DashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('GETs widgets with refresh=false by default', () => {
    service.getWidgets().subscribe((res) => expect(res).toEqual(response));
    const req = httpMock.expectOne(
      (r) => r.url === `${base}/widgets` && r.params.get('refresh') === 'false'
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(response);
  });

  it('GETs widgets with refresh=true when requested', () => {
    service.getWidgets(true).subscribe((res) => expect(res).toEqual(response));
    const req = httpMock.expectOne(
      (r) => r.url === `${base}/widgets` && r.params.get('refresh') === 'true'
    );
    expect(req.request.method).toBe('GET');
    req.flush(response);
  });

  it('refresh() is a cache-bypassing getWidgets(true)', () => {
    service.refresh().subscribe((res) => expect(res).toEqual(response));
    const req = httpMock.expectOne(
      (r) => r.url === `${base}/widgets` && r.params.get('refresh') === 'true'
    );
    req.flush(response);
  });
});
