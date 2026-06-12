import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  DestroyRef,
  ElementRef,
  viewChild,
  HostListener,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, distinctUntilChanged, catchError, of, finalize } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { OrgTreeService } from '../../services/org-tree.service';
import {
  IOrgTreeNode,
  IOrgTreeNodeState,
  OrgTreeView,
  buildTreeFromFlat,
  createNodeState,
  findNodeInTree,
  findPathToNode,
} from '../../models/org-tree.models';

/**
 * US-CHR-006: Organization Tree / Hierarchy Visualization page.
 *
 * Two views via segmented toggle: Department Hierarchy (AC-1) and
 * Reporting Structure (AC-3). Nodes are mini-cards with avatar, name,
 * title/department, employee-count badge. Expand/collapse with lazy
 * child loading (FR-2, FR-6, AC-5). Pan/zoom on desktop (FR-3).
 * Search with typeahead and auto-scroll (FR-4, AC-4).
 * Export as PNG (FR-7). Responsive: mobile accordion (NFR-4).
 * WCAG 2.1 AA: keyboard navigation, screen reader (NFR-5).
 */
@Component({
  selector: 'app-org-tree-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('expandCollapse', [
      transition(':enter', [
        style({ opacity: 0, height: 0, overflow: 'hidden' }),
        animate('200ms ease-out', style({ opacity: 1, height: '*' })),
      ]),
      transition(':leave', [
        style({ overflow: 'hidden' }),
        animate('150ms ease-in', style({ opacity: 0, height: 0 })),
      ]),
    ]),
    trigger('slideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(16px)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0, transform: 'translateX(16px)' })),
      ]),
    ]),
  ],
  template: `
    <!-- Page header -->
    <div class="px-6 pt-6 pb-4">
      <h1 class="text-xl font-semibold text-neutral-900">Organization Chart</h1>
      <p class="text-sm text-neutral-500 mt-1">Visualize department hierarchy and reporting structures</p>
    </div>

    <!-- Toolbar -->
    <div class="px-6 pb-4 flex flex-wrap items-center gap-3">
      <!-- View toggle (segmented control) -->
      <div
        class="inline-flex rounded-lg bg-neutral-100 p-0.5"
        role="radiogroup"
        aria-label="Organization tree view"
      >
        <button
          type="button"
          role="radio"
          [attr.aria-checked]="currentView() === 'department'"
          class="px-4 py-1.5 text-sm font-medium rounded-md transition-all duration-200"
          [class]="currentView() === 'department'
            ? 'bg-white text-neutral-900 shadow-sm'
            : 'text-neutral-500 hover:text-neutral-700'"
          (click)="switchView('department')"
        >
          Department Hierarchy
        </button>
        <button
          type="button"
          role="radio"
          [attr.aria-checked]="currentView() === 'reporting'"
          class="px-4 py-1.5 text-sm font-medium rounded-md transition-all duration-200"
          [class]="currentView() === 'reporting'
            ? 'bg-white text-neutral-900 shadow-sm'
            : 'text-neutral-500 hover:text-neutral-700'"
          (click)="switchView('reporting')"
        >
          Reporting Structure
        </button>
      </div>

      <!-- Search -->
      <div class="relative flex-1 min-w-[200px] max-w-xs ml-auto">
        <svg
          class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-neutral-400"
          xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true"
        >
          <path fill-rule="evenodd" d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z" clip-rule="evenodd" />
        </svg>
        <input
          type="text"
          placeholder="Search employee or department..."
          class="w-full pl-9 pr-3 py-2 text-sm bg-white border border-neutral-200 rounded-lg
                 focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-400
                 transition-all duration-200"
          [ngModel]="searchQuery()"
          (ngModelChange)="onSearchInput($event)"
          aria-label="Search organization tree"
        />
      </div>

      <!-- Zoom controls (desktop only) -->
      <div class="hidden md:flex items-center gap-1 bg-neutral-100 rounded-lg p-0.5">
        <button
          type="button"
          class="w-8 h-8 flex items-center justify-center rounded-md text-neutral-500 hover:text-neutral-700 hover:bg-white transition-colors duration-150"
          (click)="zoomIn()"
          aria-label="Zoom in"
          title="Zoom in"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
            <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
          </svg>
        </button>
        <button
          type="button"
          class="w-8 h-8 flex items-center justify-center rounded-md text-neutral-500 hover:text-neutral-700 hover:bg-white transition-colors duration-150"
          (click)="zoomOut()"
          aria-label="Zoom out"
          title="Zoom out"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
            <path fill-rule="evenodd" d="M4 10a.75.75 0 0 1 .75-.75h10.5a.75.75 0 0 1 0 1.5H4.75A.75.75 0 0 1 4 10Z" clip-rule="evenodd" />
          </svg>
        </button>
        <button
          type="button"
          class="w-8 h-8 flex items-center justify-center rounded-md text-neutral-500 hover:text-neutral-700 hover:bg-white transition-colors duration-150"
          (click)="fitToScreen()"
          aria-label="Fit to screen"
          title="Fit to screen"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
            <path fill-rule="evenodd" d="M4.25 2A2.25 2.25 0 0 0 2 4.25v2a.75.75 0 0 0 1.5 0v-2a.75.75 0 0 1 .75-.75h2a.75.75 0 0 0 0-1.5h-2ZM13.75 2a.75.75 0 0 0 0 1.5h2a.75.75 0 0 1 .75.75v2a.75.75 0 0 0 1.5 0v-2A2.25 2.25 0 0 0 15.75 2h-2ZM3.5 13.75a.75.75 0 0 0-1.5 0v2A2.25 2.25 0 0 0 4.25 18h2a.75.75 0 0 0 0-1.5h-2a.75.75 0 0 1-.75-.75v-2ZM18 13.75a.75.75 0 0 0-1.5 0v2a.75.75 0 0 1-.75.75h-2a.75.75 0 0 0 0 1.5h2A2.25 2.25 0 0 0 18 15.75v-2Z" clip-rule="evenodd" />
          </svg>
        </button>
        <span class="text-xs text-neutral-400 px-1">{{ zoomPercent() }}%</span>
      </div>

      <!-- Export button -->
      <button
        type="button"
        class="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-neutral-600
               bg-white border border-neutral-200 rounded-lg hover:bg-neutral-50
               transition-colors duration-150"
        (click)="exportAsPng()"
        [disabled]="exporting()"
        aria-label="Export organization chart as PNG"
      >
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
          <path d="M10.75 2.75a.75.75 0 0 0-1.5 0v8.614L6.295 8.235a.75.75 0 1 0-1.09 1.03l4.25 4.5a.75.75 0 0 0 1.09 0l4.25-4.5a.75.75 0 0 0-1.09-1.03l-2.955 3.129V2.75Z" />
          <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z" />
        </svg>
        {{ exporting() ? 'Exporting...' : 'Export PNG' }}
      </button>
    </div>

    <!-- Main content -->
    <div class="px-6 pb-6 flex-1 min-h-0">
      @if (loading()) {
        <!-- Skeleton loading -->
        <div class="card-notion p-6 space-y-4" aria-busy="true" aria-label="Loading organization chart">
          @for (i of [1,2,3]; track i) {
            <div class="flex items-center gap-3">
              <div class="w-8 h-8 rounded-full bg-neutral-200 animate-pulse"></div>
              <div class="flex-1 space-y-2">
                <div class="h-4 bg-neutral-200 rounded w-1/4 animate-pulse"></div>
                <div class="h-3 bg-neutral-100 rounded w-1/6 animate-pulse"></div>
              </div>
            </div>
            @for (j of [1,2]; track j) {
              <div class="flex items-center gap-3 ml-10">
                <div class="w-8 h-8 rounded-full bg-neutral-200 animate-pulse"></div>
                <div class="flex-1 space-y-2">
                  <div class="h-4 bg-neutral-200 rounded w-1/5 animate-pulse"></div>
                  <div class="h-3 bg-neutral-100 rounded w-1/8 animate-pulse"></div>
                </div>
              </div>
            }
          }
        </div>
      } @else if (errorMessage()) {
        <!-- Error state -->
        <div class="card-notion p-8 text-center">
          <svg class="mx-auto w-12 h-12 text-neutral-300 mb-3" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" aria-hidden="true">
            <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
          </svg>
          <p class="text-sm text-neutral-600 mb-2">{{ errorMessage() }}</p>
          <button
            type="button"
            class="text-sm text-blue-600 hover:text-blue-700 font-medium"
            (click)="loadInitialTree()"
          >
            Try again
          </button>
        </div>
      } @else if (treeRoots().length === 0) {
        <!-- Empty state -->
        <div class="card-notion p-8 text-center">
          <svg class="mx-auto w-12 h-12 text-neutral-300 mb-3" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" aria-hidden="true">
            <path stroke-linecap="round" stroke-linejoin="round" d="M2.25 21h19.5m-18-18v18m10.5-18v18m6-13.5V21M6.75 6.75h.75m-.75 3h.75m-.75 3h.75m3-6h.75m-.75 3h.75m-.75 3h.75M6.75 21v-3.375c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125V21M3 3h12m-.75 4.5H21m-3.75 7.5h.008v.008h-.008v-.008Zm0 3h.008v.008h-.008v-.008Zm0 3h.008v.008h-.008v-.008Z" />
          </svg>
          @if (currentView() === 'reporting') {
            <p class="text-sm text-neutral-600 mb-1">No reporting structure available yet.</p>
            <p class="text-xs text-neutral-400">Manager assignments may not be configured. Try the Department Hierarchy view.</p>
          } @else {
            <p class="text-sm text-neutral-600 mb-1">No departments found.</p>
            <p class="text-xs text-neutral-400">Create departments to see the organization chart.</p>
          }
        </div>
      } @else {
        <!-- Desktop: zoomable tree canvas -->
        <div
          class="hidden md:block card-notion overflow-hidden relative"
          style="min-height: 400px"
          #treeCanvas
        >
          <div
            class="org-tree-viewport"
            [style.transform]="'scale(' + zoom() + ') translate(' + panX() + 'px, ' + panY() + 'px)'"
            [style.transform-origin]="'top left'"
            (mousedown)="onPanStart($event)"
            #treeViewport
          >
            <div
              class="p-6"
              role="tree"
              aria-label="Organization chart"
              #treeContent
            >
              @for (root of treeRoots(); track root.node.nodeId) {
                <ng-container
                  *ngTemplateOutlet="desktopNodeTpl; context: { $implicit: root }"
                ></ng-container>
              }
            </div>
          </div>
        </div>

        <!-- Mobile: vertical accordion list -->
        <div
          class="md:hidden card-notion p-4"
          role="tree"
          aria-label="Organization chart"
        >
          @for (root of treeRoots(); track root.node.nodeId) {
            <ng-container
              *ngTemplateOutlet="mobileNodeTpl; context: { $implicit: root }"
            ></ng-container>
          }
        </div>
      }
    </div>

    <!-- Detail panel (slides in from right) -->
    @if (selectedNode()) {
      <div
        @slideIn
        class="fixed inset-y-0 right-0 w-80 bg-white shadow-lg border-l border-neutral-200
               z-50 overflow-y-auto p-6"
        role="dialog"
        aria-label="Node details"
      >
        <div class="flex items-center justify-between mb-4">
          <h2 class="text-base font-semibold text-neutral-900">Details</h2>
          <button
            type="button"
            class="w-8 h-8 flex items-center justify-center rounded-md text-neutral-400
                   hover:text-neutral-600 hover:bg-neutral-100 transition-colors duration-150"
            (click)="closeDetail()"
            aria-label="Close detail panel"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
              <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
            </svg>
          </button>
        </div>

        <!-- Node info -->
        <div class="flex items-center gap-3 mb-4">
          <div class="node-avatar" [class.node-avatar-dept]="selectedNode()!.node.nodeType === 'department'">
            @if (selectedNode()!.node.avatarUrl) {
              <img [src]="selectedNode()!.node.avatarUrl" alt="" class="w-full h-full object-cover rounded-full" />
            } @else if (selectedNode()!.node.nodeType === 'department') {
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
                <path fill-rule="evenodd" d="M4 16.5v-13h-.25a.75.75 0 0 1 0-1.5h12.5a.75.75 0 0 1 0 1.5H16v13h.25a.75.75 0 0 1 0 1.5h-3.5a.75.75 0 0 1-.75-.75v-2.5a.75.75 0 0 0-.75-.75h-2.5a.75.75 0 0 0-.75.75v2.5a.75.75 0 0 1-.75.75h-3.5a.75.75 0 0 1 0-1.5H4Zm3-11a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 .5.5v1a.5.5 0 0 1-.5.5h-1a.5.5 0 0 1-.5-.5v-1Zm.5 3.5a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h1a.5.5 0 0 0 .5-.5v-1a.5.5 0 0 0-.5-.5h-1Zm3.5-3.5a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 .5.5v1a.5.5 0 0 1-.5.5h-1a.5.5 0 0 1-.5-.5v-1Zm.5 3.5a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h1a.5.5 0 0 0 .5-.5v-1a.5.5 0 0 0-.5-.5h-1Z" clip-rule="evenodd" />
              </svg>
            } @else {
              <span class="text-sm font-medium">{{ getInitials(selectedNode()!.node.name) }}</span>
            }
          </div>
          <div class="min-w-0">
            <p class="text-sm font-semibold text-neutral-900 truncate">{{ selectedNode()!.node.name }}</p>
            @if (selectedNode()!.node.title) {
              <p class="text-xs text-neutral-500 truncate">{{ selectedNode()!.node.title }}</p>
            }
            @if (selectedNode()!.node.nodeType === 'department') {
              <p class="text-xs text-neutral-400">{{ selectedNode()!.node.employeeCount }} employees</p>
            }
          </div>
        </div>

        <hr class="border-neutral-100 mb-4" />

        <!-- Manager info -->
        @if (detailManager()) {
          <div class="mb-4">
            <p class="text-xs font-medium text-neutral-400 uppercase tracking-wide mb-2">Manager</p>
            <div class="flex items-center gap-2">
              <div class="w-7 h-7 rounded-full bg-neutral-100 flex items-center justify-center text-xs text-neutral-500">
                {{ getInitials(detailManager()!.name) }}
              </div>
              <div>
                <p class="text-sm text-neutral-700">{{ detailManager()!.name }}</p>
                @if (detailManager()!.title) {
                  <p class="text-xs text-neutral-400">{{ detailManager()!.title }}</p>
                }
              </div>
            </div>
          </div>
        }

        <!-- Direct reports / sub-departments -->
        @if (detailChildren().length > 0) {
          <div class="mb-4">
            <p class="text-xs font-medium text-neutral-400 uppercase tracking-wide mb-2">
              {{ selectedNode()!.node.nodeType === 'department' ? 'Sub-departments & Employees' : 'Direct Reports' }}
            </p>
            <div class="space-y-1.5">
              @for (child of detailChildren(); track child.node.nodeId) {
                <div class="flex items-center gap-2 py-1">
                  <div
                    class="w-6 h-6 rounded-full flex items-center justify-center text-xs"
                    [class]="child.node.nodeType === 'department'
                      ? 'bg-blue-50 text-blue-500'
                      : 'bg-neutral-100 text-neutral-500'"
                  >
                    @if (child.node.nodeType === 'department') {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3 h-3" aria-hidden="true">
                        <path fill-rule="evenodd" d="M4 16.5v-13h-.25a.75.75 0 0 1 0-1.5h12.5a.75.75 0 0 1 0 1.5H16v13h.25a.75.75 0 0 1 0 1.5h-3.5a.75.75 0 0 1-.75-.75v-2.5a.75.75 0 0 0-.75-.75h-2.5a.75.75 0 0 0-.75.75v2.5a.75.75 0 0 1-.75.75h-3.5a.75.75 0 0 1 0-1.5H4Z" clip-rule="evenodd" />
                      </svg>
                    } @else {
                      {{ getInitials(child.node.name) }}
                    }
                  </div>
                  <div class="min-w-0">
                    <p class="text-sm text-neutral-700 truncate">{{ child.node.name }}</p>
                    @if (child.node.title) {
                      <p class="text-xs text-neutral-400 truncate">{{ child.node.title }}</p>
                    }
                  </div>
                </div>
              }
            </div>
          </div>
        }

        <!-- Link to department management -->
        @if (selectedNode()!.node.nodeType === 'department') {
          <a
            [routerLink]="['/departments']"
            class="inline-flex items-center gap-1.5 text-sm text-blue-600 hover:text-blue-700 font-medium mt-2"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
              <path fill-rule="evenodd" d="M4.22 11.78a.75.75 0 0 1 0-1.06L9.44 5.5H5.75a.75.75 0 0 1 0-1.5h5.5a.75.75 0 0 1 .75.75v5.5a.75.75 0 0 1-1.5 0V6.56l-5.22 5.22a.75.75 0 0 1-1.06 0Z" clip-rule="evenodd" />
            </svg>
            Go to Department Management
          </a>
        }
      </div>
    }

    <!-- Desktop tree node template (recursive) -->
    <ng-template #desktopNodeTpl let-nodeState>
      <div
        class="org-node-wrapper"
        [style.margin-left.px]="nodeState.level * 32"
      >
        <!-- SVG connector line from parent -->
        @if (nodeState.level > 0) {
          <div class="connector-line" aria-hidden="true">
            <svg width="32" height="100%" class="absolute -left-8 top-0 h-full">
              <path
                d="M16 0 C16 16, 16 16, 32 16"
                fill="none"
                stroke="#e5e7eb"
                stroke-width="1.5"
              />
            </svg>
          </div>
        }

        <!-- Node card -->
        <div
          class="org-node-card group"
          [class.org-node-highlighted]="nodeState.highlighted"
          [attr.id]="'org-node-' + nodeState.node.nodeId"
          role="treeitem"
          [attr.aria-expanded]="nodeState.node.childrenCount > 0 ? nodeState.expanded : null"
          [attr.aria-level]="nodeState.level + 1"
          [attr.aria-label]="nodeState.node.name + (nodeState.node.title ? ', ' + nodeState.node.title : '') + ', level ' + (nodeState.level + 1)"
          tabindex="0"
          (click)="selectNode(nodeState)"
          (keydown)="onNodeKeydown($event, nodeState)"
        >
          <!-- Avatar -->
          <div class="node-avatar" [class.node-avatar-dept]="nodeState.node.nodeType === 'department'">
            @if (nodeState.node.avatarUrl) {
              <img [src]="nodeState.node.avatarUrl" alt="" class="w-full h-full object-cover rounded-full" />
            } @else if (nodeState.node.nodeType === 'department') {
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                <path fill-rule="evenodd" d="M4 16.5v-13h-.25a.75.75 0 0 1 0-1.5h12.5a.75.75 0 0 1 0 1.5H16v13h.25a.75.75 0 0 1 0 1.5h-3.5a.75.75 0 0 1-.75-.75v-2.5a.75.75 0 0 0-.75-.75h-2.5a.75.75 0 0 0-.75.75v2.5a.75.75 0 0 1-.75.75h-3.5a.75.75 0 0 1 0-1.5H4Zm3-11a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 .5.5v1a.5.5 0 0 1-.5.5h-1a.5.5 0 0 1-.5-.5v-1Zm.5 3.5a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h1a.5.5 0 0 0 .5-.5v-1a.5.5 0 0 0-.5-.5h-1Zm3.5-3.5a.5.5 0 0 1 .5-.5h1a.5.5 0 0 1 .5.5v1a.5.5 0 0 1-.5.5h-1a.5.5 0 0 1-.5-.5v-1Zm.5 3.5a.5.5 0 0 0-.5.5v1a.5.5 0 0 0 .5.5h1a.5.5 0 0 0 .5-.5v-1a.5.5 0 0 0-.5-.5h-1Z" clip-rule="evenodd" />
              </svg>
            } @else {
              <span class="text-xs font-medium">{{ getInitials(nodeState.node.name) }}</span>
            }
          </div>

          <!-- Info -->
          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-2">
              <span class="text-sm font-medium text-neutral-900 truncate">{{ nodeState.node.name }}</span>
              @if (nodeState.node.nodeType === 'department' && nodeState.node.employeeCount > 0) {
                <span class="inline-flex items-center px-1.5 py-0.5 rounded-full text-xs font-medium bg-blue-50 text-blue-600 whitespace-nowrap">
                  {{ nodeState.node.employeeCount }}
                </span>
              }
            </div>
            @if (nodeState.node.title) {
              <p class="text-xs text-neutral-400 truncate mt-0.5">{{ nodeState.node.title }}</p>
            }
          </div>

          <!-- Expand/collapse -->
          @if (nodeState.node.childrenCount > 0) {
            <button
              type="button"
              class="expand-toggle"
              (click)="toggleNode($event, nodeState)"
              [attr.aria-label]="nodeState.expanded ? 'Collapse ' + nodeState.node.name : 'Expand ' + nodeState.node.name"
            >
              @if (nodeState.loadingChildren) {
                <svg class="w-4 h-4 animate-spin text-neutral-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" aria-hidden="true">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
              } @else {
                <svg
                  xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                  class="w-4 h-4 transition-transform duration-200"
                  [class.rotate-90]="nodeState.expanded"
                  aria-hidden="true"
                >
                  <path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 0 1 .02-1.06L11.168 10 7.23 6.29a.75.75 0 1 1 1.04-1.08l4.5 4.25a.75.75 0 0 1 0 1.08l-4.5 4.25a.75.75 0 0 1-1.06-.02Z" clip-rule="evenodd" />
                </svg>
              }
            </button>
          }
        </div>

        <!-- Children (expanded) -->
        @if (nodeState.expanded && nodeState.children.length > 0) {
          <div @expandCollapse role="group">
            @for (child of nodeState.children; track child.node.nodeId) {
              <ng-container
                *ngTemplateOutlet="desktopNodeTpl; context: { $implicit: child }"
              ></ng-container>
            }
          </div>
        }
      </div>
    </ng-template>

    <!-- Mobile node template (accordion) -->
    <ng-template #mobileNodeTpl let-nodeState>
      <div
        class="mobile-node"
        [style.padding-left.rem]="nodeState.level * 1.25"
        role="treeitem"
        [attr.aria-expanded]="nodeState.node.childrenCount > 0 ? nodeState.expanded : null"
        [attr.aria-level]="nodeState.level + 1"
        [attr.aria-label]="nodeState.node.name + ', level ' + (nodeState.level + 1)"
      >
        <!-- Left indent border -->
        @if (nodeState.level > 0) {
          <div
            class="absolute left-0 top-0 bottom-0 border-l-2 border-neutral-200"
            [style.left.rem]="(nodeState.level - 1) * 1.25 + 0.625"
            aria-hidden="true"
          ></div>
        }

        <div
          class="mobile-node-card"
          [class.org-node-highlighted]="nodeState.highlighted"
          tabindex="0"
          (click)="selectNode(nodeState)"
          (keydown)="onNodeKeydown($event, nodeState)"
        >
          <!-- Expand/collapse for mobile -->
          @if (nodeState.node.childrenCount > 0) {
            <button
              type="button"
              class="expand-toggle-mobile"
              (click)="toggleNode($event, nodeState)"
              [attr.aria-label]="nodeState.expanded ? 'Collapse' : 'Expand'"
            >
              @if (nodeState.loadingChildren) {
                <svg class="w-3.5 h-3.5 animate-spin text-neutral-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" aria-hidden="true">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
              } @else {
                <svg
                  xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                  class="w-3.5 h-3.5 transition-transform duration-200"
                  [class.rotate-90]="nodeState.expanded"
                  aria-hidden="true"
                >
                  <path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 0 1 .02-1.06L11.168 10 7.23 6.29a.75.75 0 1 1 1.04-1.08l4.5 4.25a.75.75 0 0 1 0 1.08l-4.5 4.25a.75.75 0 0 1-1.06-.02Z" clip-rule="evenodd" />
                </svg>
              }
            </button>
          } @else {
            <div class="w-6" aria-hidden="true"></div>
          }

          <!-- Avatar -->
          <div class="node-avatar-sm" [class.node-avatar-dept]="nodeState.node.nodeType === 'department'">
            @if (nodeState.node.avatarUrl) {
              <img [src]="nodeState.node.avatarUrl" alt="" class="w-full h-full object-cover rounded-full" />
            } @else if (nodeState.node.nodeType === 'department') {
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3 h-3" aria-hidden="true">
                <path fill-rule="evenodd" d="M4 16.5v-13h-.25a.75.75 0 0 1 0-1.5h12.5a.75.75 0 0 1 0 1.5H16v13h.25a.75.75 0 0 1 0 1.5h-3.5a.75.75 0 0 1-.75-.75v-2.5a.75.75 0 0 0-.75-.75h-2.5a.75.75 0 0 0-.75.75v2.5a.75.75 0 0 1-.75.75h-3.5a.75.75 0 0 1 0-1.5H4Z" clip-rule="evenodd" />
              </svg>
            } @else {
              <span class="text-[10px] font-medium">{{ getInitials(nodeState.node.name) }}</span>
            }
          </div>

          <!-- Info -->
          <div class="flex-1 min-w-0">
            <span class="text-sm font-medium text-neutral-900 truncate block">{{ nodeState.node.name }}</span>
            @if (nodeState.node.title) {
              <span class="text-xs text-neutral-400 truncate block">{{ nodeState.node.title }}</span>
            }
          </div>

          @if (nodeState.node.nodeType === 'department' && nodeState.node.employeeCount > 0) {
            <span class="inline-flex items-center px-1.5 py-0.5 rounded-full text-xs font-medium bg-blue-50 text-blue-600 whitespace-nowrap">
              {{ nodeState.node.employeeCount }}
            </span>
          }
        </div>

        <!-- Mobile children -->
        @if (nodeState.expanded && nodeState.children.length > 0) {
          <div @expandCollapse>
            @for (child of nodeState.children; track child.node.nodeId) {
              <ng-container
                *ngTemplateOutlet="mobileNodeTpl; context: { $implicit: child }"
              ></ng-container>
            }
          </div>
        }
      </div>
    </ng-template>
  `,
  styles: [`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    .card-notion {
      @apply bg-white rounded-xl border border-neutral-100 shadow-sm;
    }

    /* ─── Desktop node card ─────────────────────────── */

    .org-node-wrapper {
      @apply relative mb-1;
    }

    .org-node-card {
      @apply flex items-center gap-3 rounded-lg bg-white border border-neutral-100
        shadow-sm px-3 py-2.5 cursor-pointer transition-all duration-200
        hover:shadow-md hover:border-neutral-200;
    }

    .org-node-highlighted {
      @apply ring-2 ring-blue-400 border-blue-300 bg-blue-50/50;
    }

    .org-node-card:focus-visible {
      @apply outline-none ring-2 ring-blue-500 ring-offset-1;
    }

    /* ─── Avatar ────────────────────────────────────── */

    .node-avatar {
      @apply w-8 h-8 rounded-full bg-neutral-100 flex items-center justify-center
        text-neutral-500 flex-shrink-0 overflow-hidden;
    }

    .node-avatar-sm {
      @apply w-6 h-6 rounded-full bg-neutral-100 flex items-center justify-center
        text-neutral-500 flex-shrink-0 overflow-hidden;
    }

    .node-avatar-dept {
      @apply bg-blue-50 text-blue-500;
    }

    /* ─── Expand toggle ─────────────────────────────── */

    .expand-toggle {
      @apply w-7 h-7 rounded-md flex items-center justify-center flex-shrink-0
        text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors duration-150;
    }

    .expand-toggle-mobile {
      @apply w-6 h-6 rounded-md flex items-center justify-center flex-shrink-0
        text-neutral-400 hover:text-neutral-600 transition-colors duration-150;
    }

    /* ─── Connector line (desktop) ──────────────────── */

    .connector-line {
      @apply absolute -left-8 top-0 h-full pointer-events-none;
    }

    /* ─── Viewport (pan/zoom) ───────────────────────── */

    .org-tree-viewport {
      @apply transition-transform duration-100 cursor-grab;
      min-width: fit-content;
    }

    .org-tree-viewport:active {
      @apply cursor-grabbing;
    }

    /* ─── Mobile ────────────────────────────────────── */

    .mobile-node {
      @apply relative;
    }

    .mobile-node-card {
      @apply flex items-center gap-2 py-2 px-2 rounded-lg cursor-pointer
        transition-colors duration-150 hover:bg-neutral-50;
    }

    .mobile-node-card:focus-visible {
      @apply outline-none ring-2 ring-blue-500 ring-offset-1;
    }
  `],
})
export class OrgTreePageComponent implements OnInit {
  private readonly orgTreeService = inject(OrgTreeService);
  private readonly toastr = inject(ToastrService);
  private readonly destroyRef = inject(DestroyRef);

  readonly treeContent = viewChild<ElementRef<HTMLDivElement>>('treeContent');
  readonly treeCanvas = viewChild<ElementRef<HTMLDivElement>>('treeCanvas');

  // ─── State signals ──────────────────────────────────────────

  readonly currentView = signal<OrgTreeView>('department');
  readonly treeRoots = signal<IOrgTreeNodeState[]>([]);
  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly searchQuery = signal('');
  readonly selectedNode = signal<IOrgTreeNodeState | null>(null);
  readonly exporting = signal(false);

  // Zoom/pan state
  readonly zoom = signal(1);
  readonly panX = signal(0);
  readonly panY = signal(0);
  readonly zoomPercent = computed(() => Math.round(this.zoom() * 100));

  // Detail panel computed data
  readonly detailManager = computed<IOrgTreeNode | null>(() => {
    const sel = this.selectedNode();
    if (!sel || !sel.node.parentId) return null;
    const parent = findNodeInTree(this.treeRoots(), sel.node.parentId);
    return parent ? parent.node : null;
  });

  readonly detailChildren = computed<IOrgTreeNodeState[]>(() => {
    const sel = this.selectedNode();
    return sel ? sel.children : [];
  });

  // Search debounce
  private readonly searchSubject = new Subject<string>();

  // Pan tracking
  private isPanning = false;
  private panStartX = 0;
  private panStartY = 0;
  private panOriginX = 0;
  private panOriginY = 0;

  ngOnInit(): void {
    this.loadInitialTree();

    this.searchSubject
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((query) => this.performSearch(query));
  }

  // ─── View switching ─────────────────────────────────────────

  switchView(view: OrgTreeView): void {
    if (this.currentView() === view) return;
    this.currentView.set(view);
    this.selectedNode.set(null);
    this.searchQuery.set('');
    this.loadInitialTree();
  }

  // ─── Data loading ───────────────────────────────────────────

  loadInitialTree(): void {
    this.loading.set(true);
    this.errorMessage.set(null);

    this.orgTreeService
      .getOrgTree({
        view: this.currentView(),
        depth: 2,
      })
      .pipe(
        catchError((err) => {
          const msg =
            err?.status === 404
              ? 'Organization tree endpoint not available.'
              : 'Failed to load organization chart. Please try again.';
          this.errorMessage.set(msg);
          return of([] as IOrgTreeNode[]);
        }),
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((nodes) => {
        const tree = buildTreeFromFlat(nodes, 0);
        // Auto-expand root nodes
        for (const root of tree) {
          root.expanded = true;
        }
        this.treeRoots.set(tree);
      });
  }

  // ─── Node expand/collapse with lazy loading (FR-2, FR-6) ───

  toggleNode(event: Event, nodeState: IOrgTreeNodeState): void {
    event.stopPropagation();

    if (nodeState.expanded) {
      // Collapse
      this.updateNodeInTree(nodeState.node.nodeId, (n) => ({
        ...n,
        expanded: false,
      }));
      return;
    }

    // Expand — lazy load children if not yet fetched
    if (!nodeState.childrenLoaded && nodeState.node.childrenCount > 0) {
      this.updateNodeInTree(nodeState.node.nodeId, (n) => ({
        ...n,
        loadingChildren: true,
      }));

      this.orgTreeService
        .getOrgTree({
          view: this.currentView(),
          parentId: nodeState.node.nodeId,
          depth: 1,
        })
        .pipe(
          catchError(() => {
            this.toastr.error('Failed to load children.');
            return of([] as IOrgTreeNode[]);
          }),
          takeUntilDestroyed(this.destroyRef)
        )
        .subscribe((childNodes) => {
          const children = childNodes.map((cn) =>
            createNodeState(cn, nodeState.level + 1, [], cn.childrenCount === 0)
          );
          this.updateNodeInTree(nodeState.node.nodeId, (n) => ({
            ...n,
            children,
            childrenLoaded: true,
            loadingChildren: false,
            expanded: true,
          }));
        });
    } else {
      // Already loaded — just expand
      this.updateNodeInTree(nodeState.node.nodeId, (n) => ({
        ...n,
        expanded: true,
      }));
    }
  }

  // ─── Node selection ─────────────────────────────────────────

  selectNode(nodeState: IOrgTreeNodeState): void {
    this.selectedNode.set(nodeState);
  }

  closeDetail(): void {
    this.selectedNode.set(null);
  }

  // ─── Search (FR-4, AC-4) ────────────────────────────────────

  onSearchInput(query: string): void {
    this.searchQuery.set(query);
    this.searchSubject.next(query);
  }

  private performSearch(query: string): void {
    if (!query.trim()) {
      // Clear highlights
      this.clearHighlights(this.treeRoots());
      this.treeRoots.update((roots) => [...roots]);
      return;
    }

    const lowerQuery = query.toLowerCase();

    // Client-side search through loaded nodes
    const match = this.findFirstMatch(this.treeRoots(), lowerQuery);
    if (match) {
      // Expand path to node
      const path = findPathToNode(this.treeRoots(), match.node.nodeId);
      this.clearHighlights(this.treeRoots());

      for (const nodeId of path) {
        this.updateNodeInTree(nodeId, (n) => ({
          ...n,
          expanded: true,
          highlighted: nodeId === match.node.nodeId,
        }));
      }

      // Scroll to node
      requestAnimationFrame(() => {
        const el = document.getElementById('org-node-' + match.node.nodeId);
        if (el) {
          el.scrollIntoView({ behavior: 'smooth', block: 'center' });
          el.focus();
        }
      });
    }
  }

  private findFirstMatch(
    roots: IOrgTreeNodeState[],
    query: string
  ): IOrgTreeNodeState | null {
    for (const root of roots) {
      if (root.node.name.toLowerCase().includes(query)) return root;
      if (root.node.title?.toLowerCase().includes(query)) return root;
      const childMatch = this.findFirstMatch(root.children, query);
      if (childMatch) return childMatch;
    }
    return null;
  }

  private clearHighlights(nodes: IOrgTreeNodeState[]): void {
    for (const node of nodes) {
      node.highlighted = false;
      this.clearHighlights(node.children);
    }
  }

  // ─── Zoom controls (FR-3) ──────────────────────────────────

  zoomIn(): void {
    this.zoom.update((z) => Math.min(z + 0.1, 2));
  }

  zoomOut(): void {
    this.zoom.update((z) => Math.max(z - 0.1, 0.3));
  }

  fitToScreen(): void {
    this.zoom.set(1);
    this.panX.set(0);
    this.panY.set(0);
  }

  @HostListener('wheel', ['$event'])
  onWheel(event: WheelEvent): void {
    // Only zoom when over the tree canvas area
    const canvas = this.treeCanvas()?.nativeElement;
    if (!canvas || !canvas.contains(event.target as Node)) return;

    event.preventDefault();
    const delta = event.deltaY > 0 ? -0.05 : 0.05;
    this.zoom.update((z) => Math.min(Math.max(z + delta, 0.3), 2));
  }

  // ─── Pan (FR-3) ────────────────────────────────────────────

  onPanStart(event: MouseEvent): void {
    // Only start pan on left click and not on a button/input
    if (event.button !== 0) return;
    const target = event.target as HTMLElement;
    if (target.closest('button, a, input')) return;

    this.isPanning = true;
    this.panStartX = event.clientX;
    this.panStartY = event.clientY;
    this.panOriginX = this.panX();
    this.panOriginY = this.panY();
    event.preventDefault();
  }

  @HostListener('document:mousemove', ['$event'])
  onPanMove(event: MouseEvent): void {
    if (!this.isPanning) return;
    const dx = (event.clientX - this.panStartX) / this.zoom();
    const dy = (event.clientY - this.panStartY) / this.zoom();
    this.panX.set(this.panOriginX + dx);
    this.panY.set(this.panOriginY + dy);
  }

  @HostListener('document:mouseup')
  onPanEnd(): void {
    this.isPanning = false;
  }

  // ─── Keyboard navigation (NFR-5) ───────────────────────────

  onNodeKeydown(event: KeyboardEvent, nodeState: IOrgTreeNodeState): void {
    switch (event.key) {
      case 'Enter':
      case ' ':
        event.preventDefault();
        this.selectNode(nodeState);
        break;
      case 'ArrowRight':
        event.preventDefault();
        if (nodeState.node.childrenCount > 0 && !nodeState.expanded) {
          this.toggleNode(event, nodeState);
        } else if (nodeState.expanded && nodeState.children.length > 0) {
          this.focusNode(nodeState.children[0].node.nodeId);
        }
        break;
      case 'ArrowLeft':
        event.preventDefault();
        if (nodeState.expanded) {
          this.toggleNode(event, nodeState);
        } else if (nodeState.node.parentId) {
          this.focusNode(nodeState.node.parentId);
        }
        break;
      case 'ArrowDown':
        event.preventDefault();
        this.focusNextSibling(nodeState, 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.focusNextSibling(nodeState, -1);
        break;
      case 'Escape':
        event.preventDefault();
        this.closeDetail();
        break;
    }
  }

  private focusNode(nodeId: string): void {
    requestAnimationFrame(() => {
      const el = document.getElementById('org-node-' + nodeId);
      if (el) el.focus();
    });
  }

  private focusNextSibling(nodeState: IOrgTreeNodeState, direction: number): void {
    // Find siblings from parent
    const parentId = nodeState.node.parentId;
    const siblings = parentId
      ? (findNodeInTree(this.treeRoots(), parentId)?.children ?? [])
      : this.treeRoots();

    const idx = siblings.findIndex((s) => s.node.nodeId === nodeState.node.nodeId);
    const nextIdx = idx + direction;
    if (nextIdx >= 0 && nextIdx < siblings.length) {
      this.focusNode(siblings[nextIdx].node.nodeId);
    }
  }

  // ─── Export PNG (FR-7) ──────────────────────────────────────

  exportAsPng(): void {
    const content = this.treeContent()?.nativeElement;
    if (!content) {
      this.toastr.warning('No chart content to export.');
      return;
    }

    this.exporting.set(true);

    // Use a simple approach: capture the tree DOM via canvas
    // We create an SVG foreignObject wrapping the HTML, then draw to canvas.
    try {
      const clone = content.cloneNode(true) as HTMLElement;
      clone.style.transform = 'none';
      clone.style.width = content.scrollWidth + 'px';

      const svgData = `
        <svg xmlns="http://www.w3.org/2000/svg" width="${content.scrollWidth}" height="${content.scrollHeight}">
          <foreignObject width="100%" height="100%">
            <div xmlns="http://www.w3.org/1999/xhtml" style="font-family: system-ui, -apple-system, sans-serif; font-size: 14px;">
              ${clone.innerHTML}
            </div>
          </foreignObject>
        </svg>`;

      const svgBlob = new Blob([svgData], { type: 'image/svg+xml;charset=utf-8' });
      const url = URL.createObjectURL(svgBlob);

      const img = new Image();
      img.onload = () => {
        const canvas = document.createElement('canvas');
        canvas.width = content.scrollWidth * 2; // 2x for retina
        canvas.height = content.scrollHeight * 2;
        const ctx = canvas.getContext('2d');
        if (ctx) {
          ctx.scale(2, 2);
          ctx.drawImage(img, 0, 0);
          canvas.toBlob((blob) => {
            if (blob) {
              const downloadUrl = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = downloadUrl;
              a.download = `org-chart-${this.currentView()}-${new Date().toISOString().slice(0, 10)}.png`;
              a.click();
              URL.revokeObjectURL(downloadUrl);
              this.toastr.success('Chart exported as PNG.');
            }
            this.exporting.set(false);
          }, 'image/png');
        } else {
          this.exporting.set(false);
        }
        URL.revokeObjectURL(url);
      };
      img.onerror = () => {
        this.toastr.error('Failed to export chart.');
        this.exporting.set(false);
        URL.revokeObjectURL(url);
      };
      img.src = url;
    } catch {
      this.toastr.error('Export failed. Try a smaller chart.');
      this.exporting.set(false);
    }
  }

  // ─── Helpers ────────────────────────────────────────────────

  getInitials(name: string): string {
    return name
      .split(' ')
      .map((w) => w.charAt(0))
      .join('')
      .toUpperCase()
      .slice(0, 2);
  }

  /**
   * Immutably update a specific node in the tree by nodeId.
   * Uses DFS to find and replace the node.
   */
  private updateNodeInTree(
    nodeId: string,
    updater: (node: IOrgTreeNodeState) => IOrgTreeNodeState
  ): void {
    const updateLevel = (nodes: IOrgTreeNodeState[]): IOrgTreeNodeState[] => {
      return nodes.map((n) => {
        if (n.node.nodeId === nodeId) {
          return updater(n);
        }
        if (n.children.length > 0) {
          const updatedChildren = updateLevel(n.children);
          if (updatedChildren !== n.children) {
            return { ...n, children: updatedChildren };
          }
        }
        return n;
      });
    };

    this.treeRoots.update((roots) => updateLevel(roots));
  }
}
