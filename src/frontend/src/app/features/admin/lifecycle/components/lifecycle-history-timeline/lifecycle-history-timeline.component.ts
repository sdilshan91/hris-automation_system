import {
  Component,
  ChangeDetectionStrategy,
  input,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import {
  ITenantLifecycleEvent,
  lifecycleEventLabel,
} from '../../models/lifecycle.models';

/**
 * US-ADM-004: presentational lifecycle history timeline.
 *
 * Pure/dumb component — renders the `tenant_lifecycle_event` list (newest first)
 * passed in by the host as a vertical timeline. No data fetching of its own.
 */
@Component({
  selector: 'app-lifecycle-history-timeline',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './lifecycle-history-timeline.component.html',
})
export class LifecycleHistoryTimelineComponent {
  readonly events = input.required<ITenantLifecycleEvent[]>();
  readonly loading = input(false);

  readonly eventLabel = lifecycleEventLabel;

  /** Marker colour for a given event type. */
  dotClass(eventType: ITenantLifecycleEvent['eventType']): string {
    switch (eventType) {
      case 'suspended':
        return 'bg-amber-500';
      case 'termination_initiated':
        return 'bg-red-500';
      case 'terminated':
        return 'bg-red-600';
      case 'reactivated':
      case 'restored':
        return 'bg-green-500';
      default:
        return 'bg-neutral-400';
    }
  }

  /** Best-effort extraction of a `reason` from the audit detail JSON. */
  reasonOf(event: ITenantLifecycleEvent): string | null {
    if (!event.detailJson) {
      return null;
    }
    try {
      const parsed = JSON.parse(event.detailJson) as { reason?: string };
      return parsed?.reason ?? null;
    } catch {
      return null;
    }
  }
}
