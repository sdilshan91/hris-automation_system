import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { signal } from '@angular/core';

import { IdleTimeoutWarningComponent } from './idle-timeout-warning.component';
import { IdleTimeoutService } from '../../../core/services/idle-timeout.service';

describe('IdleTimeoutWarningComponent', () => {
  let component: IdleTimeoutWarningComponent;
  let fixture: ComponentFixture<IdleTimeoutWarningComponent>;
  let mockIdleService: {
    showWarning: ReturnType<typeof signal<boolean>>;
    secondsRemaining: ReturnType<typeof signal<number>>;
    stayLoggedIn: jasmine.Spy;
  };

  beforeEach(async () => {
    mockIdleService = {
      showWarning: signal(false),
      secondsRemaining: signal(0),
      stayLoggedIn: jasmine.createSpy('stayLoggedIn'),
    };

    await TestBed.configureTestingModule({
      imports: [IdleTimeoutWarningComponent],
      providers: [
        provideAnimationsAsync(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideToastr(),
        { provide: IdleTimeoutService, useValue: mockIdleService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(IdleTimeoutWarningComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should format countdown as m:ss', () => {
    mockIdleService.secondsRemaining.set(300);
    expect(component.formatCountdown()).toBe('5:00');

    mockIdleService.secondsRemaining.set(65);
    expect(component.formatCountdown()).toBe('1:05');

    mockIdleService.secondsRemaining.set(0);
    expect(component.formatCountdown()).toBe('0:00');

    mockIdleService.secondsRemaining.set(59);
    expect(component.formatCountdown()).toBe('0:59');

    mockIdleService.secondsRemaining.set(120);
    expect(component.formatCountdown()).toBe('2:00');
  });

  it('should not render dialog when showWarning is false', () => {
    mockIdleService.showWarning.set(false);
    fixture.detectChanges();

    const overlay = fixture.nativeElement.querySelector('.warning-overlay');
    expect(overlay).toBeNull();
  });

  it('should render dialog when showWarning is true', () => {
    mockIdleService.showWarning.set(true);
    mockIdleService.secondsRemaining.set(120);
    fixture.detectChanges();

    const overlay = fixture.nativeElement.querySelector('.warning-overlay');
    expect(overlay).toBeTruthy();

    const countdownValue = fixture.nativeElement.querySelector('.countdown-value');
    expect(countdownValue?.textContent?.trim()).toBe('2:00');
  });

  it('should call stayLoggedIn when button is clicked', () => {
    mockIdleService.showWarning.set(true);
    mockIdleService.secondsRemaining.set(60);
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.btn-stay');
    expect(button).toBeTruthy();

    button.click();
    expect(mockIdleService.stayLoggedIn).toHaveBeenCalled();
  });
});
