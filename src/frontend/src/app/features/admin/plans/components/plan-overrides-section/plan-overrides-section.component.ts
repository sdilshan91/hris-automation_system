import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { SubscriptionPlanService } from '../../services/subscription-plan.service';
import {
  IPlanLimitOverride,
  LIMIT_FIELDS,
  limitDisplay,
} from '../../models/plan.models';

/**
 * US-ADM-009 AC-5 / FR-4: per-tenant plan limit overrides ("Custom Limits").
 *
 * Self-contained child component wired into the US-ADM-002 System Admin tenant
 * detail (tenant-monitoring-detail). Given a [tenantId], it loads the tenant's
 * overrides on init and lets the System Admin add / remove per-tenant limit
 * overrides (limit-key dropdown + value, with optional expiry). At runtime the
 * BE resolves override (if present and unexpired) > plan field.
 *
 * Add/remove persist via PUT (replace) of the full override set; the component
 * keeps a local working copy so multiple edits batch into one save.
 */
@Component({
  selector: 'app-plan-overrides-section',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './plan-overrides-section.component.html',
})
export class PlanOverridesSectionComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(SubscriptionPlanService);
  private readonly toastr = inject(ToastrService);

  /** The tenant whose overrides are being managed (AC-5). */
  readonly tenantId = input.required<string>();

  readonly limitFields = LIMIT_FIELDS;
  readonly limitDisplay = limitDisplay;

  readonly overrides = signal<IPlanLimitOverride[]>([]);
  readonly isLoading = signal(true);
  readonly isSaving = signal(false);

  readonly addForm: FormGroup = this.fb.group({
    limitKey: ['', [Validators.required]],
    value: [null as number | null, [Validators.required, Validators.min(0)]],
    expiresAt: [''],
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.service.getOverrides(this.tenantId()).subscribe({
      next: (rows) => {
        this.overrides.set(rows);
        this.isLoading.set(false);
      },
      error: () => {
        // Supplementary section — fail quietly to an empty list.
        this.overrides.set([]);
        this.isLoading.set(false);
      },
    });
  }

  /** Human-readable label for a limit key (falls back to the raw key). */
  labelFor(limitKey: string): string {
    return this.limitFields.find((f) => f.limitKey === limitKey)?.label ?? limitKey;
  }

  /** Limit keys not already overridden (one override per key). */
  availableKeys(): typeof LIMIT_FIELDS {
    const used = new Set(this.overrides().map((o) => o.limitKey));
    return this.limitFields.filter((f) => !used.has(f.limitKey));
  }

  add(): void {
    if (this.addForm.invalid) {
      this.addForm.markAllAsTouched();
      return;
    }
    const { limitKey, value, expiresAt } = this.addForm.getRawValue();
    const next: IPlanLimitOverride[] = [
      ...this.overrides(),
      {
        limitKey,
        value: value as number,
        expiresAt: expiresAt ? expiresAt : null,
      },
    ];
    this.persist(next, () => {
      this.addForm.reset({ limitKey: '', value: null, expiresAt: '' });
    });
  }

  remove(limitKey: string): void {
    const next = this.overrides().filter((o) => o.limitKey !== limitKey);
    this.persist(next);
  }

  private persist(next: IPlanLimitOverride[], onDone?: () => void): void {
    this.isSaving.set(true);
    this.service.saveOverrides(this.tenantId(), next).subscribe({
      next: (saved) => {
        this.overrides.set(saved ?? next);
        this.isSaving.set(false);
        this.toastr.success('Custom limits updated.');
        onDone?.();
      },
      error: () => {
        this.isSaving.set(false);
        this.toastr.error('Failed to update custom limits.');
      },
    });
  }
}
