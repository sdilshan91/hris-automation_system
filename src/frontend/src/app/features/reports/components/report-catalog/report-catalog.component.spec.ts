import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { ReportCatalogComponent } from './report-catalog.component';
import { ReportsService } from '../../services/reports.service';
import { IReportCatalogItem } from '../../models/reports.models';

describe('ReportCatalogComponent', () => {
  let fixture: ComponentFixture<ReportCatalogComponent>;
  let component: ReportCatalogComponent;
  let serviceSpy: jasmine.SpyObj<ReportsService>;
  let router: Router;

  // The service maps the server's { type, icon } into this shape (deriving the
  // i18n keys); the component consumes the mapped IReportCatalogItem.
  const catalog: IReportCatalogItem[] = [
    {
      type: 'headcount',
      titleKey: 'reports.catalog.headcount.title',
      descriptionKey: 'reports.catalog.headcount.description',
      icon: 'groups',
    },
    {
      type: 'turnover',
      titleKey: 'reports.catalog.turnover.title',
      descriptionKey: 'reports.catalog.turnover.description',
      icon: 'trending_down',
    },
    {
      type: 'demographics',
      titleKey: 'reports.catalog.demographics.title',
      descriptionKey: 'reports.catalog.demographics.description',
      icon: 'diversity_3',
    },
    {
      type: 'joiners-leavers',
      titleKey: 'reports.catalog.joiners-leavers.title',
      descriptionKey: 'reports.catalog.joiners-leavers.description',
      icon: 'swap_horiz',
    },
    {
      type: 'department-distribution',
      titleKey: 'reports.catalog.department-distribution.title',
      descriptionKey: 'reports.catalog.department-distribution.description',
      icon: 'apartment',
    },
    {
      type: 'employment-type-breakdown',
      titleKey: 'reports.catalog.employment-type-breakdown.title',
      descriptionKey: 'reports.catalog.employment-type-breakdown.description',
      icon: 'badge',
    },
  ];

  function setup(): void {
    fixture = TestBed.createComponent(ReportCatalogComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  }

  beforeEach(() => {
    serviceSpy = jasmine.createSpyObj<ReportsService>('ReportsService', [
      'getCatalog',
      'generateReport',
    ]);
    TestBed.configureTestingModule({
      imports: [ReportCatalogComponent],
      providers: [
        provideRouter([]),
        provideTranslateService(),
        { provide: ReportsService, useValue: serviceSpy },
      ],
    });
  });

  it('renders the six report cards (AC-1)', () => {
    serviceSpy.getCatalog.and.returnValue(of(catalog));
    setup();
    expect(serviceSpy.getCatalog).toHaveBeenCalled();
    const cards = fixture.nativeElement.querySelectorAll('.rpt-card');
    expect(cards.length).toBe(6);
    expect(component.loading()).toBeFalse();
  });

  it('navigates to the viewer keyed by report type on Generate', () => {
    serviceSpy.getCatalog.and.returnValue(of(catalog));
    setup();
    const navSpy = spyOn(router, 'navigate');
    component.generate(catalog[1]);
    expect(navSpy).toHaveBeenCalledWith(['/reports', 'turnover']);
  });

  it('surfaces a load error and supports retry', () => {
    serviceSpy.getCatalog.and.returnValue(
      throwError(() => ({ error: { message: 'boom' } }))
    );
    setup();
    expect(component.loadError()).toBe('boom');

    serviceSpy.getCatalog.and.returnValue(of(catalog));
    component.load();
    fixture.detectChanges();
    expect(component.loadError()).toBeNull();
    expect(component.catalog().length).toBe(6);
  });

  it('renders the server-provided Material icon token for each card', () => {
    serviceSpy.getCatalog.and.returnValue(of(catalog));
    setup();
    const icons = fixture.nativeElement.querySelectorAll('.rpt-card-icon');
    expect(icons.length).toBe(6);
    // <mat-icon> renders the ligature token as its text content.
    expect(icons[0].textContent.trim()).toBe('groups');
    expect(icons[1].textContent.trim()).toBe('trending_down');
  });

  // ── US-RPT-002: server-driven new report types + light grouping ──

  // The catalog is fully data-driven: whatever the server returns renders,
  // including the six US-RPT-002 leave & attendance types.
  const rpt002Catalog: IReportCatalogItem[] = [
    {
      type: 'leave-utilization',
      titleKey: 'reports.catalog.leave-utilization.title',
      descriptionKey: 'reports.catalog.leave-utilization.description',
      icon: 'beach_access',
      category: 'leave-attendance',
    },
    {
      type: 'leave-balance',
      titleKey: 'reports.catalog.leave-balance.title',
      descriptionKey: 'reports.catalog.leave-balance.description',
      icon: 'account_balance',
      category: 'leave-attendance',
    },
    {
      type: 'attendance-summary',
      titleKey: 'reports.catalog.attendance-summary.title',
      descriptionKey: 'reports.catalog.attendance-summary.description',
      icon: 'event_available',
      category: 'leave-attendance',
    },
    {
      type: 'headcount',
      titleKey: 'reports.catalog.headcount.title',
      descriptionKey: 'reports.catalog.headcount.description',
      icon: 'groups',
      category: 'hr',
    },
  ];

  it('renders whatever report types the server returns, including US-RPT-002 types', () => {
    serviceSpy.getCatalog.and.returnValue(of(rpt002Catalog));
    setup();
    const cards = fixture.nativeElement.querySelectorAll('.rpt-card');
    expect(cards.length).toBe(4);
    expect(component.catalog().map((c) => c.type)).toContain('leave-balance');
    expect(component.catalog().map((c) => c.type)).toContain(
      'attendance-summary'
    );
  });

  it('renders grouped sections when the server tags items with a category (§8)', () => {
    serviceSpy.getCatalog.and.returnValue(of(rpt002Catalog));
    setup();
    expect(component.grouped()).toBeTrue();
    const groups = fixture.nativeElement.querySelectorAll('.rpt-group');
    // Two categories present: 'leave-attendance' and 'hr'.
    expect(groups.length).toBe(2);
    expect(component.groups().length).toBe(2);
    expect(component.groups()[0].key).toBe('leave-attendance');
    expect(component.groups()[0].items.length).toBe(3);
    expect(component.groups()[0].headingKey).toBe(
      'reports.catalog.groups.leave-attendance'
    );
  });

  it('falls back to a single ungrouped grid when no item has a category', () => {
    serviceSpy.getCatalog.and.returnValue(of(catalog));
    setup();
    expect(component.grouped()).toBeFalse();
    expect(fixture.nativeElement.querySelectorAll('.rpt-group').length).toBe(0);
    expect(fixture.nativeElement.querySelectorAll('.rpt-grid').length).toBe(1);
  });
});
