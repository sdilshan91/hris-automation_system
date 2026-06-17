import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import {
  CdkDragDrop,
  DragDropModule,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { TranslateModule } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { WorkflowService } from '../../services/workflow.service';
import {
  ApproverType,
  APPROVER_TYPES,
  CONDITION_OPERATORS,
  ConditionOperator,
  IApproverRoleOption,
  IApproverUserOption,
  ICreateWorkflowRequest,
  IUpdateWorkflowRequest,
  IWorkflowDefinition,
  IWorkflowStep,
  WORKFLOW_ENTITY_TYPES,
  WorkflowEntityType,
  approverNeedsIdentifier,
  parseCondition,
  serializeCondition,
} from '../../models/workflow.models';

/**
 * Editor-local view of a step. Mirrors IWorkflowStep but holds the condition as
 * a structured object (the wire form serializes it on save) plus per-card UI
 * expand flags. Tablet-usable at 768px+ (NFR-5); WCAG-labelled controls (AC-2).
 */
interface IEditableStep {
  uid: number;
  approverType: ApproverType;
  approverIdentifier: string;
  isParallel: boolean;
  parallelApproverIdentifiers: string[];
  slaHours: number;
  // Condition (expandable)
  conditionEnabled: boolean;
  conditionField: string;
  conditionOperator: ConditionOperator;
  conditionValue: string;
  // Escalation (expandable)
  escalationEnabled: boolean;
  escalationApproverType: ApproverType;
  escalationApproverIdentifier: string;
  // Delegation
  delegationEnabled: boolean;
  delegationBackupUserId: string;
  // UI state
  expanded: boolean;
}

/**
 * US-ADM-007 AC-2/3/5 — visual step-by-step workflow editor.
 *
 * Creates a new definition or edits an existing one (→ new version, AC-3). Each
 * step is a draggable card (CDK DragDrop) with approver-type, approver picker,
 * SLA, an expandable condition builder, an expandable escalation section, a
 * parallel toggle (multi-approver) and a delegation toggle (backup selector).
 * Configures DEFINITIONS only — the runtime engine is backend-deferred.
 */
@Component({
  selector: 'app-workflow-editor',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    DragDropModule,
    TranslateModule,
  ],
  templateUrl: './workflow-editor.component.html',
})
export class WorkflowEditorComponent {
  private readonly service = inject(WorkflowService);
  private readonly toastr = inject(ToastrService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  // Static option lists for the template.
  readonly entityTypes = WORKFLOW_ENTITY_TYPES;
  readonly approverTypes = APPROVER_TYPES;
  readonly operators = CONDITION_OPERATORS;

  // ─── State ─────────────────────────────────────────────────
  readonly workflowId = signal<string | null>(null);
  readonly name = signal('');
  readonly entityType = signal<WorkflowEntityType>('Leave');
  readonly steps = signal<IEditableStep[]>([]);
  readonly version = signal<number>(0);
  readonly isDefault = signal(false);

  readonly roles = signal<IApproverRoleOption[]>([]);
  readonly users = signal<IApproverUserOption[]>([]);

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly showPreview = signal(false);

  private uidSeq = 0;

  /** True when editing an existing workflow (vs. creating a new one). */
  readonly isEdit = computed(() => this.workflowId() !== null);

  /** AC-3 banner: editing an active workflow will create a new version. */
  readonly willCreateNewVersion = computed(() => this.isEdit());

  /** FR-5: at least one step required to enable save. */
  readonly canSave = computed(
    () => this.name().trim().length > 0 && this.steps().length > 0
  );

  constructor() {
    this.loadPickers();
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.workflowId.set(id);
      this.loadWorkflow(id);
    } else {
      // New workflow: pre-seed an entity type from the query param if provided.
      const et = this.route.snapshot.queryParamMap.get('entityType');
      if (et && (WORKFLOW_ENTITY_TYPES as readonly string[]).includes(et)) {
        this.entityType.set(et as WorkflowEntityType);
      }
      this.addStep();
    }
  }

  // ─── Data loading ──────────────────────────────────────────

  private loadPickers(): void {
    this.service.getApproverRoles().subscribe({
      next: (roles) => this.roles.set(roles),
      error: () => this.roles.set([]),
    });
    this.service.getApproverUsers().subscribe({
      next: (users) => this.users.set(users),
      error: () => this.users.set([]),
    });
  }

  private loadWorkflow(id: string): void {
    this.loading.set(true);
    this.service.getWorkflow(id).subscribe({
      next: (def) => {
        this.applyDefinition(def);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toastr.error('admin.workflows.editor.loadError');
      },
    });
  }

  private applyDefinition(def: IWorkflowDefinition): void {
    this.name.set(def.name);
    this.entityType.set(def.entityType);
    this.version.set(def.version);
    this.isDefault.set(def.isDefault);
    this.steps.set(def.steps.map((s) => this.toEditable(s)));
  }

  private toEditable(step: IWorkflowStep): IEditableStep {
    const condition = parseCondition(step.conditionJson);
    return {
      uid: this.uidSeq++,
      approverType: step.approverType,
      approverIdentifier: step.approverIdentifier ?? '',
      isParallel: step.isParallel,
      parallelApproverIdentifiers: step.parallelApproverIdentifiers ?? [],
      slaHours: step.slaHours,
      conditionEnabled: condition !== null,
      conditionField: condition?.field ?? '',
      conditionOperator: condition?.operator ?? '>',
      conditionValue: condition != null ? String(condition.value) : '',
      escalationEnabled: step.escalationApproverType != null,
      escalationApproverType: step.escalationApproverType ?? 'Role',
      escalationApproverIdentifier: step.escalationApproverIdentifier ?? '',
      delegationEnabled: step.delegationEnabled,
      delegationBackupUserId: step.delegationBackupUserId ?? '',
      expanded: false,
    };
  }

  // ─── Step list mutations ───────────────────────────────────

  addStep(): void {
    this.steps.update((list) => [
      ...list,
      {
        uid: this.uidSeq++,
        approverType: 'LineManager',
        approverIdentifier: '',
        isParallel: false,
        parallelApproverIdentifiers: [],
        slaHours: 24,
        conditionEnabled: false,
        conditionField: '',
        conditionOperator: '>',
        conditionValue: '',
        escalationEnabled: false,
        escalationApproverType: 'Role',
        escalationApproverIdentifier: '',
        delegationEnabled: false,
        delegationBackupUserId: '',
        expanded: true,
      },
    ]);
  }

  removeStep(uid: number): void {
    this.steps.update((list) => list.filter((s) => s.uid !== uid));
  }

  /** Drag-and-drop reorder of step cards (AC-2, §8). */
  dropStep(event: CdkDragDrop<IEditableStep[]>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }
    this.steps.update((list) => {
      const next = [...list];
      moveItemInArray(next, event.previousIndex, event.currentIndex);
      return next;
    });
  }

  toggleExpand(uid: number): void {
    this.patch(uid, (s) => ({ ...s, expanded: !s.expanded }));
  }

  togglePreview(): void {
    this.showPreview.update((v) => !v);
  }

  // ─── Per-field mutations (FormsModule binds via these) ─────

  /** Generic immutable patch of a single step by uid. */
  patch(uid: number, fn: (s: IEditableStep) => IEditableStep): void {
    this.steps.update((list) => list.map((s) => (s.uid === uid ? fn(s) : s)));
  }

  setApproverType(uid: number, value: ApproverType): void {
    this.patch(uid, (s) => ({
      ...s,
      approverType: value,
      // Clear an identifier that no longer applies to the new type.
      approverIdentifier: approverNeedsIdentifier(value)
        ? s.approverIdentifier
        : '',
    }));
  }

  setApproverIdentifier(uid: number, value: string): void {
    this.patch(uid, (s) => ({ ...s, approverIdentifier: value }));
  }

  setSlaHours(uid: number, value: number): void {
    this.patch(uid, (s) => ({ ...s, slaHours: value }));
  }

  toggleParallel(uid: number, value: boolean): void {
    this.patch(uid, (s) => ({
      ...s,
      isParallel: value,
      parallelApproverIdentifiers: value ? s.parallelApproverIdentifiers : [],
    }));
  }

  addParallelApprover(uid: number): void {
    this.patch(uid, (s) => ({
      ...s,
      parallelApproverIdentifiers: [...s.parallelApproverIdentifiers, ''],
    }));
  }

  setParallelApprover(uid: number, index: number, value: string): void {
    this.patch(uid, (s) => {
      const next = [...s.parallelApproverIdentifiers];
      next[index] = value;
      return { ...s, parallelApproverIdentifiers: next };
    });
  }

  removeParallelApprover(uid: number, index: number): void {
    this.patch(uid, (s) => ({
      ...s,
      parallelApproverIdentifiers: s.parallelApproverIdentifiers.filter(
        (_, i) => i !== index
      ),
    }));
  }

  toggleCondition(uid: number, value: boolean): void {
    this.patch(uid, (s) => ({ ...s, conditionEnabled: value }));
  }

  setConditionField(uid: number, value: string): void {
    this.patch(uid, (s) => ({ ...s, conditionField: value }));
  }

  setConditionOperator(uid: number, value: ConditionOperator): void {
    this.patch(uid, (s) => ({ ...s, conditionOperator: value }));
  }

  setConditionValue(uid: number, value: string): void {
    this.patch(uid, (s) => ({ ...s, conditionValue: value }));
  }

  toggleEscalation(uid: number, value: boolean): void {
    this.patch(uid, (s) => ({ ...s, escalationEnabled: value }));
  }

  setEscalationApproverType(uid: number, value: ApproverType): void {
    this.patch(uid, (s) => ({
      ...s,
      escalationApproverType: value,
      escalationApproverIdentifier: approverNeedsIdentifier(value)
        ? s.escalationApproverIdentifier
        : '',
    }));
  }

  setEscalationApproverIdentifier(uid: number, value: string): void {
    this.patch(uid, (s) => ({ ...s, escalationApproverIdentifier: value }));
  }

  toggleDelegation(uid: number, value: boolean): void {
    this.patch(uid, (s) => ({
      ...s,
      delegationEnabled: value,
      delegationBackupUserId: value ? s.delegationBackupUserId : '',
    }));
  }

  setDelegationBackup(uid: number, value: string): void {
    this.patch(uid, (s) => ({ ...s, delegationBackupUserId: value }));
  }

  // ─── Template helpers ──────────────────────────────────────

  needsIdentifier(type: ApproverType): boolean {
    return approverNeedsIdentifier(type);
  }

  /** Human label for a step's approver (used in the preview, AC §8). */
  approverLabel(step: IEditableStep): string {
    if (step.approverType === 'Role') {
      return (
        this.roles().find((r) => r.id === step.approverIdentifier)?.name ??
        'Role'
      );
    }
    if (step.approverType === 'NamedUser') {
      return (
        this.users().find((u) => u.id === step.approverIdentifier)
          ?.displayName ?? 'User'
      );
    }
    return step.approverType;
  }

  trackByUid(_index: number, step: IEditableStep): number {
    return step.uid;
  }

  // ─── Build the wire payload (pure, testable via getSteps) ──

  /** Project the editor's steps into the wire IWorkflowStep[] (1-based order). */
  buildSteps(): IWorkflowStep[] {
    return this.steps().map((s, i) => {
      const conditionJson = s.conditionEnabled
        ? serializeCondition({
            field: s.conditionField,
            operator: s.conditionOperator,
            value: s.conditionValue,
          })
        : null;
      return {
        stepOrder: i + 1,
        approverType: s.approverType,
        approverIdentifier: approverNeedsIdentifier(s.approverType)
          ? s.approverIdentifier || null
          : null,
        isParallel: s.isParallel,
        parallelApproverIdentifiers: s.isParallel
          ? s.parallelApproverIdentifiers.filter((v) => v.trim().length > 0)
          : [],
        slaHours: Number(s.slaHours),
        escalationApproverType: s.escalationEnabled
          ? s.escalationApproverType
          : null,
        escalationApproverIdentifier:
          s.escalationEnabled &&
          approverNeedsIdentifier(s.escalationApproverType)
            ? s.escalationApproverIdentifier || null
            : null,
        conditionJson,
        delegationEnabled: s.delegationEnabled,
        delegationBackupUserId: s.delegationEnabled
          ? s.delegationBackupUserId || null
          : null,
      };
    });
  }

  // ─── Save (create or update → new version) ─────────────────

  save(): void {
    if (!this.canSave() || this.saving()) {
      return;
    }
    this.saving.set(true);
    const steps = this.buildSteps();
    const id = this.workflowId();

    if (id) {
      const body: IUpdateWorkflowRequest = { name: this.name().trim(), steps };
      this.service.updateWorkflow(id, body).subscribe({
        next: () => this.onSaved('admin.workflows.editor.updated'),
        error: (err) => this.onSaveError(err),
      });
    } else {
      const body: ICreateWorkflowRequest = {
        name: this.name().trim(),
        entityType: this.entityType(),
        steps,
      };
      this.service.createWorkflow(body).subscribe({
        next: () => this.onSaved('admin.workflows.editor.created'),
        error: (err) => this.onSaveError(err),
      });
    }
  }

  private onSaved(messageKey: string): void {
    this.saving.set(false);
    this.toastr.success(messageKey);
    this.router.navigate(['/admin/workflows']);
  }

  /**
   * Surface the server error. The plan-limit rejection (AC-4) carries the EXACT
   * message verbatim from the backend, which we display unchanged.
   */
  private onSaveError(err: unknown): void {
    this.saving.set(false);
    const message = this.extractError(err);
    this.toastr.error(message);
  }

  private extractError(err: unknown): string {
    const e = err as { error?: { message?: string } | string };
    if (e?.error && typeof e.error === 'object' && e.error.message) {
      return e.error.message;
    }
    if (typeof e?.error === 'string') {
      return e.error;
    }
    return 'admin.workflows.editor.saveError';
  }

  cancel(): void {
    this.router.navigate(['/admin/workflows']);
  }
}
