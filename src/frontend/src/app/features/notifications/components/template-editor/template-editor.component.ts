import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { RichTextEditorComponent } from '../../../recruitment/components/rich-text-editor/rich-text-editor.component';
import { NotificationTemplateService } from '../../services/notification-template.service';
import {
  IRenderedPreview,
  placeholderToken,
} from '../../models/notification-template.models';

type MobileTab = 'editor' | 'preview' | 'variables';

/**
 * US-NTF-002 AC-2..AC-4: the template editor.
 *
 * Desktop (>= 768px): split-pane — editor form on the left, live preview on the
 * right. Mobile (< 768px): a tab toggle (Editor / Preview / Variables) over a
 * single full-width column (UI notes / NFR-4).
 *
 * Fields: subject line, HTML body (the dependency-free RichTextEditorComponent
 * reused from US-REC-001), and plain-text body. A variable reference panel lists
 * the template's `availablePlaceholders`; clicking one inserts the
 * `{{placeholder}}` token at the caret of whichever field last had focus (FR-3).
 *
 * Live preview (FR-4) calls the server preview endpoint, debounced on edits, and
 * renders the merged subject + HTML (via Angular's sanitizing [innerHTML], so no
 * bypassSecurityTrust — NFR-5 XSS-safe).
 *
 * "Save" persists the override (AC-3); "Reset to Default" is gated behind a
 * confirm dialog (AC-4); "Send Test Email" opens a recipient modal (FR-8).
 */
@Component({
  selector: 'app-template-editor',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    TranslateModule,
    RichTextEditorComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './template-editor.component.html',
  styleUrl: './template-editor.component.css',
})
export class TemplateEditorComponent implements OnInit {
  protected readonly svc = inject(NotificationTemplateService);
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);
  private readonly translate = inject(TranslateService);

  private readonly rte = viewChild(RichTextEditorComponent);

  protected eventKey = '';
  protected language = 'en';

  /** Active tab on the mobile single-column layout (ignored on desktop). */
  protected readonly mobileTab = signal<MobileTab>('editor');

  /** Which field last had focus — drives where a clicked placeholder lands. */
  private lastFocusedField: 'subject' | 'bodyHtml' | 'bodyText' = 'bodyHtml';

  // ─── Live preview state (FR-4) ────────────────────────────
  protected readonly preview = signal<IRenderedPreview | null>(null);
  protected readonly previewing = signal(false);
  private previewTimer: ReturnType<typeof setTimeout> | null = null;

  // ─── Modals ───────────────────────────────────────────────
  protected readonly showResetConfirm = signal(false);
  protected readonly showTestModal = signal(false);
  protected readonly sendingTest = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    subject: ['', [Validators.required, Validators.maxLength(200)]],
    bodyHtml: ['', [Validators.required]],
    bodyText: ['', [Validators.required]],
  });

  protected readonly testForm = this.fb.nonNullable.group({
    recipientEmail: ['', [Validators.required, Validators.email]],
  });

  ngOnInit(): void {
    this.eventKey = this.route.snapshot.paramMap.get('eventKey') ?? '';
    this.language = this.route.snapshot.queryParamMap.get('language') ?? 'en';
    this.svc.loadTemplate(this.eventKey, this.language);

    // Populate the form once the detail arrives. We poll the signal via a tiny
    // effect-free subscription pattern: the service sets currentTemplate, and we
    // hydrate the form lazily here on each detail change.
    this.hydrateWhenLoaded();
  }

  // The detail loads asynchronously; mirror it into the form when present.
  private hydrateWhenLoaded(): void {
    const apply = () => {
      const detail = this.svc.currentTemplate();
      if (detail) {
        this.form.setValue({
          subject: detail.subject,
          bodyHtml: detail.bodyHtml,
          bodyText: detail.bodyText,
        });
        this.runPreview();
        return;
      }
      // not yet — retry on the next macrotask until loading settles
      if (this.svc.loadingTemplate()) {
        setTimeout(apply, 50);
      }
    };
    apply();
  }

  // ─── Variable insertion (FR-3) ────────────────────────────

  protected get placeholders(): string[] {
    return this.svc.currentTemplate()?.availablePlaceholders ?? [];
  }

  protected trackFocus(field: 'subject' | 'bodyHtml' | 'bodyText'): void {
    this.lastFocusedField = field;
  }

  /** Insert a `{{placeholder}}` token into the last-focused field (FR-3). */
  protected insertPlaceholder(name: string): void {
    const token = placeholderToken(name);
    if (this.lastFocusedField === 'bodyHtml') {
      this.rte()?.insertText(token);
      return;
    }
    const control = this.form.controls[this.lastFocusedField];
    control.setValue((control.value ?? '') + token);
    this.schedulePreview();
  }

  // ─── Live preview (FR-4) ──────────────────────────────────

  /** Debounce preview calls so each keystroke doesn't hit the server. */
  protected schedulePreview(): void {
    if (this.previewTimer) {
      clearTimeout(this.previewTimer);
    }
    this.previewTimer = setTimeout(() => this.runPreview(), 500);
  }

  protected runPreview(): void {
    const v = this.form.getRawValue();
    this.previewing.set(true);
    this.svc
      .preview(this.eventKey, {
        subject: v.subject,
        bodyHtml: v.bodyHtml,
        bodyText: v.bodyText,
        language: this.language,
      })
      .subscribe({
        next: (rendered) => {
          this.preview.set(rendered);
          this.previewing.set(false);
        },
        error: () => this.previewing.set(false),
      });
  }

  // ─── Save (AC-3) ──────────────────────────────────────────

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.svc
      .saveTemplate(this.eventKey, this.form.getRawValue(), this.language)
      .subscribe({
        next: () =>
          this.toastr.success(
            this.translate.instant('notificationTemplates.editor.saved'),
          ),
        error: () =>
          this.toastr.error(
            this.translate.instant('notificationTemplates.editor.saveError'),
          ),
      });
  }

  // ─── Reset to default (AC-4) ──────────────────────────────

  protected openResetConfirm(): void {
    this.showResetConfirm.set(true);
  }

  protected cancelReset(): void {
    this.showResetConfirm.set(false);
  }

  protected confirmReset(): void {
    this.svc.resetToDefault(this.eventKey, this.language).subscribe({
      next: (detail) => {
        this.form.setValue({
          subject: detail.subject,
          bodyHtml: detail.bodyHtml,
          bodyText: detail.bodyText,
        });
        this.runPreview();
        this.showResetConfirm.set(false);
        this.toastr.success(
          this.translate.instant('notificationTemplates.editor.resetDone'),
        );
      },
      error: () => {
        this.showResetConfirm.set(false);
        this.toastr.error(
          this.translate.instant('notificationTemplates.editor.resetError'),
        );
      },
    });
  }

  // ─── Send test email (FR-8) ───────────────────────────────

  protected openTestModal(): void {
    this.testForm.reset();
    this.showTestModal.set(true);
  }

  protected cancelTest(): void {
    this.showTestModal.set(false);
  }

  protected sendTest(): void {
    if (this.testForm.invalid) {
      this.testForm.markAllAsTouched();
      return;
    }
    this.sendingTest.set(true);
    this.svc
      .sendTestEmail(this.eventKey, {
        recipientEmail: this.testForm.controls.recipientEmail.value,
        language: this.language,
      })
      .subscribe({
        next: (res) => {
          this.sendingTest.set(false);
          this.showTestModal.set(false);
          if (res.sent) {
            this.toastr.success(
              this.translate.instant('notificationTemplates.editor.testSent'),
            );
          } else {
            this.toastr.error(
              this.translate.instant('notificationTemplates.editor.testError'),
            );
          }
        },
        error: () => {
          this.sendingTest.set(false);
          this.toastr.error(
            this.translate.instant('notificationTemplates.editor.testError'),
          );
        },
      });
  }

  // ─── Navigation ───────────────────────────────────────────

  protected back(): void {
    this.router.navigate(['/admin/notification-templates']);
  }
}
