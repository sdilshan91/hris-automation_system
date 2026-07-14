import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { StatutoryConfigurationComponent } from './statutory-configuration.component';
import { StatutoryService } from '../../services/statutory.service';
import {
  IStatutoryRule,
  ITestCalculationResult,
} from '../../models/statutory.models';

describe('StatutoryConfigurationComponent', () => {
  let fixture: ComponentFixture<StatutoryConfigurationComponent>;
  let component: StatutoryConfigurationComponent;
  let statutory: jasmine.SpyObj<StatutoryService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const taxRule: IStatutoryRule = {
    id: 'tax-1',
    ruleType: 'IncomeTax',
    ruleName: 'Income Tax',
    countryCode: 'LK',
    fiscalYear: '2026-2027',
    effectiveFrom: '2026-04-01',
    effectiveTo: null,
    isActive: true,
    slabs: [
      { id: 's1', slabFrom: 0, slabTo: 250000, ratePercentage: 0 },
      { id: 's2', slabFrom: 250000, slabTo: null, ratePercentage: 10 },
    ],
    updatedAt: '2026-06-01T10:00:00Z',
  };
  const epfRule: IStatutoryRule = {
    id: 'epf-1',
    ruleType: 'EPF',
    ruleName: 'Employee Provident Fund',
    countryCode: 'LK',
    fiscalYear: '2026-2027',
    effectiveFrom: '2026-04-01',
    effectiveTo: null,
    isActive: true,
    socialSecurity: {
      employeeRate: 8,
      employerRate: 12,
      wageCeilingAnnual: 180000,
      applicableOn: 'Basic',
    },
    updatedAt: '2026-06-02T10:00:00Z',
  };

  function setup(): void {
    fixture = TestBed.createComponent(StatutoryConfigurationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    statutory = jasmine.createSpyObj<StatutoryService>('StatutoryService', [
      'listRules',
      'listFiscalYears',
      'createRule',
      'updateRule',
      'deleteRule',
      'testCalculation',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'info',
    ]);

    statutory.listFiscalYears.and.returnValue(of(['2026-2027', '2025-2026']));
    statutory.listRules.and.returnValue(of([taxRule, epfRule]));
    statutory.createRule.and.returnValue(of(taxRule));
    statutory.updateRule.and.returnValue(of(taxRule));

    await TestBed.configureTestingModule({
      imports: [StatutoryConfigurationComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: StatutoryService, useValue: statutory },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();
  });

  it('loads fiscal years then hydrates the editors from the loaded rules', () => {
    setup();
    expect(statutory.listFiscalYears).toHaveBeenCalled();
    expect(statutory.listRules).toHaveBeenCalledWith('2026-2027');
    expect(component.fiscalYears()).toEqual(['2026-2027', '2025-2026']);
    expect(component.taxSlabs().length).toBe(2);
    expect(component.epf().employeeRate).toBe(8);
    expect(component.loading()).toBeFalse();
  });

  it('falls back to a default fiscal year when none are returned', () => {
    statutory.listFiscalYears.and.returnValue(of([]));
    setup();
    expect(component.fiscalYears().length).toBe(1);
    expect(statutory.listRules).toHaveBeenCalled();
  });

  it('still loads rules when fiscal-year lookup errors', () => {
    statutory.listFiscalYears.and.returnValue(throwError(() => new Error('x')));
    setup();
    expect(component.fiscalYears().length).toBe(1);
    expect(statutory.listRules).toHaveBeenCalled();
  });

  it('shows an error state when rule loading fails', () => {
    statutory.listRules.and.returnValue(throwError(() => new Error('x')));
    setup();
    expect(component.error()).toContain('Could not load');
    expect(component.loading()).toBeFalse();
  });

  it('switching fiscal year reloads rules and clears the test result', () => {
    setup();
    component.testResult.set({} as ITestCalculationResult);
    statutory.listRules.calls.reset();
    component.selectFiscalYear('2025-2026');
    expect(component.fiscalYear()).toBe('2025-2026');
    expect(component.testResult()).toBeNull();
    expect(statutory.listRules).toHaveBeenCalledWith('2025-2026');
  });

  it('adds a new fiscal year and resets the editors to a clean slate', () => {
    setup();
    const next = `${Number(component.fiscalYear().split('-')[0]) + 1}-${
      Number(component.fiscalYear().split('-')[0]) + 2
    }`;
    component.addFiscalYear();
    expect(component.fiscalYear()).toBe(next);
    expect(component.fiscalYears()).toContain(next);
    expect(component.taxSlabs()).toEqual([]);
    expect(component.rules()).toEqual([]);
  });

  // ─── Tax slab editor (FR-1/FR-6) ─────────────────────────────

  it('adds a slab continuing from the previous slabTo', () => {
    setup();
    // existing: [0-250000@0, 250000-null@10]; remove the unlimited then add.
    component.removeSlab(1);
    component.addSlab();
    const slabs = component.taxSlabs();
    expect(slabs.length).toBe(2);
    expect(slabs[1].slabFrom).toBe(250000);
    expect(slabs[1].slabTo).toBeNull();
  });

  it('removes a slab by index', () => {
    setup();
    component.removeSlab(0);
    expect(component.taxSlabs().length).toBe(1);
    expect(component.taxSlabs()[0].ratePercentage).toBe(10);
  });

  it('updateSlab coerces numbers and treats blank slabTo as unlimited', () => {
    setup();
    component.updateSlab(0, 'slabTo', '');
    expect(component.taxSlabs()[0].slabTo).toBeNull();
    component.updateSlab(0, 'ratePercentage', '7');
    expect(component.taxSlabs()[0].ratePercentage).toBe(7);
  });

  it('flags overlapping slabs in real time (FR-6) and gates save', () => {
    setup();
    // Make slab 0 overlap slab 1 (0-300000 vs 250000-…).
    component.updateSlab(0, 'slabTo', 300000);
    expect(component.slabsValid()).toBeFalse();
    expect(component.slabIssue(0)).toBe('overlap');
    expect(component.slabIssue(1)).toBe('overlap');
  });

  it('does not save income tax while slabs are invalid', () => {
    setup();
    component.updateSlab(0, 'slabTo', 300000); // overlap
    component.saveIncomeTax();
    expect(statutory.updateRule).not.toHaveBeenCalled();
    expect(statutory.createRule).not.toHaveBeenCalled();
  });

  it('updates the existing income-tax rule when slabs are valid', () => {
    setup();
    component.saveIncomeTax();
    expect(statutory.updateRule).toHaveBeenCalledWith(
      'tax-1',
      jasmine.objectContaining({ ruleType: 'IncomeTax', fiscalYear: '2026-2027' }),
    );
    expect(toastr.success).toHaveBeenCalled();
  });

  it('creates an income-tax rule when none exists for the year', () => {
    statutory.listRules.and.returnValue(of([epfRule])); // no IncomeTax rule
    setup();
    component.addSlab();
    component.updateSlab(0, 'slabTo', null);
    component.updateSlab(0, 'ratePercentage', 5);
    component.saveIncomeTax();
    expect(statutory.createRule).toHaveBeenCalled();
  });

  it('surfaces a toast when saving income tax fails', () => {
    statutory.updateRule.and.returnValue(throwError(() => new Error('x')));
    setup();
    component.saveIncomeTax();
    expect(toastr.error).toHaveBeenCalled();
    expect(component.saving()).toBeFalse();
  });

  // ─── Provident fund (FR-2/AC-2) ──────────────────────────────

  it('updateEpf coerces numeric fields and keeps the enum string', () => {
    setup();
    component.updateEpf('employeeRate', '12');
    expect(component.epf().employeeRate).toBe(12);
    component.updateEpf('wageCeilingAnnual', '');
    expect(component.epf().wageCeilingAnnual).toBeNull();
    component.updateEpf('applicableOn', 'Gross');
    expect(component.epf().applicableOn).toBe('Gross');
  });

  it('saves provident fund as an update to the existing EPF rule', () => {
    setup();
    component.updateEpf('employeeRate', 12);
    component.saveProvidentFund();
    expect(statutory.updateRule).toHaveBeenCalledWith(
      'epf-1',
      jasmine.objectContaining({ ruleType: 'EPF' }),
    );
  });

  // ─── Other deductions (FR-3) ─────────────────────────────────

  it('does not save an other-deduction without a name', () => {
    setup();
    component.otherName = '   ';
    component.saveOtherDeduction();
    expect(statutory.createRule).not.toHaveBeenCalled();
  });

  it('creates a professional-tax other-deduction rule', () => {
    statutory.createRule.and.returnValue(of(epfRule));
    setup();
    component.otherName = 'Professional tax';
    component.otherType = 'ProfessionalTax';
    component.otherRate = 200;
    component.saveOtherDeduction();
    expect(statutory.createRule).toHaveBeenCalledWith(
      jasmine.objectContaining({
        ruleType: 'ProfessionalTax',
        ruleName: 'Professional tax',
      }),
    );
  });

  // ─── Test calculation (FR-5) ─────────────────────────────────

  it('runs a test calculation and stores the result', () => {
    const result: ITestCalculationResult = {
      monthlyGross: 100000,
      incomeTax: 5000,
      employeeEpf: 8000,
      employerEpf: 12000,
      etf: 3000,
      otherDeductions: 0,
      totalDeductions: 13000,
      netPay: 87000,
    };
    statutory.testCalculation.and.returnValue(of(result));
    setup();
    component.sampleGross = 100000;
    component.runTestCalculation();
    expect(statutory.testCalculation).toHaveBeenCalledWith(
      jasmine.objectContaining({ monthlyGross: 100000, fiscalYear: '2026-2027' }),
    );
    expect(component.testResult()?.netPay).toBe(87000);
    expect(component.testing()).toBeFalse();
  });

  it('does nothing when running a test calc with no sample gross', () => {
    setup();
    component.sampleGross = null;
    component.runTestCalculation();
    expect(statutory.testCalculation).not.toHaveBeenCalled();
  });

  it('toasts an error when the test calculation fails', () => {
    statutory.testCalculation.and.returnValue(throwError(() => new Error('x')));
    setup();
    component.sampleGross = 100000;
    component.runTestCalculation();
    expect(toastr.error).toHaveBeenCalled();
    expect(component.testing()).toBeFalse();
  });

  // ─── Version history (FR-4) ──────────────────────────────────

  it('orders version-history entries newest-first', () => {
    setup();
    const history = component.historyEntries();
    expect(history[0].id).toBe('epf-1'); // updatedAt 06-02 > 06-01
    expect(history[1].id).toBe('tax-1');
  });

  it('toggles the version-history panel open', () => {
    setup();
    expect(component.historyOpen()).toBeFalse();
    component.historyOpen.set(true);
    fixture.detectChanges();
    const history = fixture.nativeElement.querySelector(
      '[data-testid="version-history"]',
    );
    expect(history).toBeTruthy();
  });

  // ─── a11y: fiscal-year tablist children (BUG-110) ────────────

  it('keeps only role=tab buttons inside the Fiscal year tablist (BUG-110)', () => {
    setup();
    const tablist = fixture.nativeElement.querySelector(
      '[role="tablist"][aria-label="Fiscal year"]',
    ) as HTMLElement;
    expect(tablist).toBeTruthy();

    // No interactive non-tab child remains inside the tablist (aria-required-children).
    expect(tablist.querySelector('button:not([role="tab"])')).toBeNull();
    const tabButtons = Array.from(tablist.querySelectorAll('button'));
    expect(tabButtons.length).toBeGreaterThan(0);
    tabButtons.forEach((b) => expect(b.getAttribute('role')).toBe('tab'));

    // The "+ Add year" action still exists, but as a sibling outside the tablist.
    const addYear = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.includes('+ Add year'));
    expect(addYear).toBeTruthy();
    expect(addYear!.closest('[role="tablist"]')).toBeNull();
  });
});
