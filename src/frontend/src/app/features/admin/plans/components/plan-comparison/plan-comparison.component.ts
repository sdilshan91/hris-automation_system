import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { SubscriptionPlanService } from '../../services/subscription-plan.service';
import {
  IPlanDetail,
  CANONICAL_MODULES,
  LIMIT_FIELDS,
  FEATURE_FLAG_FIELDS,
  limitDisplay,
} from '../../models/plan.models';

/**
 * US-ADM-009 §8 (optional): side-by-side plan comparison.
 *
 * A lightweight table comparing limits / modules / feature flags across all
 * plans. Loads the list, then fetches each plan's full detail in parallel.
 */
@Component({
  selector: 'app-plan-comparison',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './plan-comparison.component.html',
})
export class PlanComparisonComponent implements OnInit {
  private readonly service = inject(SubscriptionPlanService);

  readonly limitFields = LIMIT_FIELDS;
  readonly modules = CANONICAL_MODULES;
  readonly featureFlagFields = FEATURE_FLAG_FIELDS;
  readonly limitDisplay = limitDisplay;

  readonly plans = signal<IPlanDetail[]>([]);
  readonly isLoading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.service.list().subscribe({
      next: (summaries) => {
        if (summaries.length === 0) {
          this.plans.set([]);
          this.isLoading.set(false);
          return;
        }
        forkJoin(summaries.map((s) => this.service.get(s.id))).subscribe({
          next: (details) => {
            this.plans.set(details);
            this.isLoading.set(false);
          },
          error: () => {
            this.errorMessage.set('Failed to load plan details.');
            this.isLoading.set(false);
          },
        });
      },
      error: () => {
        this.errorMessage.set('Failed to load plans.');
        this.isLoading.set(false);
      },
    });
  }

  limitValue(plan: IPlanDetail, controlName: string): number | null {
    return (plan as unknown as Record<string, number | null>)[controlName] ?? null;
  }

  hasModule(plan: IPlanDetail, key: string): boolean {
    return plan.enabledModules.includes(key);
  }

  hasFlag(plan: IPlanDetail, key: keyof IPlanDetail['featureFlags']): boolean {
    return !!plan.featureFlags[key];
  }
}
