import { TestBed, ComponentFixture } from '@angular/core/testing';
import {
  ReportChartComponent,
  toChartConfig,
  deriveChartLabels,
} from './report-chart.component';
import { IReportChart } from '../../models/reports.models';

describe('toChartConfig (chart.js mapping)', () => {
  const barChart: IReportChart = {
    kind: 'bar',
    title: 'Headcount',
    series: [{ name: 'Headcount', points: [{ label: 'Eng', value: 20 }, { label: 'Ops', value: 5 }] }],
  };

  it('maps a single-series bar chart to one dataset with aligned labels', () => {
    const cfg = toChartConfig(barChart);
    expect(cfg.type).toBe('bar');
    expect(cfg.data.labels).toEqual(['Eng', 'Ops']);
    expect(cfg.data.datasets.length).toBe(1);
    expect(cfg.data.datasets[0].data).toEqual([20, 5]);
  });

  it('maps stacked-bar to a bar type with stacked scales', () => {
    const stacked: IReportChart = {
      kind: 'stacked-bar',
      title: 'By department',
      series: [
        { name: 'Full-time', points: [{ label: 'Eng', value: 10 }] },
        { name: 'Contract', points: [{ label: 'Eng', value: 3 }] },
      ],
    };
    const cfg = toChartConfig(stacked);
    expect(cfg.type).toBe('bar');
    expect(cfg.data.datasets.length).toBe(2);
    const scales = cfg.options?.scales as Record<string, { stacked?: boolean }>;
    expect(scales['x'].stacked).toBeTrue();
    expect(scales['y'].stacked).toBeTrue();
  });

  it('maps horizontal-bar to a bar with indexAxis y', () => {
    const cfg = toChartConfig({ ...barChart, kind: 'horizontal-bar' });
    expect(cfg.type).toBe('bar');
    expect(cfg.options?.indexAxis).toBe('y');
  });

  it('maps line and pie kinds and gives a pie one colour per slice', () => {
    expect(toChartConfig({ ...barChart, kind: 'line' }).type).toBe('line');
    const pie = toChartConfig({ ...barChart, kind: 'pie' });
    expect(pie.type).toBe('pie');
    expect(Array.isArray(pie.data.datasets[0].backgroundColor)).toBeTrue();
  });

  it('fills missing points with 0 across the unioned label set', () => {
    const sparse: IReportChart = {
      kind: 'bar',
      title: 'Sparse',
      series: [
        { name: 'A', points: [{ label: 'Jan', value: 1 }] },
        { name: 'B', points: [{ label: 'Feb', value: 2 }] },
      ],
    };
    expect(deriveChartLabels(sparse)).toEqual(['Jan', 'Feb']);
    const cfg = toChartConfig(sparse);
    expect(cfg.data.datasets[0].data).toEqual([1, 0]);
    expect(cfg.data.datasets[1].data).toEqual([0, 2]);
  });
});

describe('ReportChartComponent', () => {
  let fixture: ComponentFixture<ReportChartComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ReportChartComponent],
    });
  });

  it('renders a canvas with role=img and a descriptive aria-label (NFR-5)', () => {
    fixture = TestBed.createComponent(ReportChartComponent);
    fixture.componentRef.setInput('chart', {
      kind: 'bar',
      title: 'Headcount',
      series: [{ name: 'Headcount', points: [{ label: 'Eng', value: 20 }] }],
    } satisfies IReportChart);
    fixture.detectChanges();

    const canvas: HTMLCanvasElement =
      fixture.nativeElement.querySelector('canvas');
    expect(canvas).toBeTruthy();
    expect(canvas.getAttribute('role')).toBe('img');
    expect(canvas.getAttribute('aria-label')).toContain('Headcount');
    expect(canvas.getAttribute('aria-label')).toContain('data table');

    fixture.destroy();
  });
});
