import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import {
  IAuditLogDetail,
  IDiffEntry,
  diffAuditRaw,
} from '../../models/audit-log.models';

/**
 * US-ADM-008 AC-3 / FR-3: right slide-in detail panel for a single audit record.
 *
 * Shows the full record metadata plus a color-coded field-level diff of the
 * `before` vs `after` JSON (green added / red removed / amber modified). The diff
 * is computed by the pure `diffAuditRaw` helper in the models file (unit-tested
 * separately). Sensitive values arrive pre-masked as "***REDACTED***" from the
 * server (FR-4) and are rendered verbatim — never unmasked here.
 */
@Component({
  selector: 'app-audit-detail-panel',
  standalone: true,
  imports: [CommonModule, DatePipe, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('slideIn', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate(
          '240ms cubic-bezier(0.16,1,0.3,1)',
          style({ transform: 'translateX(0)' })
        ),
      ]),
      transition(':leave', [
        animate('180ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
  ],
  templateUrl: './audit-detail-panel.component.html',
  styles: [
    `
      .meta-label {
        @apply text-xs font-medium uppercase tracking-wide text-neutral-400;
      }
      .meta-value {
        @apply mt-0.5 text-sm text-neutral-800 break-words;
      }
    `,
  ],
})
export class AuditDetailPanelComponent {
  /** The record to show; null/undefined means closed (parent guards rendering). */
  readonly record = input.required<IAuditLogDetail>();
  /** Whether the detail is still loading (skeleton vs content). */
  readonly loading = input(false);

  readonly closed = output<void>();

  /** Field-level diff of before vs after JSON (AC-3). */
  readonly diff = computed<IDiffEntry[]>(() => {
    const rec = this.record();
    return diffAuditRaw(rec.before, rec.after);
  });

  /** Whether there is any actual change to render (added/removed/modified). */
  readonly hasChanges = computed(() =>
    this.diff().some((d) => d.changeType !== 'unchanged')
  );

  close(): void {
    this.closed.emit();
  }

  rowClass(changeType: string): string {
    switch (changeType) {
      case 'added':
        return 'bg-emerald-50 border-emerald-200';
      case 'removed':
        return 'bg-red-50 border-red-200';
      case 'modified':
        return 'bg-amber-50 border-amber-200';
      default:
        return 'bg-white border-neutral-100';
    }
  }

  badgeClass(changeType: string): string {
    switch (changeType) {
      case 'added':
        return 'bg-emerald-100 text-emerald-700';
      case 'removed':
        return 'bg-red-100 text-red-700';
      case 'modified':
        return 'bg-amber-100 text-amber-700';
      default:
        return 'bg-neutral-100 text-neutral-500';
    }
  }
}
