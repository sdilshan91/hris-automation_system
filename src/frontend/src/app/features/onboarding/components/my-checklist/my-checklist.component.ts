import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpEventType, HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { OnboardingChecklistService } from '../../services/onboarding-checklist.service';
import {
  IMyChecklist,
  IMyOnboardingTask,
  IMyOnboardingCategory,
  categoryCompletedCount,
  isTaskOverdue,
  daysOverdue,
  isEmployeeActionable,
  myTaskStatusLabel,
  ringDashOffset,
  validateUploadFile,
  formatUploadSize,
  UPLOAD_ACCEPT_ATTR,
} from '../../models/my-onboarding.models';

/**
 * US-ONB-003: "My Onboarding" checklist page for the logged-in new hire (FR-1).
 *
 * Tasks are grouped into collapsible category accordions with count badges
 * (e.g. "Documentation 2/4"). Each task card shows title, description, due date,
 * status chip and responsible-party badge. Completed tasks render with
 * strikethrough + a green check; overdue tasks get a red border, warning icon
 * and an "X days overdue" badge (AC-2 / AC-5).
 *
 * Mark-complete is only offered on tasks where responsibleRole === 'Employee'
 * (others are read-only — BR-1 / FR-7), with an optional comment and, for
 * requiresDocument tasks, a drag-and-drop upload zone with client-side MIME/size
 * validation and an upload progress bar (AC-3 / AC-4). On success the progress
 * ring updates from the API's recalculated progress (no SignalR — re-render
 * from the completion response; a later Notifications story adds live push).
 *
 * Standalone, signals-first, OnPush. WCAG 2.1 AA: keyboard-operable accordions
 * and drop zone, live-region status announcements, labelled controls.
 */
@Component({
  selector: 'app-my-checklist',
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
        animate('180ms ease-in', style({ opacity: 0, height: '0', overflow: 'hidden' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container max-w-3xl">
      <div class="mb-6">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">My Onboarding</h1>
        <p class="mt-1 text-sm text-neutral-500">
          Complete your joining tasks and track your progress.
        </p>
      </div>

      <!-- Loading skeleton -->
      @if (loading()) {
        <div class="space-y-4" aria-busy="true" aria-label="Loading your checklist">
          @for (_ of [1, 2, 3]; track $index) {
            <div class="skeleton-card">
              <div class="skeleton-line w-40 h-4 mb-3"></div>
              <div class="skeleton-line w-full h-3 mb-2"></div>
              <div class="skeleton-line w-2/3 h-3"></div>
            </div>
          }
        </div>
      }

      <!-- Load error -->
      @if (loadError()) {
        <div class="text-center py-10" @fadeIn>
          <p class="text-sm text-red-600 mb-3">{{ loadError() }}</p>
          <button type="button" class="btn-secondary" (click)="load()">Retry</button>
        </div>
      }

      <!-- Empty (no checklist assigned) -->
      @if (!loading() && !loadError() && checklist() && totalTasks() === 0) {
        <div class="empty-state" @fadeIn>
          <p class="text-sm text-neutral-500">No onboarding tasks have been assigned to you yet.</p>
        </div>
      }

      <!-- Checklist -->
      @if (!loading() && !loadError() && checklist() && totalTasks() > 0) {
        <div @fadeIn>
          <!-- Progress summary ring -->
          <section class="progress-panel mb-6" aria-labelledby="progress-heading">
            <h2 id="progress-heading" class="sr-only">Overall progress</h2>
            <div class="flex items-center gap-5">
              <div class="relative flex-shrink-0" aria-hidden="true">
                <svg width="96" height="96" viewBox="0 0 96 96" class="-rotate-90">
                  <circle cx="48" cy="48" r="40" fill="none" stroke="#e5e7eb" stroke-width="9" />
                  <circle
                    cx="48" cy="48" r="40" fill="none"
                    stroke="#4f46e5" stroke-width="9" stroke-linecap="round"
                    [attr.stroke-dasharray]="circumference"
                    [attr.stroke-dashoffset]="dashOffset()"
                    class="transition-all duration-500 ease-out"
                  />
                </svg>
                <span class="absolute inset-0 flex items-center justify-center text-xl font-semibold text-neutral-900">
                  {{ checklist()!.progressPercent }}%
                </span>
              </div>
              <div class="flex-1 grid grid-cols-3 gap-3">
                <div class="count-cell">
                  <span class="count-value text-green-600">{{ checklist()!.completedTasks }}</span>
                  <span class="count-label">Completed</span>
                </div>
                <div class="count-cell">
                  <span class="count-value text-amber-600">{{ checklist()!.pendingTasks }}</span>
                  <span class="count-label">Pending</span>
                </div>
                <div class="count-cell">
                  <span class="count-value" [class.text-red-600]="checklist()!.overdueTasks > 0"
                    [class.text-neutral-400]="checklist()!.overdueTasks === 0">{{ checklist()!.overdueTasks }}</span>
                  <span class="count-label">Overdue</span>
                </div>
              </div>
            </div>
            <!-- SR live region for progress changes (AC-3) -->
            <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>

            @if (checklist()!.progressPercent === 100) {
              <p class="mt-4 text-sm font-medium text-green-700" @fadeIn role="status">
                All tasks complete — welcome aboard!
              </p>
            }
          </section>

          <!-- Category accordions (AC-2) -->
          <div class="space-y-3">
            @for (cat of checklist()!.categories; track cat.category) {
              <section class="accordion">
                <h3>
                  <button
                    type="button"
                    class="accordion-header"
                    (click)="toggleCategory(cat.category)"
                    [attr.aria-expanded]="isExpanded(cat.category)"
                    [attr.aria-controls]="'panel-' + cat.category"
                  >
                    <span class="flex items-center gap-2">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                        class="w-4 h-4 text-neutral-400 transition-transform duration-200"
                        [class.rotate-90]="isExpanded(cat.category)" aria-hidden="true">
                        <path fill-rule="evenodd" d="M8.22 5.22a.75.75 0 0 1 1.06 0l4.25 4.25a.75.75 0 0 1 0 1.06l-4.25 4.25a.75.75 0 0 1-1.06-1.06L11.94 10 8.22 6.28a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd"/>
                      </svg>
                      <span class="font-medium text-neutral-900">{{ cat.category }}</span>
                    </span>
                    <span class="count-badge">{{ completedIn(cat) }}/{{ cat.tasks.length }}</span>
                  </button>
                </h3>

                @if (isExpanded(cat.category)) {
                  <div [id]="'panel-' + cat.category" class="accordion-body" @slideDown role="region">
                    <ul class="space-y-3">
                      @for (task of cat.tasks; track task.taskInstanceId) {
                        <li
                          class="task-card"
                          [class.task-overdue]="overdue(task)"
                          [class.task-done]="task.status === 'completed'"
                        >
                          <div class="flex items-start gap-3">
                            <!-- Status icon -->
                            <span class="task-status-icon" aria-hidden="true">
                              @if (task.status === 'completed') {
                                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 text-green-600">
                                  <path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd"/>
                                </svg>
                              } @else if (overdue(task)) {
                                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 text-red-500">
                                  <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd"/>
                                </svg>
                              } @else {
                                <span class="w-5 h-5 rounded-full border-2 border-neutral-300 block"></span>
                              }
                            </span>

                            <div class="flex-1 min-w-0">
                              <div class="flex items-start justify-between gap-2">
                                <p class="task-title" [class.line-through]="task.status === 'completed'">
                                  {{ task.title }}
                                  @if (task.isMandatory) {
                                    <span class="mandatory-dot" aria-label="Mandatory">*</span>
                                  }
                                </p>
                                <span class="status-chip" [ngClass]="chipClass(task)">
                                  {{ statusLabel(task) }}
                                </span>
                              </div>

                              @if (task.description) {
                                <p class="task-desc">{{ task.description }}</p>
                              }

                              <div class="flex flex-wrap items-center gap-2 mt-2">
                                <span class="meta-badge">
                                  Due {{ task.dueDate | date:'mediumDate' }}
                                </span>
                                @if (task.responsibleRole) {
                                  <span class="role-badge">{{ task.responsibleRole }}</span>
                                }
                                @if (overdue(task) && task.status !== 'completed') {
                                  <span class="overdue-badge">{{ overdueDays(task) }} {{ overdueDays(task) === 1 ? 'day' : 'days' }} overdue</span>
                                }
                                @if (task.requiresDocument) {
                                  <span class="doc-badge">Document required</span>
                                }
                              </div>

                              @if (task.status === 'completed') {
                                <p class="completed-meta">
                                  Completed{{ task.completedAt ? ' on ' + (task.completedAt | date:'medium') : '' }}{{ task.completedBy ? ' by ' + task.completedBy : '' }}
                                </p>
                                @if (task.attachmentUrl) {
                                  <a [href]="task.attachmentUrl" target="_blank" rel="noopener" class="attachment-link">
                                    View attachment
                                  </a>
                                }
                              }

                              <!-- Mark-complete action (Employee role only — BR-1/FR-7) -->
                              @if (canComplete(task)) {
                                @if (openTaskId() !== task.taskInstanceId) {
                                  <button
                                    type="button"
                                    class="btn-complete mt-3"
                                    (click)="openComplete(task)"
                                  >
                                    Mark Complete
                                  </button>
                                } @else {
                                  <div class="complete-form mt-3" @slideDown>
                                    <form [formGroup]="completeForm" (ngSubmit)="submit(task)">
                                      <label class="label-notion" [attr.for]="'comment-' + task.taskInstanceId">
                                        Comment (optional)
                                      </label>
                                      <textarea
                                        [id]="'comment-' + task.taskInstanceId"
                                        formControlName="comment"
                                        class="input-notion"
                                        rows="2"
                                        maxlength="500"
                                        placeholder="Add a note..."
                                      ></textarea>

                                      @if (task.requiresDocument) {
                                        <div class="mt-3">
                                          <span class="label-notion block mb-1">Attach document *</span>
                                          @if (!selectedFile()) {
                                            <div
                                              class="drop-zone"
                                              [class.drop-zone-active]="isDragOver()"
                                              [class.drop-zone-error]="!!fileError()"
                                              (dragover)="onDragOver($event)"
                                              (dragleave)="onDragLeave($event)"
                                              (drop)="onDrop($event)"
                                              (click)="fileInput.click()"
                                              (keydown.enter)="fileInput.click()"
                                              (keydown.space)="$event.preventDefault(); fileInput.click()"
                                              tabindex="0"
                                              role="button"
                                              aria-label="Drop a file here or browse to upload"
                                            >
                                              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-7 h-7 text-neutral-300 mb-1" aria-hidden="true">
                                                <path fill-rule="evenodd" d="M11.47 2.47a.75.75 0 0 1 1.06 0l4.5 4.5a.75.75 0 0 1-1.06 1.06l-3.22-3.22V16.5a.75.75 0 0 1-1.5 0V4.81L8.03 8.03a.75.75 0 0 1-1.06-1.06l4.5-4.5ZM3 15.75a.75.75 0 0 1 .75.75v2.25a1.5 1.5 0 0 0 1.5 1.5h13.5a1.5 1.5 0 0 0 1.5-1.5V16.5a.75.75 0 0 1 1.5 0v2.25a3 3 0 0 1-3 3H5.25a3 3 0 0 1-3-3V16.5a.75.75 0 0 1 .75-.75Z" clip-rule="evenodd"/>
                                              </svg>
                                              <p class="text-xs text-neutral-500">Drop a file or tap to browse</p>
                                              <p class="text-[11px] text-neutral-400 mt-0.5">PDF, JPG, PNG, DOC up to 10 MB</p>
                                            </div>
                                          } @else {
                                            <div class="selected-file-row">
                                              <span class="text-sm text-neutral-700 truncate">{{ selectedFile()!.name }}</span>
                                              <span class="text-xs text-neutral-400 ml-2">{{ fileSize(selectedFile()!) }}</span>
                                              <button type="button" class="ml-auto text-neutral-400 hover:text-red-600"
                                                (click)="clearFile()" aria-label="Remove file">
                                                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                                                  <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                                                </svg>
                                              </button>
                                            </div>
                                          }
                                          <input
                                            #fileInput
                                            type="file"
                                            class="sr-only"
                                            [accept]="acceptAttr"
                                            (change)="onFileSelected($event)"
                                            aria-hidden="true"
                                          />
                                          @if (fileError()) {
                                            <p class="text-xs text-red-600 mt-1" role="alert">{{ fileError() }}</p>
                                          }
                                        </div>
                                      }

                                      @if (uploadProgress() !== null) {
                                        <div class="progress-row mt-3" role="progressbar"
                                          [attr.aria-valuenow]="uploadProgress()" aria-valuemin="0" aria-valuemax="100">
                                          <div class="progress-track">
                                            <div class="progress-fill" [style.width.%]="uploadProgress()"></div>
                                          </div>
                                          <span class="text-xs text-neutral-500 w-9 text-right">{{ uploadProgress() }}%</span>
                                        </div>
                                      }

                                      <div class="flex items-center justify-end gap-2 mt-3">
                                        <button type="button" class="btn-secondary" (click)="cancelComplete()" [disabled]="submitting()">
                                          Cancel
                                        </button>
                                        <button type="submit" class="btn-primary" [disabled]="submitting() || (task.requiresDocument && !selectedFile())">
                                          @if (submitting()) {
                                            <span class="btn-spinner"></span> Submitting...
                                          } @else {
                                            Confirm Complete
                                          }
                                        </button>
                                      </div>
                                    </form>
                                  </div>
                                }
                              }
                            </div>
                          </div>
                        </li>
                      }
                    </ul>
                  </div>
                }
              </section>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .progress-panel { @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-5; }
    .count-cell { @apply flex flex-col items-center justify-center rounded-lg bg-neutral-50 py-2 px-1; }
    .count-value { @apply text-xl font-semibold leading-none; }
    .count-label { @apply mt-1 text-[11px] font-medium text-neutral-400; }

    .accordion { @apply rounded-xl bg-white border border-neutral-100 shadow-notion overflow-hidden; }
    .accordion-header {
      @apply w-full flex items-center justify-between px-4 py-3 text-left text-sm
        transition-colors duration-150 hover:bg-neutral-50;
    }
    .count-badge { @apply text-xs font-medium px-2 py-0.5 rounded-full bg-neutral-100 text-neutral-600; }
    .accordion-body { @apply px-4 pb-4 pt-1; }

    .task-card {
      @apply rounded-lg border border-neutral-100 bg-white p-3.5 transition-all duration-150;
    }
    .task-overdue { @apply border-red-300 bg-red-50/40; }
    .task-done { @apply bg-neutral-50/60; }
    .task-status-icon { @apply flex-shrink-0 mt-0.5; }
    .task-title { @apply text-sm font-medium text-neutral-900; }
    .task-desc { @apply text-sm text-neutral-500 mt-1; }
    .mandatory-dot { @apply text-red-500 ml-0.5; }
    .completed-meta { @apply text-xs text-neutral-400 mt-2; }
    .attachment-link { @apply text-xs font-medium text-brand-600 hover:text-brand-700 inline-block mt-1; }

    .status-chip { @apply text-[11px] font-medium px-2 py-0.5 rounded-full whitespace-nowrap flex-shrink-0; }
    .chip-pending { @apply bg-amber-50 text-amber-700; }
    .chip-progress { @apply bg-blue-50 text-blue-700; }
    .chip-completed { @apply bg-green-50 text-green-700; }
    .chip-overdue { @apply bg-red-50 text-red-700; }
    .chip-cancelled { @apply bg-neutral-100 text-neutral-500; }

    .meta-badge { @apply text-[11px] text-neutral-500 bg-neutral-50 rounded px-1.5 py-0.5; }
    .role-badge { @apply text-[11px] font-medium text-indigo-700 bg-indigo-50 rounded px-1.5 py-0.5; }
    .overdue-badge { @apply text-[11px] font-medium text-red-700 bg-red-100 rounded px-1.5 py-0.5; }
    .doc-badge { @apply text-[11px] font-medium text-neutral-600 bg-neutral-100 rounded px-1.5 py-0.5; }

    .btn-complete {
      @apply inline-flex items-center rounded-lg bg-brand-600 px-3 py-1.5 text-xs font-medium
        text-white transition-colors duration-150 hover:bg-brand-700;
    }
    .complete-form { @apply rounded-lg bg-neutral-50 border border-neutral-100 p-3; }

    .drop-zone {
      @apply flex flex-col items-center justify-center rounded-lg border-2 border-dashed
        border-neutral-200 bg-white p-4 cursor-pointer transition-all duration-200
        hover:border-brand-300;
    }
    .drop-zone-active { @apply border-brand-400 bg-brand-50/50; }
    .drop-zone-error { @apply border-red-300 bg-red-50/30; }
    .selected-file-row { @apply flex items-center rounded-lg bg-white border border-neutral-100 px-3 py-2; }

    .progress-row { @apply flex items-center gap-2; }
    .progress-track { @apply flex-1 h-2 rounded-full bg-neutral-100 overflow-hidden; }
    .progress-fill { @apply h-full rounded-full bg-brand-500 transition-all duration-300; }

    .empty-state { @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-8 text-center; }
    .skeleton-card { @apply rounded-xl bg-white border border-neutral-100 p-4; }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2 text-sm
        font-medium text-white transition-all duration-200 hover:bg-brand-700
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2 text-sm
        font-medium text-neutral-700 ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50 disabled:opacity-50;
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class MyChecklistComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly toastr = inject(ToastrService);
  private readonly checklistService = inject(OnboardingChecklistService);
  private readonly destroy$ = new Subject<void>();

  readonly acceptAttr = UPLOAD_ACCEPT_ATTR;
  readonly circumference = 2 * Math.PI * 40;

  readonly checklist = signal<IMyChecklist | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);

  /** Expanded category names (all expanded by default after load). */
  readonly expanded = signal<Set<string>>(new Set());

  // Mark-complete state (one task open at a time).
  readonly openTaskId = signal<string | null>(null);
  readonly selectedFile = signal<File | null>(null);
  readonly fileError = signal<string | null>(null);
  readonly isDragOver = signal(false);
  readonly submitting = signal(false);
  readonly uploadProgress = signal<number | null>(null);
  readonly liveMessage = signal('');

  readonly completeForm: FormGroup = this.fb.group({
    comment: ['', [Validators.maxLength(500)]],
  });

  readonly totalTasks = computed(() => this.checklist()?.totalTasks ?? 0);
  readonly dashOffset = computed(() =>
    ringDashOffset(this.checklist()?.progressPercent ?? 0, this.circumference),
  );

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.checklistService
      .getMyChecklist()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.checklist.set(data);
          this.expanded.set(new Set(data.categories.map((c) => c.category)));
          this.loading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.loading.set(false);
          if (err.status === 404) {
            // No checklist assigned — treat as empty, not an error.
            this.checklist.set({
              checklistInstanceId: '',
              status: 'none',
              progressPercent: 0,
              totalTasks: 0,
              completedTasks: 0,
              pendingTasks: 0,
              overdueTasks: 0,
              categories: [],
            });
          } else {
            this.loadError.set('Failed to load your checklist. Please try again.');
          }
        },
      });
  }

  // ─── Accordion ──────────────────────────────────────────────
  isExpanded(category: string): boolean {
    return this.expanded().has(category);
  }

  toggleCategory(category: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (next.has(category)) next.delete(category);
      else next.add(category);
      return next;
    });
  }

  completedIn(cat: IMyOnboardingCategory): number {
    return categoryCompletedCount(cat);
  }

  // ─── Per-task display helpers ───────────────────────────────
  overdue(task: IMyOnboardingTask): boolean {
    return isTaskOverdue(task);
  }

  overdueDays(task: IMyOnboardingTask): number {
    return daysOverdue(task);
  }

  canComplete(task: IMyOnboardingTask): boolean {
    return isEmployeeActionable(task);
  }

  statusLabel(task: IMyOnboardingTask): string {
    return this.overdue(task) && task.status !== 'completed'
      ? myTaskStatusLabel('overdue')
      : myTaskStatusLabel(task.status);
  }

  chipClass(task: IMyOnboardingTask): string {
    if (task.status === 'completed') return 'chip-completed';
    if (this.overdue(task)) return 'chip-overdue';
    if (task.status === 'in_progress') return 'chip-progress';
    if (task.status === 'cancelled') return 'chip-cancelled';
    return 'chip-pending';
  }

  fileSize(file: File): string {
    return formatUploadSize(file.size);
  }

  // ─── Mark-complete flow ─────────────────────────────────────
  openComplete(task: IMyOnboardingTask): void {
    this.openTaskId.set(task.taskInstanceId);
    this.completeForm.reset({ comment: '' });
    this.clearFile();
    this.uploadProgress.set(null);
  }

  cancelComplete(): void {
    this.openTaskId.set(null);
    this.clearFile();
    this.uploadProgress.set(null);
    this.submitting.set(false);
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
    const file = event.dataTransfer?.files?.[0];
    if (file) this.selectFile(file);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.selectFile(file);
    input.value = '';
  }

  clearFile(): void {
    this.selectedFile.set(null);
    this.fileError.set(null);
  }

  submit(task: IMyOnboardingTask): void {
    if (this.completeForm.invalid) return;
    const file = task.requiresDocument ? this.selectedFile() : null;
    if (task.requiresDocument && !file) {
      this.fileError.set('A document is required to complete this task.');
      return;
    }

    this.submitting.set(true);
    if (file) this.uploadProgress.set(0);

    const comment = (this.completeForm.value.comment as string)?.trim() || null;

    this.checklistService
      .completeTask(task.taskInstanceId, { comment, file })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress) {
            const pct = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
            this.uploadProgress.set(pct);
          } else if (event.type === HttpEventType.Response && event.body) {
            this.submitting.set(false);
            this.uploadProgress.set(null);
            this.applyCompletion(task.taskInstanceId, event.body);
            this.openTaskId.set(null);
            this.clearFile();
            const msg = `"${task.title}" marked complete.`;
            this.liveMessage.set(`${msg} Progress now ${event.body.progress.progressPercent}%.`);
            this.toastr.success(msg);
          }
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          this.uploadProgress.set(null);
          this.toastr.error(OnboardingChecklistService.parseErrorMessage(err));
        },
      });
  }

  // ─── Apply server completion result (no SignalR — re-render locally) ──
  private applyCompletion(
    taskInstanceId: string,
    body: { task: IMyOnboardingTask; progress: { progressPercent: number; totalTasks: number; completedTasks: number; pendingTasks: number; overdueTasks: number } },
  ): void {
    const current = this.checklist();
    if (!current) return;
    const categories = current.categories.map((cat) => ({
      ...cat,
      tasks: cat.tasks.map((t) => (t.taskInstanceId === taskInstanceId ? body.task : t)),
    }));
    this.checklist.set({ ...current, ...body.progress, categories });
  }

  private selectFile(file: File): void {
    const error = validateUploadFile(file);
    if (error) {
      this.fileError.set(error);
      this.selectedFile.set(null);
      return;
    }
    this.fileError.set(null);
    this.selectedFile.set(file);
  }
}
