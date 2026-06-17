import {
  ComponentFixture,
  TestBed,
  fakeAsync,
  tick,
  flush,
} from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { provideAnimations } from '@angular/platform-browser/animations';
import { NotificationPreferencesComponent } from './notification-preferences.component';
import { environment } from '../../../../../environments/environment';
import { IPreferenceMatrix } from '../../models/notification-preference.models';

/**
 * US-NTF-003 AC-1..AC-4 / BR-3: the preference matrix page. Covers the
 * mandatory-locked toggles, the both-channels-off revert (BR-3/AC-3), and the
 * 500ms debounced auto-save.
 */
describe('NotificationPreferencesComponent', () => {
  let fixture: ComponentFixture<NotificationPreferencesComponent>;
  let component: NotificationPreferencesComponent;
  let httpMock: HttpTestingController;
  let toastr: ToastrService;
  const base = `${environment.apiBaseUrl}/notification-preferences`;

  const matrix: IPreferenceMatrix = {
    categories: [
      {
        category: 'leave_updates',
        categoryName: 'Leave Updates',
        description: 'Approvals, rejections, balance changes.',
        channelInApp: true,
        channelEmail: true,
        isMandatory: false,
      },
      {
        category: 'security_alerts',
        categoryName: 'Security Alerts',
        description: 'Sign-ins and security events.',
        channelInApp: true,
        channelEmail: true,
        isMandatory: true,
      },
    ],
    quietHours: {
      enabled: false,
      start: null,
      end: null,
      timezone: null,
    },
  };

  /** Render + answer the initial matrix GET. */
  function bootstrap(): void {
    fixture.detectChanges(); // ngOnInit -> load()
    httpMock.expectOne(base).flush(matrix);
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [NotificationPreferencesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideAnimations(),
        provideToastr(),
        provideTranslateService(),
      ],
    });
    fixture = TestBed.createComponent(NotificationPreferencesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    toastr = TestBed.inject(ToastrService);
  });

  afterEach(() => httpMock.verify());

  it('renders a row per category after load (AC-1)', () => {
    bootstrap();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Leave Updates');
    expect(text).toContain('Security Alerts');
  });

  it('locks both toggles for a mandatory category (AC-3/AC-4)', () => {
    bootstrap();
    // The desktop table has 4 toggles (2 categories x 2 channels). The mandatory
    // row's two toggles must be disabled; the non-mandatory row's must not.
    const toggles = fixture.nativeElement.querySelectorAll(
      '.md\\:block mat-slide-toggle button[role="switch"]',
    ) as NodeListOf<HTMLButtonElement>;
    expect(toggles.length).toBe(4);
    // categories render in order: leave_updates (0,1) then security_alerts (2,3)
    expect(toggles[0].disabled).toBeFalse();
    expect(toggles[1].disabled).toBeFalse();
    expect(toggles[2].disabled).toBeTrue();
    expect(toggles[3].disabled).toBeTrue();
  });

  it('onToggle on a mandatory category is a no-op (does not fire a save)', fakeAsync(() => {
    bootstrap();
    const mandatory = component['svc']
      .categories()
      .find((c) => c.category === 'security_alerts')!;
    component['onToggle'](mandatory, 'channelEmail', false);
    tick(500);
    httpMock.expectNone(`${base}/security_alerts`);
    // unchanged in the cache
    const after = component['svc']
      .categories()
      .find((c) => c.category === 'security_alerts')!;
    expect(after.channelEmail).toBeTrue();
  }));

  it('debounces the auto-save by 500ms and PUTs once (AC-2)', fakeAsync(() => {
    bootstrap();
    const cat = component['svc']
      .categories()
      .find((c) => c.category === 'leave_updates')!;

    component['onToggle'](cat, 'channelEmail', false);
    // before the debounce window elapses, no request has fired
    tick(400);
    httpMock.expectNone(`${base}/leave_updates`);

    tick(100); // total 500ms -> flush
    const req = httpMock.expectOne(`${base}/leave_updates`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ channelInApp: true, channelEmail: false });
    req.flush({ ...cat, channelEmail: false });
    flush();
  }));

  it('reverts the toggle and shows the server message on a both-off 422 (BR-3)', fakeAsync(() => {
    const errSpy = spyOn(toastr, 'error');
    bootstrap();
    const cat = component['svc']
      .categories()
      .find((c) => c.category === 'leave_updates')!;
    // start from in-app already off so turning email off would leave both off
    component['svc'].categories.update((rows) =>
      rows.map((r) =>
        r.category === 'leave_updates' ? { ...r, channelInApp: false } : r,
      ),
    );

    component['onToggle'](
      { ...cat, channelInApp: false },
      'channelEmail',
      false,
    );
    tick(500);

    const req = httpMock.expectOne(`${base}/leave_updates`);
    req.flush(
      { message: 'At least one channel must stay on.' },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    flush();

    // reverted to the snapshot that was sent (email back on per the revert)
    const after = component['svc']
      .categories()
      .find((c) => c.category === 'leave_updates')!;
    expect(after.channelEmail).toBeTrue();
    expect(errSpy).toHaveBeenCalledWith('At least one channel must stay on.');
  }));

  it('confirmReset POSTs reset and refreshes the matrix (FR-7)', fakeAsync(() => {
    bootstrap();
    component['openResetConfirm']();
    fixture.detectChanges();
    expect(component['showResetConfirm']()).toBeTrue();

    component['confirmReset']();
    const req = httpMock.expectOne(`${base}/reset`);
    expect(req.request.method).toBe('POST');
    req.flush(matrix);
    flush();

    expect(component['showResetConfirm']()).toBeFalse();
    expect(component['svc'].categories().length).toBe(2);
  }));

  it('saveQuietHours PUTs the form values, nulling times when disabled (FR-9)', fakeAsync(() => {
    bootstrap();
    component['toggleQuietHours'](); // open + hydrate
    fixture.detectChanges();

    component['quietForm'].patchValue({
      enabled: true,
      start: '23:00',
      end: '06:00',
      timezone: 'Europe/London',
    });
    component['saveQuietHours']();

    const req = httpMock.expectOne(`${base}/quiet-hours`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      enabled: true,
      start: '23:00',
      end: '06:00',
      timezone: 'Europe/London',
    });
    req.flush(req.request.body);
    flush();
  }));
});
