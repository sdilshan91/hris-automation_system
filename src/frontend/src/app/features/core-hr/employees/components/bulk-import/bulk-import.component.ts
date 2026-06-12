import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import {
  trigger,
  transition,
  style,
  animate,
  query,
  group,
} from '@angular/animations';
import { HttpEventType, HttpErrorResponse } from '@angular/common/http';
import { finalize } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { BulkImportService } from '../../services/bulk-import.service';
import {
  ImportTemplateFormat,
  IImportResult,
  IImportRowError,
  IImportJobStatus,
  ImportResponse,
  isImportResult,
  isImportJobRef,
  isPlanLimitWarning,
  IPlanLimitWarning,
  getImportOutcome,
  ImportOutcome,
  ALLOWED_IMPORT_EXTENSIONS,
  MAX_IMPORT_FILE_SIZE_BYTES,
  MAX_IMPORT_FILE_SIZE_LABEL,
  TEMPLATE_COLUMNS,
  ITemplateColumn,
} from '../../models/bulk-import.models';

/**
 * US-CHR-010: Bulk Employee Import wizard.
 *
 * 3-step card wizard: Download Template -> Upload File -> Review Results.
 * - Step 1: Download CSV/Excel template + collapsible column guide (AC-1).
 * - Step 2: Drag-and-drop + file picker upload with client-side validation (AC-2, BR-7).
 * - Step 3: Summary banner + error table + error report download (AC-3, FR-8).
 * - Plan-limit pre-check (AC-5) via modal warning.
 * - Async import polling for large files (AC-4, FR-7).
 */
@Component({
  selector: 'app-bulk-import',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('slideStep', [
      transition(':increment', [
        group([
          query(':enter', [
            style({ transform: 'translateX(100%)', opacity: 0 }),
            animate('300ms ease-out', style({ transform: 'translateX(0)', opacity: 1 })),
          ], { optional: true }),
          query(':leave', [
            animate('300ms ease-out', style({ transform: 'translateX(-100%)', opacity: 0 })),
          ], { optional: true }),
        ]),
      ]),
      transition(':decrement', [
        group([
          query(':enter', [
            style({ transform: 'translateX(-100%)', opacity: 0 }),
            animate('300ms ease-out', style({ transform: 'translateX(0)', opacity: 1 })),
          ], { optional: true }),
          query(':leave', [
            animate('300ms ease-out', style({ transform: 'translateX(100%)', opacity: 0 })),
          ], { optional: true }),
        ]),
      ]),
    ]),
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container max-w-3xl mx-auto px-4 sm:px-6 py-8">
      <!-- Back link -->
      <a
        routerLink="/employees"
        class="inline-flex items-center text-sm text-neutral-500 hover:text-neutral-700
               transition-colors duration-200 mb-6"
        aria-label="Back to employee directory"
      >
        <svg class="w-4 h-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7"/>
        </svg>
        Back to Directory
      </a>

      <!-- Header -->
      <div class="mb-8">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
          Bulk Employee Import
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          Import multiple employee records from a CSV or Excel file.
        </p>
      </div>

      <!-- Step indicator -->
      <div class="flex items-center mb-8" role="list" aria-label="Import steps">
        @for (step of steps; track step.index) {
          <div
            class="flex items-center"
            [class.flex-1]="step.index < 2"
            role="listitem"
            [attr.aria-current]="step.index === currentStep() ? 'step' : null"
          >
            <div class="flex items-center">
              <div
                class="w-8 h-8 rounded-full flex items-center justify-center text-sm font-medium
                       transition-colors duration-200"
                [class.bg-neutral-900]="step.index <= currentStep()"
                [class.text-white]="step.index <= currentStep()"
                [class.bg-neutral-100]="step.index > currentStep()"
                [class.text-neutral-400]="step.index > currentStep()"
              >
                @if (step.index < currentStep()) {
                  <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"/>
                  </svg>
                } @else {
                  {{ step.index + 1 }}
                }
              </div>
              <span
                class="ml-2 text-sm font-medium hidden sm:inline transition-colors duration-200"
                [class.text-neutral-900]="step.index <= currentStep()"
                [class.text-neutral-400]="step.index > currentStep()"
              >
                {{ step.label }}
              </span>
            </div>
            @if (step.index < 2) {
              <div
                class="flex-1 h-px mx-4 transition-colors duration-200"
                [class.bg-neutral-900]="step.index < currentStep()"
                [class.bg-neutral-200]="step.index >= currentStep()"
              ></div>
            }
          </div>
        }
      </div>

      <!-- Step content card -->
      <div
        class="bg-white rounded-xl shadow-sm border border-neutral-100 overflow-hidden relative"
      >
        <div class="overflow-hidden" [@slideStep]="currentStep()">
          <!-- Step 1: Download Template -->
          @if (currentStep() === 0) {
            <div class="p-6 sm:p-8" @fadeIn>
              <h2 class="text-lg font-semibold text-neutral-900 mb-2">
                Download Template
              </h2>
              <p class="text-sm text-neutral-500 mb-6">
                Download the import template, fill it with employee data, then proceed to upload.
              </p>

              <div class="flex flex-col sm:flex-row gap-3 mb-6">
                <button
                  type="button"
                  class="inline-flex items-center justify-center px-4 py-2.5 rounded-lg
                         border border-neutral-200 text-sm font-medium text-neutral-700
                         hover:bg-neutral-50 transition-colors duration-200
                         focus:outline-none focus:ring-2 focus:ring-neutral-300"
                  [disabled]="isDownloadingTemplate()"
                  (click)="downloadTemplate('csv')"
                >
                  <svg class="w-4 h-4 mr-2 text-neutral-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                          d="M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
                  </svg>
                  Download CSV Template
                </button>
                <button
                  type="button"
                  class="inline-flex items-center justify-center px-4 py-2.5 rounded-lg
                         border border-neutral-200 text-sm font-medium text-neutral-700
                         hover:bg-neutral-50 transition-colors duration-200
                         focus:outline-none focus:ring-2 focus:ring-neutral-300"
                  [disabled]="isDownloadingTemplate()"
                  (click)="downloadTemplate('xlsx')"
                >
                  <svg class="w-4 h-4 mr-2 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                          d="M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
                  </svg>
                  Download Excel Template
                </button>
              </div>

              <!-- Collapsible column guide -->
              <div class="border border-neutral-100 rounded-lg">
                <button
                  type="button"
                  class="w-full flex items-center justify-between px-4 py-3 text-sm font-medium
                         text-neutral-700 hover:bg-neutral-50 transition-colors duration-200
                         focus:outline-none focus:ring-2 focus:ring-inset focus:ring-neutral-300"
                  (click)="showColumnGuide.set(!showColumnGuide())"
                  [attr.aria-expanded]="showColumnGuide()"
                  aria-controls="column-guide"
                >
                  <span>File Format &amp; Column Guide</span>
                  <svg
                    class="w-4 h-4 transition-transform duration-200"
                    [class.rotate-180]="showColumnGuide()"
                    fill="none" stroke="currentColor" viewBox="0 0 24 24"
                  >
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"/>
                  </svg>
                </button>
                @if (showColumnGuide()) {
                  <div id="column-guide" class="px-4 pb-4" @fadeIn>
                    <div class="overflow-x-auto">
                      <table class="w-full text-sm" role="table">
                        <thead>
                          <tr class="border-b border-neutral-100">
                            <th class="text-left py-2 pr-4 font-medium text-neutral-500">Column</th>
                            <th class="text-left py-2 pr-4 font-medium text-neutral-500">Required</th>
                            <th class="text-left py-2 font-medium text-neutral-500">Validation</th>
                          </tr>
                        </thead>
                        <tbody>
                          @for (col of templateColumns; track col.name) {
                            <tr class="border-b border-neutral-50">
                              <td class="py-2 pr-4 font-mono text-xs text-neutral-800">{{ col.name }}</td>
                              <td class="py-2 pr-4">
                                @if (col.required) {
                                  <span class="text-xs font-medium text-red-600">Yes</span>
                                } @else {
                                  <span class="text-xs text-neutral-400">No</span>
                                }
                              </td>
                              <td class="py-2 text-xs text-neutral-500">{{ col.validation }}</td>
                            </tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  </div>
                }
              </div>

              <!-- Next button -->
              <div class="flex justify-end mt-6">
                <button
                  type="button"
                  class="px-5 py-2.5 rounded-lg bg-neutral-900 text-white text-sm font-medium
                         hover:bg-neutral-800 transition-colors duration-200
                         focus:outline-none focus:ring-2 focus:ring-neutral-400 focus:ring-offset-2"
                  (click)="goToStep(1)"
                >
                  Next: Upload File
                </button>
              </div>
            </div>
          }

          <!-- Step 2: Upload File -->
          @if (currentStep() === 1) {
            <div class="p-6 sm:p-8" @fadeIn>
              <h2 class="text-lg font-semibold text-neutral-900 mb-2">
                Upload File
              </h2>
              <p class="text-sm text-neutral-500 mb-6">
                Upload your filled CSV or Excel file. Maximum file size: {{ maxFileSizeLabel }}.
              </p>

              <!-- Plan-limit warning (AC-5) -->
              @if (planLimitWarning()) {
                <div
                  class="mb-6 p-4 rounded-lg bg-amber-50 border border-amber-200"
                  role="alert"
                  @fadeIn
                >
                  <div class="flex items-start gap-3">
                    <svg class="w-5 h-5 text-amber-600 mt-0.5 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                            d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"/>
                    </svg>
                    <div>
                      <p class="text-sm font-medium text-amber-800">
                        {{ planLimitWarning()!.message }}
                      </p>
                      <div class="mt-3 flex flex-col sm:flex-row gap-2">
                        <button
                          type="button"
                          class="px-4 py-2 rounded-lg bg-amber-600 text-white text-sm font-medium
                                 hover:bg-amber-700 transition-colors duration-200
                                 focus:outline-none focus:ring-2 focus:ring-amber-400"
                          (click)="importUpToLimit()"
                        >
                          Import {{ planLimitWarning()!.importableCount }} records
                        </button>
                        <button
                          type="button"
                          class="px-4 py-2 rounded-lg border border-neutral-200 text-sm font-medium
                                 text-neutral-700 hover:bg-neutral-50 transition-colors duration-200
                                 focus:outline-none focus:ring-2 focus:ring-neutral-300"
                          (click)="cancelPlanLimit()"
                        >
                          Cancel
                        </button>
                      </div>
                    </div>
                  </div>
                </div>
              }

              <!-- Drop zone (desktop) / File picker (mobile) -->
              <div
                class="relative border-2 border-dashed rounded-xl p-8 text-center
                       transition-colors duration-200 cursor-pointer"
                [class.border-neutral-200]="!isDragOver()"
                [class.bg-neutral-50]="!isDragOver()"
                [class.border-neutral-400]="isDragOver()"
                [class.bg-neutral-100]="isDragOver()"
                (dragover)="onDragOver($event)"
                (dragleave)="onDragLeave($event)"
                (drop)="onDrop($event)"
                (click)="fileInput.click()"
                (keydown.enter)="fileInput.click()"
                (keydown.space)="fileInput.click(); $event.preventDefault()"
                role="button"
                tabindex="0"
                [attr.aria-label]="selectedFile() ? 'Change file. Current file: ' + selectedFile()!.name : 'Choose a file to upload'"
              >
                <input
                  #fileInput
                  type="file"
                  class="sr-only"
                  accept=".csv,.xlsx"
                  (change)="onFileSelected($event)"
                  aria-hidden="true"
                  tabindex="-1"
                />

                @if (!selectedFile()) {
                  <svg class="mx-auto w-10 h-10 text-neutral-300 mb-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5"
                          d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"/>
                  </svg>
                  <p class="text-sm text-neutral-600 mb-1">
                    <span class="hidden sm:inline">Drag and drop your file here, or </span>
                    <span class="font-medium text-neutral-900">browse</span>
                  </p>
                  <p class="text-xs text-neutral-400">
                    Accepts .csv and .xlsx files up to {{ maxFileSizeLabel }}
                  </p>
                } @else {
                  <div class="flex items-center justify-center gap-3">
                    <svg class="w-8 h-8 text-neutral-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5"
                            d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
                    </svg>
                    <div class="text-left">
                      <p class="text-sm font-medium text-neutral-900">{{ selectedFile()!.name }}</p>
                      <p class="text-xs text-neutral-400">{{ formatFileSize(selectedFile()!.size) }}</p>
                    </div>
                    <button
                      type="button"
                      class="ml-2 p-1 rounded hover:bg-neutral-200 transition-colors"
                      (click)="clearFile($event)"
                      aria-label="Remove selected file"
                    >
                      <svg class="w-4 h-4 text-neutral-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/>
                      </svg>
                    </button>
                  </div>
                }
              </div>

              <!-- Validation error -->
              @if (fileValidationError()) {
                <p class="mt-2 text-sm text-red-600" role="alert">
                  {{ fileValidationError() }}
                </p>
              }

              <!-- Upload progress -->
              @if (uploadProgress() !== null) {
                <div class="mt-4" @fadeIn>
                  <div class="flex items-center justify-between text-xs text-neutral-500 mb-1">
                    <span>Uploading...</span>
                    <span>{{ uploadProgress() }}%</span>
                  </div>
                  <div class="w-full bg-neutral-100 rounded-full h-2">
                    <div
                      class="bg-neutral-900 h-2 rounded-full transition-all duration-300"
                      [style.width.%]="uploadProgress()"
                      role="progressbar"
                      [attr.aria-valuenow]="uploadProgress()"
                      aria-valuemin="0"
                      aria-valuemax="100"
                    ></div>
                  </div>
                </div>
              }

              <!-- Async job progress (AC-4) -->
              @if (asyncJobStatus()) {
                <div class="mt-4 p-4 rounded-lg bg-blue-50 border border-blue-100" @fadeIn>
                  <div class="flex items-center gap-2 mb-2">
                    <svg class="w-4 h-4 text-blue-600 animate-spin" fill="none" viewBox="0 0 24 24">
                      <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                      <path class="opacity-75" fill="currentColor"
                            d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"/>
                    </svg>
                    <span class="text-sm font-medium text-blue-800">
                      Processing import...
                    </span>
                  </div>
                  <div class="flex items-center justify-between text-xs text-blue-600 mb-1">
                    <span>{{ asyncJobStatus()!.status | titlecase }}</span>
                    <span>{{ asyncJobStatus()!.progress }}%</span>
                  </div>
                  <div class="w-full bg-blue-100 rounded-full h-2">
                    <div
                      class="bg-blue-600 h-2 rounded-full transition-all duration-300"
                      [style.width.%]="asyncJobStatus()!.progress"
                      role="progressbar"
                      [attr.aria-valuenow]="asyncJobStatus()!.progress"
                      aria-valuemin="0"
                      aria-valuemax="100"
                    ></div>
                  </div>
                  <p class="mt-2 text-xs text-blue-500">
                    You'll be notified when the import completes. You can navigate away from this page.
                  </p>
                </div>
              }

              <!-- Navigation buttons -->
              <div class="flex justify-between mt-6">
                <button
                  type="button"
                  class="px-4 py-2.5 rounded-lg border border-neutral-200 text-sm font-medium
                         text-neutral-700 hover:bg-neutral-50 transition-colors duration-200
                         focus:outline-none focus:ring-2 focus:ring-neutral-300"
                  (click)="goToStep(0)"
                >
                  Back
                </button>
                <button
                  type="button"
                  class="px-5 py-2.5 rounded-lg bg-neutral-900 text-white text-sm font-medium
                         hover:bg-neutral-800 transition-colors duration-200
                         focus:outline-none focus:ring-2 focus:ring-neutral-400 focus:ring-offset-2
                         disabled:opacity-50 disabled:cursor-not-allowed"
                  [disabled]="!canImport()"
                  (click)="startImport()"
                >
                  @if (isUploading()) {
                    <span class="inline-flex items-center gap-2">
                      <svg class="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                        <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                        <path class="opacity-75" fill="currentColor"
                              d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"/>
                      </svg>
                      Importing...
                    </span>
                  } @else {
                    Import
                  }
                </button>
              </div>
            </div>
          }

          <!-- Step 3: Review Results -->
          @if (currentStep() === 2) {
            <div class="p-6 sm:p-8" @fadeIn>
              <h2 class="text-lg font-semibold text-neutral-900 mb-4">
                Import Results
              </h2>

              @if (importResult()) {
                <!-- Summary banner -->
                <div
                  class="p-4 rounded-lg mb-6"
                  [class.bg-green-50]="outcome() === 'all-success'"
                  [class.border-green-200]="outcome() === 'all-success'"
                  [class.bg-amber-50]="outcome() === 'partial'"
                  [class.border-amber-200]="outcome() === 'partial'"
                  [class.bg-red-50]="outcome() === 'all-failed'"
                  [class.border-red-200]="outcome() === 'all-failed'"
                  class="border"
                  role="status"
                  [attr.aria-label]="summaryText()"
                >
                  <div class="flex items-start gap-3">
                    <!-- Icon -->
                    @if (outcome() === 'all-success') {
                      <svg class="w-5 h-5 text-green-600 mt-0.5 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"/>
                      </svg>
                    }
                    @if (outcome() === 'partial') {
                      <svg class="w-5 h-5 text-amber-600 mt-0.5 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                              d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"/>
                      </svg>
                    }
                    @if (outcome() === 'all-failed') {
                      <svg class="w-5 h-5 text-red-600 mt-0.5 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z"/>
                      </svg>
                    }
                    <div>
                      <p class="text-sm font-medium"
                         [class.text-green-800]="outcome() === 'all-success'"
                         [class.text-amber-800]="outcome() === 'partial'"
                         [class.text-red-800]="outcome() === 'all-failed'"
                      >
                        {{ summaryText() }}
                      </p>
                      <div class="flex flex-wrap gap-4 mt-2 text-sm">
                        <span class="text-neutral-600">
                          Total: <span class="font-medium text-neutral-900">{{ importResult()!.total }}</span>
                        </span>
                        <span class="text-green-700">
                          Success: <span class="font-medium">{{ importResult()!.success }}</span>
                        </span>
                        @if (importResult()!.failed > 0) {
                          <span class="text-red-700">
                            Failed: <span class="font-medium">{{ importResult()!.failed }}</span>
                          </span>
                        }
                      </div>
                    </div>
                  </div>
                </div>

                <!-- Error table (AC-3) -->
                @if (importResult()!.errors.length > 0) {
                  <div class="mb-6">
                    <div class="flex items-center justify-between mb-3">
                      <h3 class="text-sm font-medium text-neutral-700">Error Details</h3>
                      <button
                        type="button"
                        class="inline-flex items-center text-sm text-neutral-600 hover:text-neutral-900
                               transition-colors duration-200
                               focus:outline-none focus:ring-2 focus:ring-neutral-300 rounded px-2 py-1"
                        (click)="downloadErrorReportCsv()"
                        [disabled]="isDownloadingErrorReport()"
                      >
                        <svg class="w-4 h-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
                                d="M12 10v6m0 0l-3-3m3 3l3-3m2 8H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
                        </svg>
                        Download Error Report (CSV)
                      </button>
                    </div>
                    <div class="overflow-x-auto rounded-lg border border-neutral-100">
                      <table class="w-full text-sm" role="table">
                        <thead>
                          <tr class="bg-neutral-50 border-b border-neutral-100">
                            <th class="text-left py-2.5 px-4 font-medium text-neutral-500 whitespace-nowrap">Row</th>
                            <th class="text-left py-2.5 px-4 font-medium text-neutral-500 whitespace-nowrap">Field</th>
                            <th class="text-left py-2.5 px-4 font-medium text-neutral-500">Error</th>
                          </tr>
                        </thead>
                        <tbody>
                          @for (err of importResult()!.errors; track err.row + err.field) {
                            <tr class="border-b border-neutral-50 hover:bg-neutral-50 transition-colors">
                              <td class="py-2 px-4 text-neutral-800 font-mono text-xs">{{ err.row }}</td>
                              <td class="py-2 px-4 text-neutral-800 font-mono text-xs whitespace-nowrap">{{ err.field }}</td>
                              <td class="py-2 px-4 text-neutral-600 text-xs">{{ err.error }}</td>
                            </tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  </div>
                }
              }

              <!-- Action buttons -->
              <div class="flex flex-col sm:flex-row justify-between gap-3 mt-6">
                <button
                  type="button"
                  class="px-4 py-2.5 rounded-lg border border-neutral-200 text-sm font-medium
                         text-neutral-700 hover:bg-neutral-50 transition-colors duration-200
                         focus:outline-none focus:ring-2 focus:ring-neutral-300"
                  (click)="importAnother()"
                >
                  Import Another File
                </button>
                <a
                  routerLink="/employees"
                  class="inline-flex items-center justify-center px-5 py-2.5 rounded-lg
                         bg-neutral-900 text-white text-sm font-medium
                         hover:bg-neutral-800 transition-colors duration-200
                         focus:outline-none focus:ring-2 focus:ring-neutral-400 focus:ring-offset-2"
                >
                  Go to Directory
                </a>
              </div>
            </div>
          }
        </div>
      </div>
    </div>
  `,
})
export class BulkImportComponent implements OnDestroy {
  private readonly importService = inject(BulkImportService);
  private readonly toastr = inject(ToastrService);

  // ─── Constants exposed to template ──────────────────────────

  readonly templateColumns: ITemplateColumn[] = TEMPLATE_COLUMNS;
  readonly maxFileSizeLabel = MAX_IMPORT_FILE_SIZE_LABEL;

  readonly steps = [
    { index: 0, label: 'Download Template' },
    { index: 1, label: 'Upload File' },
    { index: 2, label: 'Review Results' },
  ];

  // ─── State signals ──────────────────────────────────────────

  readonly currentStep = signal(0);
  readonly showColumnGuide = signal(false);
  readonly isDownloadingTemplate = signal(false);

  // File selection
  readonly selectedFile = signal<File | null>(null);
  readonly fileValidationError = signal<string | null>(null);
  readonly isDragOver = signal(false);

  // Upload / import
  readonly isUploading = signal(false);
  readonly uploadProgress = signal<number | null>(null);
  readonly planLimitWarning = signal<IPlanLimitWarning | null>(null);

  // Async job tracking
  readonly asyncJobStatus = signal<IImportJobStatus | null>(null);
  private pollingTimerId: ReturnType<typeof setInterval> | null = null;
  private currentJobId: string | null = null;

  // Results
  readonly importResult = signal<IImportResult | null>(null);
  readonly isDownloadingErrorReport = signal(false);

  // ─── Computed ───────────────────────────────────────────────

  readonly canImport = computed(
    () => !!this.selectedFile() && !this.isUploading() && !this.fileValidationError() && !this.asyncJobStatus()
  );

  readonly outcome = computed<ImportOutcome | null>(() => {
    const result = this.importResult();
    return result ? getImportOutcome(result) : null;
  });

  readonly summaryText = computed(() => {
    const result = this.importResult();
    if (!result) return '';
    const outcome = this.outcome();
    if (outcome === 'all-success') {
      return `${result.success} of ${result.total} records imported successfully.`;
    }
    if (outcome === 'all-failed') {
      return `All ${result.total} records failed. Please fix the errors and try again.`;
    }
    return `${result.success} of ${result.total} records imported. ${result.failed} failed.`;
  });

  // ─── Cleanup ────────────────────────────────────────────────

  ngOnDestroy(): void {
    this.stopPolling();
  }

  // ─── Step navigation ────────────────────────────────────────

  goToStep(step: number): void {
    this.currentStep.set(step);
  }

  // ─── Step 1: Template download ──────────────────────────────

  downloadTemplate(format: ImportTemplateFormat): void {
    this.isDownloadingTemplate.set(true);
    this.importService
      .downloadTemplate(format)
      .pipe(finalize(() => this.isDownloadingTemplate.set(false)))
      .subscribe({
        next: (blob) => {
          const ext = format === 'csv' ? 'csv' : 'xlsx';
          this.triggerBlobDownload(blob, `employee-import-template.${ext}`);
          this.toastr.success(`Template downloaded as ${ext.toUpperCase()}.`);
        },
        error: () => {
          this.toastr.error('Failed to download template.');
        },
      });
  }

  // ─── Step 2: File selection ─────────────────────────────────

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.selectFile(file);
    // Reset so re-selecting the same file triggers change
    input.value = '';
  }

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
    const file = event.dataTransfer?.files[0] ?? null;
    this.selectFile(file);
  }

  clearFile(event: Event): void {
    event.stopPropagation();
    this.selectedFile.set(null);
    this.fileValidationError.set(null);
    this.planLimitWarning.set(null);
  }

  // ─── Step 2: Import trigger ─────────────────────────────────

  startImport(): void {
    const file = this.selectedFile();
    if (!file) return;
    this.executeImport(file);
  }

  importUpToLimit(): void {
    const file = this.selectedFile();
    if (!file) return;
    this.planLimitWarning.set(null);
    this.executeImport(file, { importUpToLimit: true });
  }

  cancelPlanLimit(): void {
    this.planLimitWarning.set(null);
  }

  // ─── Step 3: Error report download ──────────────────────────

  downloadErrorReportCsv(): void {
    // If we have a jobId from async, use the backend endpoint.
    // Otherwise generate client-side CSV from the errors array.
    if (this.currentJobId) {
      this.downloadErrorReportFromBackend(this.currentJobId);
    } else {
      this.downloadErrorReportClientSide();
    }
  }

  importAnother(): void {
    this.selectedFile.set(null);
    this.fileValidationError.set(null);
    this.importResult.set(null);
    this.uploadProgress.set(null);
    this.asyncJobStatus.set(null);
    this.planLimitWarning.set(null);
    this.currentJobId = null;
    this.currentStep.set(1);
  }

  // ─── Internal: file validation (client-side) ────────────────

  private selectFile(file: File | null): void {
    this.fileValidationError.set(null);
    this.planLimitWarning.set(null);

    if (!file) {
      this.selectedFile.set(null);
      return;
    }

    const error = validateImportFile(file);
    if (error) {
      this.fileValidationError.set(error);
      this.selectedFile.set(null);
      return;
    }

    this.selectedFile.set(file);
  }

  // ─── Internal: upload execution ─────────────────────────────

  private executeImport(
    file: File,
    options?: { importUpToLimit?: boolean }
  ): void {
    this.isUploading.set(true);
    this.uploadProgress.set(0);

    this.importService
      .uploadImport(file, options)
      .pipe(finalize(() => {
        this.isUploading.set(false);
        this.uploadProgress.set(null);
      }))
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress && event.total) {
            const pct = Math.round((100 * event.loaded) / event.total);
            this.uploadProgress.set(pct);
          } else if (event.type === HttpEventType.Response && event.body) {
            this.handleImportResponse(event.body);
          }
        },
        error: (err: HttpErrorResponse) => {
          if (err.status === 409 && isPlanLimitWarning(err.error)) {
            this.planLimitWarning.set(err.error);
            return;
          }
          this.toastr.error('Import failed. Please try again.');
        },
      });
  }

  private handleImportResponse(response: ImportResponse): void {
    if (isImportResult(response)) {
      this.importResult.set(response);
      this.currentStep.set(2);
    } else if (isImportJobRef(response)) {
      this.currentJobId = response.jobId;
      this.asyncJobStatus.set({
        jobId: response.jobId,
        status: response.status,
        progress: 0,
        result: null,
      });
      this.startPolling(response.jobId);
    }
  }

  // ─── Internal: async job polling ────────────────────────────

  private startPolling(jobId: string): void {
    this.stopPolling();
    this.pollingTimerId = setInterval(() => {
      this.importService.getImportJobStatus(jobId).subscribe({
        next: (status) => {
          this.asyncJobStatus.set(status);
          if (status.status === 'completed' && status.result) {
            this.stopPolling();
            this.asyncJobStatus.set(null);
            this.importResult.set(status.result);
            this.currentStep.set(2);
            this.toastr.success('Bulk import completed.');
          } else if (status.status === 'failed') {
            this.stopPolling();
            this.asyncJobStatus.set(null);
            this.toastr.error('Import job failed. Please try again.');
          }
        },
        error: () => {
          // Polling failure is non-fatal; keep trying
        },
      });
    }, 3000);
  }

  private stopPolling(): void {
    if (this.pollingTimerId !== null) {
      clearInterval(this.pollingTimerId);
      this.pollingTimerId = null;
    }
  }

  // ─── Internal: blob download helper ─────────────────────────

  /**
   * Trigger a file download from a Blob without navigating.
   * Uses URL.createObjectURL + programmatic anchor click.
   *
   * NOTE: In tests, the anchor click is stubbed to prevent Karma reloads.
   * The triggerBlobDownload method is exposed as non-private for testability.
   */
  triggerBlobDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  // ─── Internal: error report downloads ───────────────────────

  private downloadErrorReportFromBackend(jobId: string): void {
    this.isDownloadingErrorReport.set(true);
    this.importService
      .downloadErrorReport(jobId)
      .pipe(finalize(() => this.isDownloadingErrorReport.set(false)))
      .subscribe({
        next: (blob) => {
          this.triggerBlobDownload(blob, 'import-error-report.csv');
        },
        error: () => {
          this.toastr.error('Failed to download error report.');
        },
      });
  }

  private downloadErrorReportClientSide(): void {
    const errors = this.importResult()?.errors;
    if (!errors?.length) return;
    const csv = generateErrorReportCsv(errors);
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    this.triggerBlobDownload(blob, 'import-error-report.csv');
  }

  // ─── Helpers ────────────────────────────────────────────────

  formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}

// ─── Pure functions (exported for unit testing) ──────────────

/**
 * Client-side file validation for bulk import (BR-7).
 * Returns an error message string if invalid, null if valid.
 */
export function validateImportFile(file: File): string | null {
  const ext = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();
  if (!ALLOWED_IMPORT_EXTENSIONS.includes(ext)) {
    return `Invalid file type. Only ${ALLOWED_IMPORT_EXTENSIONS.join(', ')} files are accepted.`;
  }
  if (file.size > MAX_IMPORT_FILE_SIZE_BYTES) {
    return `File size exceeds the maximum of ${MAX_IMPORT_FILE_SIZE_LABEL}.`;
  }
  if (file.size === 0) {
    return 'The selected file is empty.';
  }
  return null;
}

/**
 * Generate a CSV string from import row errors for client-side download.
 */
export function generateErrorReportCsv(errors: IImportRowError[]): string {
  const header = 'Row,Field,Error';
  const rows = errors.map(
    (e) => `${e.row},"${escapeCsvField(e.field)}","${escapeCsvField(e.error)}"`
  );
  return [header, ...rows].join('\n');
}

function escapeCsvField(value: string): string {
  return value.replace(/"/g, '""');
}
