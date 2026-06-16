import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { PendingApprovalsComponent } from './pending-approvals.component';
import { PayrollApprovalService } from '../../services/payroll-approval.service';
import { IPayrollRun } from '../../models/payroll-run.models';

describe('PendingApprovalsComponent (US-PAY-008)', () => {
  let fixture: ComponentFixture<PendingApprovalsComponent>;
  let component: PendingApprovalsComponent;
  let approval: jasmine.SpyObj<PayrollApprovalService>;

  const run = (id: string, month: number): IPayrollRun => ({
    id,
    payMonth: month,
    payYear: 2026,
    status: 'AwaitingApproval',
    totalEmployees: 250,
    processedEmployees: 247,
    skippedEmployees: 3,
    totalGross: 1000000,
    totalDeductions: 200000,
    totalNet: 800000,
    initiatedByName: 'Alex HR',
    initiatedAt: '2026-05-31T10:00:00Z',
    completedAt: '2026-05-31T10:05:00Z',
  });

  function setup(
    result: ReturnType<PayrollApprovalService['listPendingApprovals']>,
  ): void {
    approval = jasmine.createSpyObj<PayrollApprovalService>(
      'PayrollApprovalService',
      ['listPendingApprovals'],
    );
    approval.listPendingApprovals.and.returnValue(result);

    TestBed.configureTestingModule({
      imports: [PendingApprovalsComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: PayrollApprovalService, useValue: approval },
      ],
    });

    fixture = TestBed.createComponent(PendingApprovalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('loads pending approvals on init and exposes a badge count', () => {
    setup(of([run('r-1', 5), run('r-2', 4)]));

    expect(approval.listPendingApprovals).toHaveBeenCalled();
    expect(component.loading()).toBeFalse();
    expect(component.runs().length).toBe(2);
    expect(component.count()).toBe(2);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Pending approvals');
    expect(text).toContain('May 2026');
  });

  it('renders the empty state when there are no pending runs', () => {
    setup(of([]));

    expect(component.count()).toBe(0);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('All caught up');
  });

  it('shows an error and allows retry when the load fails', () => {
    setup(throwError(() => new Error('boom')));

    expect(component.error()).toBe('Could not load pending approvals.');
    expect(component.loading()).toBeFalse();

    // Retry succeeds.
    approval.listPendingApprovals.and.returnValue(of([run('r-3', 6)]));
    component.load();
    expect(component.error()).toBeNull();
    expect(component.runs().length).toBe(1);
  });

  it('builds a human period label', () => {
    setup(of([]));
    expect(component.periodLabel(run('r-1', 1))).toBe('January 2026');
  });
});
