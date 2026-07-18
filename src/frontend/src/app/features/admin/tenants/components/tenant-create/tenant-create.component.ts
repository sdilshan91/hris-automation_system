import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  OnDestroy,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ActiveToast, ToastrService } from 'ngx-toastr';
import {
  Subject,
  Subscription,
  of,
} from 'rxjs';
import {
  debounceTime,
  distinctUntilChanged,
  switchMap,
  catchError,
} from 'rxjs/operators';
import { TenantProvisioningService } from '../../services/tenant-provisioning.service';
import {
  ISubscriptionPlan,
  REGION_OPTIONS,
  RESERVED_SUBDOMAINS,
  SUBDOMAIN_PATTERN,
} from '../../models/tenant.models';

type AvailabilityState = 'idle' | 'checking' | 'available' | 'unavailable';

/**
 * US-ADM-001: System Admin "Create Tenant" form.
 *
 * Reactive form, OnPush, signals-first. Subdomain field runs a DEBOUNCED (300ms)
 * availability check (AC-2) against the backend AND client-side pattern/reserved
 * validators (AC-5). On success a toast surfaces the new tenant URL as a link.
 */
@Component({
  selector: 'app-tenant-create',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container max-w-3xl">
      <!-- Back link -->
      <a
        routerLink="/admin/tenants"
        class="inline-flex items-center gap-1.5 text-sm text-neutral-500 hover:text-neutral-700 transition-colors mb-6"
      >
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4">
          <path fill-rule="evenodd" d="M9.78 4.22a.75.75 0 0 1 0 1.06L7.06 8l2.72 2.72a.75.75 0 1 1-1.06 1.06L5.47 8.53a.75.75 0 0 1 0-1.06l3.25-3.25a.75.75 0 0 1 1.06 0Z" clip-rule="evenodd" />
        </svg>
        Back to Tenants
      </a>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>
        <!-- Header -->
        <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
          <div>
            <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
              Create Tenant
            </h1>
            <p class="mt-1 text-sm text-neutral-500">
              Provision a new customer workspace.
            </p>
          </div>
          <div class="flex items-center gap-3">
            <a routerLink="/admin/tenants" class="btn-secondary">Cancel</a>
            <button
              type="submit"
              class="btn-primary"
              [disabled]="isSaving() || form.invalid || availability() === 'unavailable' || availability() === 'checking'"
            >
              @if (isSaving()) {
                <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" aria-hidden="true">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                </svg>
                Creating...
              } @else {
                Create Tenant
              }
            </button>
          </div>
        </div>

        <!-- Organization details card -->
        <div class="card-notion mb-6">
          <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider mb-4">
            Organization
          </h2>
          <div class="space-y-4">
            <!-- Name -->
            <div>
              <label for="name" class="label-notion">Organization name</label>
              <input
                id="name"
                type="text"
                formControlName="name"
                class="input-notion"
                maxlength="200"
                placeholder="e.g. Acme Corporation"
                autocomplete="organization"
              />
              @if (showError('name')) {
                <p class="text-xs text-red-500 mt-1" role="alert">
                  @if (form.get('name')?.errors?.['required']) {
                    Organization name is required.
                  } @else if (form.get('name')?.errors?.['maxlength']) {
                    Organization name must be at most 200 characters.
                  }
                </p>
              }
            </div>

            <!-- Subdomain -->
            <div>
              <label for="subdomain" class="label-notion">Subdomain</label>
              <div class="relative">
                <input
                  id="subdomain"
                  type="text"
                  formControlName="subdomain"
                  class="input-notion pr-28"
                  placeholder="acme"
                  autocomplete="off"
                  autocapitalize="none"
                  spellcheck="false"
                  [attr.aria-invalid]="availability() === 'unavailable' || showError('subdomain')"
                  aria-describedby="subdomain-hint subdomain-status"
                />
                <span class="absolute inset-y-0 right-3 flex items-center text-sm text-neutral-400 pointer-events-none">
                  .yourhrm.com
                </span>
              </div>

              <!-- Real-time availability status (AC-2) -->
              <div id="subdomain-status" class="min-h-[1.25rem] mt-1" aria-live="polite">
                @if (availability() === 'checking') {
                  <span @fadeIn class="inline-flex items-center gap-1.5 text-xs text-neutral-400">
                    <svg class="animate-spin h-3 w-3" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" aria-hidden="true">
                      <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                      <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
                    </svg>
                    Checking availability...
                  </span>
                } @else if (availability() === 'available') {
                  <span @fadeIn class="inline-flex items-center gap-1.5 text-xs text-green-600">
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                      <path fill-rule="evenodd" d="M12.416 3.376a.75.75 0 0 1 .208 1.04l-5 7.5a.75.75 0 0 1-1.154.114l-3-3a.75.75 0 0 1 1.06-1.06l2.353 2.353 4.493-6.74a.75.75 0 0 1 1.04-.207Z" clip-rule="evenodd" />
                    </svg>
                    Subdomain is available
                  </span>
                } @else if (availability() === 'unavailable') {
                  <span @fadeIn class="inline-flex items-center gap-1.5 text-xs text-red-500">
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                      <path d="M5.28 4.22a.75.75 0 0 0-1.06 1.06L6.94 8l-2.72 2.72a.75.75 0 1 0 1.06 1.06L8 9.06l2.72 2.72a.75.75 0 1 0 1.06-1.06L9.06 8l2.72-2.72a.75.75 0 0 0-1.06-1.06L8 6.94 5.28 4.22Z" />
                    </svg>
                    {{ availabilityReason() || 'Subdomain is unavailable' }}
                  </span>
                } @else if (showError('subdomain')) {
                  <span class="text-xs text-red-500" role="alert">
                    @if (form.get('subdomain')?.errors?.['required']) {
                      Subdomain is required.
                    } @else if (form.get('subdomain')?.errors?.['minlength'] || form.get('subdomain')?.errors?.['maxlength']) {
                      Subdomain must be 3-50 characters.
                    } @else if (form.get('subdomain')?.errors?.['pattern']) {
                      Use lowercase letters, numbers and hyphens only (no leading/trailing hyphen).
                    } @else if (form.get('subdomain')?.errors?.['reserved']) {
                      This subdomain is reserved.
                    }
                  </span>
                }
              </div>
              <p id="subdomain-hint" class="text-xs text-neutral-400">
                Lowercase letters, numbers and hyphens. 3-50 characters.
              </p>
            </div>
          </div>
        </div>

        <!-- Subscription plan picker (card-based) -->
        <div class="mb-6">
          <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider mb-3">
            Subscription plan
          </h2>

          @if (plansLoading()) {
            <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
              @for (i of [1, 2, 3]; track i) {
                <div class="card-notion animate-pulse h-28"></div>
              }
            </div>
          } @else if (plans().length === 0) {
            <div class="card-notion text-sm text-neutral-500">
              No active subscription plans are available.
            </div>
          } @else {
            <div
              class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3"
              role="radiogroup"
              aria-label="Subscription plan"
            >
              @for (plan of plans(); track plan.id) {
                <button
                  type="button"
                  role="radio"
                  [attr.aria-checked]="form.get('subscriptionPlanId')?.value === plan.id"
                  (click)="selectPlan(plan)"
                  class="text-left card-notion transition-all duration-200 hover:shadow-md focus:outline-none focus:ring-2 focus:ring-brand-500"
                  [class.ring-2]="form.get('subscriptionPlanId')?.value === plan.id"
                  [class.ring-brand-500]="form.get('subscriptionPlanId')?.value === plan.id"
                  [class.border-brand-500]="form.get('subscriptionPlanId')?.value === plan.id"
                >
                  <div class="flex items-start justify-between">
                    <span class="text-sm font-semibold text-neutral-900">{{ plan.name }}</span>
                    @if (form.get('subscriptionPlanId')?.value === plan.id) {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4 text-brand-600" aria-hidden="true">
                        <path fill-rule="evenodd" d="M12.416 3.376a.75.75 0 0 1 .208 1.04l-5 7.5a.75.75 0 0 1-1.154.114l-3-3a.75.75 0 0 1 1.06-1.06l2.353 2.353 4.493-6.74a.75.75 0 0 1 1.04-.207Z" clip-rule="evenodd" />
                      </svg>
                    }
                  </div>
                  <div class="mt-1 text-lg font-semibold text-neutral-900">
                    {{ plan.priceMonthly | currency }}<span class="text-xs font-normal text-neutral-400">/mo</span>
                  </div>
                  <div class="mt-2 text-xs text-neutral-500">
                    @if (plan.maxEmployees) {
                      Up to {{ plan.maxEmployees }} employees
                    } @else {
                      Unlimited employees
                    }
                  </div>
                  @if (plan.trialDays > 0) {
                    <div class="mt-1 text-xs text-brand-600">
                      {{ plan.trialDays }}-day trial
                    </div>
                  }
                </button>
              }
            </div>
            @if (showError('subscriptionPlanId')) {
              <p class="text-xs text-red-500 mt-2" role="alert">Select a subscription plan.</p>
            }
          }
        </div>

        <!-- Owner + region + trial card -->
        <div class="card-notion mb-6">
          <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider mb-4">
            Owner &amp; configuration
          </h2>
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <!-- Owner email -->
            <div class="sm:col-span-2">
              <label for="ownerEmail" class="label-notion">Owner email</label>
              <input
                id="ownerEmail"
                type="email"
                formControlName="ownerEmail"
                class="input-notion"
                placeholder="owner@acme.com"
                autocomplete="email"
              />
              @if (showError('ownerEmail')) {
                <p class="text-xs text-red-500 mt-1" role="alert">
                  @if (form.get('ownerEmail')?.errors?.['required']) {
                    Owner email is required.
                  } @else if (form.get('ownerEmail')?.errors?.['email']) {
                    Enter a valid email address.
                  }
                </p>
              }
            </div>

            <!-- Region -->
            <div>
              <label for="region" class="label-notion">Region</label>
              <select id="region" formControlName="region" class="input-notion">
                @for (r of regions; track r.code) {
                  <option [value]="r.code">{{ r.label }}</option>
                }
              </select>
              @if (showError('region')) {
                <p class="text-xs text-red-500 mt-1" role="alert">Region is required.</p>
              }
            </div>

            <!-- Trial days -->
            <div>
              <label for="trialDays" class="label-notion">Trial days</label>
              <input
                id="trialDays"
                type="number"
                formControlName="trialDays"
                class="input-notion"
                min="0"
                max="365"
                placeholder="0"
              />
              <p class="text-xs text-neutral-400 mt-1">Defaults from the selected plan.</p>
            </div>
          </div>
        </div>
      </form>
    </div>
  `,
})
export class TenantCreateComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);
  private readonly service = inject(TenantProvisioningService);

  readonly regions = REGION_OPTIONS;

  readonly isSaving = signal(false);
  readonly plansLoading = signal(true);
  readonly plans = signal<ISubscriptionPlan[]>([]);
  readonly availability = signal<AvailabilityState>('idle');
  readonly availabilityReason = signal<string | undefined>(undefined);

  /** Submit should be blocked unless the subdomain is confirmed available. */
  readonly canProvision = computed(() => this.availability() === 'available');

  form!: FormGroup;

  private readonly subdomain$ = new Subject<string>();
  private subscription = new Subscription();

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      subdomain: [
        '',
        [
          Validators.required,
          Validators.minLength(3),
          Validators.maxLength(50),
          Validators.pattern(SUBDOMAIN_PATTERN),
          this.reservedValidator,
        ],
      ],
      subscriptionPlanId: ['', [Validators.required]],
      ownerEmail: ['', [Validators.required, Validators.email]],
      region: [REGION_OPTIONS[0].code, [Validators.required]],
      trialDays: [null as number | null],
    });

    this.loadPlans();
    this.wireSubdomainCheck();
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }

  private loadPlans(): void {
    this.plansLoading.set(true);
    this.service.getSubscriptionPlans().subscribe({
      next: (plans) => {
        this.plans.set(plans);
        this.plansLoading.set(false);
      },
      error: () => {
        this.plans.set([]);
        this.plansLoading.set(false);
        this.toastr.error('Failed to load subscription plans.');
      },
    });
  }

  /** Debounced (300ms) availability pipeline (AC-2 / FR-2). */
  private wireSubdomainCheck(): void {
    const ctrl = this.form.get('subdomain');
    ctrl?.valueChanges.subscribe((raw: string) => {
      // Reset status; only push valid candidates through the debounced check.
      if (ctrl.invalid || !raw) {
        this.availability.set('idle');
        this.availabilityReason.set(undefined);
        return;
      }
      this.availability.set('checking');
      this.subdomain$.next(raw);
    });

    this.subscription.add(
      this.subdomain$
        .pipe(
          debounceTime(300),
          distinctUntilChanged(),
          switchMap((value) =>
            this.service.checkSubdomainAvailability(value).pipe(
              catchError(() => of({ available: false, reason: 'Could not verify availability' })),
            ),
          ),
        )
        .subscribe((result) => {
          if (result.available) {
            this.availability.set('available');
            this.availabilityReason.set(undefined);
          } else {
            this.availability.set('unavailable');
            this.availabilityReason.set(result.reason);
          }
        }),
    );
  }

  selectPlan(plan: ISubscriptionPlan): void {
    this.form.get('subscriptionPlanId')?.setValue(plan.id);
    this.form.get('subscriptionPlanId')?.markAsTouched();
    // Default trial days from the plan if the admin hasn't overridden it.
    const trialCtrl = this.form.get('trialDays');
    if (trialCtrl && (trialCtrl.value === null || trialCtrl.value === '')) {
      trialCtrl.setValue(plan.trialDays);
    }
  }

  showError(controlName: string): boolean {
    const ctrl = this.form.get(controlName);
    return !!ctrl && ctrl.touched && ctrl.invalid;
  }

  onSubmit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.availability() === 'unavailable') {
      return;
    }

    this.isSaving.set(true);
    const v = this.form.value;
    const trialDays =
      v.trialDays === null || v.trialDays === '' ? undefined : Number(v.trialDays);

    this.service
      .provisionTenant({
        name: v.name,
        subdomain: v.subdomain,
        subscriptionPlanId: v.subscriptionPlanId,
        ownerEmail: v.ownerEmail,
        region: v.region,
        ...(trialDays !== undefined ? { trialDays } : {}),
      })
      .subscribe({
        next: (res) => {
          this.showSuccessToast(res.subdomain);
          this.router.navigate(['/admin/tenants']);
        },
        error: () => {
          this.isSaving.set(false);
        },
      });
  }

  /** Success toast with the tenant URL as a clickable link (UI note). */
  private showSuccessToast(subdomain: string): void {
    const url = `https://${subdomain}.yourhrm.com`;
    const toast: ActiveToast<unknown> = this.toastr.success(
      `Tenant created. <a href="${url}" target="_blank" rel="noopener" class="underline font-medium">${subdomain}.yourhrm.com</a>`,
      'Tenant provisioned',
      { enableHtml: true, closeButton: true, timeOut: 8000 },
    );
    void toast;
  }

  /** Reject reserved subdomains client-side (AC-2). */
  private reservedValidator = (control: { value: string }) => {
    const value = (control.value || '').toLowerCase();
    return RESERVED_SUBDOMAINS.includes(value) ? { reserved: true } : null;
  };
}
