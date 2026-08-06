import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  FormControl,
  Validators,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateModule } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { trigger, transition, style, animate } from '@angular/animations';
import { SubscriptionPlanService } from '../../services/subscription-plan.service';
import {
  IPlanDetail,
  IPlanUpsert,
  IPlanLimitNumbers,
  CANONICAL_MODULES,
  LIMIT_FIELDS,
  FEATURE_FLAG_FIELDS,
  SLA_TIERS,
  CURRENCIES,
  emptyFeatureFlags,
} from '../../models/plan.models';

/**
 * US-ADM-009 AC-2/AC-3/§8: System Admin subscription-plan editor.
 *
 * One component handles both create (no :id) and edit (:id). The form is
 * organized in Notion-style sections: General, Limits (each numeric input with
 * an "Unlimited" toggle that nulls the value — BR-3), Modules (checkbox grid;
 * CoreHR always-on/disabled — FR-6), Feature Flags (toggle switches), and
 * SLA/Trial. `code` is read-only when editing (FR-3 / BR-5).
 *
 * Errors surfaced clearly: the unique-code conflict (409 on create) and the
 * delete-referenced conflict (409 on delete → "only archiving is permitted").
 */
@Component({
  selector: 'app-plan-editor',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  templateUrl: './plan-editor.component.html',
})
export class PlanEditorComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(SubscriptionPlanService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly modules = CANONICAL_MODULES;
  readonly limitFields = LIMIT_FIELDS;
  readonly featureFlagFields = FEATURE_FLAG_FIELDS;
  readonly slaTiers = SLA_TIERS;
  readonly currencies = CURRENCIES;

  /** null until known; non-null when editing an existing plan. */
  readonly planId = signal<string | null>(null);
  readonly isEdit = computed(() => this.planId() !== null);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly loadError = signal<string | null>(null);
  /** A clear, top-of-form error (e.g. duplicate code, delete-409). */
  readonly formError = signal<string | null>(null);

  /** Which limit keys are currently "Unlimited" (null). */
  readonly unlimited = signal<Record<string, boolean>>({});

  readonly form: FormGroup = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^[a-z0-9-]+$/)]],
    name: ['', [Validators.required, Validators.maxLength(80)]],
    description: [''],
    isPublic: [true],
    isActive: [true],
    priceMonthly: [0, [Validators.min(0)]],
    priceYearly: [0, [Validators.min(0)]],
    currency: ['USD', [Validators.required]],
    trialDays: [14, [Validators.required, Validators.min(0)]],
    slaTier: ['standard', [Validators.required]],
    // Limits group — each control nullable; null === "Unlimited" (BR-3).
    limits: this.fb.group({
      maxEmployees: this.fb.control<number | null>(100),
      maxStorageGb: this.fb.control<number | null>(50),
      maxApiCallsPerMonth: this.fb.control<number | null>(null),
      maxEmailSendsPerMonth: this.fb.control<number | null>(null),
      maxCustomRoles: this.fb.control<number | null>(5),
      maxCustomFieldsPerEntity: this.fb.control<number | null>(20),
      maxWorkflows: this.fb.control<number | null>(10),
      auditLogRetentionDays: this.fb.control<number | null>(90),
    }),
    // Module entitlements — keyed checkbox controls (CoreHR locked on, FR-6).
    modulesGroup: this.fb.group(
      CANONICAL_MODULES.reduce<Record<string, FormControl<boolean>>>((acc, m) => {
        acc[m.key] = this.fb.nonNullable.control({
          value: m.alwaysOn,
          disabled: m.alwaysOn,
        });
        return acc;
      }, {}),
    ),
    // ISSUE-358: flags whose feature does not exist are DISABLED at the control, not merely styled. An
    // [attr.disabled] binding is ignored by reactive forms — the control's own state is what governs — so a
    // template-only treatment would have looked right and still been sellable. Built from
    // FEATURE_FLAG_FIELDS so the list lives in exactly one place, mirroring the modules group above.
    // The save path uses getRawValue(), so a disabled flag still round-trips and is never silently wiped.
    flags: this.fb.group(
      FEATURE_FLAG_FIELDS.reduce<Record<string, unknown>>((acc, ff) => {
        acc[ff.key] = this.fb.nonNullable.control({
          value: false,
          disabled: !!ff.unavailableReason,
        });
        return acc;
      }, {}),
    ),
  });

  get limitsGroup(): FormGroup {
    return this.form.get('limits') as FormGroup;
  }

  ngOnInit(): void {
    // Initialize the "Unlimited" map from the current limit control values.
    this.syncUnlimitedFromForm();

    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.planId.set(id);
      this.load(id);
    }
  }

  private syncUnlimitedFromForm(): void {
    const map: Record<string, boolean> = {};
    for (const f of this.limitFields) {
      map[f.controlName] = this.limitsGroup.get(f.controlName)?.value === null;
    }
    this.unlimited.set(map);
  }

  load(id: string): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.service.get(id).subscribe({
      next: (plan) => {
        this.patch(plan);
        this.isLoading.set(false);
      },
      error: () => {
        this.loadError.set('Failed to load the plan.');
        this.isLoading.set(false);
      },
    });
  }

  private patch(plan: IPlanDetail): void {
    this.form.patchValue({
      code: plan.code,
      name: plan.name,
      description: plan.description ?? '',
      isPublic: plan.isPublic,
      isActive: plan.isActive,
      priceMonthly: plan.priceMonthly,
      priceYearly: plan.priceYearly,
      currency: plan.currency,
      trialDays: plan.trialDays,
      slaTier: plan.slaTier,
      limits: {
        maxEmployees: plan.maxEmployees,
        maxStorageGb: plan.maxStorageGb,
        maxApiCallsPerMonth: plan.maxApiCallsPerMonth,
        maxEmailSendsPerMonth: plan.maxEmailSendsPerMonth,
        maxCustomRoles: plan.maxCustomRoles,
        maxCustomFieldsPerEntity: plan.maxCustomFieldsPerEntity,
        maxWorkflows: plan.maxWorkflows,
        auditLogRetentionDays: plan.auditLogRetentionDays,
      },
      flags: { ...emptyFeatureFlags(), ...plan.featureFlags },
    });

    // FR-3 / BR-5: code is immutable when editing.
    this.form.get('code')?.disable();

    // Module toggles (CoreHR stays locked on).
    const modulesGroup = this.form.get('modulesGroup') as FormGroup;
    for (const m of this.modules) {
      if (m.alwaysOn) continue;
      modulesGroup.get(m.key)?.setValue(plan.enabledModules.includes(m.key));
    }

    this.syncUnlimitedFromForm();
  }

  /** BR-3: toggling "Unlimited" nulls the limit; un-toggling restores a 0. */
  toggleUnlimited(controlName: keyof IPlanLimitNumbers): void {
    const ctrl = this.limitsGroup.get(controlName);
    if (!ctrl) return;
    const nowUnlimited = !this.unlimited()[controlName];
    this.unlimited.update((m) => ({ ...m, [controlName]: nowUnlimited }));
    if (nowUnlimited) {
      ctrl.setValue(null);
      ctrl.disable();
    } else {
      ctrl.enable();
      ctrl.setValue(0);
    }
  }

  isUnlimited(controlName: string): boolean {
    return !!this.unlimited()[controlName];
  }

  /** Collect the enabled module keys (CoreHR always included). */
  private collectModules(): string[] {
    const modulesGroup = this.form.get('modulesGroup') as FormGroup;
    const raw = modulesGroup.getRawValue() as Record<string, boolean>;
    return this.modules.filter((m) => m.alwaysOn || raw[m.key]).map((m) => m.key);
  }

  private buildPayload(): IPlanUpsert {
    const raw = this.form.getRawValue() as {
      code: string;
      name: string;
      description: string;
      isPublic: boolean;
      isActive: boolean;
      priceMonthly: number | null;
      priceYearly: number | null;
      currency: string;
      trialDays: number;
      slaTier: string;
      limits: IPlanLimitNumbers;
      flags: IPlanUpsert['featureFlags'];
    };

    return {
      code: raw.code,
      name: raw.name,
      description: raw.description?.trim() ? raw.description.trim() : null,
      isPublic: raw.isPublic,
      isActive: raw.isActive,
      priceMonthly: raw.priceMonthly,
      priceYearly: raw.priceYearly,
      currency: raw.currency,
      trialDays: raw.trialDays,
      maxEmployees: raw.limits.maxEmployees,
      maxStorageGb: raw.limits.maxStorageGb,
      maxApiCallsPerMonth: raw.limits.maxApiCallsPerMonth,
      maxEmailSendsPerMonth: raw.limits.maxEmailSendsPerMonth,
      maxCustomRoles: raw.limits.maxCustomRoles,
      maxCustomFieldsPerEntity: raw.limits.maxCustomFieldsPerEntity,
      maxWorkflows: raw.limits.maxWorkflows,
      auditLogRetentionDays: raw.limits.auditLogRetentionDays,
      enabledModules: this.collectModules(),
      featureFlags: raw.flags,
      slaTier: raw.slaTier,
    };
  }

  save(): void {
    this.formError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.isSaving.set(true);
    const payload = this.buildPayload();
    const id = this.planId();

    const request$ = id
      ? this.service.update(id, payload)
      : this.service.create(payload);

    request$.subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toastr.success(
          this.isEdit() ? 'Plan updated.' : 'Plan created.',
        );
        this.router.navigate(['/admin/plans']);
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        if (err.status === 409) {
          // FR-3: duplicate / unique-code violation.
          this.formError.set(
            'A plan with this code already exists. Codes must be unique and cannot be reused.',
          );
        } else {
          this.formError.set('Failed to save the plan. Please try again.');
        }
      },
    });
  }

  // ─── Archive / Delete (AC-4 / FR-7) ─────────────────────────

  readonly confirmArchive = signal(false);
  readonly confirmDelete = signal(false);

  openArchive(): void {
    this.confirmArchive.set(true);
  }
  openDelete(): void {
    this.confirmDelete.set(true);
  }
  closeConfirm(): void {
    this.confirmArchive.set(false);
    this.confirmDelete.set(false);
  }

  archive(): void {
    const id = this.planId();
    if (!id) return;
    this.isSaving.set(true);
    this.service.archive(id).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.closeConfirm();
        this.toastr.success('Plan archived.');
        this.router.navigate(['/admin/plans']);
      },
      error: () => {
        this.isSaving.set(false);
        this.closeConfirm();
        this.formError.set('Failed to archive the plan.');
      },
    });
  }

  remove(): void {
    const id = this.planId();
    if (!id) return;
    this.isSaving.set(true);
    this.service.delete(id).subscribe({
      next: () => {
        this.isSaving.set(false);
        this.closeConfirm();
        this.toastr.success('Plan deleted.');
        this.router.navigate(['/admin/plans']);
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.closeConfirm();
        if (err.status === 409) {
          // FR-7: referenced plans can only be archived.
          this.formError.set(
            'This plan is referenced by one or more tenants and cannot be deleted. Archive it instead.',
          );
        } else {
          this.formError.set('Failed to delete the plan.');
        }
      },
    });
  }

  cancel(): void {
    this.router.navigate(['/admin/plans']);
  }
}
