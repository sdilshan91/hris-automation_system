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
});
