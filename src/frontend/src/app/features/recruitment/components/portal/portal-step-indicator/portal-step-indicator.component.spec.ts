import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PortalStepIndicatorComponent } from './portal-step-indicator.component';

describe('PortalStepIndicatorComponent (US-REC-008)', () => {
  let fixture: ComponentFixture<PortalStepIndicatorComponent>;
  let component: PortalStepIndicatorComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PortalStepIndicatorComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(PortalStepIndicatorComponent);
    component = fixture.componentInstance;
  });

  it('renders five steps with the current one marked aria-current', () => {
    fixture.componentRef.setInput('stage', 'Screening');
    fixture.detectChanges();
    expect(component.steps().length).toBe(5);
    const current = fixture.nativeElement.querySelector('[aria-current="step"]');
    expect(current).toBeTruthy();
    // Screening is the current step.
    expect(component.steps()[1].state).toBe('current');
  });

  it('shows the closed banner instead of the bar for a Rejected application', () => {
    fixture.componentRef.setInput('stage', 'Rejected');
    fixture.detectChanges();
    expect(component.rejected()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('no longer active');
    expect(fixture.nativeElement.querySelector('ol')).toBeNull();
  });
});
