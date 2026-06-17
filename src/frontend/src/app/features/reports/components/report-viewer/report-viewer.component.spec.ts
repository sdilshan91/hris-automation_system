import { TestBed, ComponentFixture } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { ReportViewerComponent } from './report-viewer.component';
import { ReportsService } from '../../services/reports.service';
import { IReportResult, emptyReportFilters } from '../../models/reports.models';

describe('ReportViewerComponent', () => {
  let fixture: ComponentFixture<ReportViewerComponent>;
  let component: ReportViewerComponent;
  let serviceSpy: jasmine.SpyObj<ReportsService>;

  const result: IReportResult = {
    metadata: {
      type: 'headcount',
      title: 'Headcount Summary',
      generatedAt: '2026-06-17T00:00:00Z',
      appliedFilters: emptyReportFilters(),
      summary: [{ label: 'Total', value: 42 }],
    },
    charts: [
      {
        kind: 'bar',
        title: 'Headcount by sub-department',
        series: [{ name: 'Headcount', points: [{ label: 'Eng', value: 20 }] }],
      },
    ],
    table: {
      columns: ['Sub-department', 'Headcount'],
      rows: [['Eng', 20]],
    },
  };

  function setup(): void {
    fixture = TestBed.createComponent(ReportViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    serviceSpy = jasmine.createSpyObj<ReportsService>('ReportsService', [
      'getCatalog',
      'generateReport',
    ]);
    serviceSpy.generateReport.and.returnValue(of(result));

    TestBed.configureTestingModule({
      imports: [ReportViewerComponent],
      providers: [
        provideTranslateService(),
        { provide: ReportsService, useValue: serviceSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ type: 'headcount' }) },
          },
        },
      ],
    });
  });

  it('generates the report for the route type on init (AC-2)', () => {
    setup();
    expect(component.reportType()).toBe('headcount');
    expect(serviceSpy.generateReport).toHaveBeenCalledWith(
      'headcount',
      jasmine.any(Object),
      false
    );
    expect(component.result()?.metadata.title).toBe('Headcount Summary');
  });

  it('toggles between chart and table view (FR-4)', () => {
    setup();
    expect(component.view()).toBe('chart');
    // Chart view shows chart cards.
    expect(
      fixture.nativeElement.querySelectorAll('.rv-chart-card').length
    ).toBe(1);

    component.setView('table');
    fixture.detectChanges();
    // Table view (the screen-reader alternative) is reachable (NFR-5).
    const table = fixture.nativeElement.querySelector('.rv-table');
    expect(table).toBeTruthy();
    expect(table.querySelectorAll('thead th').length).toBe(2);
    expect(table.querySelectorAll('tbody tr').length).toBe(1);
  });

  it('Refresh regenerates with the cache-bypass flag (FR-8)', () => {
    setup();
    serviceSpy.generateReport.calls.reset();
    component.refresh();
    expect(serviceSpy.generateReport).toHaveBeenCalledWith(
      'headcount',
      jasmine.any(Object),
      true
    );
  });

  it('builds filters from the bound form fields', () => {
    setup();
    component.dateFrom = '2026-06-01';
    component.departmentsRaw = 'dep-1, dep-2';
    component.employmentTypes = ['full-time'];
    const filters = component.buildFilters();
    expect(filters.dateFrom).toBe('2026-06-01');
    expect(filters.departmentIds).toEqual(['dep-1', 'dep-2']);
    expect(filters.employmentTypes).toEqual(['full-time']);
  });

  it('renders summary KPI stats from metadata', () => {
    setup();
    const stats = fixture.nativeElement.querySelectorAll('.rv-stat');
    expect(stats.length).toBe(1);
  });

  it('surfaces a generate error', () => {
    serviceSpy.generateReport.and.returnValue(
      throwError(() => ({ error: { message: 'failed' } }))
    );
    setup();
    expect(component.loadError()).toBe('failed');
    expect(component.loading()).toBeFalse();
  });
});
