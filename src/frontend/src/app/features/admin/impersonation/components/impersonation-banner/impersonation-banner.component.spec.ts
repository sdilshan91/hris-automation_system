import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { ImpersonationBannerComponent } from './impersonation-banner.component';
import { AuthService } from '../../../../../core/auth/auth.service';
import { environment } from '../../../../../../environments/environment';

describe('ImpersonationBannerComponent', () => {
  let fixture: ComponentFixture<ImpersonationBannerComponent>;
  let component: ImpersonationBannerComponent;
  let httpMock: HttpTestingController;

  const base = `${environment.apiBaseUrl}/system/impersonation`;

  // Controllable AuthService signals standing in for the decoded-token computeds.
  const isImpersonating = signal(false);
  const sessionId = signal<string | null>(null);
  const readOnly = signal(false);
  let endImpersonationSpy: jasmine.Spy;
  let logoutSpy: jasmine.Spy;
  let navigateSpy: jasmine.Spy;

  function build(): void {
    fixture = TestBed.createComponent(ImpersonationBannerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    isImpersonating.set(false);
    sessionId.set(null);
    readOnly.set(false);
    endImpersonationSpy = jasmine.createSpy('endImpersonation').and.returnValue(true);
    logoutSpy = jasmine.createSpy('logout');

    const authStub: Partial<AuthService> = {
      isImpersonating,
      impersonationSessionId: sessionId,
      impersonationReadOnly: readOnly,
      endImpersonation: endImpersonationSpy,
      logout: logoutSpy,
    } as unknown as Partial<AuthService>;

    TestBed.configureTestingModule({
      imports: [ImpersonationBannerComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTranslateService(),
        { provide: AuthService, useValue: authStub },
        { provide: ToastrService, useValue: { info: jasmine.createSpy('info') } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    navigateSpy = spyOn(TestBed.inject(Router), 'navigate');
  });

  afterEach(() => httpMock.verify());

  it('is hidden when not impersonating', () => {
    build();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="impersonation-banner"]')).toBeNull();
  });

  it('shows the banner with the session ref when impersonating (NFR-4)', () => {
    isImpersonating.set(true);
    sessionId.set('sess-9');
    build();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="impersonation-banner"]')).not.toBeNull();
    expect(component.sessionId()).toBe('sess-9');
    // Read-only marker absent for a write session.
    expect(el.querySelector('[data-testid="banner-readonly"]')).toBeNull();
  });

  it('shows the read-only marker when imp_readonly is true (AC-5)', () => {
    isImpersonating.set(true);
    sessionId.set('sess-9');
    readOnly.set(true);
    build();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="banner-readonly"]')).not.toBeNull();
  });

  it('End Session calls the end endpoint, restores the admin session, and navigates (AC-3)', () => {
    isImpersonating.set(true);
    sessionId.set('sess-9');
    build();

    component.endSession();

    const req = httpMock.expectOne(`${base}/sess-9/end`);
    expect(req.request.method).toBe('POST');
    req.flush({ sessionId: 'sess-9', status: 'ended', actionsCount: 0, endedAt: '' });

    expect(endImpersonationSpy).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/admin/monitoring']);
    expect(component.ending()).toBeFalse();
  });

  it('still clears the session if the end endpoint fails (fail-closed)', () => {
    isImpersonating.set(true);
    sessionId.set('sess-9');
    build();

    component.endSession();
    const req = httpMock.expectOne(`${base}/sess-9/end`);
    req.flush({}, { status: 500, statusText: 'Server Error' });

    expect(endImpersonationSpy).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/admin/monitoring']);
  });

  it('falls back to logout when no admin session can be restored', () => {
    endImpersonationSpy.and.returnValue(false);
    isImpersonating.set(true);
    sessionId.set('sess-9');
    build();

    component.endSession();
    httpMock
      .expectOne(`${base}/sess-9/end`)
      .flush({ sessionId: 'sess-9', status: 'ended', actionsCount: 0, endedAt: '' });

    expect(logoutSpy).toHaveBeenCalled();
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
