import {
  Component,
  ChangeDetectionStrategy,
  forwardRef,
  signal,
  input,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ControlValueAccessor,
  NG_VALUE_ACCESSOR,
} from '@angular/forms';
import {
  CareersService,
} from '../../../services/careers.service';
import {
  RESUME_ACCEPT_ATTR,
  RESUME_ACCEPTED_LABEL,
  RESUME_MAX_LABEL,
} from '../../../models/applicant.models';

/**
 * US-REC-002: Accessible drag-and-drop resume upload zone (FR-1 / AC-2 / NFR-2).
 *
 * A ControlValueAccessor wrapping a hidden <input type="file"> so the host
 * reactive form holds the selected `File | null`. The zone is keyboard-operable
 * (role="button", Enter/Space open the picker — NFR-2 WCAG AA) and shows the
 * accepted formats + max size (§8). Client-side type/size validation runs HERE on
 * pick/drop (AC-2) and surfaces an inline error; the parent form additionally
 * marks the control required.
 *
 * The actual upload + progress bar are owned by the application form (it needs the
 * vacancy id + the rest of the payload) — this component only selects/validates
 * the file and emits it through the form control.
 */
@Component({
  selector: 'app-resume-upload',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => ResumeUploadComponent),
      multi: true,
    },
  ],
  template: `
    <div
      class="dropzone"
      [class.dropzone--drag]="dragging()"
      [class.dropzone--error]="!!error()"
      [class.dropzone--has-file]="!!file()"
      role="button"
      tabindex="0"
      [attr.aria-label]="'Upload resume. Accepted formats ' + acceptedLabel + ', maximum ' + maxLabel"
      [attr.aria-invalid]="!!error()"
      [attr.aria-describedby]="error() ? 'resume-error' : 'resume-hint'"
      (click)="picker.click()"
      (keydown.enter)="picker.click(); $event.preventDefault()"
      (keydown.space)="picker.click(); $event.preventDefault()"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
    >
      <input
        #picker
        type="file"
        class="sr-only"
        [accept]="acceptAttr"
        [disabled]="disabled()"
        (change)="onPicked($event)"
        aria-hidden="true"
        tabindex="-1"
      />

      @if (file(); as f) {
        <div class="flex items-center gap-3">
          <svg class="h-8 w-8 shrink-0 text-indigo-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true">
            <path d="M14 3v4a1 1 0 0 0 1 1h4" />
            <path d="M5 3h9l5 5v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z" />
          </svg>
          <div class="min-w-0 flex-1 text-left">
            <p class="truncate text-sm font-medium text-neutral-800">{{ f.name }}</p>
            <p class="text-xs text-neutral-500">{{ humanSize(f.size) }}</p>
          </div>
          <button
            type="button"
            class="rounded-md px-2 py-1 text-xs font-medium text-neutral-500 transition hover:bg-neutral-100 hover:text-red-600"
            (click)="remove($event)"
            aria-label="Remove selected resume"
          >
            Remove
          </button>
        </div>
      } @else {
        <div class="flex flex-col items-center gap-2 text-center">
          <svg class="h-8 w-8 text-neutral-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true">
            <path d="M12 16V4" /><path d="m7 9 5-5 5 5" />
            <path d="M5 16v2a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-2" />
          </svg>
          <p class="text-sm font-medium text-neutral-700">
            <span class="text-indigo-600">Click to upload</span> or drag and drop
          </p>
          <p id="resume-hint" class="text-xs text-neutral-500">
            {{ acceptedLabel }} · up to {{ maxLabel }}
          </p>
        </div>
      }
    </div>

    @if (error(); as e) {
      <p id="resume-error" class="mt-1.5 text-xs font-medium text-red-600" role="alert">
        {{ e }}
      </p>
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .dropzone {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 100%;
        min-height: 7rem;
        padding: 1.25rem;
        border: 1.5px dashed #d4d4d4;
        border-radius: 0.75rem;
        background: #fafafa;
        cursor: pointer;
        transition: all 200ms ease;
      }
      .dropzone:hover {
        border-color: #a5b4fc;
        background: #f5f7ff;
      }
      .dropzone:focus-visible {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 3px #e0e7ff;
      }
      .dropzone--drag {
        border-color: #6366f1;
        background: #eef2ff;
      }
      .dropzone--has-file {
        border-style: solid;
        border-color: #e5e5e5;
        background: #fff;
        cursor: default;
      }
      .dropzone--error {
        border-color: #fca5a5;
        background: #fef2f2;
      }
      .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
      }
    `,
  ],
})
export class ResumeUploadComponent implements ControlValueAccessor {
  /** Optional override for the accepted-formats copy (defaults to PDF/DOCX/DOC). */
  readonly acceptedLabel = RESUME_ACCEPTED_LABEL;
  readonly maxLabel = RESUME_MAX_LABEL;
  readonly acceptAttr = RESUME_ACCEPT_ATTR;

  readonly disabled = input(false);

  readonly file = signal<File | null>(null);
  readonly dragging = signal(false);
  readonly error = signal<string | null>(null);

  private onChange: (value: File | null) => void = () => {};
  private onTouched: () => void = () => {};

  // ─── ControlValueAccessor ────────────────────────────────

  writeValue(value: File | null): void {
    this.file.set(value ?? null);
    if (!value) {
      this.error.set(null);
    }
  }

  registerOnChange(fn: (value: File | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  // ─── Drag & drop ─────────────────────────────────────────

  onDragOver(event: DragEvent): void {
    if (this.disabled()) return;
    event.preventDefault();
    this.dragging.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    if (this.disabled()) return;
    const dropped = event.dataTransfer?.files?.[0];
    if (dropped) {
      this.accept(dropped);
    }
  }

  // ─── Picker ──────────────────────────────────────────────

  onPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    const picked = input.files?.[0];
    if (picked) {
      this.accept(picked);
    }
    // Reset so re-selecting the same file fires change again.
    input.value = '';
  }

  remove(event: Event): void {
    event.stopPropagation();
    this.file.set(null);
    this.error.set(null);
    this.onChange(null);
    this.onTouched();
  }

  // ─── Validation (AC-2 / BR-4) ────────────────────────────

  private accept(candidate: File): void {
    this.onTouched();
    const verdict = CareersService.validateResume(candidate);
    if (verdict === 'too_large') {
      this.error.set(
        `That file is larger than ${this.maxLabel}. Please upload a smaller resume.`,
      );
      this.file.set(null);
      this.onChange(null);
      return;
    }
    if (verdict === 'bad_type') {
      this.error.set(
        `Unsupported file type. Please upload a ${this.acceptedLabel} file.`,
      );
      this.file.set(null);
      this.onChange(null);
      return;
    }
    this.error.set(null);
    this.file.set(candidate);
    this.onChange(candidate);
  }

  // ─── View helpers ────────────────────────────────────────

  humanSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    const kb = bytes / 1024;
    if (kb < 1024) return `${kb.toFixed(0)} KB`;
    return `${(kb / 1024).toFixed(1)} MB`;
  }
}
