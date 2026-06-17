import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { OnboardingProgressWidgetComponent } from './onboarding-progress-widget.component';
import { environment } from '../../../../../environments/environment';
import { IOnboardingProgress } from '../../models/my-onboarding.models';

describe('OnboardingProgressWidgetComponent', () => {
  let fixture: ComponentFixture<OnboardingProgressWidgetComponent>;
  let component: OnboardingProgressWidgetComponent;
  let httpMock: HttpTestingController;
  const url = `${environment.apiBaseUrl}/onboarding/checklists/me/progress`;

  const progress = (over: Partial<IOnboardingProgress> = {}): IOnboardingProgress => ({
    progressPercent: 60,
    totalTasks: 5,
    completedTasks: 3,
    pendingTasks: 1,
    overdueTasks: 1,
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OnboardingProgressWidgetComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
      ],
    });
    fixture = TestBed.createComponent(OnboardingProgressWidgetComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches /me/progress on init and renders the ring + counts (AC-1)', () => {
    fixture.detectChanges(); // triggers ngOnInit
    const req = httpMock.expectOne(url);
    expect(req.request.method).toBe('GET');
    req.flush(progress());
    fixture.detectChanges();

    expect(component.progress()).toEqual(progress());
    expect(component.loading()).toBeFalse();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('60%');
    expect(text).toContain('Onboarding Progress');
  });

  it('computes the ring dash offset from the percent', () => {
    fixture.detectChanges();
    httpMock.expectOne(url).flush(progress({ progressPercent: 50 }));
    fixture.detectChanges();
    // 50% => half the circumference offset.
    expect(component.dashOffset()).toBeCloseTo(component.circumference / 2, 5);
  });

  it('hides the widget when no checklist is assigned (totalTasks === 0)', () => {
    fixture.detectChanges();
    httpMock.expectOne(url).flush(progress({ totalTasks: 0, progressPercent: 0 }));
    fixture.detectChanges();

    expect(component.progress()).toBeNull();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Onboarding Progress');
  });

  it('hides the widget (fail-closed) on a 404', () => {
    fixture.detectChanges();
    httpMock
      .expectOne(url)
      .flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(component.progress()).toBeNull();
    expect(component.loading()).toBeFalse();
  });
});
