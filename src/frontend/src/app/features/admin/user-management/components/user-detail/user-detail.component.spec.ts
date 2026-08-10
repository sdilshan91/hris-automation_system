import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { UserDetailComponent } from './user-detail.component';
import { IUserDetail } from '../../models/user-management.models';
import { environment } from '../../../../../../environments/environment';

describe('UserDetailComponent', () => {
  let component: UserDetailComponent;
  let fixture: ComponentFixture<UserDetailComponent>;
  let httpMock: HttpTestingController;

  const usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  const detail: IUserDetail = {
    userTenantId: 'ut-1',
    userId: 'u-1',
    displayName: 'Jane Doe',
    email: 'jane@acme.com',
    status: 'active',
    lastLoginAt: '2026-06-01T00:00:00Z',
    roles: [{ id: 'r-1', name: 'HR Officer' }],
    linkedEmployee: {
      employeeId: 'e-1',
      fullName: 'Jane Doe',
      jobTitle: 'HR Lead',
      department: 'People',
    },
    activeSessions: [
      {
        id: 's-1',
        device: 'Chrome / macOS',
        ipAddress: '1.2.3.4',
        lastActiveAt: '2026-06-01T00:00:00Z',
      },
    ],
    recentAudit: [],
    invitationHistory: [],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UserDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ userTenantId: 'ut-1' }) },
          },
        },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UserDetailComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads the user detail on init', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(`${usersUrl}/ut-1/detail`);
    expect(req.request.method).toBe('GET');
    req.flush(detail);
    expect(component.user()?.displayName).toBe('Jane Doe');
    expect(component.loading()).toBeFalse();
  });

  it('renders the linked employee section', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${usersUrl}/ut-1/detail`).flush(detail);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('HR Lead');
  });

  it('shows an error state on failure', () => {
    fixture.detectChanges();
    httpMock
      .expectOne(`${usersUrl}/ut-1/detail`)
      .flush('boom', { status: 500, statusText: 'Server Error' });
    expect(component.error()).toBeTruthy();
    expect(component.loading()).toBeFalse();
  });
});
