import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import {
  CdkDragDrop,
  DragDropModule,
  moveItemInArray,
  transferArrayItem,
} from '@angular/cdk/drag-drop';
import { ToastrService } from 'ngx-toastr';
import { PipelineService } from '../../services/pipeline.service';
import {
  ApplicantDetailComponent,
} from '../applicant-detail/applicant-detail.component';
import {
  StageReasonDialogComponent,
  IStageReasonResult,
} from '../stage-reason-dialog/stage-reason-dialog.component';
import {
  IApplicantCard,
  IPipelineFilters,
  IPipelineStage,
  PIPELINE_STAGES,
  SOURCE_BADGE,
  STAGE_BADGE,
  moveRequiresReason,
  relativeAppliedTime,
  timeInStageLabel,
} from '../../models/pipeline.models';
import { ApplicantSource, ApplicantStage } from '../../models/applicant.models';

/** A pending move awaiting a reason from the dialog (BR-3/BR-4). */
interface IPendingMove {
  card: IApplicantCard;
  from: ApplicantStage;
  to: ApplicantStage;
  /** Snapshot of columns before the optimistic move, for rollback. */
  snapshot: IPipelineStage[];
}

/**
 * US-REC-003: Recruiter applicant pipeline (Kanban) for a vacancy.
 *
 * Kanban board with one column per stage (AC-1/FR-1), per-column count + total
 * (FR-5), CDK drag-and-drop to move cards between stages with optimistic UI +
 * server persist and rollback on failure (AC-2/FR-3/NFR-2). Moving to Rejected
 * (BR-3) or backward (BR-4) prompts for a reason. Sticky filter bar (AC-4/FR-6),
 * a table/list toggle (FR-4), and a detail slide-over on card click (AC-3/FR-7).
 *
 * Mobile (<768px, NFR-4): columns become horizontally scrollable; a stage tab
 * strip lets the user focus one column at a time.
 *
 * Role-gated by the route guard; the backend enforces Recruitment.Read/Manage
 * permissions + tenant RLS (BR-1/BR-2/AC-5).
 */
@Component({
  selector: 'app-applicant-pipeline',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    DragDropModule,
    ApplicantDetailComponent,
    StageReasonDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '220ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-[110rem] px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div
        class="mb-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between"
      >
        <div>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Applicant pipeline
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            {{ boardTitle() }} · {{ total() }} applicant{{
              total() === 1 ? '' : 's'
            }}
          </p>
        </div>

        <!-- View toggle (FR-4) -->
        <div
          class="flex items-center gap-1 self-start rounded-lg bg-neutral-100 p-1 sm:self-auto"
          role="radiogroup"
          aria-label="View mode"
        >
          <button
            type="button"
            class="view-btn"
            role="radio"
            [attr.aria-checked]="viewMode() === 'kanban'"
            [class.view-active]="viewMode() === 'kanban'"
            (click)="viewMode.set('kanban')"
          >
            Board
          </button>
          <button
            type="button"
            class="view-btn"
            role="radio"
            [attr.aria-checked]="viewMode() === 'table'"
            [class.view-active]="viewMode() === 'table'"
            (click)="viewMode.set('table')"
          >
            Table
          </button>
        </div>
      </div>

      <!-- Sticky filter bar (AC-4 / FR-6) -->
      <div
        class="sticky top-0 z-20 mb-5 rounded-xl border border-neutral-200 bg-white/95 p-3 shadow-sm backdrop-blur"
      >
        <div class="flex flex-wrap items-end gap-3">
          <div class="min-w-[12rem] flex-1">
            <label for="f-search" class="filter-label">Search</label>
            <input
              id="f-search"
              type="search"
              class="field"
              placeholder="Name or email…"
              [ngModel]="filters().search"
              (ngModelChange)="onSearchChange($event)"
            />
          </div>
          <div>
            <label for="f-stage" class="filter-label">Stage</label>
            <select
              id="f-stage"
              class="field"
              [ngModel]="filters().stage"
              (ngModelChange)="updateFilter({ stage: $event || null })"
            >
              <option [ngValue]="null">All stages</option>
              @for (s of stages; track s) {
                <option [ngValue]="s">{{ s }}</option>
              }
            </select>
          </div>
          <div>
            <label for="f-source" class="filter-label">Source</label>
            <select
              id="f-source"
              class="field"
              [ngModel]="filters().source"
              (ngModelChange)="updateFilter({ source: $event || null })"
            >
              <option [ngValue]="null">All sources</option>
              <option value="public">Public</option>
              <option value="internal">Internal</option>
              <option value="referral">Referral</option>
            </select>
          </div>
          <div>
            <label for="f-from" class="filter-label">Applied from</label>
            <input
              id="f-from"
              type="date"
              class="field"
              [ngModel]="filters().from"
              (ngModelChange)="updateFilter({ from: $event || null })"
            />
          </div>
          <div>
            <label for="f-to" class="filter-label">Applied to</label>
            <input
              id="f-to"
              type="date"
              class="field"
              [ngModel]="filters().to"
              (ngModelChange)="updateFilter({ to: $event || null })"
            />
          </div>
          @if (hasActiveFilters()) {
            <button
              type="button"
              class="rounded-lg px-3 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
              (click)="clearFilters()"
            >
              Clear
            </button>
          }
        </div>
      </div>

      <!-- Loading -->
      @if (loading()) {
        <div class="flex gap-4 overflow-x-auto">
          @for (n of stages; track n) {
            <div class="w-72 shrink-0 space-y-2">
              <div class="h-8 animate-pulse rounded-lg bg-neutral-100"></div>
              <div class="h-20 animate-pulse rounded-lg bg-neutral-100"></div>
              <div class="h-20 animate-pulse rounded-lg bg-neutral-100"></div>
            </div>
          }
        </div>
      } @else if (total() === 0) {
        <!-- Empty state -->
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-white py-20 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">No applicants found</p>
          <p class="mt-1 text-sm text-neutral-500">
            {{
              hasActiveFilters()
                ? 'Try adjusting or clearing your filters.'
                : 'Applications will appear here as candidates apply.'
            }}
          </p>
        </div>
      } @else if (viewMode() === 'kanban') {
        <!-- Mobile stage tabs (NFR-4) -->
        <div class="mb-3 flex gap-1 overflow-x-auto md:hidden" role="tablist">
          @for (col of board(); track col.stage) {
            <button
              type="button"
              class="stage-tab"
              role="tab"
              [attr.aria-selected]="mobileStage() === col.stage"
              [class.stage-tab-active]="mobileStage() === col.stage"
              (click)="mobileStage.set(col.stage)"
            >
              {{ col.stage }} ({{ col.count }})
            </button>
          }
        </div>

        <!-- Kanban board (AC-1) -->
        <div
          @fadeIn
          class="flex gap-4 overflow-x-auto pb-4"
          cdkDropListGroup
        >
          @for (col of board(); track col.stage) {
            <section
              class="w-full shrink-0 rounded-xl bg-neutral-50 p-3 md:w-72"
              [class.hidden]="isColumnHiddenOnMobile(col.stage)"
              [attr.aria-label]="col.stage + ' column'"
            >
              <header class="mb-3 flex items-center justify-between px-1">
                <span
                  class="badge ring-1 ring-inset"
                  [class]="stageBadge(col.stage)"
                >
                  {{ col.stage }}
                </span>
                <span class="text-xs font-medium text-neutral-400">
                  {{ col.count }}
                </span>
              </header>

              <div
                class="flex min-h-[4rem] flex-col gap-2"
                cdkDropList
                [cdkDropListData]="col"
                [id]="col.stage"
                (cdkDropListDropped)="onDrop($event)"
              >
                @for (card of col.applicants; track card.id) {
                  <article
                    cdkDrag
                    [cdkDragData]="card"
                    class="kanban-card group"
                    role="button"
                    tabindex="0"
                    [attr.aria-label]="cardName(card) + ', ' + col.stage"
                    (click)="openDetail(card)"
                    (keydown.enter)="openDetail(card)"
                    (keydown.space)="openDetail(card); $event.preventDefault()"
                  >
                    <div class="flex items-start gap-2">
                      <span
                        class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-indigo-50 text-xs font-semibold text-indigo-600"
                        aria-hidden="true"
                      >
                        {{ initials(card) }}
                      </span>
                      <div class="min-w-0 flex-1">
                        <p class="truncate text-sm font-medium text-neutral-900">
                          {{ cardName(card) }}
                        </p>
                        <p class="mt-0.5 text-xs text-neutral-400">
                          {{ appliedAgo(card) }}
                        </p>
                      </div>
                    </div>
                    <div class="mt-2 flex flex-wrap items-center gap-1.5">
                      <span
                        class="badge ring-1 ring-inset"
                        [class]="sourceBadge(card.source)"
                      >
                        {{ card.source }}
                      </span>
                      @if (timeInStage(card); as t) {
                        <span
                          class="badge bg-neutral-100 text-neutral-500 ring-1 ring-inset ring-neutral-200"
                          [attr.title]="t"
                        >
                          {{ t }}
                        </span>
                      }
                    </div>
                  </article>
                }

                @if (col.applicants.length === 0) {
                  <p class="px-1 py-3 text-center text-xs text-neutral-300">
                    Drop here
                  </p>
                }
              </div>
            </section>
          }
        </div>
      } @else {
        <!-- Table view (FR-4) -->
        <div
          @fadeIn
          class="overflow-hidden rounded-xl border border-neutral-200 bg-white shadow-sm"
        >
          <table class="min-w-full divide-y divide-neutral-100">
            <thead class="bg-neutral-50">
              <tr>
                <th class="th cursor-pointer" (click)="sortBy('name')">
                  Name {{ sortIndicator('name') }}
                </th>
                <th class="th hidden sm:table-cell cursor-pointer" (click)="sortBy('email')">
                  Email {{ sortIndicator('email') }}
                </th>
                <th class="th cursor-pointer" (click)="sortBy('stage')">
                  Stage {{ sortIndicator('stage') }}
                </th>
                <th class="th hidden md:table-cell cursor-pointer" (click)="sortBy('source')">
                  Source {{ sortIndicator('source') }}
                </th>
                <th class="th cursor-pointer" (click)="sortBy('appliedAt')">
                  Applied {{ sortIndicator('appliedAt') }}
                </th>
              </tr>
            </thead>
            <tbody class="divide-y divide-neutral-50">
              @for (card of tableRows(); track card.id) {
                <tr
                  class="cursor-pointer transition hover:bg-neutral-50/60"
                  (click)="openDetail(card)"
                >
                  <td class="td font-medium text-neutral-900">
                    {{ cardName(card) }}
                  </td>
                  <td class="td hidden sm:table-cell text-neutral-600">
                    {{ card.email }}
                  </td>
                  <td class="td">
                    <span
                      class="badge ring-1 ring-inset"
                      [class]="stageBadge(card.stage)"
                    >
                      {{ card.stage }}
                    </span>
                  </td>
                  <td class="td hidden md:table-cell">
                    <span
                      class="badge ring-1 ring-inset"
                      [class]="sourceBadge(card.source)"
                    >
                      {{ card.source }}
                    </span>
                  </td>
                  <td class="td text-neutral-500">{{ appliedAgo(card) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>

    <!-- Detail slide-over (AC-3 / FR-7) -->
    @if (selectedId(); as id) {
      <app-applicant-detail
        [applicantId]="id"
        (closed)="closeDetail()"
        (moved)="onDetailMoved()"
      />
    }

    <!-- Reason prompt (BR-3 / BR-4) -->
    @if (pendingMove(); as pm) {
      <app-stage-reason-dialog
        [applicantName]="cardName(pm.card)"
        [toStage]="pm.to"
        (confirmed)="confirmMove($event)"
        (cancelled)="cancelMove()"
      />
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .view-btn {
        border-radius: 0.375rem;
        padding: 0.25rem 0.75rem;
        font-size: 0.8125rem;
        color: #525252;
        transition: all 150ms ease;
      }
      .view-active {
        background: #fff;
        color: #171717;
        box-shadow: 0 1px 2px rgb(0 0 0 / 0.06);
      }
      .filter-label {
        display: block;
        margin-bottom: 0.25rem;
        font-size: 0.6875rem;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: #a3a3a3;
      }
      .field {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.4375rem 0.625rem;
        font-size: 0.8125rem;
        color: #171717;
      }
      .field:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 2px #e0e7ff;
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
      .stage-tab {
        white-space: nowrap;
        border-radius: 9999px;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.3125rem 0.75rem;
        font-size: 0.75rem;
        color: #525252;
      }
      .stage-tab-active {
        background: #eef2ff;
        border-color: #c7d2fe;
        color: #4338ca;
      }
      .kanban-card {
        cursor: pointer;
        border-radius: 0.625rem;
        border: 1px solid #f0f0f0;
        background: #fff;
        padding: 0.625rem;
        box-shadow: 0 1px 2px rgb(0 0 0 / 0.04);
        transition:
          box-shadow 200ms ease,
          transform 200ms ease;
      }
      .kanban-card:hover {
        box-shadow: 0 4px 10px rgb(0 0 0 / 0.08);
        transform: translateY(-1px);
      }
      .kanban-card:focus-visible {
        outline: 2px solid #818cf8;
        outline-offset: 2px;
      }
      .cdk-drag-preview {
        box-shadow: 0 8px 20px rgb(0 0 0 / 0.18);
        border-radius: 0.625rem;
      }
      .cdk-drag-placeholder {
        opacity: 0.4;
      }
      .cdk-drop-list-dragging .cdk-drag {
        transition: transform 200ms cubic-bezier(0, 0, 0.2, 1);
      }
      .th {
        padding: 0.75rem 1rem;
        text-align: left;
        font-size: 0.6875rem;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: #737373;
        user-select: none;
      }
      .td {
        padding: 0.875rem 1rem;
        font-size: 0.875rem;
        vertical-align: middle;
      }
    `,
  ],
})
export class ApplicantPipelineComponent implements OnInit {
  private readonly pipelineService = inject(PipelineService);
  private readonly route = inject(ActivatedRoute);
  private readonly toastr = inject(ToastrService);

  readonly stages: readonly ApplicantStage[] = PIPELINE_STAGES;

  readonly vacancyId = signal<string>('');
  readonly board = signal<IPipelineStage[]>([]);
  readonly boardTitle = signal<string>('Vacancy');
  readonly loading = signal(true);

  readonly viewMode = signal<'kanban' | 'table'>('kanban');
  readonly mobileStage = signal<ApplicantStage>('Applied');

  readonly filters = signal<IPipelineFilters>({});

  readonly selectedId = signal<string | null>(null);
  readonly pendingMove = signal<IPendingMove | null>(null);

  readonly sortKey = signal<
    'name' | 'email' | 'stage' | 'source' | 'appliedAt'
  >('appliedAt');
  readonly sortDir = signal<'asc' | 'desc'>('desc');

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  // ─── Derived ─────────────────────────────────────────────

  readonly total = computed(() =>
    this.board().reduce((sum, s) => sum + s.applicants.length, 0),
  );

  readonly hasActiveFilters = computed(() => {
    const f = this.filters();
    return Boolean(f.stage || f.source || f.from || f.to || f.search);
  });

  /** Flattened + sorted rows for the table view (FR-4). */
  readonly tableRows = computed<IApplicantCard[]>(() => {
    const rows = this.board().flatMap((s) => s.applicants);
    const key = this.sortKey();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => {
      const av = this.sortValue(a, key);
      const bv = this.sortValue(b, key);
      if (av < bv) {
        return -1 * dir;
      }
      if (av > bv) {
        return 1 * dir;
      }
      return 0;
    });
  });

  // ─── Lifecycle ───────────────────────────────────────────

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('vacancyId') ?? '';
    this.vacancyId.set(id);
    this.load();
  }

  // ─── Data ────────────────────────────────────────────────

  load(): void {
    this.loading.set(true);
    this.pipelineService.getPipeline(this.vacancyId(), this.filters()).subscribe({
      next: (b) => {
        this.board.set(b.stages);
        this.boardTitle.set(b.vacancyTitle || 'Vacancy');
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.toastr.error(PipelineService.parseErrorMessage(err));
      },
    });
  }

  // ─── Filters (AC-4 / FR-6) ───────────────────────────────

  updateFilter(patch: Partial<IPipelineFilters>): void {
    this.filters.update((f) => ({ ...f, ...patch }));
    this.load();
  }

  /** Debounce free-text search so we don't hammer the endpoint per keystroke. */
  onSearchChange(value: string): void {
    this.filters.update((f) => ({ ...f, search: value || null }));
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }
    this.searchTimer = setTimeout(() => this.load(), 300);
  }

  clearFilters(): void {
    this.filters.set({});
    this.load();
  }

  // ─── Drag-and-drop (AC-2 / FR-3) ─────────────────────────

  onDrop(event: CdkDragDrop<IPipelineStage>): void {
    const targetCol = event.container.data;
    const sourceCol = event.previousContainer.data;

    // Reorder within the same column — local only, no stage change.
    if (event.previousContainer === event.container) {
      this.board.update((cols) =>
        cols.map((c) =>
          c.stage === targetCol.stage
            ? this.withReorder(c, event.previousIndex, event.currentIndex)
            : c,
        ),
      );
      return;
    }

    const card = event.item.data as IApplicantCard;
    const from = sourceCol.stage;
    const to = targetCol.stage;

    // Snapshot for rollback before mutating optimistically.
    const snapshot = this.cloneBoard();

    // Optimistic transfer between columns.
    this.applyTransfer(event);

    if (moveRequiresReason(from, to)) {
      // Defer the persist until the user supplies a reason (BR-3 / BR-4).
      this.pendingMove.set({ card: { ...card, stage: to }, from, to, snapshot });
      return;
    }

    this.persistMove(card.id, to, snapshot);
  }

  /** Confirm the reason dialog and persist the deferred move. */
  confirmMove(result: IStageReasonResult): void {
    const pm = this.pendingMove();
    this.pendingMove.set(null);
    if (!pm) {
      return;
    }
    this.persistMove(pm.card.id, pm.to, pm.snapshot, result);
  }

  /** Cancel the reason dialog → roll back the optimistic move. */
  cancelMove(): void {
    const pm = this.pendingMove();
    this.pendingMove.set(null);
    if (pm) {
      this.board.set(pm.snapshot);
    }
  }

  private persistMove(
    applicantId: string,
    to: ApplicantStage,
    snapshot: IPipelineStage[],
    reason?: IStageReasonResult,
  ): void {
    this.pipelineService
      .changeStage(applicantId, {
        toStage: to,
        reason: reason?.reason,
        rejectionReason: reason?.rejectionReason,
        notes: reason?.notes,
      })
      .subscribe({
        next: (updated) => {
          // Reconcile the moved card with the server's version (keeps counts honest);
          // stamp enteredStageAt so the time-in-stage badge resets (US-REC-004 §8).
          this.board.update((cols) =>
            cols.map((c) => ({
              ...c,
              applicants: c.applicants.map((a) =>
                a.id === updated.id
                  ? {
                      ...a,
                      stage: updated.stage,
                      enteredStageAt: updated.enteredStageAt ?? a.enteredStageAt,
                    }
                  : a,
              ),
              count: c.applicants.length,
            })),
          );
          this.recountColumns();
          this.toastr.success(`Moved to ${to}.`);
          // Soft-gate / headcount warnings: the move SUCCEEDED, just warn (BR-4 / FR-1).
          for (const w of updated.warnings) {
            this.toastr.warning(w, 'Heads up');
          }
        },
        error: (err: HttpErrorResponse) => {
          // Roll back to the pre-move snapshot (AC-2 failure path). A vacancy
          // closed/cancelled rejection (FR-8) lands here; show the message verbatim.
          this.board.set(snapshot);
          this.toastr.error(PipelineService.parseErrorMessage(err));
        },
      });
  }

  // ─── Detail slide-over (AC-3 / FR-7) ─────────────────────

  openDetail(card: IApplicantCard): void {
    this.selectedId.set(card.id);
  }

  closeDetail(): void {
    this.selectedId.set(null);
  }

  /**
   * A stage move was made from the detail slide-over's action buttons
   * (US-REC-004 AC-1/AC-2/AC-4). Reload the board so columns + counts stay in
   * sync (FR-7); the drawer stays open showing the refreshed detail.
   */
  onDetailMoved(): void {
    this.load();
  }

  // ─── Table sorting (FR-4) ────────────────────────────────

  sortBy(key: 'name' | 'email' | 'stage' | 'source' | 'appliedAt'): void {
    if (this.sortKey() === key) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortKey.set(key);
      this.sortDir.set('asc');
    }
  }

  sortIndicator(key: string): string {
    if (this.sortKey() !== key) {
      return '';
    }
    return this.sortDir() === 'asc' ? '▲' : '▼';
  }

  private sortValue(
    card: IApplicantCard,
    key: 'name' | 'email' | 'stage' | 'source' | 'appliedAt',
  ): string {
    switch (key) {
      case 'name':
        return `${card.lastName} ${card.firstName}`.toLowerCase();
      case 'email':
        return card.email.toLowerCase();
      case 'stage':
        return card.stage;
      case 'source':
        return card.source;
      case 'appliedAt':
        return card.appliedAt;
    }
  }

  // ─── View helpers ────────────────────────────────────────

  cardName(card: IApplicantCard): string {
    return `${card.firstName} ${card.lastName}`.trim();
  }

  initials(card: IApplicantCard): string {
    return `${card.firstName?.[0] ?? ''}${card.lastName?.[0] ?? ''}`.toUpperCase();
  }

  appliedAgo(card: IApplicantCard): string {
    return relativeAppliedTime(card.appliedAt);
  }

  /** Time-in-stage badge text, e.g. "In Screening 5 days" (US-REC-004 §8). */
  timeInStage(card: IApplicantCard): string {
    return timeInStageLabel(card.stage, card.enteredStageAt, card.appliedAt);
  }

  stageBadge(stage: ApplicantStage): string {
    return STAGE_BADGE[stage] ?? STAGE_BADGE.Applied;
  }

  sourceBadge(source: ApplicantSource): string {
    return SOURCE_BADGE[source] ?? SOURCE_BADGE.public;
  }

  isColumnHiddenOnMobile(stage: ApplicantStage): boolean {
    // Tailwind handles desktop; on mobile only the selected stage tab shows.
    return this.mobileStage() !== stage;
  }

  // ─── Internal mutation helpers ───────────────────────────

  private withReorder(
    col: IPipelineStage,
    prev: number,
    curr: number,
  ): IPipelineStage {
    const applicants = [...col.applicants];
    moveItemInArray(applicants, prev, curr);
    return { ...col, applicants };
  }

  /** Apply a CDK cross-column transfer to a fresh board signal value + update stage. */
  private applyTransfer(event: CdkDragDrop<IPipelineStage>): void {
    const sourceStage = event.previousContainer.data.stage;
    const targetStage = event.container.data.stage;

    this.board.update((cols) => {
      const next = cols.map((c) => ({ ...c, applicants: [...c.applicants] }));
      const source = next.find((c) => c.stage === sourceStage)!;
      const target = next.find((c) => c.stage === targetStage)!;
      transferArrayItem(
        source.applicants,
        target.applicants,
        event.previousIndex,
        event.currentIndex,
      );
      // Stamp the moved card's new stage + refresh counts.
      const moved = target.applicants[event.currentIndex];
      if (moved) {
        target.applicants[event.currentIndex] = {
          ...moved,
          stage: targetStage,
        };
      }
      source.count = source.applicants.length;
      target.count = target.applicants.length;
      return next;
    });
  }

  private recountColumns(): void {
    this.board.update((cols) =>
      cols.map((c) => ({ ...c, count: c.applicants.length })),
    );
  }

  private cloneBoard(): IPipelineStage[] {
    return this.board().map((c) => ({
      ...c,
      applicants: c.applicants.map((a) => ({ ...a })),
    }));
  }
}
