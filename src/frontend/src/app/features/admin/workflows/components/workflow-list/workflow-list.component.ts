import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { WorkflowService } from '../../services/workflow.service';
import {
  IWorkflowSummary,
  WorkflowStatus,
  groupByEntityType,
} from '../../models/workflow.models';

/** A pending confirm action targeting one workflow. */
interface IPendingAction {
  kind: 'archive' | 'restore' | 'delete';
  workflow: IWorkflowSummary;
}

/**
 * US-ADM-007 AC-1/4 — workflow list, grouped by request type.
 *
 * Shows each workflow's name, step count, status badge, last-modified date and
 * a "Default" badge for seeded defaults (AC-1). The "New workflow" button routes
 * to the editor; the plan-limit rejection (AC-4) is surfaced verbatim there when
 * the create POST is rejected. Archive / restore / delete (FR-6, BR-6) run from
 * the row menu behind a confirm dialog.
 */
@Component({
  selector: 'app-workflow-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, RouterLink, TranslateModule],
  templateUrl: './workflow-list.component.html',
})
export class WorkflowListComponent {
  private readonly service = inject(WorkflowService);
  private readonly toastr = inject(ToastrService);
  private readonly router = inject(Router);

  readonly workflows = signal<IWorkflowSummary[]>([]);
  readonly loading = signal(false);
  readonly error = signal(false);
  readonly pendingAction = signal<IPendingAction | null>(null);
  readonly busy = signal(false);

  /** AC-1: workflows grouped by entity type, empty groups omitted. */
  readonly groups = computed(() => groupByEntityType(this.workflows()));

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.service.getWorkflows().subscribe({
      next: (list) => {
        this.workflows.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  createWorkflow(): void {
    this.router.navigate(['/admin/workflows/new']);
  }

  editWorkflow(workflow: IWorkflowSummary): void {
    this.router.navigate(['/admin/workflows', workflow.id]);
  }

  // ─── Confirm-gated lifecycle actions (FR-6, BR-6) ──────────

  requestArchive(workflow: IWorkflowSummary): void {
    this.pendingAction.set({ kind: 'archive', workflow });
  }

  requestRestore(workflow: IWorkflowSummary): void {
    this.pendingAction.set({ kind: 'restore', workflow });
  }

  requestDelete(workflow: IWorkflowSummary): void {
    this.pendingAction.set({ kind: 'delete', workflow });
  }

  cancelAction(): void {
    this.pendingAction.set(null);
  }

  confirmAction(): void {
    const action = this.pendingAction();
    if (!action || this.busy()) {
      return;
    }
    this.busy.set(true);
    const { kind, workflow } = action;
    const op =
      kind === 'archive'
        ? this.service.archiveWorkflow(workflow.id)
        : kind === 'restore'
          ? this.service.restoreWorkflow(workflow.id)
          : this.service.deleteWorkflow(workflow.id);

    op.subscribe({
      next: () => {
        this.busy.set(false);
        this.pendingAction.set(null);
        this.toastr.success(`admin.workflows.list.${kind}d`);
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.pendingAction.set(null);
        this.toastr.error(this.extractError(err));
      },
    });
  }

  // ─── Template helpers ──────────────────────────────────────

  statusBadgeClass(status: WorkflowStatus): string {
    switch (status) {
      case 'Active':
        return 'bg-green-100 text-green-700';
      case 'Draft':
        return 'bg-amber-100 text-amber-700';
      case 'Archived':
        return 'bg-gray-100 text-gray-500';
    }
  }

  private extractError(err: unknown): string {
    const e = err as { error?: { message?: string } | string };
    if (e?.error && typeof e.error === 'object' && e.error.message) {
      return e.error.message;
    }
    if (typeof e?.error === 'string') {
      return e.error;
    }
    return 'admin.workflows.list.actionError';
  }
}
