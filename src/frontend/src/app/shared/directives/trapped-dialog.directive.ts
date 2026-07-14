import {
  AfterViewInit,
  Directive,
  ElementRef,
  OnDestroy,
  inject,
  output,
} from '@angular/core';
import { FocusTrap, FocusTrapFactory } from '@angular/cdk/a11y';

/**
 * ISSUE-296: shared trapped-dialog directive for the app's hand-rolled `role="dialog"` overlays.
 *
 * Applies a CDK focus trap (with initial-focus capture) + Escape-to-dismiss to whatever element
 * already carries `role="dialog" aria-modal="true"`, generalizing the employee-profile / PR #295
 * `cdkTrapFocus [cdkTrapFocusAutoCapture]="true" (keydown.escape)` pattern so every dialog gets the
 * same WCAG 2.1 AA keyboard behaviour (2.1.2 No Keyboard Trap / 2.4.3 Focus Order) without a
 * per-feature copy. Each migrated component keeps its own animations, backdrop and close handler.
 *
 * Usage:
 *   <div role="dialog" aria-modal="true" appTrappedDialog (dismiss)="close()"> … </div>
 *
 * NB: background `inert` on the app root is intentionally NOT applied here — there is no precedent
 * for it in this repo, and `aria-modal="true"` (already present on every target) signals modality to
 * assistive tech. It can be added later as an opt-in input without changing any migration.
 */
@Directive({
  selector: '[appTrappedDialog]',
  standalone: true,
  host: {
    '(keydown.escape)': 'onEscape()',
  },
})
export class TrappedDialogDirective implements AfterViewInit, OnDestroy {
  /** Emitted when Escape is pressed while focus is inside the dialog. */
  readonly dismiss = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly focusTrapFactory = inject(FocusTrapFactory);
  private focusTrap?: FocusTrap;

  ngAfterViewInit(): void {
    this.focusTrap = this.focusTrapFactory.create(this.host.nativeElement);
    // Mirror [cdkTrapFocusAutoCapture]="true": move focus into the dialog once its content is ready.
    void this.focusTrap.focusInitialElementWhenReady();
  }

  onEscape(): void {
    this.dismiss.emit();
  }

  ngOnDestroy(): void {
    this.focusTrap?.destroy();
  }
}
