import {
  Component,
  ChangeDetectionStrategy,
  signal,
  output,
  input,
  ElementRef,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ALLOWED_PHOTO_TYPES,
  MAX_PHOTO_SIZE_BYTES,
  MAX_PHOTO_SIZE_LABEL,
} from '../../models/employee.models';

/**
 * US-CHR-001 AC-4: Profile photo upload with drag-and-drop, avatar preview,
 * circular crop, MIME (JPEG/PNG/WebP) + 5 MB client-side checks.
 *
 * This is a presentational (dumb) component that emits selected files
 * and validation errors to the parent.
 */
@Component({
  selector: 'app-photo-upload',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="photo-upload-wrapper">
      <!-- Preview circle -->
      <div class="avatar-preview-container">
        @if (previewUrl()) {
          <img
            [src]="previewUrl()"
            alt="Profile photo preview"
            class="avatar-preview-image"
          />
          <button
            type="button"
            class="avatar-remove-btn"
            (click)="removePhoto()"
            aria-label="Remove photo"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
              <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
            </svg>
          </button>
        } @else {
          <div class="avatar-placeholder">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-10 h-10 text-neutral-300" aria-hidden="true">
              <path fill-rule="evenodd" d="M18.685 19.097A9.723 9.723 0 0 0 21.75 12c0-5.385-4.365-9.75-9.75-9.75S2.25 6.615 2.25 12a9.723 9.723 0 0 0 3.065 7.097A9.716 9.716 0 0 0 12 21.75a9.716 9.716 0 0 0 6.685-2.653Zm-12.54-1.285A7.486 7.486 0 0 1 12 15a7.486 7.486 0 0 1 5.855 2.812A8.224 8.224 0 0 1 12 20.25a8.224 8.224 0 0 1-5.855-2.438ZM15.75 9.75a3.75 3.75 0 1 1-7.5 0 3.75 3.75 0 0 1 7.5 0Z" clip-rule="evenodd"/>
            </svg>
          </div>
        }
      </div>

      <!-- Drop zone -->
      <div
        class="drop-zone"
        [class.drop-zone-active]="isDragOver()"
        [class.drop-zone-error]="!!validationError()"
        (dragover)="onDragOver($event)"
        (dragleave)="onDragLeave($event)"
        (drop)="onDrop($event)"
        (click)="fileInput.click()"
        (keydown.enter)="fileInput.click()"
        (keydown.space)="fileInput.click()"
        tabindex="0"
        role="button"
        [attr.aria-label]="previewUrl() ? 'Change profile photo' : 'Upload profile photo'"
        [attr.aria-describedby]="validationError() ? 'photo-error' : null"
      >
        <input
          #fileInput
          type="file"
          class="sr-only"
          [accept]="acceptTypes"
          (change)="onFileSelected($event)"
          aria-hidden="true"
        />
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
          class="w-6 h-6 text-neutral-400 mb-1" aria-hidden="true">
          <path d="M9.25 13.25a.75.75 0 0 0 1.5 0V4.636l2.955 3.129a.75.75 0 0 0 1.09-1.03l-4.25-4.5a.75.75 0 0 0-1.09 0l-4.25 4.5a.75.75 0 1 0 1.09 1.03L9.25 4.636v8.614Z"/>
          <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z"/>
        </svg>
        <span class="drop-zone-text">
          @if (previewUrl()) {
            Click or drag to replace
          } @else {
            Drag & drop or click to upload
          }
        </span>
        <span class="drop-zone-hint">
          JPEG, PNG, or WebP. Max {{ maxSizeLabel }}.
        </span>
      </div>

      <!-- Validation error -->
      @if (validationError()) {
        <p id="photo-error" class="photo-error" role="alert">
          {{ validationError() }}
        </p>
      }
    </div>
  `,
  styles: [`
    .photo-upload-wrapper {
      @apply flex flex-col items-center gap-3;
    }

    .avatar-preview-container {
      @apply relative w-24 h-24 rounded-full overflow-hidden
        border-2 border-neutral-200 bg-neutral-50 flex-shrink-0;
    }

    .avatar-preview-image {
      @apply w-full h-full object-cover;
    }

    .avatar-placeholder {
      @apply w-full h-full flex items-center justify-center;
    }

    .avatar-remove-btn {
      @apply absolute top-0 right-0 w-6 h-6 rounded-full bg-white
        shadow-md flex items-center justify-center text-neutral-500
        hover:text-red-600 hover:bg-red-50 transition-colors duration-150;
    }

    .drop-zone {
      @apply flex flex-col items-center justify-center w-full max-w-xs
        rounded-xl border-2 border-dashed border-neutral-200
        bg-neutral-50/50 px-4 py-4 cursor-pointer
        transition-all duration-200
        hover:border-brand-300 hover:bg-brand-50/30
        focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600;
    }

    .drop-zone-active {
      @apply border-brand-400 bg-brand-50/50;
    }

    .drop-zone-error {
      @apply border-red-300 bg-red-50/30;
    }

    .drop-zone-text {
      @apply text-sm font-medium text-neutral-600;
    }

    .drop-zone-hint {
      @apply text-xs text-neutral-400 mt-0.5;
    }

    .photo-error {
      @apply text-xs text-red-600 mt-1;
    }
  `],
})
export class PhotoUploadComponent {
  @ViewChild('fileInput') fileInput!: ElementRef<HTMLInputElement>;

  /** Current photo file passed from parent */
  readonly currentPhoto = input<File | null>(null);

  /** Emitted when a valid file is selected */
  readonly photoSelected = output<File>();

  /** Emitted when the photo is removed */
  readonly photoRemoved = output<void>();

  readonly previewUrl = signal<string | null>(null);
  readonly isDragOver = signal(false);
  readonly validationError = signal<string | null>(null);

  readonly acceptTypes = ALLOWED_PHOTO_TYPES.join(',');
  readonly maxSizeLabel = MAX_PHOTO_SIZE_LABEL;

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.processFile(files[0]);
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.processFile(input.files[0]);
      // Reset to allow re-selecting the same file
      input.value = '';
    }
  }

  removePhoto(): void {
    this.previewUrl.set(null);
    this.validationError.set(null);
    this.photoRemoved.emit();
  }

  private processFile(file: File): void {
    this.validationError.set(null);

    // MIME type check (FR-6)
    if (!ALLOWED_PHOTO_TYPES.includes(file.type)) {
      this.validationError.set(
        'Invalid file type. Only JPEG, PNG, and WebP are allowed.'
      );
      return;
    }

    // Size check (FR-6: max 5 MB)
    if (file.size > MAX_PHOTO_SIZE_BYTES) {
      this.validationError.set(
        `File is too large. Maximum size is ${MAX_PHOTO_SIZE_LABEL}.`
      );
      return;
    }

    // Create preview URL
    const reader = new FileReader();
    reader.onload = () => {
      this.previewUrl.set(reader.result as string);
    };
    reader.readAsDataURL(file);

    this.photoSelected.emit(file);
  }
}
