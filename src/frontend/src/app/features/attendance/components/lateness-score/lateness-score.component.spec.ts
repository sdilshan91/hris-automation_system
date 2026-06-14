import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { LatenessScoreComponent } from './lateness-score.component';
import { AttendanceService } from '../../services/attendance.service';
import { ILatenessScore } from '../../models/attendance.models';

/**
 * US-ATT-008 (§8, AC-4) lateness-score self-service spec. AttendanceService is mocked.
 * Covers: the "X of N" progress renders, an under-budget score is green (ok tone), an
 * at/over-budget score is amber (warn tone) + the warning note, and a load failure
 * shows the unavailable state.
 */
describe('LatenessScoreComponent', () => {
  let fixture: ComponentFixture<LatenessScoreComponent>;
  let component: LatenessScoreComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;

  function setup(score: ILatenessScore): void {
    attendanceSpy.getMyLatenessScore.and.returnValue(of(score));
    fixture = TestBed.createComponent(LatenessScoreComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getMyLatenessScore',
    ]);

    await TestBed.configureTestingModule({
      imports: [LatenessScoreComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
      ],
    }).compileComponents();
  });

  it('renders the "X of N allowed lates" progress (AC-4)', () => {
    setup({ yearMonth: '2026-06', lateCount: 2, allowedLates: 3, earlyDepartureCount: 1 });
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('2');
    expect(text).toContain('of 3');
    expect(component.usedPercent()).toBe(67); // round(2/3*100)
  });

  it('uses the ok (green) tone when under the allowance', () => {
    setup({ yearMonth: '2026-06', lateCount: 1, allowedLates: 3, earlyDepartureCount: 0 });
    expect(component.tone()).toBe('ok');
    expect(fixture.nativeElement.querySelector('[data-test="warn-note"]')).toBeNull();
  });

  it('uses the warn (amber) tone + note at/over the allowance', () => {
    setup({ yearMonth: '2026-06', lateCount: 3, allowedLates: 3, earlyDepartureCount: 2 });
    expect(component.tone()).toBe('warn');
    expect(component.usedPercent()).toBe(100);
    expect(fixture.nativeElement.querySelector('[data-test="warn-note"]')).toBeTruthy();
  });

  it('shows the early-departure count alongside the score', () => {
    setup({ yearMonth: '2026-06', lateCount: 0, allowedLates: 3, earlyDepartureCount: 4 });
    const early = fixture.nativeElement.querySelector('[data-test="early-count"]');
    expect((early.textContent as string)).toContain('4');
  });

  it('renders the unavailable state on a load failure', () => {
    attendanceSpy.getMyLatenessScore.and.returnValue(throwError(() => new Error('x')));
    fixture = TestBed.createComponent(LatenessScoreComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    expect(component.score()).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-test="error"]')).toBeTruthy();
  });
});
