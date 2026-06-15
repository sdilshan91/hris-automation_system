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
import {
  IApplicantDetail,
  SOURCE_BADGE,
  STAGE_BADGE,
  relativeAppliedTime,
} from '../../models/pipeline.models';
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
  imports: [CommonModule],
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
                      @if (h.reason) {
                        <p class="mt-1 text-sm text-neutral-600">
                          <span class="font-medium">Reason:</span> {{ h.reason }}
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

            <!-- Interviews (placeholder — no module yet) -->
            @if (tab() === 'interviews') {
              <div class="py-10 text-center">
                <p class="text-sm font-medium text-neutral-700">
                  No interviews scheduled
                </p>
                <p class="mt-1 text-sm text-neutral-500">
                  Interview scheduling will appear here once the interviews module
                  is available.
                </p>
              </div>
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
      </div>
    </div>
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
    `,
  ],
})
export class ApplicantDetailComponent {
  private readonly pipelineService = inject(PipelineService);
  private readonly toastr = inject(ToastrService);

  /** Applicant id to load; the parent passes the clicked card's id. */
  readonly applicantId = input.required<string>();

  /** Emitted when the user closes the drawer. */
  readonly closed = output<void>();

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
  }

  private load(id: string): void {
    this.loading.set(true);
    this.detail.set(null);
    this.tab.set('profile');
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

  stageBadge(stage: ApplicantStage): string {
    return STAGE_BADGE[stage] ?? STAGE_BADGE.Applied;
  }

  sourceBadge(source: ApplicantSource): string {
    return SOURCE_BADGE[source] ?? SOURCE_BADGE.public;
  }
}
