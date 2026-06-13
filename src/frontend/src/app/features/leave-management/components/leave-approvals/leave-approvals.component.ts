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
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { HttpErrorResponse } from '@angular/common/http';
import { LeaveApprovalsService } from '../../services/leave-approvals.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import { ILeaveType } from '../../models/leave-type.models';
import {
  IPendingLeaveRequest,
  IPendingFilterChip,
  PendingLeaveSortBy,
  PENDING_DEFAULT_PAGE_SIZE,
  PENDING_PAGE_SIZE_OPTIONS,
  PENDING_SORT_OPTIONS,
  BALANCE_TIER_CLASSES,
  balanceTier,
  IApproveLeaveRequest,
  ILeaveActionResult,
  isFurtherApprovalStatus,
} from '../../models/pending-leave.models';

/** Which action panel is currently expanded in the detail footer (US-LV-005). */
type ActionMode = 'none' | 'approve' | 'reject';

/**
 * US-LV-004: Manager pending leave-approval queue with inline balance.
 *
 * Notion-like database/table view (desktop) / compact cards (mobile) listing the
 * manager's team's pending requests, sorted oldest-first (AC-1). Server-side
 * pagination (AC-2) + chip-based filters by leave type / employee / date range
 * (AC-3) -- all paging/sorting/filtering round-trips to the API. Click-to-expand
 * detail panel slides in from the right (AC-4). Overdue rows get a red left-border
 * (BR-3, §8); inline balance pill is color-coded by remaining/entitlement (§8).
 *
 * US-LV-005 wires the Approve / Reject actions in the detail footer:
 *  - Approve (green + check): expands an OPTIONAL comment textarea, then confirms (AC-1).
 *  - Reject (red outline + X): reveals a MANDATORY reason textarea; submit disabled
 *    until the reason is non-empty (AC-2, BR-2).
 *  - Insufficient-balance confirm modal: on a 400 `insufficient_balance` with
 *    `negativeBalanceAllowed`, ask for confirmation then retry the approve with
 *    `confirmNegativeBalance: true`; a hard block surfaces the error via toast (AC-3).
 *  - On success the actioned item slides out of the queue + a toast confirms (§8).
 *  - 409 conflict -> "already actioned" toast + refresh the queue (AC-5).
 *
 * DEFER (seam + TODO, not built here):
 *  - Multi-level approval routing UI (AC-4) -> US-ADM-007. A "Pending L2"-style
 *    status is shown as a badge in the success toast; the item still leaves the queue.
 *  - Real-time SignalR push (FR-6/AC-5) -> manual "Refresh" affordance only;
 *    TODO(SignalR) hub at /hubs/notifications.
 *  - Payroll-lock messaging beyond surfacing the API error (BR-4) -> payroll module.
 *  - Leave-history (last 3) + team-calendar detail subsections -> rendered as
 *    clearly-labeled TODO seams since the API does not yet supply that data.
 */
@Component({
  selector: 'app-leave-approvals',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('slideOver', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('250ms cubic-bezier(0.4, 0, 0.2, 1)', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms cubic-bezier(0.4, 0, 1, 1)', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [style({ opacity: 0 }), animate('200ms ease-out', style({ opacity: 1 }))]),
      transition(':leave', [animate('200ms ease-in', style({ opacity: 0 }))]),
    ]),
    // US-LV-005: the actioned row slides out of the queue on success (§8).
    trigger('rowOut', [
      transition(':leave', [
        animate(
          '300ms cubic-bezier(0.4, 0, 1, 1)',
          style({ opacity: 0, transform: 'translateX(24px)', height: 0 })
        ),
      ]),
    ]),
    // US-LV-005: comment / reason textarea reveal (AC-1, AC-2).
    trigger('expand', [
      transition(':enter', [
        style({ opacity: 0, height: 0 }),
        animate('200ms ease-out', style({ opacity: 1, height: '*' })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0, height: 0 })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Leave Approvals</h1>
          <p class="text-sm text-neutral-500 mt-1">
            Pending time-off requests from your team, oldest first.
          </p>
        </div>
        <div class="flex items-center gap-2">
          <!-- AC-5 seam: manual refresh. TODO(SignalR): subscribe to /hubs/notifications
               for "new request" push and surface a refresh banner instead of polling. -->
          <button
            type="button"
            class="btn-secondary text-sm"
            (click)="refresh()"
            [disabled]="isLoading()"
            aria-label="Refresh pending queue"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
              class="w-4 h-4 mr-1.5" [class.animate-spin]="isLoading()" aria-hidden="true">
              <path fill-rule="evenodd" d="M15.312 11.424a5.5 5.5 0 0 1-9.201 2.466l-.312-.311h2.433a.75.75 0 0 0 0-1.5H3.989a.75.75 0 0 0-.75.75v4.242a.75.75 0 0 0 1.5 0v-2.43l.31.31a7 7 0 0 0 11.712-3.138.75.75 0 0 0-1.449-.39Zm1.23-3.723a.75.75 0 0 0 .219-.53V2.929a.75.75 0 0 0-1.5 0V5.36l-.31-.31A7 7 0 0 0 3.239 8.188a.75.75 0 1 0 1.448.389A5.5 5.5 0 0 1 13.89 6.11l.311.311h-2.432a.75.75 0 0 0 0 1.5h4.243a.75.75 0 0 0 .53-.219Z" clip-rule="evenodd"/>
            </svg>
            Refresh
          </button>
        </div>
      </div>

      <!-- Filter bar (§8) -->
      <div class="card-notion mb-4 !p-4">
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3">
          <!-- Leave type -->
          <div>
            <label for="f-type" class="label-notion">Leave Type</label>
            <select id="f-type" class="input-notion" [ngModel]="filterLeaveTypeId()"
              (ngModelChange)="filterLeaveTypeId.set($event)" aria-label="Filter by leave type">
              <option [ngValue]="null">All types</option>
              @for (lt of leaveTypes(); track lt.leaveTypeId) {
                <option [ngValue]="lt.leaveTypeId">{{ lt.name }}</option>
              }
            </select>
          </div>
          <!-- Employee (options derived from current result set) -->
          <div>
            <label for="f-emp" class="label-notion">Employee</label>
            <select id="f-emp" class="input-notion" [ngModel]="filterEmployeeId()"
              (ngModelChange)="filterEmployeeId.set($event)" aria-label="Filter by employee">
              <option [ngValue]="null">All employees</option>
              @for (emp of employeeOptions(); track emp.id) {
                <option [ngValue]="emp.id">{{ emp.name }}</option>
              }
            </select>
          </div>
          <!-- Date range -->
          <div>
            <label for="f-from" class="label-notion">From</label>
            <input id="f-from" type="date" class="input-notion" [ngModel]="filterStartDate()"
              (ngModelChange)="filterStartDate.set($event)" aria-label="Filter from date" />
          </div>
          <div>
            <label for="f-to" class="label-notion">To</label>
            <input id="f-to" type="date" class="input-notion" [ngModel]="filterEndDate()"
              (ngModelChange)="filterEndDate.set($event)" aria-label="Filter to date" />
          </div>
          <!-- Sort -->
          <div>
            <label for="f-sort" class="label-notion">Sort by</label>
            <select id="f-sort" class="input-notion" [ngModel]="sortBy()"
              (ngModelChange)="onSortChange($event)" aria-label="Sort queue">
              @for (opt of sortOptions; track opt.value) {
                <option [ngValue]="opt.value">{{ opt.label }}</option>
              }
            </select>
          </div>
        </div>

        <div class="flex items-center gap-3 mt-4">
          <button type="button" class="btn-primary !py-2 !px-4" (click)="applyFilters()">
            Apply Filters
          </button>
          <button type="button" class="btn-secondary !py-2 !px-4" (click)="clearFilters()">
            Clear All
          </button>
        </div>
      </div>

      <!-- Active filter chips (§8) -->
      @if (activeFilterChips().length > 0) {
        <div class="flex flex-wrap gap-2 mb-4" role="list" aria-label="Active filters">
          @for (chip of activeFilterChips(); track chip.filterKey) {
            <span class="filter-chip" role="listitem">
              <span class="text-brand-400">{{ chip.category }}:</span>
              {{ chip.label }}
              <button type="button" class="ml-0.5 hover:text-brand-900 transition-colors"
                (click)="removeChip(chip)"
                [attr.aria-label]="'Remove filter ' + chip.category">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor"
                  class="w-3.5 h-3.5" aria-hidden="true">
                  <path d="M5.28 4.22a.75.75 0 0 0-1.06 1.06L6.94 8l-2.72 2.72a.75.75 0 1 0 1.06 1.06L8 9.06l2.72 2.72a.75.75 0 1 0 1.06-1.06L9.06 8l2.72-2.72a.75.75 0 0 0-1.06-1.06L8 6.94 5.28 4.22Z"/>
                </svg>
              </button>
            </span>
          }
        </div>
      }

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="card-notion" aria-live="polite" aria-busy="true" data-test="skeleton">
          <div class="space-y-3">
            @for (_ of [1,2,3,4]; track $index) {
              <div class="skeleton-line w-full h-12"></div>
            }
          </div>
        </div>
      } @else if (requests().length === 0) {
        <!-- Empty state -->
        <div @fadeIn class="card-notion text-center py-16" data-test="empty">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No pending requests</h3>
          <p class="text-sm text-neutral-500">
            @if (hasActiveFilters()) {
              No requests match your filters.
            } @else {
              Your team has no leave requests awaiting approval.
            }
          </p>
        </div>
      } @else {
        <!-- Desktop table (Notion database view) -->
        <div class="hidden md:block card-notion !p-0 overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" aria-label="Pending leave requests">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="th">Employee</th>
                <th class="th">Type</th>
                <th class="th">Dates</th>
                <th class="th text-center">Days</th>
                <th class="th">Reason</th>
                <th class="th text-center">Balance</th>
                <th class="th text-center">Requested</th>
              </tr>
            </thead>
            <tbody>
              @for (req of requests(); track req.requestId) {
                <tr class="row" [class.row-overdue]="req.isOverdue" @rowOut
                  tabindex="0" role="button"
                  [attr.aria-label]="'View request from ' + req.employeeName"
                  (click)="openDetail(req)" (keydown.enter)="openDetail(req)"
                  data-test="queue-row">
                  <td class="td">
                    <div class="flex items-center gap-2.5">
                      <div class="avatar">
                        @if (req.employeePhoto) {
                          <img [src]="req.employeePhoto" [alt]="req.employeeName" class="w-full h-full object-cover" />
                        } @else {
                          <span>{{ initials(req.employeeName) }}</span>
                        }
                      </div>
                      <span class="font-medium text-neutral-900">{{ req.employeeName }}</span>
                      @if (req.isOverdue) {
                        <span class="overdue-tag" data-test="overdue-tag">Overdue</span>
                      }
                    </div>
                  </td>
                  <td class="td">
                    <span class="type-badge" [style.background-color]="req.leaveTypeColor" [style.color]="'#ffffff'">
                      {{ req.leaveTypeName }}
                    </span>
                  </td>
                  <td class="td text-neutral-600 whitespace-nowrap">
                    {{ req.startDate | date:'mediumDate' }} – {{ req.endDate | date:'mediumDate' }}
                  </td>
                  <td class="td text-center font-medium text-neutral-900">{{ req.totalDays }}</td>
                  <td class="td text-neutral-500 max-w-[14rem] truncate">{{ req.reason }}</td>
                  <td class="td text-center">
                    <span class="balance-pill" [class]="balanceClass(req)" data-test="balance-pill"
                      [attr.title]="req.currentBalance + ' days remaining'">
                      {{ balanceLabel(req) }}
                    </span>
                  </td>
                  <td class="td text-center text-neutral-500 text-xs whitespace-nowrap">
                    {{ req.requestedAt | date:'mediumDate' }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile cards -->
        <div class="md:hidden space-y-3" @fadeIn>
          @for (req of requests(); track req.requestId) {
            <div class="card-notion cursor-pointer" [class.row-overdue]="req.isOverdue" @rowOut
              tabindex="0" role="button"
              [attr.aria-label]="'View request from ' + req.employeeName"
              (click)="openDetail(req)" (keydown.enter)="openDetail(req)"
              data-test="queue-card">
              <div class="flex items-center justify-between mb-2">
                <div class="flex items-center gap-2.5">
                  <div class="avatar">
                    @if (req.employeePhoto) {
                      <img [src]="req.employeePhoto" [alt]="req.employeeName" class="w-full h-full object-cover" />
                    } @else {
                      <span>{{ initials(req.employeeName) }}</span>
                    }
                  </div>
                  <span class="font-medium text-neutral-900">{{ req.employeeName }}</span>
                </div>
                <span class="balance-pill" [class]="balanceClass(req)">
                  {{ balanceLabel(req) }}
                </span>
              </div>
              <div class="flex items-center gap-2 mb-1">
                <span class="type-badge" [style.background-color]="req.leaveTypeColor" [style.color]="'#ffffff'">
                  {{ req.leaveTypeName }}
                </span>
                @if (req.isOverdue) { <span class="overdue-tag">Overdue</span> }
              </div>
              <p class="text-sm text-neutral-700">
                {{ req.startDate | date:'mediumDate' }} – {{ req.endDate | date:'mediumDate' }}
                <span class="text-neutral-400">· {{ req.totalDays }} day(s)</span>
              </p>
              <p class="text-xs text-neutral-500 mt-1 truncate">{{ req.reason }}</p>
            </div>
          }
        </div>

        <!-- Pagination (AC-2) -->
        <div class="flex flex-col sm:flex-row items-center justify-between gap-3 mt-4 px-1"
          role="navigation" aria-label="Pagination">
          <p class="text-sm text-neutral-500" data-test="total-count">
            Showing {{ showingFrom() }}-{{ showingTo() }} of {{ totalCount() }} requests
          </p>
          <div class="flex items-center gap-2">
            <select class="input-notion !w-auto !py-1.5 !text-xs" [ngModel]="pageSize()"
              (ngModelChange)="onPageSizeChange($event)" aria-label="Results per page">
              @for (size of pageSizeOptions; track size) {
                <option [ngValue]="size">{{ size }} per page</option>
              }
            </select>
            <div class="flex items-center gap-1">
              <button type="button" class="pagination-btn" [disabled]="currentPage() <= 1"
                (click)="goToPage(currentPage() - 1)" aria-label="Previous page">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                  <path fill-rule="evenodd" d="M9.78 4.22a.75.75 0 0 1 0 1.06L7.06 8l2.72 2.72a.75.75 0 1 1-1.06 1.06L5.47 8.53a.75.75 0 0 1 0-1.06l3.25-3.25a.75.75 0 0 1 1.06 0Z" clip-rule="evenodd"/>
                </svg>
              </button>
              <span class="px-2 text-sm text-neutral-600" data-test="page-indicator">
                Page {{ currentPage() }} of {{ totalPages() }}
              </span>
              <button type="button" class="pagination-btn" [disabled]="currentPage() >= totalPages()"
                (click)="goToPage(currentPage() + 1)" aria-label="Next page">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                  <path fill-rule="evenodd" d="M6.22 4.22a.75.75 0 0 1 1.06 0l3.25 3.25a.75.75 0 0 1 0 1.06l-3.25 3.25a.75.75 0 0 1-1.06-1.06L8.94 8 6.22 5.28a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd"/>
                </svg>
              </button>
            </div>
          </div>
        </div>
      }
    </div>

    <!-- Detail panel: Notion page-peek slide-over from the right (AC-4) -->
    @if (selected(); as req) {
      <div class="fixed inset-0 z-40 bg-black/30" @backdrop
        (click)="closeDetail()" aria-hidden="true"></div>
      <aside
        class="fixed top-0 right-0 z-50 h-full w-full max-w-md bg-white shadow-xl overflow-y-auto"
        @slideOver role="dialog" aria-modal="true" aria-labelledby="detail-title"
        data-test="detail-panel">
        <div class="flex items-start justify-between px-6 pt-5 pb-4 border-b border-neutral-100"
          [class.detail-overdue]="req.isOverdue">
          <div class="flex items-center gap-3">
            <div class="avatar !w-11 !h-11 !text-sm">
              @if (req.employeePhoto) {
                <img [src]="req.employeePhoto" [alt]="req.employeeName" class="w-full h-full object-cover" />
              } @else {
                <span>{{ initials(req.employeeName) }}</span>
              }
            </div>
            <div>
              <h2 id="detail-title" class="text-base font-semibold text-neutral-900">{{ req.employeeName }}</h2>
              <span class="type-badge mt-1" [style.background-color]="req.leaveTypeColor" [style.color]="'#ffffff'">
                {{ req.leaveTypeName }}
              </span>
            </div>
          </div>
          <button type="button" class="icon-btn" (click)="closeDetail()" aria-label="Close detail panel">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
              <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
            </svg>
          </button>
        </div>

        <div class="px-6 py-5 space-y-5">
          @if (req.isOverdue) {
            <div class="rounded-lg bg-red-50 text-red-700 text-xs px-3 py-2">
              This request is overdue (pending more than 30 days).
            </div>
          }

          <!-- Core details -->
          <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <div>
              <dt class="detail-label">Date range</dt>
              <dd class="text-neutral-800">{{ req.startDate | date:'mediumDate' }} – {{ req.endDate | date:'mediumDate' }}</dd>
            </div>
            <div>
              <dt class="detail-label">Total days</dt>
              <dd class="text-neutral-800 font-medium">{{ req.totalDays }}</dd>
            </div>
            <div>
              <dt class="detail-label">Current balance</dt>
              <dd>
                <span class="balance-pill" [class]="balanceClass(req)">
                  {{ balanceLabel(req) }} days
                </span>
              </dd>
            </div>
            <div>
              <dt class="detail-label">Requested</dt>
              <dd class="text-neutral-800">{{ req.requestedAt | date:'medium' }}</dd>
            </div>
            @if (req.teamConflictCount > 0) {
              <div class="col-span-2">
                <dt class="detail-label">Team conflicts</dt>
                <dd class="text-amber-700">
                  {{ req.teamConflictCount }} team member(s) already off during this period.
                </dd>
              </div>
            }
          </dl>

          <!-- Reason -->
          <div>
            <h3 class="detail-label mb-1">Reason</h3>
            <p class="text-sm text-neutral-700 whitespace-pre-line">{{ req.reason || '—' }}</p>
          </div>

          <!-- Attachments (AC-4 downloadable) -->
          <div>
            <h3 class="detail-label mb-1">Attachments</h3>
            @if (req.attachmentUrls && req.attachmentUrls.length > 0) {
              <ul class="space-y-1">
                @for (url of req.attachmentUrls; track url) {
                  <li>
                    <a [href]="url" target="_blank" rel="noopener" download
                      class="text-sm text-brand-600 hover:text-brand-700 underline break-all">
                      {{ fileName(url) }}
                    </a>
                  </li>
                }
              </ul>
            } @else if (req.hasAttachments) {
              <p class="text-sm text-neutral-400">Attachments available — open the request to download.</p>
            } @else {
              <p class="text-sm text-neutral-400">No attachments.</p>
            }
          </div>

          <!-- TODO seam: leave history (last 3). API does not yet return this (AC-4). -->
          <div class="rounded-lg border border-dashed border-neutral-200 p-3">
            <h3 class="detail-label mb-1">Leave history (last 3)</h3>
            <p class="text-xs text-neutral-400">
              TODO(US-LV-004 detail): not yet supplied by the pending API — render once the
              backend returns recent history for the employee.
            </p>
          </div>

          <!-- TODO seam: team calendar snippet. API does not yet return this (AC-4). -->
          <div class="rounded-lg border border-dashed border-neutral-200 p-3">
            <h3 class="detail-label mb-1">Team calendar</h3>
            <p class="text-xs text-neutral-400">
              TODO(US-LV-004 / US-LV-009): who else is off during this period — pending the
              team-calendar endpoint.
            </p>
          </div>
        </div>

        <!-- Quick actions (US-LV-005, §8). Mobile: full-width buttons at the bottom. -->
        <div class="sticky bottom-0 bg-white border-t border-neutral-100 px-6 py-4 space-y-3">
          <!-- Approve panel: optional comment textarea (AC-1, BR-2). -->
          @if (actionMode() === 'approve') {
            <div @expand data-test="approve-panel">
              <label for="approve-comment" class="detail-label mb-1 block">
                Comment <span class="text-neutral-300 normal-case">(optional)</span>
              </label>
              <textarea id="approve-comment" rows="2" class="action-textarea"
                [ngModel]="comment()" (ngModelChange)="comment.set($event)"
                placeholder="Add an optional note for the employee…"
                data-test="approve-comment"></textarea>
            </div>
          }

          <!-- Reject panel: mandatory reason textarea (AC-2, BR-2). -->
          @if (actionMode() === 'reject') {
            <div @expand data-test="reject-panel">
              <label for="reject-reason" class="detail-label mb-1 block">
                Reason <span class="text-red-400 normal-case">(required)</span>
              </label>
              <textarea id="reject-reason" rows="2" class="action-textarea"
                [class.textarea-error]="rejectReason().length === 0"
                [ngModel]="rejectReason()" (ngModelChange)="rejectReason.set($event)"
                placeholder="Explain why this request is being rejected…"
                aria-required="true" data-test="reject-reason"></textarea>
              @if (rejectReason().trim().length === 0) {
                <p class="text-xs text-red-500 mt-1">A rejection reason is required.</p>
              }
            </div>
          }

          @if (actionMode() === 'none') {
            <!-- Idle: the two primary action buttons. -->
            <div class="flex flex-col sm:flex-row gap-3">
              <button type="button" class="btn-approve flex-1" (click)="startApprove()"
                [disabled]="isActioning()" data-test="approve-btn">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                  class="w-4 h-4 mr-1.5" aria-hidden="true">
                  <path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd"/>
                </svg>
                Approve
              </button>
              <button type="button" class="btn-reject flex-1" (click)="startReject()"
                [disabled]="isActioning()" data-test="reject-btn">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                  class="w-4 h-4 mr-1.5" aria-hidden="true">
                  <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                </svg>
                Reject
              </button>
            </div>
          } @else {
            <!-- Confirm / cancel for the chosen action. -->
            <div class="flex flex-col sm:flex-row gap-3">
              <button type="button" class="btn-secondary flex-1" (click)="cancelAction()"
                [disabled]="isActioning()" data-test="action-cancel">
                Cancel
              </button>
              @if (actionMode() === 'approve') {
                <button type="button" class="btn-approve flex-1" (click)="confirmApprove()"
                  [disabled]="isActioning()" data-test="confirm-approve">
                  {{ isActioning() ? 'Approving…' : 'Confirm approval' }}
                </button>
              } @else {
                <button type="button" class="btn-reject-solid flex-1" (click)="confirmReject()"
                  [disabled]="isActioning() || rejectReason().trim().length === 0"
                  data-test="confirm-reject">
                  {{ isActioning() ? 'Rejecting…' : 'Confirm rejection' }}
                </button>
              }
            </div>
          }
        </div>
      </aside>
    }

    <!-- Insufficient-balance confirmation modal (AC-3). Shown only when the API
         signals an insufficient balance that the leave type allows going negative. -->
    @if (confirmNegative(); as ctx) {
      <div class="fixed inset-0 z-[60] bg-black/40 flex items-center justify-center p-4"
        @backdrop role="dialog" aria-modal="true" aria-labelledby="neg-title"
        data-test="negative-modal">
        <div class="bg-white rounded-xl shadow-xl w-full max-w-sm p-6" @fadeIn>
          <div class="flex items-start gap-3 mb-3">
            <div class="w-9 h-9 rounded-full bg-amber-50 text-amber-600 flex items-center justify-center flex-shrink-0">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
                <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd"/>
              </svg>
            </div>
            <div>
              <h3 id="neg-title" class="text-base font-semibold text-neutral-900">Insufficient balance</h3>
              <p class="text-sm text-neutral-600 mt-1" data-test="negative-message">{{ ctx.message }}</p>
            </div>
          </div>
          <p class="text-xs text-neutral-500 mb-4">
            This leave type allows a negative balance. Approving will take the employee's
            balance below zero. Do you want to continue?
          </p>
          <div class="flex flex-col sm:flex-row gap-3">
            <button type="button" class="btn-secondary flex-1" (click)="dismissNegative()"
              [disabled]="isActioning()" data-test="negative-cancel">
              Cancel
            </button>
            <button type="button" class="btn-approve flex-1" (click)="approveWithNegative()"
              [disabled]="isActioning()" data-test="negative-confirm">
              {{ isActioning() ? 'Approving…' : 'Approve anyway' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-6xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .label-notion { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .input-notion {
      @apply block w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }

    .th { @apply text-left py-3 px-4 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .td { @apply py-3 px-4 align-middle; }
    .row { @apply border-b border-neutral-50 cursor-pointer transition-colors hover:bg-neutral-50/60 outline-none; }
    .row:focus-visible { @apply ring-2 ring-inset ring-brand-500; }
    .row-overdue { @apply border-l-2 border-l-red-400; }
    .detail-overdue { @apply border-l-2 border-l-red-400; }

    .avatar {
      @apply w-9 h-9 rounded-full bg-brand-100 text-brand-700 text-xs font-semibold
        flex items-center justify-center overflow-hidden flex-shrink-0;
    }
    .type-badge { @apply inline-flex items-center px-2.5 py-1 rounded-md text-xs font-semibold; }
    .balance-pill { @apply inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ring-1 ring-inset; }
    .overdue-tag { @apply inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-semibold bg-red-50 text-red-600 ring-1 ring-inset ring-red-200; }

    .filter-chip { @apply inline-flex items-center gap-1 rounded-full bg-brand-50 text-brand-700 px-3 py-1 text-xs font-medium; }

    .detail-label { @apply text-[11px] font-medium text-neutral-400 uppercase tracking-wider; }
    .icon-btn { @apply w-8 h-8 rounded-lg flex items-center justify-center text-neutral-400 hover:text-neutral-700 hover:bg-neutral-100 transition-colors; }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200 hover:bg-brand-700;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-approve {
      @apply inline-flex items-center justify-center rounded-lg bg-green-600 px-4 py-2.5
        text-sm font-medium text-white transition-colors disabled:opacity-40 disabled:cursor-not-allowed;
    }
    .btn-reject {
      @apply inline-flex items-center justify-center rounded-lg border border-red-200 bg-white px-4 py-2.5
        text-sm font-medium text-red-600 transition-colors hover:bg-red-50 disabled:opacity-40 disabled:cursor-not-allowed;
    }
    .btn-reject-solid {
      @apply inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2.5
        text-sm font-medium text-white transition-colors hover:bg-red-700 disabled:opacity-40 disabled:cursor-not-allowed;
    }
    .action-textarea {
      @apply block w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors resize-none
        focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .textarea-error { @apply border-red-300 focus:border-red-400 focus:ring-red-400; }
    .pagination-btn {
      @apply inline-flex items-center justify-center w-8 h-8 rounded-lg text-sm
        text-neutral-600 hover:bg-neutral-100 transition-colors
        disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent;
    }
  `],
})
export class LeaveApprovalsComponent implements OnInit, OnDestroy {
  private readonly approvalsService = inject(LeaveApprovalsService);
  private readonly leaveTypeService = inject(LeaveTypeService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  // ─── State ──────────────────────────────────────────────────
  readonly requests = signal<IPendingLeaveRequest[]>([]);
  readonly totalCount = signal(0);
  readonly currentPage = signal(1);
  readonly pageSize = signal<number>(PENDING_DEFAULT_PAGE_SIZE);
  readonly isLoading = signal(true);
  readonly leaveTypes = signal<ILeaveType[]>([]);
  readonly selected = signal<IPendingLeaveRequest | null>(null);

  // ─── US-LV-005 action state ─────────────────────────────────
  /** Which inline action panel is expanded in the detail footer. */
  readonly actionMode = signal<ActionMode>('none');
  /** Optional approve comment (AC-1, BR-2). */
  readonly comment = signal('');
  /** Mandatory reject reason (AC-2, BR-2). */
  readonly rejectReason = signal('');
  /** True while an approve/reject HTTP call is in flight (disables buttons). */
  readonly isActioning = signal(false);
  /**
   * Insufficient-balance confirmation context (AC-3). Non-null while the modal
   * is shown; holds the API message to display before the manager confirms.
   */
  readonly confirmNegative = signal<{ message: string } | null>(null);

  // Pending (un-applied) filter inputs
  readonly filterLeaveTypeId = signal<string | null>(null);
  readonly filterEmployeeId = signal<string | null>(null);
  readonly filterStartDate = signal<string>('');
  readonly filterEndDate = signal<string>('');
  readonly sortBy = signal<PendingLeaveSortBy>('requestedAt');

  // Applied filters (the last set sent to the server) — drive the chips.
  private readonly appliedLeaveTypeId = signal<string | null>(null);
  private readonly appliedEmployeeId = signal<string | null>(null);
  private readonly appliedStartDate = signal<string>('');
  private readonly appliedEndDate = signal<string>('');

  // Constants for the template
  readonly pageSizeOptions = PENDING_PAGE_SIZE_OPTIONS;
  readonly sortOptions = PENDING_SORT_OPTIONS;

  // ─── Computed ───────────────────────────────────────────────
  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize()))
  );
  readonly showingFrom = computed(() =>
    this.totalCount() === 0 ? 0 : (this.currentPage() - 1) * this.pageSize() + 1
  );
  readonly showingTo = computed(() =>
    Math.min(this.currentPage() * this.pageSize(), this.totalCount())
  );

  /** Distinct employee options derived from the current page's results. */
  readonly employeeOptions = computed(() => {
    const seen = new Map<string, string>();
    for (const r of this.requests()) {
      if (!seen.has(r.employeeId)) {
        seen.set(r.employeeId, r.employeeName);
      }
    }
    return Array.from(seen, ([id, name]) => ({ id, name }));
  });

  readonly hasActiveFilters = computed(
    () =>
      !!this.appliedLeaveTypeId() ||
      !!this.appliedEmployeeId() ||
      !!this.appliedStartDate() ||
      !!this.appliedEndDate()
  );

  readonly activeFilterChips = computed<IPendingFilterChip[]>(() => {
    const chips: IPendingFilterChip[] = [];
    const ltId = this.appliedLeaveTypeId();
    if (ltId) {
      const name = this.leaveTypes().find((t) => t.leaveTypeId === ltId)?.name ?? ltId;
      chips.push({ category: 'Type', label: name, filterKey: 'leaveTypeId' });
    }
    const empId = this.appliedEmployeeId();
    if (empId) {
      const name =
        this.requests().find((r) => r.employeeId === empId)?.employeeName ?? empId;
      chips.push({ category: 'Employee', label: name, filterKey: 'employeeId' });
    }
    if (this.appliedStartDate()) {
      chips.push({ category: 'From', label: this.appliedStartDate(), filterKey: 'startDate' });
    }
    if (this.appliedEndDate()) {
      chips.push({ category: 'To', label: this.appliedEndDate(), filterKey: 'endDate' });
    }
    return chips;
  });

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    this.loadLeaveTypes();
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ───────────────────────────────────────────
  load(): void {
    this.isLoading.set(true);
    this.approvalsService
      .getPendingQueue({
        page: this.currentPage(),
        pageSize: this.pageSize(),
        leaveTypeId: this.appliedLeaveTypeId(),
        employeeId: this.appliedEmployeeId(),
        startDate: this.appliedStartDate() || null,
        endDate: this.appliedEndDate() || null,
        sortBy: this.sortBy(),
        // AC-1: oldest-first. Both sort fields default to ascending here.
        sortAscending: true,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.requests.set(res.items ?? []);
          this.totalCount.set(res.totalCount ?? 0);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.toastr.error('Failed to load the pending leave queue.');
        },
      });
  }

  private loadLeaveTypes(): void {
    this.leaveTypeService
      .getLeaveTypes()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (types) => this.leaveTypes.set(types),
        // Non-fatal: the type filter simply stays empty if this fails.
        error: () => this.leaveTypes.set([]),
      });
  }

  /** AC-5 seam: manual refresh re-fetches the current page with current filters. */
  refresh(): void {
    this.load();
  }

  // ─── Filters (server-side, AC-3) ────────────────────────────
  applyFilters(): void {
    this.appliedLeaveTypeId.set(this.filterLeaveTypeId());
    this.appliedEmployeeId.set(this.filterEmployeeId());
    this.appliedStartDate.set(this.filterStartDate());
    this.appliedEndDate.set(this.filterEndDate());
    this.currentPage.set(1);
    this.load();
  }

  clearFilters(): void {
    this.filterLeaveTypeId.set(null);
    this.filterEmployeeId.set(null);
    this.filterStartDate.set('');
    this.filterEndDate.set('');
    this.appliedLeaveTypeId.set(null);
    this.appliedEmployeeId.set(null);
    this.appliedStartDate.set('');
    this.appliedEndDate.set('');
    this.currentPage.set(1);
    this.load();
  }

  removeChip(chip: IPendingFilterChip): void {
    switch (chip.filterKey) {
      case 'leaveTypeId':
        this.filterLeaveTypeId.set(null);
        this.appliedLeaveTypeId.set(null);
        break;
      case 'employeeId':
        this.filterEmployeeId.set(null);
        this.appliedEmployeeId.set(null);
        break;
      case 'startDate':
        this.filterStartDate.set('');
        this.appliedStartDate.set('');
        break;
      case 'endDate':
        this.filterEndDate.set('');
        this.appliedEndDate.set('');
        break;
    }
    this.currentPage.set(1);
    this.load();
  }

  onSortChange(value: PendingLeaveSortBy): void {
    this.sortBy.set(value);
    this.currentPage.set(1);
    this.load();
  }

  // ─── Pagination (server-side, AC-2) ─────────────────────────
  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) {
      return;
    }
    this.currentPage.set(page);
    this.load();
  }

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.currentPage.set(1);
    this.load();
  }

  // ─── Detail panel (AC-4) ────────────────────────────────────
  openDetail(req: IPendingLeaveRequest): void {
    this.selected.set(req);
    this.resetActionState();
  }

  closeDetail(): void {
    if (this.isActioning()) {
      return; // don't drop the panel mid-request
    }
    this.selected.set(null);
    this.resetActionState();
  }

  // ─── US-LV-005: Approve / Reject ────────────────────────────

  /** Reset the action footer + modal to their idle state. */
  private resetActionState(): void {
    this.actionMode.set('none');
    this.comment.set('');
    this.rejectReason.set('');
    this.confirmNegative.set(null);
  }

  /** AC-1: expand the optional comment textarea. */
  startApprove(): void {
    this.actionMode.set('approve');
  }

  /** AC-2: reveal the mandatory reason textarea. */
  startReject(): void {
    this.actionMode.set('reject');
  }

  /** Collapse the expanded panel without submitting. */
  cancelAction(): void {
    this.actionMode.set('none');
    this.comment.set('');
    this.rejectReason.set('');
  }

  /** AC-1: confirm approval (optionally with comment). */
  confirmApprove(): void {
    const req = this.selected();
    if (!req || this.isActioning()) {
      return;
    }
    const comment = this.comment().trim();
    this.sendApprove(req, { comment: comment || undefined });
  }

  /** AC-2 / BR-2: confirm rejection. No-op if the reason is empty (submit is also disabled). */
  confirmReject(): void {
    const req = this.selected();
    const reason = this.rejectReason().trim();
    if (!req || this.isActioning() || reason.length === 0) {
      return;
    }
    this.isActioning.set(true);
    this.approvalsService
      .reject(req.requestId, { reason })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => this.onActionSuccess(req, res, 'rejected'),
        error: (err: HttpErrorResponse) => this.onActionError(err),
      });
  }

  /** AC-3: user accepted the negative-balance warning — retry with the override flag. */
  approveWithNegative(): void {
    const req = this.selected();
    if (!req || this.isActioning()) {
      return;
    }
    const comment = this.comment().trim();
    this.confirmNegative.set(null);
    this.sendApprove(req, {
      comment: comment || undefined,
      confirmNegativeBalance: true,
    });
  }

  /** Dismiss the insufficient-balance modal without approving (AC-3). */
  dismissNegative(): void {
    if (this.isActioning()) {
      return;
    }
    this.confirmNegative.set(null);
  }

  /** Shared approve call used by both the first attempt and the negative-balance retry. */
  private sendApprove(req: IPendingLeaveRequest, body: IApproveLeaveRequest): void {
    this.isActioning.set(true);
    this.approvalsService
      .approve(req.requestId, body)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => this.onActionSuccess(req, res, 'approved'),
        error: (err: HttpErrorResponse) => this.onActionError(err),
      });
  }

  /**
   * Success path for approve/reject: remove the actioned item from the queue
   * (it slides out via @rowOut), close the panel, and toast the confirmation (§8).
   * For a multi-level "Pending L2"-style status (AC-4, DEFERRED) the item still
   * leaves THIS manager's queue and the toast notes the next-level status.
   */
  private onActionSuccess(
    req: IPendingLeaveRequest,
    res: ILeaveActionResult,
    verb: 'approved' | 'rejected'
  ): void {
    this.isActioning.set(false);
    this.removeFromQueue(req.requestId);
    this.closeAfterAction();

    // TODO(US-ADM-007): when multi-level approval is configured, route to the
    // next-level queue instead of just badging the status in the toast.
    if (verb === 'approved' && isFurtherApprovalStatus(res?.status)) {
      this.toastr.info(
        `Leave request for ${req.employeeName} moved to "${res.status}".`
      );
    } else {
      this.toastr.success(`Leave request ${verb} for ${req.employeeName}`);
    }
  }

  /**
   * Error path. Maps the API contract to the §8 UX:
   *  - 409 -> already actioned by another manager; toast + refresh (AC-5).
   *  - 400 insufficient_balance + negativeBalanceAllowed -> confirm modal (AC-3).
   *  - everything else (hard block, payroll lock BR-4, validation) -> toast the message.
   */
  private onActionError(err: HttpErrorResponse): void {
    this.isActioning.set(false);
    const parsed = LeaveApprovalsService.parseActionError(err);

    if (err.status === 409) {
      this.toastr.error(
        parsed?.message ?? 'This request has already been actioned.'
      );
      this.closeAfterAction();
      this.refresh(); // the item was already handled — re-sync the queue (AC-5).
      return;
    }

    if (
      err.status === 400 &&
      parsed?.code === 'insufficient_balance' &&
      parsed.negativeBalanceAllowed
    ) {
      // Soft block: ask the manager to confirm going negative, then retry (AC-3).
      this.confirmNegative.set({
        message:
          parsed.message ??
          'The employee no longer has enough balance for this request.',
      });
      return;
    }

    // Hard block / payroll lock (BR-4) / validation / unknown — surface verbatim.
    this.toastr.error(parsed?.message ?? 'Failed to action this leave request.');
  }

  /** Remove an actioned request from the in-memory queue + decrement the count. */
  private removeFromQueue(requestId: string): void {
    this.requests.update((list) => list.filter((r) => r.requestId !== requestId));
    this.totalCount.update((n) => Math.max(0, n - 1));
  }

  /** Close the detail panel + clear action state after a completed action. */
  private closeAfterAction(): void {
    this.selected.set(null);
    this.resetActionState();
  }

  // ─── View helpers ───────────────────────────────────────────
  balanceClass(req: IPendingLeaveRequest): string {
    return BALANCE_TIER_CLASSES[balanceTier(req.currentBalance, req.entitlementDays)];
  }

  /** Pill label: "remaining/entitlement" when entitlement is known, else just remaining. */
  balanceLabel(req: IPendingLeaveRequest): string {
    return req.entitlementDays != null
      ? `${req.currentBalance}/${req.entitlementDays}`
      : `${req.currentBalance}`;
  }

  initials(name: string): string {
    const parts = (name ?? '').trim().split(/\s+/);
    const first = parts[0]?.[0] ?? '';
    const last = parts.length > 1 ? parts[parts.length - 1][0] : '';
    return (first + last).toUpperCase() || '?';
  }

  fileName(url: string): string {
    try {
      const clean = url.split('?')[0];
      const segment = clean.substring(clean.lastIndexOf('/') + 1);
      return decodeURIComponent(segment) || url;
    } catch {
      return url;
    }
  }
}
