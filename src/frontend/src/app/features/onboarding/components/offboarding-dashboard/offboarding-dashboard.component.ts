import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { OffboardingService } from '../../services/offboarding.service';
import {
  IOffboardingInstance,
  IOffboardingTask,
  IAssetReturnLine,
  DepartmentClearanceStatus,
  TaskClearanceStatus,
  OffboardingReason,
  PendingBlockReason,
  OFFBOARDING_REASON_LABEL,
  ClearanceDecision,
  MAX_REMARKS_LEN,
  clearanceChipClass,
  clearanceLabel,
  trafficLightClass,
  taskClearanceChipClass,
  taskClearanceLabel,
  pendingMandatoryTitles,
  canComplete,
  assetReturnLines,
} from '../../models/offboarding.models';
import { AssetCondition, ASSET_CONDITIONS } from '../../models/onboarding-asset.models';

/**
 * US-ONB-005: Offboarding Clearance Dashboard (AC-3 / AC-5 / FR-4 / UI/UX §8).
 *
 * Routed at `offboarding/:offboardingId` (routed input() offboardingId with a
 * route-snapshot fallback). Renders:
 *   - a top summary bar of traffic-light icons (green/yellow/red) per department;
 *   - a Kanban board, one column per clearance department, with task cards +
 *     status chips and per-task approve / pending-issues actions (with remarks);
 *     on mobile (<768px) the board collapses to a vertical accordion grouped by
 *     department;
 *   - a dedicated Asset Return section (assets carrying a linkedAssetId) with a
 *     "Mark Returned" button + condition dropdown + dispose toggle (AC-2);
 *   - a "Complete Offboarding" button disabled until every mandatory task is
 *     cleared (BR-2); the tooltip lists remaining items, a confirmation modal
 *     summarizes account deactivation + F&F trigger (AC-4), and a 409 pending
 *     list is surfaced inline (AC-5).
 *
 * Every mutation re-renders from the refreshed instance the server returns, so
 * the overall clearance + progress recompute on the backend (AC-3). Standalone,
 * signals-first, OnPush. WCAG 2.1 AA throughout.
 */
@Component({
  selector: 'app-offboarding-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('modalIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.96)' }),
        animate('180ms ease-out', style({ opacity: 1, transform: 'scale(1)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container max-w-6xl">
      <div class="mb-6">
        <a routerLink="/onboarding" class="text-xs font-medium text-brand-600 hover:text-brand-700">
          &larr; Back to onboarding
        </a>
        <h1 class="mt-2 text-2xl font-semibold text-neutral-900 tracking-tight">
          Exit Clearance
        </h1>
        @if (instance(); as inst) {
          <p class="mt-1 text-sm text-neutral-500">
            {{ inst.employeeName || 'Departing employee' }} ·
            {{ reasonLabel(inst.reason) }} · Last working day {{ inst.lastWorkingDay }}
          </p>
        }
      </div>

      @if (loading()) {
        <div class="space-y-3" aria-busy="true">
          <div class="skeleton h-12"></div>
          <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div class="skeleton h-40"></div>
            <div class="skeleton h-40"></div>
            <div class="skeleton h-40"></div>
            <div class="skeleton h-40"></div>
          </div>
        </div>
      } @else if (loadError()) {
        <div class="submit-error" role="alert">{{ loadError() }}</div>
      } @else if (instance(); as inst) {
        <!-- Summary: progress + per-department traffic lights (FR-4) -->
        <section class="summary-bar" aria-label="Clearance summary">
          <div class="flex items-center gap-3 flex-wrap">
            <div class="progress-pill">
              <span class="text-xs font-medium text-neutral-500">Progress</span>
              <div class="progress-track w-32">
                <div class="progress-fill" [style.width.%]="inst.progressPercent"></div>
              </div>
              <span class="text-xs font-semibold text-neutral-700">{{ inst.progressPercent }}%</span>
            </div>
            <span class="overall-chip" [ngClass]="chipClass(inst.overallClearance)">
              Overall: {{ statusLabel(inst.overallClearance) }}
            </span>
          </div>

          <ul class="flex items-center gap-4 flex-wrap mt-3" role="list">
            @for (dept of inst.departments; track dept.department) {
              <li class="flex items-center gap-2">
                <span
                  class="traffic-light"
                  [ngClass]="lightClass(dept.clearanceStatus)"
                  [attr.aria-label]="dept.department + ': ' + statusLabel(dept.clearanceStatus)"
                  role="img"
                ></span>
                <span class="text-sm text-neutral-700">{{ dept.department }}</span>
              </li>
            }
          </ul>
        </section>

        <!-- Kanban board (>=md) / accordion (<md) -->
        <div class="mt-6">
          <!-- Kanban columns: hidden below md (UI/UX §8 responsive) -->
          <div class="hidden md:grid gap-4" [style.gridTemplateColumns]="columnTemplate(inst)">
            @for (dept of inst.departments; track dept.department) {
              <section class="kanban-col" [attr.aria-label]="dept.department + ' clearance'">
                <header class="kanban-col-head">
                  <span class="traffic-light" [ngClass]="lightClass(dept.clearanceStatus)" role="img"
                    [attr.aria-label]="statusLabel(dept.clearanceStatus)"></span>
                  <h2 class="text-sm font-semibold text-neutral-800">{{ dept.department }}</h2>
                  <span class="ml-auto text-xs text-neutral-400">{{ dept.tasks.length }}</span>
                </header>
                <div class="space-y-3 mt-3">
                  @for (task of dept.tasks; track task.id) {
                    <ng-container [ngTemplateOutlet]="taskCard" [ngTemplateOutletContext]="{ $implicit: task }" />
                  }
                </div>
              </section>
            }
          </div>

          <!-- Accordion: shown below md -->
          <div class="md:hidden space-y-3">
            @for (dept of inst.departments; track dept.department) {
              <section class="kanban-col">
                <button
                  type="button"
                  class="accordion-head"
                  (click)="toggleDept(dept.department)"
                  [attr.aria-expanded]="isExpanded(dept.department)"
                >
                  <span class="traffic-light" [ngClass]="lightClass(dept.clearanceStatus)" role="img"
                    [attr.aria-label]="statusLabel(dept.clearanceStatus)"></span>
                  <h2 class="text-sm font-semibold text-neutral-800">{{ dept.department }}</h2>
                  <span class="ml-auto text-xs text-neutral-400">{{ dept.tasks.length }}</span>
                </button>
                @if (isExpanded(dept.department)) {
                  <div class="space-y-3 mt-3" @fadeIn>
                    @for (task of dept.tasks; track task.id) {
                      <ng-container [ngTemplateOutlet]="taskCard" [ngTemplateOutletContext]="{ $implicit: task }" />
                    }
                  </div>
                }
              </section>
            }
          </div>
        </div>

        <!-- Asset Return section (AC-2 / UI/UX §8) -->
        @if (assetLines().length > 0) {
          <section class="card mt-6" aria-label="Asset return">
            <h2 class="text-sm font-semibold text-neutral-800 mb-3">Asset Return</h2>
            <ul class="space-y-3" role="list">
              @for (line of assetLines(); track line.taskId) {
                <li class="asset-row">
                  <div class="min-w-0">
                    <p class="text-sm font-medium text-neutral-800 truncate">{{ line.title }}</p>
                    <p class="text-xs text-neutral-400">Asset {{ line.assetId }}</p>
                  </div>
                  @if (line.status === 'Completed') {
                    <span class="chip chip-cleared ml-auto">Returned</span>
                  } @else {
                    <div class="flex items-center gap-2 ml-auto flex-wrap justify-end">
                      <label class="sr-only" [attr.for]="'cond-' + line.taskId">Returned condition</label>
                      <select
                        [id]="'cond-' + line.taskId"
                        class="input-notion w-auto py-1.5"
                        [ngModel]="conditionFor(line.taskId)"
                        (ngModelChange)="setReturnCondition(line.taskId, $event)"
                      >
                        @for (cond of conditions; track cond) {
                          <option [value]="cond">{{ cond }}</option>
                        }
                      </select>
                      <label class="dispose-toggle">
                        <input
                          type="checkbox"
                          [ngModel]="disposeFor(line.taskId)"
                          (ngModelChange)="setDispose(line.taskId, $event)"
                        />
                        Dispose
                      </label>
                      <button
                        type="button"
                        class="btn-secondary py-1.5"
                        [disabled]="busyTask() === line.taskId"
                        (click)="markReturned(line)"
                      >
                        @if (busyTask() === line.taskId) {
                          <span class="btn-spinner-dark"></span> Saving…
                        } @else {
                          Mark Returned
                        }
                      </button>
                    </div>
                  }
                </li>
              }
            </ul>
          </section>
        }

        <!-- Complete (AC-4 / AC-5 / BR-2) -->
        <section class="mt-6 flex flex-col items-end gap-2">
          @if (completeError()) {
            <div class="submit-error w-full" role="alert" @fadeIn>{{ completeError() }}</div>
          }
          @if (pendingTitles().length > 0) {
            <p class="text-xs text-neutral-500 text-right" id="pending-hint">
              {{ pendingTitles().length }} mandatory item(s) remaining
            </p>
          }
          <button
            type="button"
            class="btn-primary"
            [disabled]="!canFinish() || finishing()"
            [attr.title]="pendingTitles().length ? 'Pending: ' + pendingTitles().join(', ') : null"
            [attr.aria-describedby]="pendingTitles().length ? 'pending-hint' : null"
            (click)="openConfirm()"
          >
            @if (inst.status === 'Completed') {
              Offboarding Completed
            } @else {
              Complete Offboarding
            }
          </button>
        </section>
      }

      <!-- Confirmation modal (AC-4) -->
      @if (confirmOpen()) {
        <div class="modal-backdrop" (click)="closeConfirm()" @fadeIn>
          <div
            class="modal-panel"
            role="dialog"
            aria-modal="true"
            aria-labelledby="confirm-title"
            (click)="$event.stopPropagation()"
            @modalIn
          >
            <h2 id="confirm-title" class="text-lg font-semibold text-neutral-900">
              Complete offboarding?
            </h2>
            <p class="text-sm text-neutral-500 mt-2">
              This is irreversible. Completing offboarding will:
            </p>
            <ul class="mt-3 space-y-1.5 text-sm text-neutral-700 list-disc pl-5">
              <li>Change the employee's status to terminated / resigned.</li>
              <li>Deactivate the user account (they can no longer log in).</li>
              <li>Trigger the Full &amp; Final (F&amp;F) settlement notification to Payroll.</li>
              <li>Revoke all active sessions for the employee.</li>
            </ul>
            <div class="flex items-center justify-end gap-2 mt-6">
              <button type="button" class="btn-secondary" (click)="closeConfirm()">Cancel</button>
              <button type="button" class="btn-primary" [disabled]="finishing()" (click)="confirmComplete()">
                @if (finishing()) {
                  <span class="btn-spinner"></span> Completing…
                } @else {
                  Yes, complete
                }
              </button>
            </div>
          </div>
        </div>
      }

      <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>
    </div>

    <!-- Reusable task card (Kanban + accordion) -->
    <ng-template #taskCard let-task>
      <article class="task-card" @fadeIn>
        <div class="flex items-start justify-between gap-2">
          <div class="min-w-0">
            <p class="text-sm font-medium text-neutral-800">
              {{ task.title }}
              @if (task.isMandatory) {
                <span class="mandatory-dot" title="Mandatory" aria-label="Mandatory">*</span>
              }
            </p>
            <p class="text-xs text-neutral-400 mt-0.5">{{ task.responsibleRole }} · due {{ task.dueDate }}</p>
          </div>
          <span class="chip" [ngClass]="taskChipClass(task.clearanceStatus)">
            {{ taskStatusLabel(task.clearanceStatus) }}
          </span>
        </div>

        @if (task.remarks) {
          <p class="text-xs text-neutral-500 mt-2 italic">"{{ task.remarks }}"</p>
        }

        @if (task.clearanceStatus !== 'approved') {
          @if (editingTask() === task.id) {
            <div class="mt-3 space-y-2" @fadeIn>
              <label class="sr-only" [attr.for]="'remarks-' + task.id">Remarks</label>
              <textarea
                [id]="'remarks-' + task.id"
                class="input-notion text-xs"
                rows="2"
                [maxlength]="maxRemarks"
                [ngModel]="remarksDraft()"
                (ngModelChange)="remarksDraft.set($event)"
                placeholder="Remarks (optional)…"
              ></textarea>
              <div class="flex items-center gap-2">
                <button type="button" class="btn-approve" [disabled]="busyTask() === task.id"
                  (click)="recordClearance(task, 'approved')">Approve</button>
                <button type="button" class="btn-issues" [disabled]="busyTask() === task.id"
                  (click)="recordClearance(task, 'pending_issues')">Pending Issues</button>
                <button type="button" class="btn-link ml-auto" (click)="cancelEdit()">Cancel</button>
              </div>
            </div>
          } @else {
            <button type="button" class="btn-link mt-2" (click)="startEdit(task)">
              Record clearance
            </button>
          }
        }
      </article>
    </ng-template>
  `,
  styles: [`
    :host { display: block; }
    .card { @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-5; }
    .skeleton { @apply rounded-xl bg-neutral-100 animate-pulse; }

    .summary-bar { @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-4; }
    .progress-pill { @apply inline-flex items-center gap-2 rounded-lg bg-neutral-50 px-3 py-1.5; }
    .progress-track { @apply h-2 rounded-full bg-neutral-200 overflow-hidden; }
    .progress-fill { @apply h-full rounded-full bg-brand-500 transition-all duration-300; }

    .traffic-light { @apply inline-block w-3 h-3 rounded-full; }
    .light-green { @apply bg-green-500; }
    .light-yellow { @apply bg-yellow-400; }
    .light-red { @apply bg-red-500; }

    .overall-chip, .chip {
      @apply inline-flex items-center text-xs font-medium px-2.5 py-1 rounded-full ring-1 ring-inset;
    }
    .chip-cleared { @apply bg-green-50 text-green-700 ring-green-200; }
    .chip-issues { @apply bg-yellow-50 text-yellow-700 ring-yellow-200; }
    .chip-pending { @apply bg-red-50 text-red-700 ring-red-200; }

    .kanban-col { @apply rounded-xl bg-neutral-50/60 border border-neutral-100 p-4; }
    .kanban-col-head { @apply flex items-center gap-2; }
    .accordion-head { @apply w-full flex items-center gap-2 text-left; }

    .task-card { @apply rounded-lg bg-white border border-neutral-100 shadow-sm p-3; }
    .mandatory-dot { @apply text-red-500 font-bold; }

    .asset-row { @apply flex items-center gap-3 rounded-lg bg-neutral-50/60 border border-neutral-100 px-3 py-2; }
    .dispose-toggle { @apply inline-flex items-center gap-1.5 text-xs text-neutral-600; }

    .label-notion { @apply block text-xs font-medium text-neutral-600 mb-1; }
    .input-notion {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900
        transition-colors duration-150 focus:outline-none focus:ring-2 focus:ring-brand-200 focus:border-brand-400;
    }
    .submit-error { @apply rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700; }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2 text-sm
        font-medium text-white transition-all duration-200 hover:bg-brand-700
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-3 py-2 text-sm
        font-medium text-neutral-700 ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-approve {
      @apply inline-flex items-center rounded-lg bg-green-600 px-3 py-1.5 text-xs font-medium text-white
        transition-colors hover:bg-green-700 disabled:opacity-50;
    }
    .btn-issues {
      @apply inline-flex items-center rounded-lg bg-yellow-500 px-3 py-1.5 text-xs font-medium text-white
        transition-colors hover:bg-yellow-600 disabled:opacity-50;
    }
    .btn-link { @apply text-xs font-medium text-brand-600 hover:text-brand-700; }

    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    .btn-spinner-dark {
      @apply inline-block w-4 h-4 mr-2 border-2 border-neutral-300 border-t-neutral-600 rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    .modal-backdrop {
      @apply fixed inset-0 z-40 flex items-center justify-center bg-black/30 p-4;
    }
    .modal-panel {
      @apply w-full max-w-md rounded-xl bg-white shadow-xl p-6;
    }
  `],
})
export class OffboardingDashboardComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly toastr = inject(ToastrService);
  private readonly offboardingService = inject(OffboardingService);
  private readonly destroy$ = new Subject<void>();

  /** Routed input — the offboarding instance id (AC-3). */
  readonly offboardingId = input<string>('');

  readonly conditions = ASSET_CONDITIONS;
  readonly maxRemarks = MAX_REMARKS_LEN;

  readonly instance = signal<IOffboardingInstance | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly liveMessage = signal('');

  // Per-task interaction state.
  readonly editingTask = signal<string | null>(null);
  readonly remarksDraft = signal<string>('');
  readonly busyTask = signal<string | null>(null);

  // Asset-return per-row state.
  readonly returnCondition = signal<Record<string, AssetCondition>>({});
  readonly dispose = signal<Record<string, boolean>>({});

  // Mobile accordion expansion.
  readonly expanded = signal<Set<string>>(new Set());

  // Complete flow.
  readonly confirmOpen = signal(false);
  readonly finishing = signal(false);
  readonly completeError = signal<string | null>(null);

  // Derived (AC-5 / BR-2).
  readonly pendingTitles = computed(() => pendingMandatoryTitles(this.instance()));
  readonly canFinish = computed(() => canComplete(this.instance()));
  readonly assetLines = computed<IAssetReturnLine[]>(() => assetReturnLines(this.instance()));

  ngOnInit(): void {
    const id = this.offboardingId() || this.route.snapshot.paramMap.get('offboardingId') || '';
    if (!id) {
      this.loading.set(false);
      this.loadError.set('No offboarding instance specified.');
      return;
    }
    this.load(id);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private load(id: string): void {
    this.loading.set(true);
    this.offboardingService
      .getById(id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (inst) => {
          this.loading.set(false);
          this.instance.set(inst);
        },
        error: (err: HttpErrorResponse) => {
          this.loading.set(false);
          this.loadError.set(OffboardingService.parseErrorMessage(err));
        },
      });
  }

  // ─── Display helpers ────────────────────────────────────────
  chipClass(status: DepartmentClearanceStatus): string {
    return clearanceChipClass(status);
  }
  lightClass(status: DepartmentClearanceStatus): string {
    return trafficLightClass(status);
  }
  statusLabel(status: DepartmentClearanceStatus): string {
    return clearanceLabel(status);
  }

  // A task's verdict is a DIFFERENT vocabulary from a department's traffic light
  // ('approved'/'pending_issues'/null vs 'cleared'/'issues'/'pending'). Feeding one to the other is
  // exactly the confusion that disabled the Complete button forever, so they get separate helpers.
  taskChipClass(status: TaskClearanceStatus | null): string {
    return taskClearanceChipClass(status);
  }
  taskStatusLabel(status: TaskClearanceStatus | null): string {
    return taskClearanceLabel(status);
  }

  /** Display text for why a mandatory item blocks completion (AC-5). */
  blockReasonLabel(reason: PendingBlockReason | null): string {
    switch (reason) {
      case 'clearance_not_approved':
        return 'clearance not approved';
      case 'not_completed':
        return 'not completed';
      default:
        return 'pending';
    }
  }

  /**
   * Display text for the wire reason token — `ContractEnd` must not reach the screen as-is. A `null` reason
   * means the API sent a token this build does not know; say so rather than inventing one.
   */
  reasonLabel(reason: OffboardingReason | null): string {
    return reason === null ? 'Reason unavailable' : OFFBOARDING_REASON_LABEL[reason];
  }
  columnTemplate(inst: IOffboardingInstance): string {
    return `repeat(${inst.departments.length}, minmax(0, 1fr))`;
  }

  // ─── Mobile accordion ───────────────────────────────────────
  toggleDept(department: string): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      next.has(department) ? next.delete(department) : next.add(department);
      return next;
    });
  }
  isExpanded(department: string): boolean {
    return this.expanded().has(department);
  }

  // ─── Per-task clearance (AC-3) ──────────────────────────────
  startEdit(task: IOffboardingTask): void {
    this.editingTask.set(task.id);
    this.remarksDraft.set(task.remarks ?? '');
  }
  cancelEdit(): void {
    this.editingTask.set(null);
    this.remarksDraft.set('');
  }

  recordClearance(task: IOffboardingTask, status: ClearanceDecision): void {
    const remarks = this.remarksDraft().trim() || null;
    this.busyTask.set(task.id);
    this.offboardingService
      .recordClearance(task.id, { status, remarks })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (inst) => {
          this.busyTask.set(null);
          this.editingTask.set(null);
          this.remarksDraft.set('');
          this.instance.set(inst);
          const verb = status === 'approved' ? 'approved' : 'flagged with issues';
          this.liveMessage.set(`${task.title} ${verb}.`);
          this.toastr.success(`Clearance ${verb}.`);
        },
        error: (err: HttpErrorResponse) => {
          this.busyTask.set(null);
          this.toastr.error(OffboardingService.parseErrorMessage(err));
        },
      });
  }

  // ─── Asset return (AC-2) ────────────────────────────────────
  setReturnCondition(taskId: string, condition: AssetCondition): void {
    this.returnCondition.update((m) => ({ ...m, [taskId]: condition }));
  }
  setDispose(taskId: string, value: boolean): void {
    this.dispose.update((m) => ({ ...m, [taskId]: value }));
  }
  /** Current return-condition selection for a row (defaults to 'Good'). */
  conditionFor(taskId: string): AssetCondition {
    return this.returnCondition()[taskId] ?? 'Good';
  }
  /** Current dispose toggle for a row (defaults to false). */
  disposeFor(taskId: string): boolean {
    return this.dispose()[taskId] ?? false;
  }

  markReturned(line: IAssetReturnLine): void {
    const condition = this.returnCondition()[line.taskId] ?? 'Good';
    const disposed = this.dispose()[line.taskId] ?? false;
    this.busyTask.set(line.taskId);
    this.offboardingService
      .returnAsset(line.taskId, { assetId: line.assetId, condition, disposed })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (inst) => {
          this.busyTask.set(null);
          this.instance.set(inst);
          this.liveMessage.set(`${line.title} marked returned.`);
          this.toastr.success('Asset marked returned.');
        },
        error: (err: HttpErrorResponse) => {
          this.busyTask.set(null);
          this.toastr.error(OffboardingService.parseErrorMessage(err));
        },
      });
  }

  // ─── Complete (AC-4 / AC-5) ─────────────────────────────────
  openConfirm(): void {
    this.completeError.set(null);
    this.confirmOpen.set(true);
  }
  closeConfirm(): void {
    this.confirmOpen.set(false);
  }

  confirmComplete(): void {
    const inst = this.instance();
    if (!inst) return;
    this.finishing.set(true);
    this.completeError.set(null);
    this.offboardingService
      .complete(inst.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          this.finishing.set(false);
          this.confirmOpen.set(false);
          this.instance.set(updated);
          this.liveMessage.set('Offboarding completed.');
          this.toastr.success('Offboarding completed.');
        },
        error: (err: HttpErrorResponse) => {
          this.finishing.set(false);
          this.confirmOpen.set(false);
          // AC-5: surface the structured pending-mandatory list inline.
          const pending = OffboardingService.parseCompleteBlocked(err);
          if (pending) {
            // AC-5 asks for WHY, not just what. The server distinguishes "nobody has completed this yet"
            // from "a department refused it" — two situations needing different action from HR — and the
            // list used to drop that distinction on the floor.
            const detail = pending
              .map((p) => `${p.title} (${this.blockReasonLabel(p.reason)})`)
              .join(', ');
            this.completeError.set(
              `Cannot complete offboarding. The following mandatory items are pending: ${detail}.`,
            );
          } else {
            this.completeError.set(OffboardingService.parseErrorMessage(err));
          }
        },
      });
  }
}
