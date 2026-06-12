import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  computed,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { IDepartment, IDepartmentTreeNode } from '../../models/department.models';

/**
 * US-CHR-004 FR-8: Interactive tree view of department hierarchy.
 *
 * Builds a tree from the flat department list and renders each node as a
 * small card with expand/collapse toggle. On mobile, nodes use collapsible
 * accordions. Emits events for edit and deactivate actions.
 */
@Component({
  selector: 'app-department-tree',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('expandCollapse', [
      transition(':enter', [
        style({ opacity: 0, height: 0, overflow: 'hidden' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, height: '*' })
        ),
      ]),
      transition(':leave', [
        style({ overflow: 'hidden' }),
        animate(
          '150ms ease-in',
          style({ opacity: 0, height: 0 })
        ),
      ]),
    ]),
  ],
  template: `
    <div class="tree-container" role="tree" aria-label="Department hierarchy">
      @if (treeNodes().length === 0) {
        <div class="card-notion text-center py-10">
          <p class="text-sm text-neutral-500">No departments to display.</p>
        </div>
      } @else {
        @for (node of treeNodes(); track node.department.departmentId) {
          <ng-container
            *ngTemplateOutlet="treeNodeTpl; context: { $implicit: node }"
          ></ng-container>
        }
      }
    </div>

    <!-- Recursive tree node template -->
    <ng-template #treeNodeTpl let-node>
      <div
        class="tree-node"
        [style.padding-left.rem]="node.level * 1.5"
        role="treeitem"
        [attr.aria-expanded]="node.children.length > 0 ? isExpanded(node.department.departmentId) : null"
        [attr.aria-level]="node.level + 1"
      >
        <div
          class="node-card"
          [class.node-inactive]="!node.department.isActive"
        >
          <!-- Expand/collapse toggle -->
          <button
            *ngIf="node.children.length > 0"
            type="button"
            class="expand-btn"
            (click)="toggleExpand(node.department.departmentId)"
            [attr.aria-label]="isExpanded(node.department.departmentId) ? 'Collapse ' + node.department.name : 'Expand ' + node.department.name"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="w-4 h-4 transition-transform duration-200"
              [class.rotate-90]="isExpanded(node.department.departmentId)"
              aria-hidden="true"
            >
              <path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 0 1 .02-1.06L11.168 10 7.23 6.29a.75.75 0 1 1 1.04-1.08l4.5 4.25a.75.75 0 0 1 0 1.08l-4.5 4.25a.75.75 0 0 1-1.06-.02Z" clip-rule="evenodd" />
            </svg>
          </button>
          <div
            *ngIf="node.children.length === 0"
            class="expand-placeholder"
            aria-hidden="true"
          ></div>

          <!-- Node content -->
          <div class="node-content" (click)="editDepartment.emit(node.department)">
            <div class="node-main">
              <span class="node-name">{{ node.department.name }}</span>
              @if (!node.department.isActive) {
                <span class="badge-inactive">Inactive</span>
              }
            </div>
            <div class="node-meta">
              <span>{{ node.department.employeeCount }} {{ node.department.employeeCount === 1 ? 'employee' : 'employees' }}</span>
              @if (node.department.managerName) {
                <span class="meta-sep" aria-hidden="true"></span>
                <span>{{ node.department.managerName }}</span>
              }
            </div>
          </div>

          <!-- Actions -->
          <div class="node-actions">
            <button
              type="button"
              class="action-btn"
              (click)="editDepartment.emit(node.department)"
              [attr.aria-label]="'Edit ' + node.department.name"
              title="Edit"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                <path d="M13.488 2.513a1.75 1.75 0 0 0-2.475 0L6.75 6.774a2.75 2.75 0 0 0-.596.892l-.848 2.047a.75.75 0 0 0 .98.98l2.047-.848a2.75 2.75 0 0 0 .892-.596l4.261-4.262a1.75 1.75 0 0 0 0-2.474Z" />
                <path d="M4.75 3.5c-.69 0-1.25.56-1.25 1.25v6.5c0 .69.56 1.25 1.25 1.25h6.5c.69 0 1.25-.56 1.25-1.25V9A.75.75 0 0 1 14 9v2.25A2.75 2.75 0 0 1 11.25 14h-6.5A2.75 2.75 0 0 1 2 11.25v-6.5A2.75 2.75 0 0 1 4.75 2H7a.75.75 0 0 1 0 1.5H4.75Z" />
              </svg>
            </button>
            @if (node.department.isActive) {
              <button
                type="button"
                class="action-btn action-btn-danger"
                (click)="deactivateDepartment.emit(node.department)"
                [attr.aria-label]="'Deactivate ' + node.department.name"
                title="Deactivate"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                  <path d="M2 3a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1H2V3ZM2 5.5h12l-.67 6.69A1.5 1.5 0 0 1 11.84 13.5H4.16a1.5 1.5 0 0 1-1.49-1.31L2 5.5Zm4.22 1.72a.75.75 0 0 1 1.06 0L8 7.94l.72-.72a.75.75 0 1 1 1.06 1.06L9.06 9l.72.72a.75.75 0 0 1-1.06 1.06L8 10.06l-.72.72a.75.75 0 0 1-1.06-1.06L6.94 9l-.72-.72a.75.75 0 0 1 0-1.06Z" />
                </svg>
              </button>
            }
          </div>
        </div>

        <!-- Children (recursive) -->
        @if (isExpanded(node.department.departmentId) && node.children.length > 0) {
          <div @expandCollapse role="group">
            @for (child of node.children; track child.department.departmentId) {
              <ng-container
                *ngTemplateOutlet="treeNodeTpl; context: { $implicit: child }"
              ></ng-container>
            }
          </div>
        }
      </div>
    </ng-template>
  `,
  styles: [`
    :host {
      display: block;
    }

    .tree-container {
      @apply space-y-1;
    }

    .tree-node {
      /* Padding-left is set dynamically via [style.padding-left.rem] */
    }

    .node-card {
      @apply flex items-center gap-2 rounded-lg bg-white border border-neutral-100
        shadow-notion px-3 py-2.5 mb-1 transition-all duration-200
        hover:shadow-notion-md;
    }

    .node-inactive {
      @apply opacity-60;
    }

    .expand-btn {
      @apply flex-shrink-0 w-6 h-6 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors duration-150;
    }

    .expand-placeholder {
      @apply flex-shrink-0 w-6 h-6;
    }

    .node-content {
      @apply flex-1 min-w-0 cursor-pointer;
    }

    .node-main {
      @apply flex items-center gap-2;
    }

    .node-name {
      @apply text-sm font-medium text-neutral-900 truncate;
    }

    .badge-inactive {
      @apply inline-flex items-center px-1.5 py-0.5 rounded-full text-xs font-medium
        bg-neutral-100 text-neutral-500 whitespace-nowrap;
    }

    .node-meta {
      @apply flex items-center gap-1 mt-0.5 text-xs text-neutral-400;
    }

    .meta-sep {
      @apply inline-block w-1 h-1 rounded-full bg-neutral-300;
    }

    .node-actions {
      @apply flex items-center gap-1 flex-shrink-0;
    }

    .action-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors duration-150;
    }

    .action-btn-danger {
      @apply hover:text-red-500 hover:bg-red-50;
    }

    @media (max-width: 480px) {
      .node-card {
        @apply flex-wrap;
      }

      .node-actions {
        @apply w-full mt-1 justify-end;
      }
    }
  `],
})
export class DepartmentTreeComponent {
  /** Flat list of all departments */
  readonly departments = input.required<IDepartment[]>();

  /** Emitted when user clicks edit on a tree node */
  readonly editDepartment = output<IDepartment>();

  /** Emitted when user clicks deactivate on a tree node */
  readonly deactivateDepartment = output<IDepartment>();

  /** Track which nodes are expanded */
  private readonly expandedIds = signal(new Set<string>());

  /** Build tree structure from flat department list (pure, no side effects) */
  readonly treeNodes = computed(() => {
    const departments = this.departments();
    return this.buildTree(departments);
  });

  constructor() {
    // Auto-expand root nodes that have children (side effect, runs after computed)
    effect(() => {
      const nodes = this.treeNodes();
      const rootsWithChildren = nodes
        .filter((n) => n.children.length > 0)
        .map((n) => n.department.departmentId);

      if (rootsWithChildren.length > 0) {
        this.expandedIds.update((ids) => {
          const next = new Set(ids);
          for (const id of rootsWithChildren) {
            next.add(id);
          }
          return next;
        });
      }
    });
  }

  isExpanded(departmentId: string): boolean {
    return this.expandedIds().has(departmentId);
  }

  toggleExpand(departmentId: string): void {
    this.expandedIds.update((ids) => {
      const next = new Set(ids);
      if (next.has(departmentId)) {
        next.delete(departmentId);
      } else {
        next.add(departmentId);
      }
      return next;
    });
  }

  private buildTree(departments: IDepartment[]): IDepartmentTreeNode[] {
    const childrenMap = new Map<string | null, IDepartment[]>();

    for (const dept of departments) {
      const parentId = dept.parentDepartmentId;
      if (!childrenMap.has(parentId)) {
        childrenMap.set(parentId, []);
      }
      childrenMap.get(parentId)!.push(dept);
    }

    const buildLevel = (parentId: string | null, level: number): IDepartmentTreeNode[] => {
      const children = childrenMap.get(parentId) ?? [];
      return children.map((dept) => ({
        department: dept,
        children: buildLevel(dept.departmentId, level + 1),
        expanded: false,
        level,
      }));
    };

    return buildLevel(null, 0);
  }
}
