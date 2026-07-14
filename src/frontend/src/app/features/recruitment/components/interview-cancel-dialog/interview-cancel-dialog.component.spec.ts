import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { By } from '@angular/platform-browser';

import { InterviewCancelDialogComponent } from './interview-cancel-dialog.component';
import { TrappedDialogDirective } from '../../../../shared/directives';

describe('InterviewCancelDialogComponent', () => {
  let component: InterviewCancelDialogComponent;
  let fixture: ComponentFixture<InterviewCancelDialogComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InterviewCancelDialogComponent],
      providers: [provideAnimationsAsync()],
    }).compileComponents();

    fixture = TestBed.createComponent(InterviewCancelDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('creates', () => {
    expect(component).toBeTruthy();
  });

  it('traps focus and closes on Escape (ISSUE-296)', () => {
    const dialog = fixture.debugElement.query(By.directive(TrappedDialogDirective));
    expect(dialog).toBeTruthy();

    // ISSUE-296 behaviour: Escape → directive `dismiss` → the bound `cancel()` → the `cancelled` output.
    // Catches a broken/removed `(dismiss)="cancel()"` binding.
    let cancelled = false;
    component.cancelled.subscribe(() => (cancelled = true));
    (dialog.nativeElement as HTMLElement).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );
    expect(cancelled).toBeTrue();
  });

  it('confirms with the trimmed reason when provided', () => {
    const emitted: (string | undefined)[] = [];
    component.confirmed.subscribe((r) => emitted.push(r));
    component.reasonValue = '  Candidate withdrew  ';
    component.confirm();
    expect(emitted[0]).toBe('Candidate withdrew');
  });

  it('confirms with undefined when the reason is blank', () => {
    const emitted: (string | undefined)[] = [];
    component.confirmed.subscribe((r) => emitted.push(r));
    component.reasonValue = '   ';
    component.confirm();
    expect(emitted[0]).toBeUndefined();
  });

  it('emits cancelled on dismiss', () => {
    let cancelled = false;
    component.cancelled.subscribe(() => (cancelled = true));
    component.cancel();
    expect(cancelled).toBeTrue();
  });
});
