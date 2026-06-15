import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  output,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { PipelineService } from '../../services/pipeline.service';
import { InterviewService } from '../../services/interview.service';
import {
  StageReasonDialogComponent,
  IStageReasonResult,
} from '../stage-reason-dialog/stage-reason-dialog.component';
import { InterviewFormComponent } from '../interview-form/interview-form.component';
import { InterviewCardComponent } from '../interview-card/interview-card.component';
import { InterviewCancelDialogComponent } from '../interview-cancel-dialog/interview-cancel-dialog.component';
import {
  IApplicantDetail,
  SOURCE_BADGE,
  STAGE_BADGE,
  TERMINAL_STAGES,
  nextStage,
  previousStage,
  moveRequiresReason,
  rejectionReasonLabel,
  relativeAppliedTime,
} from '../../models/pipeline.models';
import { IInterview } from '../../models/interview.models';
import { ApplicantSource, ApplicantStage } from '../../models/applicant.models';

type DetailTab = 'profile' | 'resume' | 'timeline' | 'interviews' | 'notes';

/**
 * US-REC-003: Applicant detail slide-over (AC-3 / FR-7).
 *
 * Right-side drawer (full-screen on mobile) opened from a Kanban card. Loads the
 * full applicant + stage-transition history and presents tabs: Profile, Resume
 * (filename + download — inline PDF preview deferred, see note), Timeline (the
 * stage history the backend returns), and placeholder Interviews/Notes tabs (no
 * interview module yet). Mirrors the in-repo right-drawer pattern.
 */
@Component({
  selector: 'app-applicant-detail',
  standalone: true,
  imports: [
    CommonModule,
    StageReasonDialogComponent,
    InterviewFormComponent,
    InterviewCardComponent,
    InterviewCancelDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate(
          '260ms cubic-bezier(0.22, 1, 0.36, 1)',
          style({ transform: 'translateX(0)' }),
        ),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
  ],
  template: `
    <!-- Backdrop -->
    <div
      @backdrop
      class="fixed inset-0 z-40 bg-neutral-900/30"
      (click)="close()"
      aria-hidden="true"
    ></div>

    <!-- Drawer wrap -->
    <div class="pointer-events-none fixed inset-0 z-50 flex justify-end">
      <div
        @drawer
        class="pointer-events-auto flex h-full w-full max-w-2xl flex-col bg-white shadow-xl sm:w-[65%]"
        role="dialog"
        aria-modal="true"
        aria-labelledby="applicant-detail-title"
      >
        <!-- Header -->
        <div
          class="flex items-start justify-between gap-3 border-b border-neutral-100 px-6 py-4"
        >
          <div class="flex items-center gap-3">
            <span
              class="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-indigo-50 text-sm font-semibold text-indigo-600"
              aria-hidden="true"
            >
              {{ initials() }}
            </span>
            <div>
              <h2
                id="applicant-detail-title"
                class="text-base font-semibold text-neutral-900"
              >
                {{ fullName() }}
              </h2>
              <div class="mt-1 flex flex-wrap items-center gap-2">
                @if (detail(); as d) {
                  <span
                    class="badge ring-1 ring-inset"
                    [class]="stageBadge(d.stage)"
                  >
                    {{ d.stage }}
                  </span>
                  <span
                    class="badge ring-1 ring-inset"
                    [class]="sourceBadge(d.source)"
                  >
                    {{ d.source }}
                  </span>
                }
              </div>
            </div>
          </div>
          <button
            type="button"
            class="rounded-md p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
            (click)="close()"
            aria-label="Close"
          >
            <svg
              class="h-5 w-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              aria-hidden="true"
            >
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Tabs -->
        <div
          class="flex gap-1 border-b border-neutral-100 px-4"
          role="tablist"
          aria-label="Applicant details"
        >
          @for (t of tabs; track t.id) {
            <button
              type="button"
              class="tab"
              role="tab"
              [attr.aria-selected]="tab() === t.id"
              [class.tab-active]="tab() === t.id"
              (click)="tab.set(t.id)"
            >
              {{ t.label }}
            </button>
          }
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto px-6 py-5">
          @if (loading()) {
            <div class="space-y-3">
              @for (n of [1, 2, 3, 4]; track n) {
                <div class="h-5 animate-pulse rounded bg-neutral-100"></div>
              }
            </div>
          } @else if (detail(); as d) {
            <!-- Profile -->
            @if (tab() === 'profile') {
              <dl class="grid grid-cols-1 gap-x-6 gap-y-4 sm:grid-cols-2">
                <div>
                  <dt class="dt">Reference</dt>
                  <dd class="dd font-mono text-xs">
                    {{ d.applicationReferenceNumber }}
                  </dd>
                </div>
                <div>
                  <dt class="dt">Applied</dt>
                  <dd class="dd">{{ appliedRelative() }}</dd>
                </div>
                <div>
                  <dt class="dt">Email</dt>
                  <dd class="dd">
                    <a
                      class="text-indigo-600 hover:underline"
                      [href]="'mailto:' + d.email"
                      >{{ d.email }}</a
                    >
                  </dd>
                </div>
                <div>
                  <dt class="dt">Phone</dt>
                  <dd class="dd">{{ d.phone || '—' }}</dd>
                </div>
                <div>
                  <dt class="dt">Source</dt>
                  <dd class="dd capitalize">{{ d.source }}</dd>
                </div>
                <div>
                  <dt class="dt">Internal</dt>
                  <dd class="dd">{{ d.isInternal ? 'Yes' : 'No' }}</dd>
                </div>
                @if (d.coverLetter) {
                  <div class="sm:col-span-2">
                    <dt class="dt">Cover letter</dt>
                    <dd class="dd whitespace-pre-wrap">{{ d.coverLetter }}</dd>
                  </div>
                }
              </dl>
            }

            <!-- Resume -->
            @if (tab() === 'resume') {
              @if (d.resumeFileName) {
                <div
                  class="flex items-center justify-between gap-4 rounded-lg border border-neutral-200 bg-neutral-50 p-4"
                >
                  <div class="flex items-center gap-3 overflow-hidden">
                    <svg
                      class="h-8 w-8 shrink-0 text-neutral-400"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      stroke-width="1.5"
                      aria-hidden="true"
                    >
                      <path
                        d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"
                      />
                      <path d="M14 2v6h6" />
                    </svg>
                    <span class="truncate text-sm text-neutral-700">{{
                      d.resumeFileName
                    }}</span>
                  </div>
                  <button
                    type="button"
                    class="shrink-0 rounded-lg border border-neutral-200 bg-white px-3 py-1.5 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50 disabled:opacity-50"
                    [disabled]="downloading()"
                    (click)="downloadResume()"
                  >
                    {{ downloading() ? 'Downloading…' : 'Download' }}
                  </button>
                </div>
                <p class="mt-3 text-xs text-neutral-400">
                  Inline PDF preview is not available (pdf.js is not a project
                  dependency); use Download to view the file.
                </p>
              } @else {
                <p class="text-sm text-neutral-500">No resume on file.</p>
              }
            }

            <!-- Timeline -->
            @if (tab() === 'timeline') {
              @if (d.stageHistory.length === 0) {
                <p class="text-sm text-neutral-500">
                  No stage transitions recorded yet.
                </p>
              } @else {
                <ol class="relative space-y-5 border-l border-neutral-200 pl-5">
                  @for (h of d.stageHistory; track $index) {
                    <li class="relative">
                      <span
                        class="absolute -left-[1.4rem] top-1 h-2.5 w-2.5 rounded-full bg-indigo-400 ring-4 ring-white"
                        aria-hidden="true"
                      ></span>
                      <div class="flex flex-wrap items-center gap-2 text-sm">
                        <span class="font-medium text-neutral-800">
                          {{ h.fromStage || 'Applied' }} → {{ h.toStage }}
                        </span>
                        <span class="text-xs text-neutral-400">
                          {{ h.changedAt | date: 'medium' }}
                        </span>
                      </div>
                      @if (h.changedByUserName) {
                        <p class="mt-0.5 text-xs text-neutral-500">
                          by {{ h.changedByUserName }}
                        </p>
                      }
                      @if (transitionReason(h); as reasonText) {
                        <p class="mt-1 text-sm text-neutral-600">
                          <span class="font-medium">Reason:</span>
                          {{ reasonText }}
                        </p>
                      }
                      @if (h.notes) {
                        <p class="mt-0.5 text-sm text-neutral-600">{{ h.notes }}</p>
                      }
                    </li>
                  }
                </ol>
              }
            }

            <!-- Interviews (US-REC-005 AC-4 — all rounds for this applicant) -->
            @if (tab() === 'interviews') {
              <div class="mb-4 flex items-center justify-between">
                <h3 class="text-sm font-semibold text-neutral-800">
                  Interviews
                  @if (interviews().length) {
                    <span class="text-neutral-400">({{ interviews().length }})</span>
                  }
                </h3>
                <button
                  type="button"
                  class="rounded-lg bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white transition hover:bg-indigo-700"
                  (click)="openSchedule()"
                >
                  Schedule interview
                </button>
              </div>

              @if (interviewsLoading()) {
                <div class="space-y-3">
                  @for (n of [1, 2]; track n) {
                    <div class="h-24 animate-pulse rounded-xl bg-neutral-100"></div>
                  }
                </div>
              } @else if (interviews().length === 0) {
                <div class="py-10 text-center">
                  <p class="text-sm font-medium text-neutral-700">
                    No interviews scheduled
                  </p>
                  <p class="mt-1 text-sm text-neutral-500">
                    Schedule the first round to notify the applicant and
                    interviewers.
                  </p>
                </div>
              } @else {
                <div class="space-y-3">
                  @for (iv of interviews(); track iv.id) {
                    <app-interview-card
                      [interview]="iv"
                      (reschedule)="openReschedule($event)"
                      (cancel)="askCancel($event)"
                    />
                  }
                </div>
              }
            }

            <!-- Notes (placeholder — no module yet) -->
            @if (tab() === 'notes') {
              <div class="py-10 text-center">
                <p class="text-sm font-medium text-neutral-700">No notes yet</p>
                <p class="mt-1 text-sm text-neutral-500">
                  Recruiter notes and comments will appear here in a future update.
                </p>
              </div>
            }
          } @else {
            <p class="text-sm text-neutral-500">Could not load applicant.</p>
          }
        </div>

        <!-- Stage-transition action buttons (US-REC-004 AC-1/AC-2/AC-4, FR-7/§8) -->
        @if (detail(); as d) {
          @if (!isTerminal()) {
            <div
              class="flex flex-wrap items-center gap-2 border-t border-neutral-100 px-6 py-4"
            >
              @if (nextStageName(); as ns) {
                <button
                  type="button"
                  class="action-btn bg-indigo-600 text-white hover:bg-indigo-700"
                  [disabled]="moving()"
                  (click)="moveForward()"
                >
                  {{ moving() ? 'Moving…' : 'Move to ' + ns }}
                </button>
              }
              @if (prevStageName(); as ps) {
                <button
                  type="button"
                  class="action-btn border border-neutral-200 bg-white text-neutral-700 hover:bg-neutral-50"
                  [disabled]="moving()"
                  (click)="moveBack()"
                >
                  Move back to {{ ps }}
                </button>
              }
              <span class="flex-1"></span>
              <button
                type="button"
                class="action-btn border border-red-200 bg-white text-red-600 hover:bg-red-50"
                [disabled]="moving()"
                (click)="reject()"
              >
                Reject
              </button>
            </div>
          } @else {
            <div class="border-t border-neutral-100 px-6 py-4">
              <p class="text-xs text-neutral-400">
                {{ d.stage }} is a terminal stage — no further moves.
              </p>
            </div>
          }
        }
      </div>
    </div>

    <!-- Reason prompt for rejection (AC-4) / backward move (FR-5) -->
    @if (pendingTo(); as to) {
      <app-stage-reason-dialog
        [applicantName]="fullName()"
        [toStage]="to"
        (confirmed)="confirmReasonMove($event)"
        (cancelled)="cancelReasonMove()"
      />
    }

    <!-- Schedule / reschedule interview slide-over (US-REC-005 AC-1/AC-3) -->
    @if (showInterviewForm() && detail(); as d) {
      <app-interview-form
        [applicantId]="d.id"
        [vacancyId]="d.vacancyId"
        [applicantName]="fullName()"
        [interview]="editingInterview()"
        (saved)="onInterviewSaved()"
        (cancelled)="closeInterviewForm()"
      />
    }

    <!-- Cancel-interview confirmation (US-REC-005 AC-3) -->
    @if (cancellingInterview(); as iv) {
      <app-interview-cancel-dialog
        [applicantName]="fullName()"
        (confirmed)="confirmCancel($event)"
        (cancelled)="dismissCancel()"
      />
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .badge {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        padding: 0.125rem 0.625rem;
        font-size: 0.6875rem;
        font-weight: 500;
        text-transform: capitalize;
      }
      .tab {
        position: relative;
        padding: 0.625rem 0.75rem;
        font-size: 0.8125rem;
        font-weight: 500;
        color: #737373;
        transition: color 150ms ease;
      }
      .tab:hover {
        color: #404040;
      }
      .tab-active {
        color: #4338ca;
      }
      .tab-active::after {
        content: '';
        position: absolute;
        left: 0.5rem;
        right: 0.5rem;
        bottom: -1px;
        height: 2px;
        background: #4f46e5;
        border-radius: 2px;
      }
      .dt {
        font-size: 0.6875rem;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: #a3a3a3;
      }
      .dd {
        margin-top: 0.125rem;
        font-size: 0.875rem;
        color: #404040;
      }
      .action-btn {
        border-radius: 0.5rem;
        padding: 0.5rem 0.875rem;
        font-size: 0.8125rem;
        font-weight: 500;
        box-shadow: 0 1px 2px rgb(0 0 0 / 0.04);
        transition: all 150ms ease;
      }
      .action-btn:disabled {
        cursor: not-allowed;
        opacity: 0.5;
      }
    `,
  ],
})
export class ApplicantDetailComponent {
  private readonly pipelineService = inject(PipelineService);
  private readonly interviewService = inject(InterviewService);
  private readonly toastr = inject(ToastrService);

  /** Applicant id to load; the parent passes the clicked card's id. */
  readonly applicantId = input.required<string>();

  /** Emitted when the user closes the drawer. */
  readonly closed = output<void>();

  /**
   * Emitted after a successful stage move (US-REC-004) so the parent board can
   * reload its columns/counts (FR-7 keeps the Kanban in sync). Carries the new stage.
   */
  readonly moved = output<ApplicantStage>();

  readonly tabs: { id: DetailTab; label: string }[] = [
    { id: 'profile', label: 'Profile' },
    { id: 'resume', label: 'Resume' },
    { id: 'timeline', label: 'Timeline' },
    { id: 'interviews', label: 'Interviews' },
    { id: 'notes', label: 'Notes' },
  ];

  readonly tab = signal<DetailTab>('profile');
  readonly detail = signal<IApplicantDetail | null>(null);
  readonly loading = signal(true);
  readonly downloading = signal(false);

  // ─── Interviews (US-REC-005) ──────────────────────────────
  readonly interviews = signal<IInterview[]>([]);
  readonly interviewsLoading = signal(false);
  /** Whether the schedule/reschedule slide-over is open. */
  readonly showInterviewForm = signal(false);
  /** The interview being rescheduled (AC-3), or null for a new round (AC-1). */
  readonly editingInterview = signal<IInterview | null>(null);
  /** The interview pending cancellation confirmation (AC-3), or null. */
  readonly cancellingInterview = signal<IInterview | null>(null);
  /** Whether the interviews list has been loaded for the current applicant. */
  private interviewsLoaded = false;

  /** True while a stage move is in flight (disables the action buttons). */
  readonly moving = signal(false);
  /**
   * The target stage of a move awaiting a reason (Reject AC-4, or a backward move
   * FR-5). Drives the reason dialog; null when no reason is needed.
   */
  readonly pendingTo = signal<ApplicantStage | null>(null);

  /** Forward stage from the current one, or null at the end of the funnel (AC-1/AC-2). */
  readonly nextStageName = computed<ApplicantStage | null>(() => {
    const d = this.detail();
    return d ? nextStage(d.stage) : null;
  });

  /** Previous active stage for a backward move, or null (FR-5). */
  readonly prevStageName = computed<ApplicantStage | null>(() => {
    const d = this.detail();
    return d ? previousStage(d.stage) : null;
  });

  /** Whether the applicant is in a terminal stage (Hired/Rejected) — no moves. */
  readonly isTerminal = computed<boolean>(() => {
    const d = this.detail();
    return d ? TERMINAL_STAGES.includes(d.stage) : false;
  });

  readonly fullName = computed(() => {
    const d = this.detail();
    return d ? `${d.firstName} ${d.lastName}`.trim() : '';
  });

  readonly initials = computed(() => {
    const d = this.detail();
    if (!d) {
      return '';
    }
    return `${d.firstName?.[0] ?? ''}${d.lastName?.[0] ?? ''}`.toUpperCase();
  });

  readonly appliedRelative = computed(() => {
    const d = this.detail();
    return d ? relativeAppliedTime(d.appliedAt) : '';
  });

  constructor() {
    // Reload whenever the bound applicant id changes.
    effect(() => {
      const id = this.applicantId();
      this.load(id);
    });

    // Lazy-load the interviews list the first time the Interviews tab opens
    // (US-REC-005 AC-4) — avoids an extra request for users who never open it.
    effect(() => {
      const d = this.detail();
      if (this.tab() === 'interviews' && d && !this.interviewsLoaded) {
        this.loadInterviews(d.id);
      }
    });
  }

  private load(id: string): void {
    this.loading.set(true);
    this.detail.set(null);
    this.tab.set('profile');
    // Reset interview state for the newly bound applicant (US-REC-005).
    this.interviews.set([]);
    this.interviewsLoaded = false;
    this.showInterviewForm.set(false);
    this.editingInterview.set(null);
    this.cancellingInterview.set(null);
    this.pipelineService.getApplicant(id).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.toastr.error(PipelineService.parseErrorMessage(err));
      },
    });
  }

  downloadResume(): void {
    const d = this.detail();
    if (!d) {
      return;
    }
    this.downloading.set(true);
    this.pipelineService.downloadResume(d.id).subscribe({
      next: (res) => {
        this.downloading.set(false);
        const blob = res.body;
        if (!blob) {
          this.toastr.error('Resume could not be downloaded.');
          return;
        }
        const filename = PipelineService.filenameFromResponse(
          res,
          d.resumeFileName ?? 'resume',
        );
        this.triggerDownload(blob, filename);
      },
      error: (err: HttpErrorResponse) => {
        this.downloading.set(false);
        this.toastr.error(PipelineService.parseErrorMessage(err));
      },
    });
  }

  /** Create a transient object URL and click an anchor to save the blob. */
  private triggerDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }

  close(): void {
    this.closed.emit();
  }

  // ─── Stage transitions (US-REC-004 AC-1/AC-2/AC-4, FR-5) ──

  /** Move forward to the next funnel stage (AC-1/AC-2). No reason required. */
  moveForward(): void {
    const to = this.nextStageName();
    if (to) {
      this.persistMove(to);
    }
  }

  /** Move back to the previous stage — opens the reason dialog (FR-5). */
  moveBack(): void {
    const to = this.prevStageName();
    if (to) {
      this.pendingTo.set(to);
    }
  }

  /** Reject the applicant — opens the structured rejection dialog (AC-4). */
  reject(): void {
    this.pendingTo.set('Rejected');
  }

  /** Confirm the reason dialog and persist the deferred move (AC-4 / FR-5). */
  confirmReasonMove(result: IStageReasonResult): void {
    const to = this.pendingTo();
    this.pendingTo.set(null);
    if (to) {
      this.persistMove(to, result);
    }
  }

  /** Dismiss the reason dialog without moving. */
  cancelReasonMove(): void {
    this.pendingTo.set(null);
  }

  private persistMove(to: ApplicantStage, reason?: IStageReasonResult): void {
    const d = this.detail();
    if (!d || this.moving()) {
      return;
    }
    // Guard: a reason-required move triggered without a reason re-opens the dialog
    // rather than calling the API (AC-4 reason is mandatory).
    if (!reason && moveRequiresReason(d.stage, to)) {
      this.pendingTo.set(to);
      return;
    }
    this.moving.set(true);
    this.pipelineService
      .changeStage(d.id, {
        toStage: to,
        reason: reason?.reason,
        rejectionReason: reason?.rejectionReason,
        notes: reason?.notes,
      })
      .subscribe({
        next: (res) => {
          this.moving.set(false);
          this.toastr.success(`Moved to ${to}.`);
          // Soft-gate / headcount warnings — the move SUCCEEDED, just warn (BR-4 / FR-1).
          for (const w of res.warnings) {
            this.toastr.warning(w, 'Heads up');
          }
          // Refresh the detail so the timeline + buttons reflect the new stage,
          // and tell the parent board to reload its counts (FR-7).
          this.load(d.id);
          this.moved.emit(to);
        },
        error: (err: HttpErrorResponse) => {
          this.moving.set(false);
          // Vacancy closed/cancelled (FR-8) and other hard failures show verbatim.
          this.toastr.error(PipelineService.parseErrorMessage(err));
        },
      });
  }

  /**
   * Reason text for a timeline entry — prefers the structured rejection-reason
   * label when present (AC-4), else the free-text reason. Empty → no row.
   */
  transitionReason(h: {
    reason?: string | null;
    rejectionReason?: string | null;
  }): string {
    if (h.rejectionReason) {
      return rejectionReasonLabel(h.rejectionReason);
    }
    return h.reason ?? '';
  }

  stageBadge(stage: ApplicantStage): string {
    return STAGE_BADGE[stage] ?? STAGE_BADGE.Applied;
  }

  sourceBadge(source: ApplicantSource): string {
    return SOURCE_BADGE[source] ?? SOURCE_BADGE.public;
  }

  // ─── Interviews (US-REC-005 AC-1/AC-3/AC-4) ───────────────

  /** Load all interview rounds for the applicant (AC-4). */
  private loadInterviews(applicantId: string): void {
    this.interviewsLoading.set(true);
    this.interviewService.listForApplicant(applicantId).subscribe({
      next: (rows) => {
        this.interviews.set(rows);
        this.interviewsLoading.set(false);
        this.interviewsLoaded = true;
      },
      error: (err: HttpErrorResponse) => {
        this.interviewsLoading.set(false);
        this.interviewsLoaded = true;
        this.toastr.error(InterviewService.parseErrorMessage(err));
      },
    });
  }

  /** Open the slide-over to schedule a new round (AC-1). */
  openSchedule(): void {
    this.editingInterview.set(null);
    this.showInterviewForm.set(true);
  }

  /** Open the slide-over pre-filled to reschedule an interview (AC-3). */
  openReschedule(iv: IInterview): void {
    this.editingInterview.set(iv);
    this.showInterviewForm.set(true);
  }

  closeInterviewForm(): void {
    this.showInterviewForm.set(false);
    this.editingInterview.set(null);
  }

  /** After a successful schedule/reschedule, refresh the rounds list. */
  onInterviewSaved(): void {
    this.closeInterviewForm();
    const d = this.detail();
    if (d) {
      this.loadInterviews(d.id);
    }
  }

  /** Ask for confirmation before cancelling an interview (AC-3). */
  askCancel(iv: IInterview): void {
    this.cancellingInterview.set(iv);
  }

  dismissCancel(): void {
    this.cancellingInterview.set(null);
  }

  /** Confirm cancellation — calls the API then refreshes the rounds list (AC-3). */
  confirmCancel(reason?: string): void {
    const iv = this.cancellingInterview();
    this.cancellingInterview.set(null);
    if (!iv) {
      return;
    }
    this.interviewService.cancel(iv.id, reason).subscribe({
      next: () => {
        this.toastr.success('Interview cancelled. Participants notified.');
        const d = this.detail();
        if (d) {
          this.loadInterviews(d.id);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.toastr.error(InterviewService.parseErrorMessage(err));
      },
    });
  }
}
