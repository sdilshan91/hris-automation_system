import {
  ComponentFixture,
  TestBed,
  fakeAsync,
  tick,
} from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of } from 'rxjs';
import { TenantCreateComponent } from './tenant-create.component';
import { TenantProvisioningService } from '../../services/tenant-provisioning.service';
import {
  ISubscriptionPlan,
  ISubdomainAvailability,
  IProvisionTenantResponse,
} from '../../models/tenant.models';

describe('TenantCreateComponent', () => {
  let component: TenantCreateComponent;
  let fixture: ComponentFixture<TenantCreateComponent>;
  let service: jasmine.SpyObj<TenantProvisioningService>;
  let toastr: jasmine.SpyObj<ToastrService>;
  let router: Router;

  const plans: ISubscriptionPlan[] = [
    { id: 'plan-1', name: 'Starter', code: 'STARTER', priceMonthly: 0, trialDays: 14, maxEmployees: 25 },
    { id: 'plan-2', name: 'Pro', code: 'PRO', priceMonthly: 49, trialDays: 0, maxEmployees: 200 },
  ];

  function fillValidForm(): void {
    component.form.patchValue({
      name: 'Acme Corp',
      subdomain: 'acme',
      subscriptionPlanId: 'plan-1',
      ownerEmail: 'owner@acme.com',
      region: 'us-east',
      trialDays: 14,
    });
  }

  beforeEach(async () => {
    service = jasmine.createSpyObj<TenantProvisioningService>(
      'TenantProvisioningService',
      ['getSubscriptionPlans', 'checkSubdomainAvailability', 'provisionTenant', 'getTenants'],
    );
    service.getSubscriptionPlans.and.returnValue(of(plans));
    service.checkSubdomainAvailability.and.returnValue(
      of<ISubdomainAvailability>({ available: true }),
    );

    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);

    await TestBed.configureTestingModule({
      imports: [TenantCreateComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: TenantProvisioningService, useValue: service },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TenantCreateComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
  });

  it('should create and load plans on init', () => {
    expect(component).toBeTruthy();
    expect(service.getSubscriptionPlans).toHaveBeenCalled();
    expect(component.plans().length).toBe(2);
    expect(component.plansLoading()).toBeFalse();
  });

  describe('required-field validation', () => {
    it('is invalid when empty', () => {
      component.form.reset();
      expect(component.form.invalid).toBeTrue();
      expect(component.form.get('name')?.hasError('required')).toBeTrue();
      expect(component.form.get('subdomain')?.hasError('required')).toBeTrue();
      expect(component.form.get('subscriptionPlanId')?.hasError('required')).toBeTrue();
      expect(component.form.get('ownerEmail')?.hasError('required')).toBeTrue();
    });

    it('rejects an invalid owner email', () => {
      component.form.get('ownerEmail')?.setValue('not-an-email');
      expect(component.form.get('ownerEmail')?.hasError('email')).toBeTrue();
    });

    it('enforces the 200-char organization name limit', () => {
      component.form.get('name')?.setValue('a'.repeat(201));
      expect(component.form.get('name')?.hasError('maxlength')).toBeTrue();
    });
  });

  describe('subdomain validation (AC-5)', () => {
    function subError(value: string): Record<string, unknown> | null {
      component.form.get('subdomain')?.setValue(value);
      return component.form.get('subdomain')?.errors ?? null;
    }

    it('rejects uppercase characters', () => {
      expect(subError('Acme')?.['pattern']).toBeTruthy();
    });

    it('rejects special characters', () => {
      expect(subError('ac_me!')?.['pattern']).toBeTruthy();
    });

    it('rejects a leading hyphen', () => {
      expect(subError('-acme')?.['pattern']).toBeTruthy();
    });

    it('rejects too-short (< 3)', () => {
      expect(subError('ab')?.['minlength']).toBeTruthy();
    });

    it('rejects too-long (> 50)', () => {
      expect(subError('a'.repeat(51))?.['maxlength']).toBeTruthy();
    });

    it('rejects reserved subdomains (AC-2)', () => {
      expect(subError('admin')?.['reserved']).toBeTruthy();
    });

    it('accepts a valid subdomain', () => {
      expect(subError('acme-hr')).toBeNull();
    });
  });

  describe('debounced availability check (AC-2)', () => {
    it('shows "available" after a debounced available response', fakeAsync(() => {
      service.checkSubdomainAvailability.and.returnValue(of({ available: true }));
      component.form.get('subdomain')?.setValue('acme');
      expect(component.availability()).toBe('checking');

      tick(300);
      expect(service.checkSubdomainAvailability).toHaveBeenCalledWith('acme');
      expect(component.availability()).toBe('available');
    }));

    it('shows "unavailable" with a reason when taken/reserved', fakeAsync(() => {
      service.checkSubdomainAvailability.and.returnValue(
        of({ available: false, reason: 'Subdomain is reserved' }),
      );
      component.form.get('subdomain')?.setValue('acme');
      tick(300);

      expect(component.availability()).toBe('unavailable');
      expect(component.availabilityReason()).toBe('Subdomain is reserved');
    }));

    it('does NOT call the service for a client-invalid subdomain', fakeAsync(() => {
      component.form.get('subdomain')?.setValue('AB');
      tick(300);
      expect(service.checkSubdomainAvailability).not.toHaveBeenCalled();
      expect(component.availability()).toBe('idle');
    }));
  });

  describe('plan selection', () => {
    it('sets the plan id and defaults trial days from the plan', () => {
      component.form.get('trialDays')?.setValue(null);
      component.selectPlan(plans[0]);
      expect(component.form.get('subscriptionPlanId')?.value).toBe('plan-1');
      expect(component.form.get('trialDays')?.value).toBe(14);
    });
  });

  describe('submit (AC-1)', () => {
    it('calls provisionTenant with the correct payload and fires a success toast', fakeAsync(() => {
      const response: IProvisionTenantResponse = {
        tenantId: 't-1',
        subdomain: 'acme',
        status: 'trial',
        createdAt: '2026-06-01T00:00:00Z',
      };
      service.provisionTenant.and.returnValue(of(response));

      fillValidForm();
      // Confirm availability so submit is allowed.
      component.availability.set('available');

      component.onSubmit();
      tick();

      expect(service.provisionTenant).toHaveBeenCalledWith({
        name: 'Acme Corp',
        subdomain: 'acme',
        subscriptionPlanId: 'plan-1',
        ownerEmail: 'owner@acme.com',
        region: 'us-east',
        trialDays: 14,
      });
      expect(toastr.success).toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/admin/tenants']);
    }));

    it('does not submit when the form is invalid', () => {
      component.form.reset();
      component.onSubmit();
      expect(service.provisionTenant).not.toHaveBeenCalled();
    });

    it('does not submit when the subdomain is unavailable', () => {
      fillValidForm();
      component.availability.set('unavailable');
      component.onSubmit();
      expect(service.provisionTenant).not.toHaveBeenCalled();
    });

    it('omits trialDays from the payload when left blank', fakeAsync(() => {
      service.provisionTenant.and.returnValue(
        of({ tenantId: 't-2', subdomain: 'beta', status: 'active', createdAt: '2026-06-02T00:00:00Z' }),
      );
      fillValidForm();
      component.form.get('trialDays')?.setValue(null);
      component.availability.set('available');

      component.onSubmit();
      tick();

      const arg = service.provisionTenant.calls.mostRecent().args[0];
      expect('trialDays' in arg).toBeFalse();
    }));
  });
});
