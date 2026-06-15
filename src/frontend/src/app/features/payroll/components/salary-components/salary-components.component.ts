import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import {
  CdkDragDrop,
  DragDropModule,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { PayrollService } from '../../services/payroll.service';
import { ComponentFormComponent } from '../component-form/component-form.component';
import {
  ISalaryComponent,
  COMPONENT_TYPE_BADGE,
  CALCULATION_METHOD_LABELS,
} from '../../models/payroll.models';

/**
 * US-PAY-001: Salary components — a Notion-style inline database table (§8). Rows
 * carry color-coded type badges, taxable/statutory flags, and a value summary.
 *
 * Processing order (AC-4) is reorderable by drag-and-drop (CDK) OR up/down buttons
 * (the mobile/keyboard-accessible alternative); a successful reorder is persisted
 * via the reorder endpoint, with optimistic UI + rollback on error.
 *
 * Create/edit open the right slide-over (`ComponentFormComponent`) — never a
 * full-page navigation. Delete shows the affected-employee-count error when the
 * component is in use by active employees (AC-5).
 */
@Component({
  selector: 'app-salary-components',
  standalone: true,
  imports: [CommonModule, RouterLink, DragDropModule, ComponentFormComponent],
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
    <div class="mx-auto max-w-6xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div
        class="mb-6 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between"
      >
        <div>
          <nav class="mb-1 text-xs text-neutral-500" aria-label="Breadcrumb">
            <a routerLink="/payroll" class="hover:text-neutral-700">Payroll</a>
            <span class="px-1">/</span>
            <span class="text-neutral-700">Salary components</span>
          </nav>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Salary components
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Earnings, deductions and statutory items used to build salary
            structures. Drag to set the processing order.
          </p>
        </div>
        <button
          type="button"
          class="self-start rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 sm:self-auto"
          [style.background-color]="'var(--brand-primary)'"
          (click)="openCreate()"
        >
          + New component
        </button>
      </div>

      <!-- Loading -->
      @if (loading()) {
        <div class="space-y-2">
          @for (i of [1, 2, 3, 4]; track i) {
            <div class="h-14 animate-pulse rounded-lg bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (components().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-300 bg-white px-6 py-16 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">
            No salary components yet
          </p>
          <p class="mt-1 text-sm text-neutral-500">
            Create your first component (e.g. Basic Salary) to get started.
          </p>
        </div>
      } @else {
        <!-- Inline table -->
        <div
          @fadeIn
          class="overflow-hidden rounded-xl border border-neutral-200 bg-white shadow-sm"
        >
          <!-- header row (hidden on mobile) -->
          <div
            class="hidden grid-cols-12 gap-2 border-b border-neutral-200 bg-neutral-50 px-4 py-2.5 text-xs font-medium uppercase tracking-wide text-neutral-500 md:grid"
          >
            <span class="col-span-1">Order</span>
            <span class="col-span-3">Name</span>
            <span class="col-span-2">Type</span>
            <span class="col-span-3">Calculation</span>
            <span class="col-span-2">Value</span>
            <span class="col-span-1 text-right">Actions</span>
          </div>

          <div
            cdkDropList
            (cdkDropListDropped)="onDrop($event)"
            class="divide-y divide-neutral-100"
          >
            @for (c of components(); track c.id; let idx = $index) {
              <div
                cdkDrag
                [cdkDragData]="c"
                class="grid grid-cols-1 gap-2 px-4 py-3 transition hover:bg-neutral-50 md:grid-cols-12 md:items-center"
              >
                <!-- Order + drag handle + up/down -->
                <div class="col-span-1 flex items-center gap-1">
                  <button
                    cdkDragHandle
                    type="button"
                    class="cursor-grab text-neutral-300 hover:text-neutral-500"
                    aria-label="Drag to reorder"
                    title="Drag to reorder"
                  >
                    <svg
                      class="h-4 w-4"
                      viewBox="0 0 20 20"
                      fill="currentColor"
                      aria-hidden="true"
                    >
                      <path
                        d="M7 4a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm8 0a1 1 0 1 1-2 0 1 1 0 0 1 2 0ZM7 10a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm8 0a1 1 0 1 1-2 0 1 1 0 0 1 2 0ZM7 16a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm8 0a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z"
                      />
                    </svg>
                  </button>
                  <span class="text-xs font-medium text-neutral-500">{{
                    idx + 1
                  }}</span>
                </div>

                <!-- Name + code -->
                <div class="col-span-3 min-w-0">
                  <p class="truncate text-sm font-medium text-neutral-900">
                    {{ c.name }}
                    @if (!c.isActive) {
                      <span class="ml-1 text-xs text-neutral-400">(inactive)</span>
                    }
                  </p>
                  <p class="truncate font-mono text-xs text-neutral-400">
                    {{ c.code }}
                  </p>
                </div>

                <!-- Type badge -->
                <div class="col-span-2">
                  <span
                    class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                    [class]="typeBadge[c.type]"
                  >
                    {{ c.type }}
                  </span>
                  @if (c.isStatutory) {
                    <span
                      class="ml-1 inline-flex items-center rounded-full bg-neutral-100 px-1.5 py-0.5 text-[10px] font-medium text-neutral-600"
                      >statutory</span
                    >
                  }
                </div>

                <!-- Calculation -->
                <div class="col-span-3 text-sm text-neutral-600">
                  {{ methodLabels[c.calculationMethod] }}
                </div>

                <!-- Value -->
                <div class="col-span-2 text-sm text-neutral-700">
                  {{ valueLabel(c) }}
                  @if (c.isTaxable) {
                    <span class="ml-1 text-[10px] uppercase text-neutral-400"
                      >taxable</span
                    >
                  }
                </div>

                <!-- Actions -->
                <div
                  class="col-span-1 flex items-center justify-end gap-1 text-neutral-400"
                >
                  <button
                    type="button"
                    class="rounded p-1 hover:bg-neutral-100 hover:text-neutral-700 disabled:opacity-30"
                    [disabled]="idx === 0"
                    aria-label="Move up"
                    title="Move up"
                    (click)="move(idx, -1)"
                  >
                    ▲
                  </button>
                  <button
                    type="button"
                    class="rounded p-1 hover:bg-neutral-100 hover:text-neutral-700 disabled:opacity-30"
                    [disabled]="idx === components().length - 1"
                    aria-label="Move down"
                    title="Move down"
                    (click)="move(idx, 1)"
                  >
                    ▼
                  </button>
                  <button
                    type="button"
                    class="rounded p-1 hover:bg-indigo-50 hover:text-indigo-600"
                    aria-label="Edit"
                    title="Edit"
                    (click)="openEdit(c)"
                  >
                    ✎
                  </button>
                  <button
                    type="button"
                    class="rounded p-1 hover:bg-rose-50 hover:text-rose-600"
                    aria-label="Delete"
                    title="Delete"
                    (click)="confirmDelete(c)"
                  >
                    🗑
                  </button>
                </div>
              </div>
            }
          </div>
        </div>
      }
    </div>

    <!-- In-use delete error (AC-5) -->
    @if (inUse(); as u) {
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div
          class="fixed inset-0 bg-neutral-900/30"
          aria-hidden="true"
          (click)="inUse.set(null)"
        ></div>
        <div
          class="relative w-full max-w-md rounded-xl bg-white p-6 shadow-2xl"
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="inuse-title"
        >
          <h3 id="inuse-title" class="text-base font-semibold text-neutral-900">
            Can’t delete “{{ u.name }}”
          </h3>
          <p class="mt-2 text-sm text-neutral-600">
            This component is in use by
            <strong>{{ u.count }}</strong>
            active {{ u.count === 1 ? 'employee' : 'employees' }}. Reassign or
            deactivate them before deleting it.
          </p>
          <div class="mt-5 flex justify-end">
            <button
              type="button"
              class="rounded-lg bg-neutral-900 px-4 py-2 text-sm font-medium text-white hover:bg-neutral-800"
              (click)="inUse.set(null)"
            >
              Got it
            </button>
          </div>
        </div>
      </div>
    }

    <!-- Create / edit slide-over -->
    @if (formOpen()) {
      <app-component-form
        [component]="editTarget()"
        (close)="closeForm()"
        (saved)="onSaved()"
      />
    }
  `,
})
export class SalaryComponentsComponent implements OnInit {
  private readonly payroll = inject(PayrollService);
  private readonly toastr = inject(ToastrService);

  readonly typeBadge = COMPONENT_TYPE_BADGE;
  readonly methodLabels = CALCULATION_METHOD_LABELS;

  readonly components = signal<ISalaryComponent[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly formOpen = signal(false);
  readonly editTarget = signal<ISalaryComponent | null>(null);

  /** AC-5: the in-use blocking modal payload. */
  readonly inUse = signal<{ name: string; count: number } | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.payroll.listComponents().subscribe({
      next: (list) => {
        this.components.set(
          [...list].sort((a, b) => a.processingOrder - b.processingOrder),
        );
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load salary components.');
        this.loading.set(false);
      },
    });
  }

  valueLabel(c: ISalaryComponent): string {
    if (c.calculationMethod === 'Formula') {
      return c.formulaExpression ?? '—';
    }
    if (c.defaultValue == null) {
      return '—';
    }
    const isPct =
      c.calculationMethod === 'PercentageOfBasic' ||
      c.calculationMethod === 'PercentageOfGross';
    return isPct ? `${c.defaultValue}%` : `${c.defaultValue}`;
  }

  // ─── Reorder (AC-4) ────────────────────────────────────────

  onDrop(event: CdkDragDrop<ISalaryComponent[]>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }
    const next = [...this.components()];
    moveItemInArray(next, event.previousIndex, event.currentIndex);
    this.persistOrder(next);
  }

  move(index: number, delta: number): void {
    const target = index + delta;
    if (target < 0 || target >= this.components().length) {
      return;
    }
    const next = [...this.components()];
    moveItemInArray(next, index, target);
    this.persistOrder(next);
  }

  private persistOrder(next: ISalaryComponent[]): void {
    const previous = this.components();
    this.components.set(next); // optimistic
    this.payroll.reorderComponents(next.map((c) => c.id)).subscribe({
      next: (updated) => {
        if (updated.length) {
          this.components.set(
            [...updated].sort(
              (a, b) => a.processingOrder - b.processingOrder,
            ),
          );
        }
      },
      error: () => {
        this.components.set(previous); // rollback
        this.toastr.error('Could not save the new order.');
      },
    });
  }

  // ─── Delete (AC-5) ─────────────────────────────────────────

  confirmDelete(c: ISalaryComponent): void {
    this.payroll.deleteComponent(c.id).subscribe({
      next: () => {
        this.toastr.success('Component deleted.');
        this.components.set(this.components().filter((x) => x.id !== c.id));
      },
      error: (err: HttpErrorResponse) => {
        const inUse = this.payroll.parseInUseError(err);
        if (inUse) {
          this.inUse.set({ name: c.name, count: inUse.affectedEmployeeCount });
        } else {
          this.toastr.error('Could not delete the component.');
        }
      },
    });
  }

  // ─── Slide-over ────────────────────────────────────────────

  openCreate(): void {
    this.editTarget.set(null);
    this.formOpen.set(true);
  }

  openEdit(c: ISalaryComponent): void {
    this.editTarget.set(c);
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editTarget.set(null);
  }

  onSaved(): void {
    this.closeForm();
    this.load();
  }
}
