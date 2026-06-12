import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  OnInit,
  OnDestroy,
  ElementRef,
  ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpEventType, HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AuthService } from '@core/auth/auth.service';
import { DocumentService } from '../../services/document.service';
import {
  IEmployeeDocument,
  DocumentCategory,
  DOCUMENT_CATEGORIES,
  DOCUMENT_FILTER_TABS,
  validateDocumentFile,
  formatFileSize,
  getExpiryBadgeStatus,
  getMimeTypeIcon,
} from '../../models/document.models';

/**
 * US-CHR-008: Employee Document Management component.
 *
 * Displays documents in a categorized list within the employee profile
 * "Documents" tab (AC-1, FR-9). Supports upload (drag-and-drop + file picker),
 * download (signed URL), and delete (HR Officer only) with role-gating (FR-10, BR-1/2/3).
 *
 * Presentational child component embedded by EmployeeProfileComponent.
 */
@Component({
  selector: 'app-employee-documents',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('slideDown', [
      transition(':enter', [
        style({ opacity: 0, height: '0', overflow: 'hidden' }),
        animate('200ms ease-out', style({ opacity: 1, height: '*' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ opacity: 0, height: '0', overflow: 'hidden' })),
      ]),
    ]),
  ],
  template: `
    <!-- Category filter tabs (FR-9, UI/UX) -->
    <div class="filter-tabs mb-4">
      <!-- Desktop tabs -->
      <nav class="hidden md:flex gap-1 overflow-x-auto pb-1" aria-label="Document category filter">
        @for (tab of filterTabs; track tab.key) {
          <button
            type="button"
            class="tab-btn"
            [class.tab-btn-active]="activeFilter() === tab.key"
            (click)="activeFilter.set(tab.key)"
            [attr.aria-selected]="activeFilter() === tab.key"
            role="tab"
          >
            {{ tab.label }}
            @if (tab.key !== 'All') {
              <span class="tab-count">{{ getCategoryCount(tab.key) }}</span>
            }
          </button>
        }
      </nav>
      <!-- Mobile dropdown -->
      <div class="md:hidden">
        <label for="doc-category-select" class="sr-only">Filter by category</label>
        <select
          id="doc-category-select"
          class="input-notion select-input"
          [value]="activeFilter()"
          (change)="activeFilter.set($any($event.target).value)"
        >
          @for (tab of filterTabs; track tab.key) {
            <option [value]="tab.key">{{ tab.label }}</option>
          }
        </select>
      </div>
    </div>

    <!-- Upload zone (AC-1) — HR Officer only -->
    @if (canUpload()) {
      <div class="mb-4">
        @if (!showUploadForm()) {
          <button
            type="button"
            class="btn-primary"
            (click)="showUploadForm.set(true)"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
              <path d="M9.25 13.25a.75.75 0 0 0 1.5 0V4.636l2.955 3.129a.75.75 0 0 0 1.09-1.03l-4.25-4.5a.75.75 0 0 0-1.09 0l-4.25 4.5a.75.75 0 1 0 1.09 1.03L9.25 4.636v8.614Z"/>
              <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z"/>
            </svg>
            Upload Document
          </button>
        }

        @if (showUploadForm()) {
          <div @slideDown class="upload-card">
            <div class="flex items-center justify-between mb-3">
              <h4 class="text-sm font-semibold text-neutral-900">Upload Document</h4>
              <button
                type="button"
                class="close-btn"
                (click)="cancelUpload()"
                aria-label="Close upload form"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                  <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                </svg>
              </button>
            </div>

            <!-- Desktop: drag-and-drop zone; Mobile: file picker button -->
            @if (!selectedFile()) {
              <!-- Desktop drag-and-drop -->
              <div
                class="drop-zone hidden md:flex"
                [class.drop-zone-active]="isDragOver()"
                [class.drop-zone-error]="!!uploadError()"
                (dragover)="onDragOver($event)"
                (dragleave)="onDragLeave($event)"
                (drop)="onDrop($event)"
                (click)="fileInput.click()"
                (keydown.enter)="fileInput.click()"
                (keydown.space)="fileInput.click()"
                tabindex="0"
                role="button"
                aria-label="Drop files here or click to browse"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-8 h-8 text-neutral-300 mb-2" aria-hidden="true">
                  <path fill-rule="evenodd" d="M11.47 2.47a.75.75 0 0 1 1.06 0l4.5 4.5a.75.75 0 0 1-1.06 1.06l-3.22-3.22V16.5a.75.75 0 0 1-1.5 0V4.81L8.03 8.03a.75.75 0 0 1-1.06-1.06l4.5-4.5ZM3 15.75a.75.75 0 0 1 .75.75v2.25a1.5 1.5 0 0 0 1.5 1.5h13.5a1.5 1.5 0 0 0 1.5-1.5V16.5a.75.75 0 0 1 1.5 0v2.25a3 3 0 0 1-3 3H5.25a3 3 0 0 1-3-3V16.5a.75.75 0 0 1 .75-.75Z" clip-rule="evenodd"/>
                </svg>
                <p class="text-sm text-neutral-500">Drop files here or click to browse</p>
                <p class="text-xs text-neutral-400 mt-1">PDF, JPEG, PNG, DOCX, XLSX up to 10 MB</p>
              </div>

              <!-- Mobile: simple file picker button -->
              <div class="md:hidden">
                <button
                  type="button"
                  class="btn-secondary w-full"
                  (click)="fileInput.click()"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
                    <path d="M9.25 13.25a.75.75 0 0 0 1.5 0V4.636l2.955 3.129a.75.75 0 0 0 1.09-1.03l-4.25-4.5a.75.75 0 0 0-1.09 0l-4.25 4.5a.75.75 0 1 0 1.09 1.03L9.25 4.636v8.614Z"/>
                    <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z"/>
                  </svg>
                  Choose File
                </button>
                <p class="text-xs text-neutral-400 mt-2 text-center">PDF, JPEG, PNG, DOCX, XLSX up to 10 MB</p>
              </div>
            }

            <!-- Hidden file input -->
            <input
              #fileInput
              type="file"
              class="sr-only"
              accept=".pdf,.jpg,.jpeg,.png,.docx,.xlsx"
              (change)="onFileSelected($event)"
              aria-hidden="true"
            />

            <!-- Selected file + metadata form -->
            @if (selectedFile()) {
              <div @fadeIn>
                <div class="selected-file-row mb-3">
                  <div class="flex items-center gap-2 min-w-0">
                    <span class="file-icon" [attr.data-type]="getMimeIcon(selectedFile()!.type)">
                      {{ getFileIconLabel(selectedFile()!.type) }}
                    </span>
                    <div class="min-w-0">
                      <p class="text-sm font-medium text-neutral-900 truncate">{{ selectedFile()!.name }}</p>
                      <p class="text-xs text-neutral-400">{{ formatSize(selectedFile()!.size) }}</p>
                    </div>
                  </div>
                  <button
                    type="button"
                    class="remove-file-btn"
                    (click)="removeSelectedFile()"
                    aria-label="Remove selected file"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                      <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                    </svg>
                  </button>
                </div>

                <form [formGroup]="uploadForm" (ngSubmit)="submitUpload()">
                  <div class="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-3">
                    <div>
                      <label class="label-notion" for="doc-category">Category *</label>
                      <select id="doc-category" formControlName="category" class="input-notion select-input">
                        @for (cat of categories; track cat) {
                          <option [value]="cat">{{ cat }}</option>
                        }
                      </select>
                    </div>
                    <div>
                      <label class="label-notion" for="doc-description">Description</label>
                      <input
                        id="doc-description"
                        type="text"
                        formControlName="description"
                        class="input-notion"
                        placeholder="Optional description"
                      />
                    </div>
                    <div>
                      <label class="label-notion" for="doc-expiry">Expiry Date</label>
                      <input
                        id="doc-expiry"
                        type="date"
                        formControlName="expiryDate"
                        class="input-notion"
                      />
                    </div>
                  </div>

                  <!-- Upload progress bar (NFR-1) -->
                  @if (uploadProgress() !== null) {
                    <div class="progress-bar-container mb-3" role="progressbar"
                      [attr.aria-valuenow]="uploadProgress()"
                      aria-valuemin="0"
                      aria-valuemax="100"
                      [attr.aria-label]="'Upload progress: ' + uploadProgress() + '%'"
                    >
                      <div class="progress-bar-track">
                        <div
                          class="progress-bar-fill"
                          [style.width.%]="uploadProgress()"
                        ></div>
                      </div>
                      <span class="progress-bar-label">{{ uploadProgress() }}%</span>
                    </div>
                  }

                  <div class="flex items-center justify-end gap-3">
                    <button
                      type="button"
                      class="btn-secondary"
                      (click)="cancelUpload()"
                      [disabled]="isUploading()"
                    >
                      Cancel
                    </button>
                    <button
                      type="submit"
                      class="btn-primary"
                      [disabled]="isUploading() || uploadForm.invalid"
                    >
                      @if (isUploading()) {
                        <span class="btn-spinner"></span> Uploading...
                      } @else {
                        Upload
                      }
                    </button>
                  </div>
                </form>
              </div>
            }

            <!-- Upload validation error -->
            @if (uploadError()) {
              <p class="text-sm text-red-600 mt-2" role="alert" id="upload-error">
                {{ uploadError() }}
              </p>
            }
          </div>
        }
      </div>
    }

    <!-- Loading state -->
    @if (isLoading()) {
      <div class="space-y-3" aria-live="polite" aria-busy="true">
        @for (_ of [1, 2, 3]; track $index) {
          <div class="skeleton-doc-row">
            <div class="skeleton-line w-8 h-8 rounded"></div>
            <div class="flex-1 space-y-1.5">
              <div class="skeleton-line w-48 h-4"></div>
              <div class="skeleton-line w-32 h-3"></div>
            </div>
          </div>
        }
      </div>
    }

    <!-- Error state -->
    @if (loadError()) {
      <div class="text-center py-8">
        <p class="text-sm text-red-500 mb-3">{{ loadError() }}</p>
        <button type="button" class="btn-secondary" (click)="loadDocuments()">Retry</button>
      </div>
    }

    <!-- Document list (FR-9) -->
    @if (!isLoading() && !loadError()) {
      @if (filteredDocuments().length === 0) {
        <div class="text-center py-8" @fadeIn>
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
            class="w-10 h-10 mx-auto text-neutral-200 mb-3" aria-hidden="true">
            <path fill-rule="evenodd" d="M5.625 1.5c-1.036 0-1.875.84-1.875 1.875v17.25c0 1.035.84 1.875 1.875 1.875h12.75c1.035 0 1.875-.84 1.875-1.875V12.75A3.75 3.75 0 0 0 16.5 9h-1.875a1.875 1.875 0 0 1-1.875-1.875V5.25A3.75 3.75 0 0 0 9 1.5H5.625ZM7.5 15a.75.75 0 0 1 .75-.75h7.5a.75.75 0 0 1 0 1.5h-7.5A.75.75 0 0 1 7.5 15Zm.75 2.25a.75.75 0 0 0 0 1.5H12a.75.75 0 0 0 0-1.5H8.25Z" clip-rule="evenodd"/>
            <path d="M12.971 1.816A5.23 5.23 0 0 1 14.25 5.25v1.875c0 .207.168.375.375.375H16.5a5.23 5.23 0 0 1 3.434 1.279 9.768 9.768 0 0 0-6.963-6.963Z"/>
          </svg>
          <p class="text-sm text-neutral-400">
            @if (activeFilter() === 'All') {
              No documents uploaded yet.
            } @else {
              No {{ activeFilter() | lowercase }} documents found.
            }
          </p>
        </div>
      } @else {
        <!-- Desktop: table-like rows -->
        <div class="hidden md:block space-y-2" @fadeIn>
          @for (doc of filteredDocuments(); track doc.documentId) {
            <div class="doc-row" role="listitem">
              <!-- File icon -->
              <div class="doc-icon" [attr.data-type]="getMimeIcon(doc.mimeType)">
                @switch (getMimeIcon(doc.mimeType)) {
                  @case ('pdf') {
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-5 h-5 text-red-500" aria-hidden="true">
                      <path fill-rule="evenodd" d="M5.625 1.5c-1.036 0-1.875.84-1.875 1.875v17.25c0 1.035.84 1.875 1.875 1.875h12.75c1.035 0 1.875-.84 1.875-1.875V12.75A3.75 3.75 0 0 0 16.5 9h-1.875a1.875 1.875 0 0 1-1.875-1.875V5.25A3.75 3.75 0 0 0 9 1.5H5.625ZM7.5 15a.75.75 0 0 1 .75-.75h7.5a.75.75 0 0 1 0 1.5h-7.5A.75.75 0 0 1 7.5 15Zm.75 2.25a.75.75 0 0 0 0 1.5H12a.75.75 0 0 0 0-1.5H8.25Z" clip-rule="evenodd"/>
                      <path d="M12.971 1.816A5.23 5.23 0 0 1 14.25 5.25v1.875c0 .207.168.375.375.375H16.5a5.23 5.23 0 0 1 3.434 1.279 9.768 9.768 0 0 0-6.963-6.963Z"/>
                    </svg>
                  }
                  @case ('image') {
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-5 h-5 text-blue-500" aria-hidden="true">
                      <path fill-rule="evenodd" d="M1.5 6a2.25 2.25 0 0 1 2.25-2.25h16.5A2.25 2.25 0 0 1 22.5 6v12a2.25 2.25 0 0 1-2.25 2.25H3.75A2.25 2.25 0 0 1 1.5 18V6ZM3 16.06V18c0 .414.336.75.75.75h16.5A.75.75 0 0 0 21 18v-1.94l-2.69-2.689a1.5 1.5 0 0 0-2.12 0l-.88.879.97.97a.75.75 0 1 1-1.06 1.06l-5.16-5.159a1.5 1.5 0 0 0-2.12 0L3 16.061Zm10.125-7.81a1.125 1.125 0 1 1 2.25 0 1.125 1.125 0 0 1-2.25 0Z" clip-rule="evenodd"/>
                    </svg>
                  }
                  @case ('word') {
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-5 h-5 text-blue-700" aria-hidden="true">
                      <path fill-rule="evenodd" d="M5.625 1.5c-1.036 0-1.875.84-1.875 1.875v17.25c0 1.035.84 1.875 1.875 1.875h12.75c1.035 0 1.875-.84 1.875-1.875V12.75A3.75 3.75 0 0 0 16.5 9h-1.875a1.875 1.875 0 0 1-1.875-1.875V5.25A3.75 3.75 0 0 0 9 1.5H5.625ZM7.5 15a.75.75 0 0 1 .75-.75h7.5a.75.75 0 0 1 0 1.5h-7.5A.75.75 0 0 1 7.5 15Zm.75 2.25a.75.75 0 0 0 0 1.5H12a.75.75 0 0 0 0-1.5H8.25Z" clip-rule="evenodd"/>
                      <path d="M12.971 1.816A5.23 5.23 0 0 1 14.25 5.25v1.875c0 .207.168.375.375.375H16.5a5.23 5.23 0 0 1 3.434 1.279 9.768 9.768 0 0 0-6.963-6.963Z"/>
                    </svg>
                  }
                  @case ('excel') {
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-5 h-5 text-green-600" aria-hidden="true">
                      <path fill-rule="evenodd" d="M5.625 1.5c-1.036 0-1.875.84-1.875 1.875v17.25c0 1.035.84 1.875 1.875 1.875h12.75c1.035 0 1.875-.84 1.875-1.875V12.75A3.75 3.75 0 0 0 16.5 9h-1.875a1.875 1.875 0 0 1-1.875-1.875V5.25A3.75 3.75 0 0 0 9 1.5H5.625ZM7.5 15a.75.75 0 0 1 .75-.75h7.5a.75.75 0 0 1 0 1.5h-7.5A.75.75 0 0 1 7.5 15Zm.75 2.25a.75.75 0 0 0 0 1.5H12a.75.75 0 0 0 0-1.5H8.25Z" clip-rule="evenodd"/>
                      <path d="M12.971 1.816A5.23 5.23 0 0 1 14.25 5.25v1.875c0 .207.168.375.375.375H16.5a5.23 5.23 0 0 1 3.434 1.279 9.768 9.768 0 0 0-6.963-6.963Z"/>
                    </svg>
                  }
                  @default {
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-5 h-5 text-neutral-400" aria-hidden="true">
                      <path fill-rule="evenodd" d="M5.625 1.5c-1.036 0-1.875.84-1.875 1.875v17.25c0 1.035.84 1.875 1.875 1.875h12.75c1.035 0 1.875-.84 1.875-1.875V12.75A3.75 3.75 0 0 0 16.5 9h-1.875a1.875 1.875 0 0 1-1.875-1.875V5.25A3.75 3.75 0 0 0 9 1.5H5.625ZM7.5 15a.75.75 0 0 1 .75-.75h7.5a.75.75 0 0 1 0 1.5h-7.5A.75.75 0 0 1 7.5 15Zm.75 2.25a.75.75 0 0 0 0 1.5H12a.75.75 0 0 0 0-1.5H8.25Z" clip-rule="evenodd"/>
                      <path d="M12.971 1.816A5.23 5.23 0 0 1 14.25 5.25v1.875c0 .207.168.375.375.375H16.5a5.23 5.23 0 0 1 3.434 1.279 9.768 9.768 0 0 0-6.963-6.963Z"/>
                    </svg>
                  }
                }
              </div>

              <!-- File details -->
              <div class="flex-1 min-w-0">
                <p class="text-sm font-medium text-neutral-900 truncate">{{ doc.fileName }}</p>
                <div class="flex items-center gap-2 mt-0.5 flex-wrap">
                  <span class="category-tag">{{ doc.category }}</span>
                  <span class="text-xs text-neutral-400">{{ doc.createdAt | date:'mediumDate' }}</span>
                  <span class="text-xs text-neutral-400">{{ formatSize(doc.fileSizeBytes) }}</span>
                  @if (doc.uploadedByName) {
                    <span class="text-xs text-neutral-400">by {{ doc.uploadedByName }}</span>
                  }
                </div>
              </div>

              <!-- Expiry badge -->
              @if (doc.expiryDate) {
                <span
                  class="expiry-badge"
                  [class.expiry-green]="getExpiry(doc.expiryDate) === 'green'"
                  [class.expiry-amber]="getExpiry(doc.expiryDate) === 'amber'"
                  [class.expiry-red]="getExpiry(doc.expiryDate) === 'red'"
                  [attr.aria-label]="'Expires ' + (doc.expiryDate | date:'mediumDate')"
                >
                  {{ doc.expiryDate | date:'mediumDate' }}
                </span>
              }

              <!-- Actions -->
              <div class="flex items-center gap-1 ml-2">
                <!-- Download (AC-4) -->
                <button
                  type="button"
                  class="action-btn"
                  (click)="downloadDocument(doc)"
                  [disabled]="downloadingId() === doc.documentId"
                  [attr.aria-label]="'Download ' + doc.fileName"
                >
                  @if (downloadingId() === doc.documentId) {
                    <span class="action-spinner"></span>
                  } @else {
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                      <path d="M10.75 2.75a.75.75 0 0 0-1.5 0v8.614L6.295 8.235a.75.75 0 1 0-1.09 1.03l4.25 4.5a.75.75 0 0 0 1.09 0l4.25-4.5a.75.75 0 0 0-1.09-1.03l-2.955 3.129V2.75Z"/>
                      <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z"/>
                    </svg>
                  }
                </button>
                <!-- Delete (FR-7) — HR Officer only -->
                @if (canDelete()) {
                  <button
                    type="button"
                    class="action-btn action-btn-danger"
                    (click)="confirmDelete(doc)"
                    [attr.aria-label]="'Delete ' + doc.fileName"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                      <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                    </svg>
                  </button>
                }
              </div>
            </div>
          }
        </div>

        <!-- Mobile: card stack (NFR-5) -->
        <div class="md:hidden space-y-3" @fadeIn>
          @for (doc of filteredDocuments(); track doc.documentId) {
            <div class="doc-card-mobile" role="listitem">
              <div class="flex items-start gap-3 mb-2">
                <div class="doc-icon" [attr.data-type]="getMimeIcon(doc.mimeType)">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-5 h-5" aria-hidden="true"
                    [class.text-red-500]="getMimeIcon(doc.mimeType) === 'pdf'"
                    [class.text-blue-500]="getMimeIcon(doc.mimeType) === 'image'"
                    [class.text-blue-700]="getMimeIcon(doc.mimeType) === 'word'"
                    [class.text-green-600]="getMimeIcon(doc.mimeType) === 'excel'"
                    [class.text-neutral-400]="getMimeIcon(doc.mimeType) === 'file'"
                  >
                    <path fill-rule="evenodd" d="M5.625 1.5c-1.036 0-1.875.84-1.875 1.875v17.25c0 1.035.84 1.875 1.875 1.875h12.75c1.035 0 1.875-.84 1.875-1.875V12.75A3.75 3.75 0 0 0 16.5 9h-1.875a1.875 1.875 0 0 1-1.875-1.875V5.25A3.75 3.75 0 0 0 9 1.5H5.625ZM7.5 15a.75.75 0 0 1 .75-.75h7.5a.75.75 0 0 1 0 1.5h-7.5A.75.75 0 0 1 7.5 15Zm.75 2.25a.75.75 0 0 0 0 1.5H12a.75.75 0 0 0 0-1.5H8.25Z" clip-rule="evenodd"/>
                    <path d="M12.971 1.816A5.23 5.23 0 0 1 14.25 5.25v1.875c0 .207.168.375.375.375H16.5a5.23 5.23 0 0 1 3.434 1.279 9.768 9.768 0 0 0-6.963-6.963Z"/>
                  </svg>
                </div>
                <div class="flex-1 min-w-0">
                  <p class="text-sm font-medium text-neutral-900 truncate">{{ doc.fileName }}</p>
                  <span class="category-tag mt-1">{{ doc.category }}</span>
                </div>
              </div>
              <div class="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-neutral-400 mb-2">
                <span>{{ doc.createdAt | date:'mediumDate' }}</span>
                <span>{{ formatSize(doc.fileSizeBytes) }}</span>
                @if (doc.uploadedByName) {
                  <span>by {{ doc.uploadedByName }}</span>
                }
                @if (doc.expiryDate) {
                  <span
                    class="expiry-badge"
                    [class.expiry-green]="getExpiry(doc.expiryDate) === 'green'"
                    [class.expiry-amber]="getExpiry(doc.expiryDate) === 'amber'"
                    [class.expiry-red]="getExpiry(doc.expiryDate) === 'red'"
                  >
                    Exp: {{ doc.expiryDate | date:'mediumDate' }}
                  </span>
                }
              </div>
              <div class="flex items-center gap-2">
                <button
                  type="button"
                  class="btn-secondary flex-1 text-xs py-1.5"
                  (click)="downloadDocument(doc)"
                  [disabled]="downloadingId() === doc.documentId"
                >
                  @if (downloadingId() === doc.documentId) {
                    <span class="action-spinner mr-1"></span>
                  }
                  Download
                </button>
                @if (canDelete()) {
                  <button
                    type="button"
                    class="btn-danger-outline flex-1 text-xs py-1.5"
                    (click)="confirmDelete(doc)"
                  >
                    Delete
                  </button>
                }
              </div>
            </div>
          }
        </div>
      }
    }

    <!-- Delete confirmation modal (FR-7) -->
    @if (deleteTarget()) {
      <div
        class="modal-overlay"
        (click)="cancelDelete()"
        (keydown.escape)="cancelDelete()"
        role="dialog"
        aria-modal="true"
        aria-labelledby="delete-modal-title"
      >
        <div class="modal-card" (click)="$event.stopPropagation()">
          <h3 id="delete-modal-title" class="text-base font-semibold text-neutral-900 mb-2">
            Delete Document
          </h3>
          <p class="text-sm text-neutral-600 mb-4">
            Are you sure you want to delete <strong>{{ deleteTarget()!.fileName }}</strong>?
            This action cannot be undone.
          </p>
          <div class="flex items-center justify-end gap-3">
            <button
              type="button"
              class="btn-secondary"
              (click)="cancelDelete()"
              [disabled]="isDeleting()"
            >
              Cancel
            </button>
            <button
              type="button"
              class="btn-danger"
              (click)="executeDelete()"
              [disabled]="isDeleting()"
            >
              @if (isDeleting()) {
                <span class="btn-spinner"></span> Deleting...
              } @else {
                Delete
              }
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }

    /* ─── Filter tabs ──────────────────────── */
    .tab-btn {
      @apply px-3 py-2 text-sm font-medium text-neutral-500 rounded-lg
        transition-colors duration-150 whitespace-nowrap inline-flex items-center gap-1.5
        hover:text-neutral-700 hover:bg-neutral-50;
    }
    .tab-btn-active {
      @apply text-brand-700 bg-brand-50;
    }
    .tab-count {
      @apply text-xs bg-neutral-100 text-neutral-500 rounded-full px-1.5 py-0;
    }
    .tab-btn-active .tab-count {
      @apply bg-brand-100 text-brand-700;
    }
    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }

    /* ─── Upload card ──────────────────────── */
    .upload-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-5;
    }
    .close-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors duration-150;
    }

    /* ─── Drop zone ────────────────────────── */
    .drop-zone {
      @apply flex-col items-center justify-center rounded-xl border-2 border-dashed
        border-neutral-200 bg-neutral-50/50 p-8 cursor-pointer
        transition-all duration-200 hover:border-brand-300 hover:bg-brand-50/30;
    }
    .drop-zone-active {
      @apply border-brand-400 bg-brand-50/50;
    }
    .drop-zone-error {
      @apply border-red-300 bg-red-50/30;
    }

    /* ─── Selected file ────────────────────── */
    .selected-file-row {
      @apply flex items-center justify-between rounded-lg bg-neutral-50 border border-neutral-100 p-3;
    }
    .file-icon {
      @apply w-8 h-8 rounded-lg bg-neutral-100 flex items-center justify-center
        text-xs font-semibold text-neutral-500 uppercase flex-shrink-0;
    }
    .remove-file-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-red-600 hover:bg-red-50
        transition-colors duration-150 flex-shrink-0;
    }

    /* ─── Progress bar (NFR-1) ─────────────── */
    .progress-bar-container {
      @apply flex items-center gap-3;
    }
    .progress-bar-track {
      @apply flex-1 h-2 rounded-full bg-neutral-100 overflow-hidden;
    }
    .progress-bar-fill {
      @apply h-full rounded-full bg-brand-500 transition-all duration-300;
    }
    .progress-bar-label {
      @apply text-xs font-medium text-neutral-600 w-10 text-right;
    }

    /* ─── Document row (desktop) ───────────── */
    .doc-row {
      @apply flex items-center gap-3 rounded-xl bg-white border border-neutral-100
        shadow-notion px-4 py-3 transition-all duration-150 hover:shadow-notion-md;
    }
    .doc-icon {
      @apply w-9 h-9 rounded-lg bg-neutral-50 flex items-center justify-center flex-shrink-0;
    }

    /* ─── Document card (mobile) ───────────── */
    .doc-card-mobile {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-4;
    }

    /* ─── Category tag ─────────────────────── */
    .category-tag {
      @apply inline-block text-xs font-medium px-2 py-0.5 rounded-full
        bg-neutral-100 text-neutral-600;
    }

    /* ─── Expiry badges ────────────────────── */
    .expiry-badge {
      @apply text-xs font-medium px-2 py-0.5 rounded-full;
    }
    .expiry-green {
      @apply bg-green-50 text-green-700;
    }
    .expiry-amber {
      @apply bg-amber-50 text-amber-700;
    }
    .expiry-red {
      @apply bg-red-50 text-red-700;
    }

    /* ─── Action buttons ───────────────────── */
    .action-btn {
      @apply w-8 h-8 rounded-lg flex items-center justify-center
        text-neutral-400 hover:text-neutral-700 hover:bg-neutral-100
        transition-colors duration-150 disabled:opacity-50;
    }
    .action-btn-danger {
      @apply hover:text-red-600 hover:bg-red-50;
    }
    .action-spinner {
      @apply inline-block w-3.5 h-3.5 border-2 border-neutral-300 border-t-neutral-600 rounded-full;
      animation: spin 0.6s linear infinite;
    }

    /* ─── Buttons ──────────────────────────── */
    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }
    .btn-danger {
      @apply inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-danger-outline {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-red-600 shadow-sm ring-1 ring-inset ring-red-200
        transition-all duration-200 hover:bg-red-50;
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    /* ─── Modal ────────────────────────────── */
    .modal-overlay {
      @apply fixed inset-0 z-50 flex items-center justify-center
        bg-black/30 backdrop-blur-sm;
    }
    .modal-card {
      @apply bg-white rounded-2xl shadow-notion-lg p-6 w-full max-w-md mx-4;
    }

    /* ─── Skeleton ─────────────────────────── */
    .skeleton-doc-row {
      @apply flex items-center gap-3 rounded-xl bg-white border border-neutral-100 px-4 py-3;
    }
    .skeleton-line {
      @apply rounded bg-neutral-200;
      animation: shimmer 1.5s ease-in-out infinite;
    }
    @keyframes shimmer {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.4; }
    }
    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `],
})
export class EmployeeDocumentsComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly toastr = inject(ToastrService);
  private readonly documentService = inject(DocumentService);
  private readonly authService = inject(AuthService);

  private readonly destroy$ = new Subject<void>();

  @ViewChild('fileInput') fileInputRef!: ElementRef<HTMLInputElement>;

  // ─── Inputs ─────────────────────────────────────────────────
  /** Employee ID — passed by the parent profile component */
  readonly employeeId = input.required<string>();

  // ─── Constants ──────────────────────────────────────────────
  readonly filterTabs = DOCUMENT_FILTER_TABS;
  readonly categories = DOCUMENT_CATEGORIES;

  // ─── Signals ────────────────────────────────────────────────
  readonly documents = signal<IEmployeeDocument[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal<string | null>(null);

  // Upload state
  readonly showUploadForm = signal(false);
  readonly selectedFile = signal<File | null>(null);
  readonly uploadError = signal<string | null>(null);
  readonly uploadProgress = signal<number | null>(null);
  readonly isUploading = signal(false);
  readonly isDragOver = signal(false);

  // Download state
  readonly downloadingId = signal<string | null>(null);

  // Delete state
  readonly deleteTarget = signal<IEmployeeDocument | null>(null);
  readonly isDeleting = signal(false);

  // Filter state
  readonly activeFilter = signal<DocumentCategory | 'All'>('All');

  // ─── Computed ───────────────────────────────────────────────

  /** Role-based: HR Officer / Tenant Admin can upload */
  readonly canUpload = computed(() => {
    return this.authService.hasRole('HR Officer') || this.authService.hasRole('Tenant Admin');
  });

  /** Role-based: HR Officer / Tenant Admin can delete */
  readonly canDelete = computed(() => {
    return this.authService.hasRole('HR Officer') || this.authService.hasRole('Tenant Admin');
  });

  /** Filtered documents by active category tab */
  readonly filteredDocuments = computed(() => {
    const filter = this.activeFilter();
    const docs = this.documents();
    if (filter === 'All') return docs;
    return docs.filter((d) => d.category === filter);
  });

  // ─── Upload form ────────────────────────────────────────────
  uploadForm: FormGroup = this.fb.group({
    category: ['Contract' as DocumentCategory, [Validators.required]],
    description: [''],
    expiryDate: [null as string | null],
  });

  // ─── Lifecycle ──────────────────────────────────────────────

  ngOnInit(): void {
    this.loadDocuments();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Public methods ─────────────────────────────────────────

  loadDocuments(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.documentService
      .getDocuments(this.employeeId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (docs) => {
          this.documents.set(docs);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.loadError.set('Failed to load documents. Please try again.');
        },
      });
  }

  // ─── File selection / drag-and-drop ─────────────────────────

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

    const file = event.dataTransfer?.files?.[0];
    if (file) {
      this.selectFile(file);
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) {
      this.selectFile(file);
    }
    // Reset input so re-selecting the same file triggers change
    input.value = '';
  }

  removeSelectedFile(): void {
    this.selectedFile.set(null);
    this.uploadError.set(null);
    this.uploadProgress.set(null);
  }

  cancelUpload(): void {
    this.showUploadForm.set(false);
    this.selectedFile.set(null);
    this.uploadError.set(null);
    this.uploadProgress.set(null);
    this.isUploading.set(false);
    this.uploadForm.reset({ category: 'Contract', description: '', expiryDate: null });
  }

  // ─── Upload submission (AC-1, AC-2) ─────────────────────────

  submitUpload(): void {
    const file = this.selectedFile();
    if (!file || this.uploadForm.invalid) return;

    this.isUploading.set(true);
    this.uploadError.set(null);
    this.uploadProgress.set(0);

    const formValue = this.uploadForm.value;
    this.documentService
      .uploadDocument(this.employeeId(), file, {
        category: formValue.category,
        description: formValue.description || null,
        expiryDate: formValue.expiryDate || null,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress) {
            const pct = event.total
              ? Math.round((event.loaded / event.total) * 100)
              : 0;
            this.uploadProgress.set(pct);
          } else if (event.type === HttpEventType.Response) {
            this.isUploading.set(false);
            this.uploadProgress.set(100);
            this.toastr.success('Document uploaded successfully.');
            this.cancelUpload();
            this.loadDocuments();
          }
        },
        error: (err: HttpErrorResponse) => {
          this.isUploading.set(false);
          this.uploadProgress.set(null);
          const message = err.error?.message ?? 'Upload failed. Please try again.';
          this.uploadError.set(message);
          this.toastr.error(message);
        },
      });
  }

  // ─── Download (AC-4) ────────────────────────────────────────

  downloadDocument(doc: IEmployeeDocument): void {
    this.downloadingId.set(doc.documentId);

    this.documentService
      .getDownloadUrl(this.employeeId(), doc.documentId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.downloadingId.set(null);
          // Open signed URL in a hidden link to trigger download
          const a = document.createElement('a');
          a.href = response.downloadUrl;
          a.download = doc.fileName;
          a.rel = 'noopener';
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
        },
        error: (err: HttpErrorResponse) => {
          this.downloadingId.set(null);
          if (err.status === 403) {
            this.toastr.error('You do not have permission to download this document.');
          } else {
            this.toastr.error('Failed to download document. Please try again.');
          }
        },
      });
  }

  // ─── Delete (FR-7) ──────────────────────────────────────────

  confirmDelete(doc: IEmployeeDocument): void {
    this.deleteTarget.set(doc);
  }

  cancelDelete(): void {
    this.deleteTarget.set(null);
  }

  executeDelete(): void {
    const doc = this.deleteTarget();
    if (!doc) return;

    this.isDeleting.set(true);

    this.documentService
      .deleteDocument(this.employeeId(), doc.documentId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.isDeleting.set(false);
          this.deleteTarget.set(null);
          this.toastr.success('Document deleted successfully.');
          // Remove from local list without re-fetching
          this.documents.update((docs) =>
            docs.filter((d) => d.documentId !== doc.documentId)
          );
        },
        error: () => {
          this.isDeleting.set(false);
          this.toastr.error('Failed to delete document. Please try again.');
        },
      });
  }

  // ─── Template helpers ───────────────────────────────────────

  getCategoryCount(category: string): number {
    return this.documents().filter((d) => d.category === category).length;
  }

  formatSize(bytes: number): string {
    return formatFileSize(bytes);
  }

  getExpiry(expiryDate: string | null): 'green' | 'amber' | 'red' | null {
    return getExpiryBadgeStatus(expiryDate);
  }

  getMimeIcon(mimeType: string): string {
    return getMimeTypeIcon(mimeType);
  }

  getFileIconLabel(mimeType: string): string {
    const icon = getMimeTypeIcon(mimeType);
    switch (icon) {
      case 'pdf': return 'PDF';
      case 'image': return 'IMG';
      case 'word': return 'DOC';
      case 'excel': return 'XLS';
      default: return 'FILE';
    }
  }

  // ─── Private ────────────────────────────────────────────────

  private selectFile(file: File): void {
    const error = validateDocumentFile(file);
    if (error) {
      this.uploadError.set(error);
      this.selectedFile.set(null);
      return;
    }
    this.uploadError.set(null);
    this.selectedFile.set(file);
  }
}
