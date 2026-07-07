import {
  Component,
  ChangeDetectionStrategy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import { PipService } from '../../services/pip.service';
import {
  CHECKPOINT_STATUS_BADGE,
  CHECKPOINT_STATUS_LABEL,
  CHECKPOINT_STATUS_OPTIONS,
  CheckpointStatus,
  EscalationAction,
  ESCALATION_ACTION_LABEL,
  IPip,
  IPipCheckpoint,
  IPipObjective,
  PIP_CONFIDENTIAL_NOTICE,
  PIP_STATUS_BADGE,
  PIP_STATUS_LABEL,
  PipOutcome,
  addDaysIso,
  canEscalate,
  canRecordCheckpoint,
  canSetOutcome,
  checkpointMarkerPosition,
  isPipHrRole,
  pipTimelineProgress,
} from '../../models/pip.models';

/**
 * US-PRF-008 detail/timeline view (AC-3/AC-4/AC-5/§8). HR + manager reach this under
 * /performance/pips/:pipId. It shows:
 *  - A confidentiality banner (§8) + status badge.
 *  - A duration timeline with checkpoint markers (§8).
 *  - An accordion per objective with its checkpoints.
 *  - "Record Checkpoint" (AC-3, manager OR HR) → a full-screen-on-mobile modal with
 *    a traffic-light status selector, evidence notes, and an optional file attachment.
 *  - HR-ONLY (BR-1) "Set outcome" (AC-4: Successfully Completed / Extended / Not Met)
 *    and, once Not Met, "Confirm escalation" (AC-5). The manager UI never offers
 *    close/extend/escalate — those controls are gated on `isHr`.
 */
@Component({
  selector: 'app-pip-detail',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl px-4 pb-28 pt-6 sm:px-6 lg:px-8">
      <div class="mb-4">
        <a
          routerLink="/performance/pips"
          class="text-sm font-medium text-neutral-500 transition hover:text-neutral-700"
        >
          ← Back to PIPs
        </a>
      </div>

      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (loadError()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ loadError() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (pip(); as p) {
        <!-- §8 confidentiality banner -->
        <div
          class="mb-5 flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-xs text-amber-800"
          role="note"
          data-testid="confidential-banner"
        >
          <span aria-hidden="true">🔒</span>
          <span>{{ confidentialNotice }}</span>
        </div>

        <!-- Header -->
        <div class="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
              {{ p.employeeName }}
            </h1>
            <p class="mt-1 text-sm text-neutral-500">
              {{ p.jobTitle }}
              @if (p.managerName) {
                · Manager {{ p.managerName }}
              }
              @if (p.mentorName) {
                · Mentor {{ p.mentorName }}
              }
            </p>
          </div>
          <span
            class="rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
            [class]="statusBadge(p.status)"
            data-testid="pip-status"
          >
            {{ statusLabel(p.status) }}
          </span>
        </div>

        <!-- Reason -->
        <section
          class="mt-5 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
        >
          <h2 class="text-sm font-semibold text-neutral-800">Reason</h2>
          <p class="mt-1 whitespace-pre-line text-sm text-neutral-600">
            {{ p.reason }}
          </p>
          <p class="mt-3 text-xs text-neutral-400">
            Acknowledgement: {{ p.acknowledgement }}
          </p>
        </section>

        <!-- §8 duration timeline with checkpoint markers -->
        <section
          class="mt-5 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
        >
          <div class="flex items-center justify-between">
            <h2 class="text-sm font-semibold text-neutral-800">Timeline</h2>
            <span class="text-xs text-neutral-400">
              {{ p.startDate }} → {{ p.endDate }}
            </span>
          </div>
          <div
            class="relative mt-6 h-2 rounded-full bg-neutral-100"
            data-testid="timeline-bar"
          >
            <!-- elapsed -->
            <div
              class="absolute inset-y-0 left-0 rounded-full"
              [style.width.%]="progressPct()"
              [style.background-color]="'var(--brand-primary)'"
            ></div>
            <!-- today marker -->
            <div
              class="absolute -top-1 h-4 w-0.5 bg-neutral-900"
              [style.left.%]="progressPct()"
              title="Today"
            ></div>
            <!-- checkpoint markers -->
            @for (m of timelineMarkers(); track m.checkpointId) {
              <div
                class="absolute -top-1.5 h-5 w-5 -translate-x-1/2 rounded-full border-2 border-white shadow"
                [class]="m.colorClass"
                [style.left.%]="m.position"
                [title]="m.title"
                data-testid="checkpoint-marker"
              ></div>
            }
          </div>
        </section>

        <!-- Objectives accordion (§8) -->
        <section class="mt-5 space-y-3">
          <h2 class="text-sm font-semibold text-neutral-800">Objectives</h2>
          @for (obj of p.objectives; track obj.objectiveId) {
            <div
              class="overflow-hidden rounded-xl border border-neutral-200 bg-white shadow-sm"
              data-testid="objective-accordion"
            >
              <button
                type="button"
                class="flex w-full items-center justify-between gap-3 px-5 py-4 text-left transition hover:bg-neutral-50"
                (click)="toggleObjective(obj.objectiveId)"
                [attr.aria-expanded]="isExpanded(obj.objectiveId)"
              >
                <div>
                  <p class="font-medium text-neutral-900">{{ obj.title }}</p>
                  <p class="text-xs text-neutral-500">
                    Due {{ obj.dueDate }} · {{ obj.checkpoints.length }}
                    checkpoint(s)
                  </p>
                </div>
                <span class="text-neutral-400">
                  {{ isExpanded(obj.objectiveId) ? '▴' : '▾' }}
                </span>
              </button>
              @if (isExpanded(obj.objectiveId)) {
                <div class="border-t border-neutral-100 px-5 py-4">
                  <p class="text-sm text-neutral-600">{{ obj.description }}</p>
                  <p class="mt-2 text-xs font-medium text-neutral-400">
                    Success criteria
                  </p>
                  <p class="text-sm text-neutral-600">
                    {{ obj.successCriteria }}
                  </p>

                  <!-- checkpoints for this objective -->
                  <div class="mt-4 space-y-3">
                    @for (cp of obj.checkpoints; track cp.checkpointId) {
                      <div
                        class="rounded-lg border border-neutral-200 p-3"
                        data-testid="checkpoint-row"
                      >
                        <div
                          class="flex items-center justify-between gap-2"
                        >
                          <span class="text-xs text-neutral-500">
                            Due {{ cp.dueDate }}
                          </span>
                          @if (cp.status) {
                            <span
                              class="rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                              [class]="checkpointBadge(cp.status)"
                            >
                              {{ checkpointLabel(cp.status) }}
                            </span>
                          } @else if (cp.overdue) {
                            <span class="text-xs font-medium text-rose-600">
                              Overdue
                            </span>
                          } @else {
                            <span class="text-xs text-neutral-400">
                              Scheduled
                            </span>
                          }
                        </div>
                        @if (cp.notes) {
                          <p class="mt-2 text-sm text-neutral-600">
                            {{ cp.notes }}
                          </p>
                        }
                        @if (cp.recordedBy) {
                          <p class="mt-1 text-xs text-neutral-400">
                            Recorded by {{ cp.recordedBy }}
                            @if (cp.attachmentName) {
                              · 📎 {{ cp.attachmentName }}
                            }
                          </p>
                        }
                        @if (canRecord(cp)) {
                          <button
                            type="button"
                            class="mt-2 rounded-lg px-3 py-1.5 text-xs font-medium text-white shadow-sm transition hover:opacity-90"
                            [style.background-color]="'var(--brand-primary)'"
                            (click)="openCheckpoint(obj, cp)"
                            data-testid="record-checkpoint-btn"
                          >
                            Record checkpoint
                          </button>
                        }
                      </div>
                    }
                  </div>
                </div>
              }
            </div>
          }
        </section>

        <!-- HR-ONLY: outcome + escalation (BR-1) -->
        @if (isHr()) {
          @if (showOutcome()) {
            <section
              class="mt-6 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
              data-testid="outcome-section"
            >
              <h2 class="text-sm font-semibold text-neutral-800">
                Set final outcome
              </h2>
              <p class="mt-1 text-xs text-neutral-500">
                HR-only. Close, extend, or mark this PIP as not met.
              </p>
              <form
                [formGroup]="outcomeForm"
                (ngSubmit)="submitOutcome()"
                class="mt-3 space-y-3"
              >
                <select
                  formControlName="outcome"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                  data-testid="outcome-select"
                >
                  <option value="SuccessfullyCompleted">
                    Successfully completed
                  </option>
                  <option value="Extended">Extended</option>
                  <option value="NotMet">Not met</option>
                </select>
                @if (outcomeForm.value.outcome === 'Extended') {
                  <div data-testid="extend-fields">
                    <label class="mb-1 block text-xs text-neutral-500">
                      New end date
                    </label>
                    <input
                      type="date"
                      formControlName="newEndDate"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 sm:w-auto"
                      data-testid="new-end-date"
                    />
                  </div>
                }
                <div class="flex justify-end">
                  <button
                    type="submit"
                    class="rounded-lg px-5 py-2.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                    [style.background-color]="'var(--brand-primary)'"
                    [disabled]="savingOutcome() || !outcomeValid()"
                    data-testid="submit-outcome"
                  >
                    @if (savingOutcome()) {
                      Saving…
                    } @else {
                      Set outcome
                    }
                  </button>
                </div>
              </form>
            </section>
          }

          @if (showEscalation()) {
            <section
              class="mt-6 rounded-xl border border-rose-200 bg-rose-50 p-5 shadow-sm"
              data-testid="escalation-section"
            >
              <h2 class="text-sm font-semibold text-rose-800">
                Confirm escalation action
              </h2>
              <p class="mt-1 text-xs text-rose-700">
                This PIP was not met. Confirm the configured escalation action.
                This creates an immutable audit record (AC-5).
              </p>
              <form
                [formGroup]="escalationForm"
                (ngSubmit)="submitEscalation()"
                class="mt-3 space-y-3"
              >
                <div>
                  <label class="mb-1 block text-xs text-rose-700">
                    Escalation action
                  </label>
                  <select
                    formControlName="action"
                    class="w-full rounded-lg border border-rose-200 px-3 py-2 text-sm outline-none transition focus:border-rose-400 focus:ring-1"
                    data-testid="escalation-select"
                  >
                    @for (opt of escalationOptions(); track opt) {
                      <option [value]="opt">{{ escalationLabel(opt) }}</option>
                    }
                  </select>
                </div>
                <textarea
                  rows="2"
                  formControlName="note"
                  placeholder="Rationale (optional, for the audit trail)"
                  class="w-full rounded-lg border border-rose-200 px-3 py-2 text-sm outline-none transition focus:border-rose-400 focus:ring-1"
                ></textarea>
                <div class="flex justify-end">
                  <button
                    type="submit"
                    class="rounded-lg bg-rose-600 px-5 py-2.5 text-sm font-medium text-white shadow-sm transition hover:bg-rose-700 disabled:opacity-50"
                    [disabled]="savingEscalation()"
                    data-testid="confirm-escalation"
                  >
                    @if (savingEscalation()) {
                      Confirming…
                    } @else {
                      Confirm escalation
                    }
                  </button>
                </div>
              </form>
            </section>
          }

          @if (pip()!.escalation; as esc) {
            <div
              class="mt-4 rounded-lg border border-neutral-200 bg-white px-4 py-3 text-sm text-neutral-600"
              data-testid="escalation-record"
            >
              Escalation: {{ escalationLabel(esc.action) }} — confirmed by
              {{ esc.confirmedBy }} on {{ esc.confirmedOn }}.
            </div>
          }
        }
      }
    </div>

    <!-- AC-3 checkpoint modal (full-screen on mobile, NFR-5) -->
    @if (checkpointOpen()) {
      <div
        class="fixed inset-0 z-50 flex items-end justify-center bg-black/40 p-0 sm:items-center sm:p-4"
        role="dialog"
        aria-modal="true"
        aria-labelledby="checkpoint-title"
        data-testid="checkpoint-modal"
      >
        <div
          class="flex max-h-full w-full flex-col overflow-y-auto rounded-t-2xl bg-white p-6 shadow-xl sm:max-w-lg sm:rounded-2xl"
        >
          <h2 id="checkpoint-title" class="text-lg font-semibold text-neutral-900">
            Record checkpoint
          </h2>
          <p class="mt-1 text-sm text-neutral-500">
            {{ activeObjective()?.title }}
          </p>

          <form
            [formGroup]="checkpointForm"
            (ngSubmit)="submitCheckpoint()"
            class="mt-4 space-y-4"
          >
            <!-- traffic-light status selector -->
            <div>
              <span class="mb-2 block text-sm font-medium text-neutral-700">
                Status
              </span>
              <div class="grid grid-cols-3 gap-2" role="radiogroup">
                @for (opt of checkpointStatusOptions; track opt.value) {
                  <button
                    type="button"
                    class="flex flex-col items-center gap-1.5 rounded-lg border px-2 py-3 text-xs font-medium transition focus:outline-none focus:ring-2"
                    [class]="
                      checkpointStatus() === opt.value
                        ? opt.selectedClass
                        : 'border-neutral-200 hover:bg-neutral-50'
                    "
                    [attr.aria-pressed]="checkpointStatus() === opt.value"
                    (click)="selectCheckpointStatus(opt.value)"
                    [attr.data-testid]="'status-' + opt.value"
                  >
                    <span
                      class="h-4 w-4 rounded-full"
                      [class]="opt.dotClass"
                    ></span>
                    {{ opt.label }}
                  </button>
                }
              </div>
            </div>

            <div>
              <label
                class="mb-1 block text-sm font-medium text-neutral-700"
                for="cp-notes"
              >
                Evidence / progress notes
              </label>
              <textarea
                id="cp-notes"
                rows="4"
                formControlName="notes"
                class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                placeholder="What evidence supports this assessment?"
                data-testid="checkpoint-notes"
              ></textarea>
            </div>

            <div>
              <label
                class="mb-1 block text-sm font-medium text-neutral-700"
                for="cp-file"
              >
                Attachment (optional)
              </label>
              <input
                id="cp-file"
                type="file"
                (change)="onFileSelected($event)"
                class="block w-full text-sm text-neutral-500 file:mr-3 file:rounded-lg file:border-0 file:bg-neutral-100 file:px-3 file:py-2 file:text-sm file:font-medium"
                data-testid="checkpoint-file"
              />
            </div>

            <div class="flex flex-col gap-2 sm:flex-row sm:justify-end">
              <button
                type="button"
                class="rounded-lg px-4 py-2.5 text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
                (click)="closeCheckpoint()"
                data-testid="checkpoint-cancel"
              >
                Cancel
              </button>
              <button
                type="submit"
                class="rounded-lg px-5 py-2.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                [style.background-color]="'var(--brand-primary)'"
                [disabled]="savingCheckpoint() || !checkpointValid()"
                data-testid="submit-checkpoint"
              >
                @if (savingCheckpoint()) {
                  Saving…
                } @else {
                  Save checkpoint
                }
              </button>
            </div>
          </form>
        </div>
      </div>
    }
  `,
})
export class PipDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(PipService);
  private readonly auth = inject(AuthService);
  private readonly toastr = inject(ToastrService);

  readonly confidentialNotice = PIP_CONFIDENTIAL_NOTICE;
  readonly checkpointStatusOptions = CHECKPOINT_STATUS_OPTIONS;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly pip = signal<IPip | null>(null);
  private readonly expanded = signal<Set<string>>(new Set());

  // BR-1: HR may set outcome/escalate; managers cannot.
  readonly isHr = computed(() => isPipHrRole(this.auth.roles()));

  readonly escalationOptions = computed<EscalationAction[]>(() => [
    this.pip()?.escalationAction ?? 'TerminationRecommendation',
  ]);

  // Checkpoint modal state.
  readonly checkpointOpen = signal(false);
  readonly savingCheckpoint = signal(false);
  readonly activeObjective = signal<IPipObjective | null>(null);
  private activeCheckpoint: IPipCheckpoint | null = null;
  private checkpointFile: File | null = null;

  readonly checkpointForm: FormGroup = this.fb.group({
    status: ['', Validators.required],
    notes: ['', [Validators.required, Validators.minLength(5)]],
  });
  readonly checkpointStatus = signal<CheckpointStatus | ''>('');

  // Outcome form (HR-only).
  readonly savingOutcome = signal(false);
  readonly outcomeForm: FormGroup = this.fb.group({
    outcome: ['SuccessfullyCompleted', Validators.required],
    newEndDate: [''],
  });

  // Escalation form (HR-only).
  readonly savingEscalation = signal(false);
  readonly escalationForm: FormGroup = this.fb.group({
    action: ['', Validators.required],
    note: [''],
  });

  private pipId = '';

  readonly progressPct = computed(() => {
    const p = this.pip();
    return p ? pipTimelineProgress(p) * 100 : 0;
  });

  readonly timelineMarkers = computed(() => {
    const p = this.pip();
    if (!p) {
      return [];
    }
    return p.objectives.flatMap((obj) =>
      obj.checkpoints.map((cp) => ({
        checkpointId: cp.checkpointId,
        position: checkpointMarkerPosition(p, cp) * 100,
        colorClass: cp.status
          ? markerColor(cp.status)
          : 'bg-neutral-300',
        title: `${obj.title} — ${cp.status ?? 'scheduled'} (due ${cp.dueDate})`,
      })),
    );
  });

  readonly showOutcome = computed(() => {
    const p = this.pip();
    return !!p && canSetOutcome(p.status, this.isHr());
  });

  readonly showEscalation = computed(() => {
    const p = this.pip();
    return !!p && canEscalate(p, this.isHr());
  });

  ngOnInit(): void {
    this.pipId = this.route.snapshot.paramMap.get('pipId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service.getPip(this.pipId).subscribe({
      next: (pip) => {
        this.pip.set(pip);
        // Expand the first objective by default for quick scanning.
        if (pip.objectives.length > 0) {
          this.expanded.set(new Set([pip.objectives[0].objectiveId]));
        }
        this.escalationForm.patchValue({ action: pip.escalationAction });
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load this PIP.');
        this.loading.set(false);
      },
    });
  }

  // ─── Accordion ──────────────────────────────────────────────

  isExpanded(objectiveId: string): boolean {
    return this.expanded().has(objectiveId);
  }

  toggleObjective(objectiveId: string): void {
    const next = new Set(this.expanded());
    if (next.has(objectiveId)) {
      next.delete(objectiveId);
    } else {
      next.add(objectiveId);
    }
    this.expanded.set(next);
  }

  // ─── Checkpoint recording (AC-3) ────────────────────────────

  canRecord(cp: IPipCheckpoint): boolean {
    const p = this.pip();
    return !!p && canRecordCheckpoint(p, cp);
  }

  openCheckpoint(objective: IPipObjective, checkpoint: IPipCheckpoint): void {
    this.activeObjective.set(objective);
    this.activeCheckpoint = checkpoint;
    this.checkpointFile = null;
    this.checkpointStatus.set('');
    this.checkpointForm.reset({ status: '', notes: '' });
    this.checkpointOpen.set(true);
  }

  closeCheckpoint(): void {
    this.checkpointOpen.set(false);
  }

  selectCheckpointStatus(status: CheckpointStatus): void {
    this.checkpointStatus.set(status);
    this.checkpointForm.patchValue({ status });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.checkpointFile = input.files?.[0] ?? null;
  }

  checkpointValid(): boolean {
    return this.checkpointForm.valid && this.checkpointStatus() !== '';
  }

  submitCheckpoint(): void {
    if (
      !this.checkpointValid() ||
      this.savingCheckpoint() ||
      !this.activeCheckpoint
    ) {
      return;
    }
    const status = this.checkpointStatus() as CheckpointStatus;
    this.savingCheckpoint.set(true);
    this.service
      .recordCheckpoint(
        this.pipId,
        { status, notes: this.checkpointForm.value.notes },
        this.checkpointFile,
      )
      .subscribe({
        next: (pip) => {
          this.savingCheckpoint.set(false);
          this.checkpointOpen.set(false);
          this.pip.set(pip);
          this.toastr.success(
            'Checkpoint recorded. The employee has been notified.',
          );
        },
        error: () => {
          this.savingCheckpoint.set(false);
          this.toastr.error('Could not record the checkpoint. Please try again.');
        },
      });
  }

  // ─── Outcome (AC-4, HR-only) ────────────────────────────────

  outcomeValid(): boolean {
    if (this.outcomeForm.value.outcome === 'Extended') {
      return !!this.outcomeForm.value.newEndDate;
    }
    return true;
  }

  submitOutcome(): void {
    const p = this.pip();
    if (!p || !canSetOutcome(p.status, this.isHr()) || this.savingOutcome()) {
      return;
    }
    if (!this.outcomeValid()) {
      this.toastr.error('A new end date is required to extend the PIP.');
      return;
    }
    const outcome = this.outcomeForm.value.outcome as PipOutcome;
    const newEndDate =
      outcome === 'Extended'
        ? (this.outcomeForm.value.newEndDate as string)
        : undefined;

    this.savingOutcome.set(true);
    this.service
      .setOutcome(this.pipId, { outcome, newEndDate })
      .subscribe({
        next: (pip) => {
          this.savingOutcome.set(false);
          this.pip.set(pip);
          this.toastr.success('PIP outcome recorded.');
        },
        error: () => {
          this.savingOutcome.set(false);
          this.toastr.error('Could not set the outcome. Please try again.');
        },
      });
  }

  // ─── Escalation (AC-5, HR-only) ─────────────────────────────

  submitEscalation(): void {
    const p = this.pip();
    if (!p || !canEscalate(p, this.isHr()) || this.savingEscalation()) {
      return;
    }
    this.savingEscalation.set(true);
    this.service
      .escalate(this.pipId, {
        action: this.escalationForm.value.action as EscalationAction,
        note: this.escalationForm.value.note || undefined,
      })
      .subscribe({
        next: (pip) => {
          this.savingEscalation.set(false);
          this.pip.set(pip);
          this.toastr.success(
            'Escalation confirmed. Stakeholders have been notified.',
          );
        },
        error: () => {
          this.savingEscalation.set(false);
          this.toastr.error('Could not confirm escalation. Please try again.');
        },
      });
  }

  // ─── Display helpers ────────────────────────────────────────

  statusBadge = (status: IPip['status']): string => PIP_STATUS_BADGE[status];
  statusLabel = (status: IPip['status']): string => PIP_STATUS_LABEL[status];
  checkpointBadge = (status: CheckpointStatus): string =>
    CHECKPOINT_STATUS_BADGE[status];
  checkpointLabel = (status: CheckpointStatus): string =>
    CHECKPOINT_STATUS_LABEL[status];
  escalationLabel = (action: EscalationAction): string =>
    ESCALATION_ACTION_LABEL[action];

  /** Used by tests / template helpers for extend preset suggestions. */
  protected suggestExtendEnd(days: number): string {
    const p = this.pip();
    return p ? addDaysIso(p.endDate, days) : '';
  }
}

/** Map a recorded checkpoint status to a marker swatch colour. */
function markerColor(status: CheckpointStatus): string {
  switch (status) {
    case 'OnTrack':
      return 'bg-emerald-500';
    case 'AtRisk':
      return 'bg-amber-500';
    case 'NotMet':
      return 'bg-rose-500';
  }
}
