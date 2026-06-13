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
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { LeaveTypeService } from '../../services/leave-type.service';
import {
  ILeaveType,
  getContrastTextColor,
} from '../../models/leave-type.models';
import { LeaveTypeFormComponent } from '../leave-type-form/leave-type-form.component';

/**
 * US-LV-001 AC-1/AC-4: Leave Types management page.
 *
 * Displays leave types in a card-based table with:
 *   - Color-coded tags (FR-2)
 *   - Inline active/inactive toggle (AC-4)
 *   - Drag-and-drop reorder on desktop (Angular CDK), up/down arrows on mobile (FR-3)
 *   - Search filter
 *   - Create/Edit via slide-over panel
 *
 * Role-gated to Tenant Admin / HR Officer via the route guard.
 */
@Component({
  selector: 'app-leave-type-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    DragDropModule,
    LeaveTypeFormComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate(
          '250ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
    trigger('slideOver', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(100%)' }),
        animate(
          '300ms ease-out',
          style({ opacity: 1, transform: 'translateX(0)' })
        ),
      ]),
      transition(':leave', [
        animate(
          '200ms ease-in',
          style({ opacity: 0, transform: 'translateX(100%)' })
        ),
      ]),
    ]),
    trigger('overlayFade', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Leave Types
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Configure leave types and policies for your organization.
          </p>
        </div>
        <button
          type="button"
          class="btn-primary"
          (click)="openCreate()"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
            <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
          </svg>
          Add Leave Type
        </button>
      </div>

      <!-- Search bar -->
      @if (!isLoading() && !loadError() && leaveTypes().length > 0) {
        <div class="mb-5">
          <div class="relative max-w-sm">
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-neutral-400 pointer-events-none"
              aria-hidden="true"
            >
              <path fill-rule="evenodd" d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z" clip-rule="evenodd" />
            </svg>
            <input
              type="search"
              class="input-notion pl-9"
              placeholder="Search leave types..."
              [ngModel]="searchQuery()"
              (ngModelChange)="searchQuery.set($event)"
              aria-label="Search leave types"
            />
          </div>
        </div>
      }

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="card-notion overflow-hidden">
          <div class="animate-pulse space-y-4 p-2">
            <div class="h-5 bg-neutral-100 rounded w-1/3 mb-4"></div>
            @for (i of skeletonItems; track i) {
              <div class="flex items-center gap-4">
                <div class="h-4 bg-neutral-50 rounded w-6"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/4"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/6"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/5"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/12"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/12"></div>
              </div>
            }
          </div>
        </div>
      }

      <!-- Error state -->
      @if (loadError()) {
        <div class="card-notion text-center py-12">
          <div class="w-12 h-12 rounded-full bg-red-50 flex items-center justify-center mx-auto mb-4">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-6 h-6 text-red-500" aria-hidden="true">
              <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
            </svg>
          </div>
          <p class="text-sm text-neutral-600">{{ loadError() }}</p>
          <button class="btn-secondary mt-4" (click)="loadLeaveTypes()">
            Try Again
          </button>
        </div>
      }

      <!-- Content -->
      @if (!isLoading() && !loadError()) {
        @if (leaveTypes().length === 0) {
          <!-- Empty state -->
          <div class="card-notion text-center py-12">
            <div class="w-14 h-14 rounded-full bg-neutral-50 flex items-center justify-center mx-auto mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-7 h-7 text-neutral-400" aria-hidden="true">
                <path fill-rule="evenodd" d="M5.75 2a.75.75 0 0 1 .75.75V4h7V2.75a.75.75 0 0 1 1.5 0V4h.25A2.75 2.75 0 0 1 18 6.75v8.5A2.75 2.75 0 0 1 15.25 18H4.75A2.75 2.75 0 0 1 2 15.25v-8.5A2.75 2.75 0 0 1 4.75 4H5V2.75A.75.75 0 0 1 5.75 2Zm-1 5.5c-.69 0-1.25.56-1.25 1.25v6.5c0 .69.56 1.25 1.25 1.25h10.5c.69 0 1.25-.56 1.25-1.25v-6.5c0-.69-.56-1.25-1.25-1.25H4.75Z" clip-rule="evenodd" />
              </svg>
            </div>
            <p class="text-sm font-medium text-neutral-700 mb-1">No leave types yet</p>
            <p class="text-xs text-neutral-400 mb-4">
              Create leave types to define the leave policy for your organization.
            </p>
            <button type="button" class="btn-primary" (click)="openCreate()">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
                <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
              </svg>
              Add Leave Type
            </button>
          </div>
        } @else {
          <!-- Table card (AC-1, FR-3) -->
          <div class="card-notion overflow-hidden p-0" @fadeSlideIn>
            <!-- Desktop table with drag-and-drop -->
            <div class="hidden md:block overflow-x-auto">
              <table class="w-full" role="table">
                <thead>
                  <tr class="border-b border-neutral-100">
                    <th class="th-notion w-10"></th>
                    <th class="th-notion text-left">Leave Type</th>
                    <th class="th-notion text-left">Code</th>
                    <th class="th-notion text-center">Entitlement</th>
                    <th class="th-notion text-left">Accrual</th>
                    <th class="th-notion text-center">Status</th>
                    <th class="th-notion text-right">Actions</th>
                  </tr>
                </thead>
                <tbody
                  cdkDropList
                  [cdkDropListData]="filteredLeaveTypes()"
                  (cdkDropListDropped)="onDrop($event)"
                  [cdkDropListDisabled]="isSearchActive()"
                >
                  @for (lt of filteredLeaveTypes(); track lt.leaveTypeId; let i = $index) {
                    <tr
                      class="table-row-notion group"
                      [class.opacity-60]="!lt.isActive"
                      cdkDrag
                      [cdkDragData]="lt"
                      [cdkDragDisabled]="isSearchActive()"
                    >
                      <!-- Drag handle -->
                      <td class="td-notion text-center">
                        <div
                          class="drag-handle"
                          cdkDragHandle
                          [class.drag-handle-disabled]="isSearchActive()"
                          [attr.aria-label]="'Drag to reorder ' + lt.name"
                        >
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                            <path fill-rule="evenodd" d="M2 4.75A.75.75 0 0 1 2.75 4h14.5a.75.75 0 0 1 0 1.5H2.75A.75.75 0 0 1 2 4.75Zm0 5A.75.75 0 0 1 2.75 9h14.5a.75.75 0 0 1 0 1.5H2.75A.75.75 0 0 1 2 9.75Zm0 5a.75.75 0 0 1 .75-.75h14.5a.75.75 0 0 1 0 1.5H2.75a.75.75 0 0 1-.75-.75Z" clip-rule="evenodd" />
                          </svg>
                        </div>
                      </td>
                      <td class="td-notion cursor-pointer" (click)="openEdit(lt)">
                        <div class="flex items-center gap-2.5">
                          <span
                            class="color-tag"
                            [style.background-color]="lt.color"
                            [style.color]="getContrastColor(lt.color)"
                          >
                            {{ lt.code }}
                          </span>
                          <div>
                            <span class="font-medium text-neutral-900">{{ lt.name }}</span>
                            @if (lt.description) {
                              <p class="text-xs text-neutral-400 mt-0.5 line-clamp-1">{{ lt.description }}</p>
                            }
                          </div>
                        </div>
                      </td>
                      <td class="td-notion text-neutral-500">
                        <code class="text-xs font-mono">{{ lt.code }}</code>
                      </td>
                      <td class="td-notion text-center text-neutral-700 font-medium">
                        {{ lt.annualEntitlement }}d
                      </td>
                      <td class="td-notion text-neutral-500 capitalize">
                        {{ lt.accrualFrequency }}
                      </td>
                      <td class="td-notion text-center">
                        <button
                          type="button"
                          role="switch"
                          [attr.aria-checked]="lt.isActive"
                          [attr.aria-label]="lt.isActive ? 'Deactivate ' + lt.name : 'Activate ' + lt.name"
                          class="toggle-switch"
                          [class.toggle-switch-on]="lt.isActive"
                          (click)="toggleActive(lt, $event)"
                          [disabled]="isTogglingId() === lt.leaveTypeId"
                        >
                          <span
                            class="toggle-knob"
                            [class.toggle-knob-on]="lt.isActive"
                          ></span>
                        </button>
                      </td>
                      <td class="td-notion text-right">
                        <div class="flex items-center justify-end gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                          <button
                            type="button"
                            class="action-btn"
                            (click)="openEdit(lt); $event.stopPropagation()"
                            [attr.aria-label]="'Edit leave type: ' + lt.name"
                            title="Edit"
                          >
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                              <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z" />
                              <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z" />
                            </svg>
                          </button>
                        </div>
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td colspan="7" class="td-notion text-center text-neutral-400 py-8">
                        No leave types match your search.
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <!-- Mobile card list with up/down arrows -->
            <div class="md:hidden divide-y divide-neutral-100">
              @for (lt of filteredLeaveTypes(); track lt.leaveTypeId; let i = $index; let first = $first; let last = $last) {
                <div
                  class="p-4 hover:bg-neutral-50 transition-colors duration-150"
                  [class.opacity-60]="!lt.isActive"
                >
                  <div class="flex items-start gap-3">
                    <!-- Reorder arrows (mobile only) -->
                    @if (!isSearchActive()) {
                      <div class="flex flex-col gap-0.5 pt-0.5">
                        <button
                          type="button"
                          class="reorder-btn"
                          [disabled]="first"
                          (click)="moveItem(i, -1)"
                          [attr.aria-label]="'Move ' + lt.name + ' up'"
                        >
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                            <path fill-rule="evenodd" d="M11.78 9.78a.75.75 0 0 1-1.06 0L8 7.06 5.28 9.78a.75.75 0 0 1-1.06-1.06l3.25-3.25a.75.75 0 0 1 1.06 0l3.25 3.25a.75.75 0 0 1 0 1.06Z" clip-rule="evenodd"/>
                          </svg>
                        </button>
                        <button
                          type="button"
                          class="reorder-btn"
                          [disabled]="last"
                          (click)="moveItem(i, 1)"
                          [attr.aria-label]="'Move ' + lt.name + ' down'"
                        >
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                            <path fill-rule="evenodd" d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd"/>
                          </svg>
                        </button>
                      </div>
                    }

                    <!-- Card content -->
                    <div
                      class="flex-1 min-w-0 cursor-pointer"
                      (click)="openEdit(lt)"
                      (keydown.enter)="openEdit(lt)"
                      tabindex="0"
                      role="button"
                      [attr.aria-label]="'Edit leave type: ' + lt.name"
                    >
                      <div class="flex items-center justify-between mb-1">
                        <div class="flex items-center gap-2">
                          <span
                            class="color-tag"
                            [style.background-color]="lt.color"
                            [style.color]="getContrastColor(lt.color)"
                          >
                            {{ lt.code }}
                          </span>
                          <h3 class="text-sm font-semibold text-neutral-900">
                            {{ lt.name }}
                          </h3>
                        </div>
                        <button
                          type="button"
                          role="switch"
                          [attr.aria-checked]="lt.isActive"
                          [attr.aria-label]="lt.isActive ? 'Deactivate ' + lt.name : 'Activate ' + lt.name"
                          class="toggle-switch"
                          [class.toggle-switch-on]="lt.isActive"
                          (click)="toggleActive(lt, $event)"
                          [disabled]="isTogglingId() === lt.leaveTypeId"
                        >
                          <span
                            class="toggle-knob"
                            [class.toggle-knob-on]="lt.isActive"
                          ></span>
                        </button>
                      </div>
                      @if (lt.description) {
                        <p class="text-xs text-neutral-400 mb-1.5 line-clamp-2">
                          {{ lt.description }}
                        </p>
                      }
                      <div class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-neutral-400 mt-2">
                        <span>{{ lt.annualEntitlement }} days/year</span>
                        <span class="capitalize">{{ lt.accrualFrequency }}</span>
                        @if (lt.carryForwardLimit > 0) {
                          <span>CF: {{ lt.carryForwardLimit }}d</span>
                        }
                      </div>
                    </div>
                  </div>
                </div>
              } @empty {
                <div class="p-6 text-center text-sm text-neutral-400">
                  No leave types match your search.
                </div>
              }
            </div>
          </div>
        }
      }

      <!-- Slide-over form panel -->
      @if (formOpen()) {
        <div
          class="fixed inset-0 z-50"
          (keydown.escape)="closeForm()"
        >
          <!-- Overlay -->
          <div
            class="fixed inset-0 bg-black/20 backdrop-blur-sm"
            @overlayFade
            (click)="closeForm()"
            aria-hidden="true"
          ></div>

          <!-- Slide-over panel -->
          <div
            class="fixed inset-y-0 right-0 w-full sm:w-[32rem] bg-white shadow-notion-lg overflow-y-auto"
            @slideOver
            role="dialog"
            aria-modal="true"
            [attr.aria-label]="editingLeaveType() ? 'Edit leave type' : 'Create leave type'"
          >
            <app-leave-type-form
              [leaveType]="editingLeaveType()"
              (saved)="onFormSaved()"
              (cancelled)="closeForm()"
            />
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .line-clamp-1 {
      display: -webkit-box;
      -webkit-line-clamp: 1;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
    .line-clamp-2 {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    /* --- Table styles (Notion-inspired) --- */
    .th-notion {
      @apply px-4 py-3 text-xs font-semibold uppercase tracking-wider text-neutral-400
        whitespace-nowrap;
    }
    .td-notion { @apply px-4 py-3.5 text-sm; }
    .table-row-notion {
      @apply border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors
        duration-150;
    }
    .table-row-notion:last-child { @apply border-b-0; }

    /* --- CDK drag-drop --- */
    .cdk-drag-preview {
      @apply bg-white shadow-lg rounded-lg border border-neutral-200;
    }
    .cdk-drag-placeholder {
      @apply bg-brand-50/30 border-2 border-dashed border-brand-200 rounded-lg;
    }
    .cdk-drag-animating {
      transition: transform 200ms ease;
    }
    .cdk-drop-list-dragging .cdk-drag {
      transition: transform 200ms ease;
    }

    /* --- Drag handle --- */
    .drag-handle {
      @apply w-6 h-6 rounded flex items-center justify-center cursor-grab
        text-neutral-300 hover:text-neutral-500 hover:bg-neutral-100
        transition-colors duration-150;
    }
    .drag-handle-disabled {
      @apply cursor-default opacity-30 hover:bg-transparent hover:text-neutral-300;
    }

    /* --- Color tag --- */
    .color-tag {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold
        whitespace-nowrap min-w-[2rem] justify-center;
    }

    /* --- Toggle switch --- */
    .toggle-switch {
      @apply relative inline-flex h-5 w-9 items-center rounded-full flex-shrink-0
        transition-colors duration-200 bg-neutral-200 cursor-pointer
        focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .toggle-switch-on { @apply bg-brand-600; }
    .toggle-knob {
      @apply inline-block h-3.5 w-3.5 rounded-full bg-white shadow-sm
        transition-transform duration-200 translate-x-1;
    }
    .toggle-knob-on { @apply translate-x-[18px]; }

    /* --- Reorder buttons (mobile) --- */
    .reorder-btn {
      @apply w-6 h-6 rounded flex items-center justify-center
        text-neutral-300 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors duration-150
        disabled:opacity-30 disabled:cursor-not-allowed disabled:hover:bg-transparent disabled:hover:text-neutral-300;
    }

    /* --- Action buttons --- */
    .action-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 transition-colors duration-150
        hover:text-neutral-600 hover:bg-neutral-100;
    }
  `],
})
export class LeaveTypeListComponent implements OnInit, OnDestroy {
  private readonly leaveTypeService = inject(LeaveTypeService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly leaveTypes = signal<ILeaveType[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly searchQuery = signal('');

  // Form slide-over state
  readonly formOpen = signal(false);
  readonly editingLeaveType = signal<ILeaveType | null>(null);

  // Toggle state
  readonly isTogglingId = signal<string | null>(null);

  /** Filter leave types by search query */
  readonly filteredLeaveTypes = computed(() => {
    const query = this.searchQuery().toLowerCase().trim();
    const types = this.leaveTypes();
    if (!query) return types;
    return types.filter(
      (lt) =>
        lt.name.toLowerCase().includes(query) ||
        lt.code.toLowerCase().includes(query) ||
        (lt.description && lt.description.toLowerCase().includes(query))
    );
  });

  /** Whether search is active (disables reorder) */
  readonly isSearchActive = computed(() => this.searchQuery().trim().length > 0);

  /** Skeleton loading placeholder items */
  readonly skeletonItems = [1, 2, 3, 4, 5, 6];

  ngOnInit(): void {
    this.loadLeaveTypes();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  loadLeaveTypes(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.leaveTypeService
      .getLeaveTypes()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (types) => {
          this.leaveTypes.set(types);
          this.isLoading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading.set(false);
          this.loadError.set(
            err.error?.message || 'Failed to load leave types. Please try again.'
          );
        },
      });
  }

  getContrastColor(hex: string): string {
    return getContrastTextColor(hex);
  }

  // --- Form Slide-over ----------------------------------------

  openCreate(): void {
    this.editingLeaveType.set(null);
    this.formOpen.set(true);
  }

  openEdit(leaveType: ILeaveType): void {
    this.editingLeaveType.set(leaveType);
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editingLeaveType.set(null);
  }

  onFormSaved(): void {
    this.closeForm();
    this.loadLeaveTypes();
  }

  // --- Active/Inactive Toggle (AC-4) -------------------------

  toggleActive(lt: ILeaveType, event: Event): void {
    event.stopPropagation();
    this.isTogglingId.set(lt.leaveTypeId);

    const action$ = lt.isActive
      ? this.leaveTypeService.deactivateLeaveType(lt.leaveTypeId)
      : this.leaveTypeService.activateLeaveType(lt.leaveTypeId);

    action$
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          this.isTogglingId.set(null);
          const types = this.leaveTypes().map((t) =>
            t.leaveTypeId === updated.leaveTypeId ? updated : t
          );
          this.leaveTypes.set(types);
          this.toastr.success(
            updated.isActive
              ? `"${updated.name}" reactivated.`
              : `"${updated.name}" deactivated. Employees can no longer apply for this type.`
          );
        },
        error: (err: HttpErrorResponse) => {
          this.isTogglingId.set(null);
          const body = LeaveTypeService.parseError(err);
          this.toastr.error(body?.message || 'Failed to toggle leave type status.');
        },
      });
  }

  // --- Drag-and-Drop Reorder (FR-3, desktop) ------------------

  onDrop(event: CdkDragDrop<ILeaveType[]>): void {
    if (event.previousIndex === event.currentIndex) return;

    const types = [...this.leaveTypes()];
    moveItemInArray(types, event.previousIndex, event.currentIndex);

    // Update display orders
    types.forEach((t, i) => (t.displayOrder = i));
    this.leaveTypes.set(types);

    this.persistReorder(types);
  }

  // --- Arrow Reorder (FR-3, mobile) ---------------------------

  moveItem(index: number, direction: -1 | 1): void {
    const types = [...this.leaveTypes()];
    const targetIndex = index + direction;
    if (targetIndex < 0 || targetIndex >= types.length) return;

    // Swap
    const temp = types[index];
    types[index] = types[targetIndex];
    types[targetIndex] = temp;

    // Update display orders
    types.forEach((t, i) => (t.displayOrder = i));
    this.leaveTypes.set(types);

    this.persistReorder(types);
  }

  private persistReorder(types: ILeaveType[]): void {
    const orderedIds = types.map((t) => t.leaveTypeId);
    this.leaveTypeService
      .reorderLeaveTypes({ orderedIds })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        error: () => {
          this.toastr.error('Failed to reorder leave types. Please try again.');
          this.loadLeaveTypes(); // rollback
        },
      });
  }
}
