import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FocusTrap, FocusTrapFactory } from '@angular/cdk/a11y';
import { TrappedDialogDirective } from './trapped-dialog.directive';

@Component({
  standalone: true,
  imports: [TrappedDialogDirective],
  template: `
    <div role="dialog" aria-modal="true" appTrappedDialog (dismiss)="closedCount = closedCount + 1">
      <button type="button">First</button>
    </div>
  `,
})
class HostComponent {
  closedCount = 0;
}

describe('TrappedDialogDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let focusTrap: jasmine.SpyObj<FocusTrap>;
  let factory: jasmine.SpyObj<FocusTrapFactory>;

  beforeEach(() => {
    focusTrap = jasmine.createSpyObj<FocusTrap>('FocusTrap', [
      'focusInitialElementWhenReady',
      'destroy',
    ]);
    focusTrap.focusInitialElementWhenReady.and.returnValue(Promise.resolve(true));
    factory = jasmine.createSpyObj<FocusTrapFactory>('FocusTrapFactory', ['create']);
    factory.create.and.returnValue(focusTrap);

    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{ provide: FocusTrapFactory, useValue: factory }],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  function dialogEl(): HTMLElement {
    return fixture.nativeElement.querySelector('[role="dialog"]') as HTMLElement;
  }

  it('creates a focus trap on the dialog element and captures initial focus', () => {
    expect(factory.create).toHaveBeenCalledWith(dialogEl());
    expect(focusTrap.focusInitialElementWhenReady).toHaveBeenCalled();
  });

  it('emits dismiss when Escape is pressed inside the dialog', () => {
    dialogEl().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(fixture.componentInstance.closedCount).toBe(1);
  });

  it('does NOT emit dismiss for other keys', () => {
    dialogEl().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();
    expect(fixture.componentInstance.closedCount).toBe(0);
  });

  it('releases the focus trap on destroy', () => {
    fixture.destroy();
    expect(focusTrap.destroy).toHaveBeenCalled();
  });
});
