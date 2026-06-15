import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';

import {
  StageReasonDialogComponent,
  IStageReasonResult,
} from './stage-reason-dialog.component';

describe('StageReasonDialogComponent', () => {
  let component: StageReasonDialogComponent;
  let fixture: ComponentFixture<StageReasonDialogComponent>;

  function setup(toStage: 'Rejected' | 'Applied'): void {
    fixture = TestBed.createComponent(StageReasonDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('applicantName', 'Ada Lovelace');
    fixture.componentRef.setInput('toStage', toStage);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StageReasonDialogComponent],
      providers: [provideAnimationsAsync()],
    }).compileComponents();
  });

  describe('rejection (BR-3)', () => {
    beforeEach(() => setup('Rejected'));

    it('marks itself as a rejection', () => {
      expect(component.isRejection()).toBeTrue();
      expect(component.title()).toBe('Reject applicant');
    });

    it('cannot confirm without a reason', () => {
      expect(component.canConfirm()).toBeFalse();
      let emitted: IStageReasonResult | undefined;
      component.confirmed.subscribe((r) => (emitted = r));
      component.confirm();
      expect(emitted).toBeUndefined();
    });

    it('confirms with the selected reason + optional notes', () => {
      component.selectedReason = 'Not qualified';
      component.notes = 'see panel';
      component.onInput();
      expect(component.canConfirm()).toBeTrue();
      let emitted: IStageReasonResult | undefined;
      component.confirmed.subscribe((r) => (emitted = r));
      component.confirm();
      expect(emitted).toEqual({ reason: 'Not qualified', notes: 'see panel' });
    });

    it('omits empty notes', () => {
      component.selectedReason = 'Position filled';
      component.onInput();
      let emitted: IStageReasonResult | undefined;
      component.confirmed.subscribe((r) => (emitted = r));
      component.confirm();
      expect(emitted).toEqual({ reason: 'Position filled', notes: undefined });
    });
  });

  describe('backward move (BR-4)', () => {
    beforeEach(() => setup('Applied'));

    it('is not a rejection and uses free-text reason', () => {
      expect(component.isRejection()).toBeFalse();
      expect(component.title()).toBe('Move applicant back');
    });

    it('requires a free-text reason to confirm', () => {
      expect(component.canConfirm()).toBeFalse();
      component.freeReason = 'Needs re-screen';
      component.onInput();
      expect(component.canConfirm()).toBeTrue();
      let emitted: IStageReasonResult | undefined;
      component.confirmed.subscribe((r) => (emitted = r));
      component.confirm();
      expect(emitted).toEqual({ reason: 'Needs re-screen' });
    });
  });

  it('emits cancelled on cancel()', () => {
    setup('Rejected');
    let cancelled = false;
    component.cancelled.subscribe(() => (cancelled = true));
    component.cancel();
    expect(cancelled).toBeTrue();
  });
});
