import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { IdleTimeoutService } from './idle-timeout.service';
import { AuthService } from '../auth/auth.service';

describe('IdleTimeoutService', () => {
  let service: IdleTimeoutService;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    authServiceSpy = jasmine.createSpyObj('AuthService', [
      'keepAlive',
      'logout',
    ]);
    authServiceSpy.keepAlive.and.returnValue(of({ message: 'ok' }));

    TestBed.configureTestingModule({
      providers: [
        IdleTimeoutService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideToastr(),
        { provide: AuthService, useValue: authServiceSpy },
      ],
    });

    service = TestBed.inject(IdleTimeoutService);
  });

  afterEach(() => {
    service.stop();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should not show warning initially', () => {
    expect(service.showWarning()).toBeFalse();
    expect(service.secondsRemaining()).toBe(0);
  });

  it('should not start if idleTimeoutMinutes is 0 or negative', () => {
    service.start(0);
    expect(service.showWarning()).toBeFalse();
  });

  it('should show warning after idle period', fakeAsync(() => {
    // Start with 6 minutes timeout (warning fires at 1 minute = 60s)
    // (6 min = 360s total, warning at 360 - 300 = 60s delay)
    service.start(6);

    // Advance past the delay to trigger the warning (60s)
    tick(60 * 1000);

    expect(service.showWarning()).toBeTrue();
    expect(service.secondsRemaining()).toBe(300);

    // Cleanup periodic timers
    service.stop();
    discardPeriodicTasks();
  }));

  it('should count down seconds after warning is shown', fakeAsync(() => {
    service.start(6);

    // Trigger warning at 60s
    tick(60 * 1000);
    expect(service.showWarning()).toBeTrue();
    expect(service.secondsRemaining()).toBe(300);

    // Advance 3 seconds
    tick(3000);
    expect(service.secondsRemaining()).toBe(297);

    service.stop();
    discardPeriodicTasks();
  }));

  it('should call logout when countdown reaches zero', fakeAsync(() => {
    service.start(6);

    // Trigger warning
    tick(60 * 1000);

    // Count down all 300 seconds
    tick(300 * 1000);

    expect(authServiceSpy.logout).toHaveBeenCalled();
    expect(service.showWarning()).toBeFalse();
    expect(service.secondsRemaining()).toBe(0);

    discardPeriodicTasks();
  }));

  it('should call keepAlive and reset when stayLoggedIn is invoked', fakeAsync(() => {
    service.start(6);

    // Trigger warning
    tick(60 * 1000);
    expect(service.showWarning()).toBeTrue();

    service.stayLoggedIn();

    expect(authServiceSpy.keepAlive).toHaveBeenCalled();
    expect(service.showWarning()).toBeFalse();

    service.stop();
    discardPeriodicTasks();
  }));

  it('should handle keepAlive failure gracefully', fakeAsync(() => {
    authServiceSpy.keepAlive.and.returnValue(
      throwError(() => ({ status: 401 }))
    );

    service.start(6);
    tick(60 * 1000);

    service.stayLoggedIn();
    // Should not throw
    expect(service.showWarning()).toBeFalse();

    service.stop();
    discardPeriodicTasks();
  }));

  it('should stop all timers on stop()', fakeAsync(() => {
    service.start(6);

    service.stop();

    expect(service.showWarning()).toBeFalse();
    expect(service.secondsRemaining()).toBe(0);

    discardPeriodicTasks();
  }));

  it('should reset warning timer on activity when not in warning state', fakeAsync(() => {
    service.start(6);

    // Advance 50s (before 60s warning trigger)
    tick(50 * 1000);

    // Simulate user activity by dispatching a mousemove
    document.dispatchEvent(new Event('mousemove'));

    // Wait another 50s -- the timer should have been reset so no warning yet
    tick(50 * 1000);
    expect(service.showWarning()).toBeFalse();

    service.stop();
    discardPeriodicTasks();
  }));
});
