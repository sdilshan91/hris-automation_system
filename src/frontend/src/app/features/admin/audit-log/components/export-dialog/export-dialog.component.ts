import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { AuditExportFormat } from '../../models/audit-log.models';
import { TrappedDialogDirective } from '../../../../../shared/directives';

/**
 * US-ADM-008 AC-4: export confirmation dialog. Shows the record count that will
 * be exported (with the current filters applied) and a CSV/JSON format toggle,
 * then emits `confirmed` with the chosen format. Purely presentational — the
 * parent owns the actual download + 403 handling.
 */
@Component({
  selector: 'app-audit-export-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule, TrappedDialogDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('dialogPop', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.96)' }),
        animate(
          '180ms cubic-bezier(0.16,1,0.3,1)',
          style({ opacity: 1, transform: 'scale(1)' })
        ),
      ]),
    ]),
  ],
  templateUrl: './export-dialog.component.html',
})
export class ExportDialogComponent {
  /** Number of records that will be exported with current filters. */
  readonly recordCount = input.required<number>();
  /** Whether an export request is in flight (disables actions). */
  readonly busy = input(false);

  readonly confirmed = output<AuditExportFormat>();
  readonly cancelled = output<void>();

  readonly format = signal<AuditExportFormat>('csv');

  selectFormat(value: AuditExportFormat): void {
    this.format.set(value);
  }

  confirm(): void {
    this.confirmed.emit(this.format());
  }

  cancel(): void {
    this.cancelled.emit();
  }
}
