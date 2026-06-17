import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  forwardRef,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

/**
 * US-REC-001 (FR-1, NFR-4): a lightweight, dependency-free rich text editor for the
 * vacancy description / qualifications fields.
 *
 * Implemented as a `contenteditable` host that emits an HTML string via the
 * ControlValueAccessor contract, so it drops into Reactive Forms exactly like an
 * <input>. We deliberately avoid a heavy 3rd-party editor (ngx-editor / prosemirror)
 * to keep the build + test gate lean for this foundation story.
 *
 * SECURITY (NFR-4): the value is *displayed* elsewhere via Angular's default
 * `[innerHTML]` binding, which sanitizes and strips <script>/event handlers — we do
 * NOT bypassSecurityTrust anywhere. The toolbar only produces inline formatting +
 * lists + links, all of which survive sanitization.
 *
 * A11y: toolbar buttons are real <button>s with aria-labels and aria-pressed; the
 * editable region has role="textbox" + aria-multiline and an accessible label.
 */
type ToolbarCommand =
  | 'bold'
  | 'italic'
  | 'underline'
  | 'insertUnorderedList'
  | 'insertOrderedList';

@Component({
  selector: 'app-rich-text-editor',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => RichTextEditorComponent),
      multi: true,
    },
  ],
  template: `
    <div
      class="rte rounded-lg border border-neutral-200 bg-white shadow-sm transition focus-within:border-indigo-400 focus-within:ring-2 focus-within:ring-indigo-100"
      [class.opacity-60]="disabled()"
    >
      <!-- Toolbar (collapses to wrap on narrow screens) -->
      <div
        class="flex flex-wrap items-center gap-1 border-b border-neutral-100 px-2 py-1.5"
        role="toolbar"
        [attr.aria-label]="ariaLabel() + ' formatting toolbar'"
      >
        <button
          type="button"
          class="rte-btn"
          [class.rte-active]="active().bold"
          [attr.aria-pressed]="active().bold"
          aria-label="Bold"
          [disabled]="disabled()"
          (mousedown)="$event.preventDefault()"
          (click)="exec('bold')"
        >
          <span class="font-bold">B</span>
        </button>
        <button
          type="button"
          class="rte-btn"
          [class.rte-active]="active().italic"
          [attr.aria-pressed]="active().italic"
          aria-label="Italic"
          [disabled]="disabled()"
          (mousedown)="$event.preventDefault()"
          (click)="exec('italic')"
        >
          <span class="italic">I</span>
        </button>
        <button
          type="button"
          class="rte-btn"
          [class.rte-active]="active().underline"
          [attr.aria-pressed]="active().underline"
          aria-label="Underline"
          [disabled]="disabled()"
          (mousedown)="$event.preventDefault()"
          (click)="exec('underline')"
        >
          <span class="underline">U</span>
        </button>
        <span class="mx-1 h-5 w-px bg-neutral-200" aria-hidden="true"></span>
        <button
          type="button"
          class="rte-btn"
          aria-label="Bulleted list"
          [disabled]="disabled()"
          (mousedown)="$event.preventDefault()"
          (click)="exec('insertUnorderedList')"
        >
          &bull;&nbsp;List
        </button>
        <button
          type="button"
          class="rte-btn"
          aria-label="Numbered list"
          [disabled]="disabled()"
          (mousedown)="$event.preventDefault()"
          (click)="exec('insertOrderedList')"
        >
          1.&nbsp;List
        </button>
        <button
          type="button"
          class="rte-btn"
          aria-label="Insert link"
          [disabled]="disabled()"
          (mousedown)="$event.preventDefault()"
          (click)="insertLink()"
        >
          Link
        </button>
      </div>

      <!-- Editable region -->
      <div
        #editable
        class="rte-content prose prose-sm max-w-none px-3 py-2 min-h-[8rem] focus:outline-none"
        [attr.contenteditable]="!disabled()"
        role="textbox"
        aria-multiline="true"
        [attr.aria-label]="ariaLabel()"
        (input)="onInput()"
        (blur)="onBlur()"
        (keyup)="refreshActive()"
        (mouseup)="refreshActive()"
      ></div>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .rte-btn {
        min-width: 2rem;
        padding: 0.25rem 0.5rem;
        border-radius: 0.375rem;
        font-size: 0.8125rem;
        color: #525252;
        transition: background-color 150ms ease, color 150ms ease;
      }
      .rte-btn:hover:not(:disabled) {
        background-color: #f5f5f5;
      }
      .rte-btn:disabled {
        cursor: not-allowed;
        opacity: 0.5;
      }
      .rte-active {
        background-color: #eef2ff;
        color: #4338ca;
      }
      .rte-content:empty::before {
        content: attr(data-placeholder);
        color: #a3a3a3;
        pointer-events: none;
      }
    `,
  ],
})
export class RichTextEditorComponent implements ControlValueAccessor {
  /** Accessible label for the editable region + toolbar. */
  readonly ariaLabel = input<string>('Rich text editor');
  /** Placeholder shown when the editor is empty. */
  readonly placeholder = input<string>('');

  private readonly editableRef =
    viewChild.required<ElementRef<HTMLDivElement>>('editable');

  readonly disabled = signal(false);
  /** Which inline formats are active at the caret — drives toolbar aria-pressed. */
  readonly active = signal<{ bold: boolean; italic: boolean; underline: boolean }>(
    { bold: false, italic: false, underline: false },
  );

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  // ─── ControlValueAccessor ────────────────────────────────

  writeValue(value: string | null): void {
    const el = this.editableRef().nativeElement;
    el.innerHTML = value ?? '';
    el.setAttribute('data-placeholder', this.placeholder());
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  // ─── Editing ─────────────────────────────────────────────

  onInput(): void {
    this.onChange(this.currentHtml());
  }

  onBlur(): void {
    this.onTouched();
  }

  /** Apply a formatting command to the current selection. */
  exec(command: ToolbarCommand): void {
    if (this.disabled()) {
      return;
    }
    this.editableRef().nativeElement.focus();
    // execCommand is deprecated but remains the only zero-dependency way to do
    // inline rich-text editing across browsers; output is plain HTML we sanitize.
    document.execCommand(command, false);
    this.onChange(this.currentHtml());
    this.refreshActive();
  }

  /** Prompt for a URL and wrap the selection in an anchor. */
  insertLink(): void {
    if (this.disabled()) {
      return;
    }
    const url = window.prompt('Enter a URL');
    if (!url) {
      return;
    }
    this.editableRef().nativeElement.focus();
    document.execCommand('createLink', false, url);
    this.onChange(this.currentHtml());
  }

  /**
   * Insert plain text at the current caret position (used by the US-NTF-002
   * variable reference panel to drop a `{{placeholder}}` where the cursor is).
   * Falls back to appending when the editor has never been focused.
   */
  insertText(text: string): void {
    if (this.disabled()) {
      return;
    }
    const el = this.editableRef().nativeElement;
    el.focus();
    // execCommand('insertText') honours the live selection/caret; it is the only
    // zero-dependency way that keeps undo history intact across browsers.
    const inserted = document.execCommand('insertText', false, text);
    if (!inserted) {
      // jsdom / browsers that no-op insertText: append as a safe fallback.
      el.append(document.createTextNode(text));
    }
    this.onChange(this.currentHtml());
  }

  /** Re-read the active inline formats so the toolbar reflects the caret. */
  refreshActive(): void {
    this.active.set({
      bold: this.queryState('bold'),
      italic: this.queryState('italic'),
      underline: this.queryState('underline'),
    });
  }

  private queryState(command: string): boolean {
    try {
      return document.queryCommandState(command);
    } catch {
      return false;
    }
  }

  private currentHtml(): string {
    const html = this.editableRef().nativeElement.innerHTML.trim();
    // contenteditable leaves an empty <br> / <div><br></div> when cleared — treat
    // those as empty so `required` validation behaves intuitively.
    return html === '<br>' || html === '<div><br></div>' ? '' : html;
  }
}
