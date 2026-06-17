import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { SubscriptionPlanService } from '../../services/subscription-plan.service';
import { IPlanSummary } from '../../models/plan.models';

type SortKey = 'name' | 'price' | 'tenants';

/**
 * US-ADM-009 AC-1: System Admin Console subscription-plan list.
 *
 * Card-based layout showing each plan's name, code, monthly/yearly price,
 * currency, active tenant count (FR-5), and public/private + active/archived
 * badges. Sortable by name / price / tenant count. A "Create plan" CTA routes
 * to the editor.
 */
@Component({
  selector: 'app-plan-list',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  templateUrl: './plan-list.component.html',
})
export class PlanListComponent implements OnInit {
  private readonly service = inject(SubscriptionPlanService);

  readonly isLoading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly plans = signal<IPlanSummary[]>([]);

  readonly sortKey = signal<SortKey>('name');
  /** Ascending when true. Name defaults asc; price/tenants default desc. */
  readonly sortAsc = signal(true);

  /** AC-1: sorted view of the loaded plans. */
  readonly sortedPlans = computed(() => {
    const key = this.sortKey();
    const dir = this.sortAsc() ? 1 : -1;
    return [...this.plans()].sort((a, b) => {
      switch (key) {
        case 'price':
          return ((a.priceMonthly ?? 0) - (b.priceMonthly ?? 0)) * dir;
        case 'tenants':
          return (a.activeTenantCount - b.activeTenantCount) * dir;
        default:
          return a.name.localeCompare(b.name) * dir;
      }
    });
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.service.list().subscribe({
      next: (plans) => {
        this.plans.set(plans);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to load plans.');
        this.isLoading.set(false);
      },
    });
  }

  /** Toggle the sort key; flips direction when re-selecting the same key. */
  setSort(key: SortKey): void {
    if (this.sortKey() === key) {
      this.sortAsc.update((v) => !v);
    } else {
      this.sortKey.set(key);
      // Name reads naturally A→Z; numeric metrics read best high→low first.
      this.sortAsc.set(key === 'name');
    }
  }
}
