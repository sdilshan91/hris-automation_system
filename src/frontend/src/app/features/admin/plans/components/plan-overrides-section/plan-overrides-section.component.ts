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
  OVERRIDE_LIMIT_FIELDS,
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
 * Each user action maps 1:1 onto the single-item overrides API: add POSTs one
 * override and appends the row the server returns, remove DELETEs one by its id.
 * The local list is patched from those responses rather than re-fetched.
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

  readonly limitFields = OVERRIDE_LIMIT_FIELDS;
  readonly limitDisplay = limitDisplay;

  readonly overrides = signal<IPlanLimitOverride[]>([]);
  readonly isLoading = signal(true);
  readonly isSaving = signal(false);
  /** Non-null when the load failed — the list is then UNKNOWN, not empty (BUG-471). */
  readonly loadError = signal<string | null>(null);

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
    this.loadError.set(null);
    this.service.getOverrides(this.tenantId()).subscribe({
      next: (rows) => {
        this.overrides.set(rows);
        this.isLoading.set(false);
      },
      error: () => {
        // Non-fatal — the rest of the tenant detail still renders. But it must NOT
        // fall through to the empty state: rendering "no custom limits" for a broken
        // API is how BUG-471 (every override call a 404) went unnoticed for months.
        this.overrides.set([]);
        this.loadError.set('Could not load custom limits.');
        this.isLoading.set(false);
        this.toastr.error('Could not load custom limits.');
      },
    });
  }

  /** Human-readable label for a limit key (falls back to the raw key). */
  labelFor(limitKey: string): string {
    return this.limitFields.find((f) => f.limitKey === limitKey)?.label ?? limitKey;
  }

  /** Limit keys not already overridden (one override per key). */
  availableKeys(): typeof OVERRIDE_LIMIT_FIELDS {
    const used = new Set(this.overrides().map((o) => o.limitKey));
    return this.limitFields.filter((f) => !used.has(f.limitKey));
  }

  /** POST exactly one override; the server returns the persisted row (with its id). */
  add(): void {
    if (this.addForm.invalid) {
      this.addForm.markAllAsTouched();
      return;
    }
    const { limitKey, value, expiresAt } = this.addForm.getRawValue();
    this.isSaving.set(true);
    this.service
      .upsertOverride(this.tenantId(), {
        limitKey,
        value: value as number,
        expiresAt: expiresAt ? expiresAt : null,
      })
      .subscribe({
        next: (saved) => {
          this.overrides.update((rows) => [...rows, saved]);
          this.isSaving.set(false);
          this.toastr.success('Custom limit saved.');
          this.addForm.reset({ limitKey: '', value: null, expiresAt: '' });
        },
        error: () => {
          this.isSaving.set(false);
          this.toastr.error('Failed to save the custom limit.');
        },
      });
  }

  /** DELETE one override by its own id — the key the route accepts. */
  remove(override: IPlanLimitOverride): void {
    this.isSaving.set(true);
    this.service.deleteOverride(override.id).subscribe({
      next: () => {
        this.overrides.update((rows) => rows.filter((o) => o.id !== override.id));
        this.isSaving.set(false);
        this.toastr.success('Custom limit removed.');
      },
      error: () => {
        this.isSaving.set(false);
        this.toastr.error('Failed to remove the custom limit.');
      },
    });
  }
}
