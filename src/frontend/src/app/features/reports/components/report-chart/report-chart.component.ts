import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  effect,
  input,
  viewChild,
  OnDestroy,
} from '@angular/core';
import {
  Chart,
  ChartConfiguration,
  ChartType as ChartJsType,
  registerables,
} from 'chart.js';
import { IReportChart } from '../../models/reports.models';

Chart.register(...registerables);

/** Default palette; first colour is the tenant brand primary. */
export const DEFAULT_CHART_PALETTE: readonly string[] = [
  '#4f46e5',
  '#0ea5e9',
  '#10b981',
  '#f59e0b',
  '#ef4444',
  '#8b5cf6',
  '#14b8a6',
  '#ec4899',
];

/** Histogram + stacked-bar + horizontal-bar all render as a chart.js 'bar'. */
function toChartJsType(kind: IReportChart['kind']): ChartJsType {
  switch (kind) {
    case 'line':
      return 'line';
    case 'pie':
      return 'pie';
    case 'bar':
    case 'horizontal-bar':
    case 'histogram':
    case 'stacked-bar':
    default:
      return 'bar';
  }
}

/** Union of all point labels across series, preserving first-seen order. */
export function deriveChartLabels(block: IReportChart): string[] {
  const seen = new Set<string>();
  const labels: string[] = [];
  for (const s of block.series) {
    for (const p of s.points) {
      if (!seen.has(p.label)) {
        seen.add(p.label);
        labels.push(p.label);
      }
    }
  }
  return labels;
}

/**
 * Pure mapping: generic IReportChart contract → chart.js config. Exported so it
 * can be unit-tested without instantiating a canvas (the lib-isolation seam).
 */
export function toChartConfig(
  block: IReportChart,
  palette: readonly string[] = DEFAULT_CHART_PALETTE
): ChartConfiguration {
  const labels = deriveChartLabels(block);
  const isPie = block.kind === 'pie';
  const isHorizontal = block.kind === 'horizontal-bar';
  const isStacked = block.kind === 'stacked-bar';

  const datasets = block.series.map((s, i) => {
    const color = palette[i % palette.length];
    // For pie charts a single series uses one colour per slice.
    const backgroundColor = isPie
      ? s.points.map((_, j) => palette[j % palette.length])
      : color;
    return {
      label: s.name,
      data: labels.map((label) => {
        const pt = s.points.find((p) => p.label === label);
        return pt ? pt.value : 0;
      }),
      backgroundColor,
      borderColor: color,
      borderWidth: block.kind === 'line' ? 2 : 1,
      fill: false,
      tension: 0.3,
    };
  });

  return {
    type: toChartJsType(block.kind),
    data: { labels, datasets },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      indexAxis: isHorizontal ? 'y' : 'x',
      plugins: {
        legend: {
          display: block.series.length > 1 || isPie,
          position: 'bottom',
        },
        tooltip: { enabled: true },
      },
      scales: isPie
        ? {}
        : {
            x: {
              stacked: isStacked,
              title: {
                display: !!block.xAxisLabel,
                text: block.xAxisLabel ?? '',
              },
            },
            y: {
              stacked: isStacked,
              beginAtZero: true,
              title: {
                display: !!block.yAxisLabel,
                text: block.yAxisLabel ?? '',
              },
            },
          },
    },
  };
}

/**
 * US-RPT-001: reusable chart wrapper (FR-3).
 *
 * This is the ONLY component that imports chart.js — report screens bind to the
 * generic IReportChart contract and never touch the lib directly, so swapping
 * the rendering engine later is a single-file change. Supports bar,
 * horizontal-bar, line, pie/donut, histogram, and stacked-bar via the
 * IReportChart.kind discriminator.
 *
 * Accessibility (NFR-5 / WCAG 2.1 AA): the <canvas> carries role="img" + an
 * aria-label summarising the chart; the data-table view (rendered by the
 * viewer) is the full screen-reader alternative.
 */
@Component({
  selector: 'app-report-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="rc-wrap">
      <canvas
        #canvas
        role="img"
        [attr.aria-label]="ariaLabel()"
      ></canvas>
    </div>
  `,
  styles: [
    `
      .rc-wrap {
        position: relative;
        width: 100%;
        height: 18rem;
      }
      :host {
        display: block;
      }
    `,
  ],
})
export class ReportChartComponent implements OnDestroy {
  /** The generic chart block to render. */
  readonly chart = input.required<IReportChart>();

  /** A multi-colour palette; first colour is the tenant brand primary. */
  readonly palette = input<readonly string[]>(DEFAULT_CHART_PALETTE);

  private readonly canvasRef =
    viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');

  private instance: Chart | null = null;

  constructor() {
    // Re-render whenever the chart input changes (signals-driven, OnPush-safe).
    effect(() => {
      const block = this.chart();
      const palette = this.palette();
      const canvas = this.canvasRef().nativeElement;
      this.render(canvas, block, palette);
    });
  }

  /** Plain-language summary for the canvas aria-label (NFR-5). */
  ariaLabel(): string {
    const block = this.chart();
    const points = block.series.reduce((n, s) => n + s.points.length, 0);
    return `${block.title}: ${block.kind} chart with ${block.series.length} series and ${points} data points. A data table alternative is available below.`;
  }

  ngOnDestroy(): void {
    this.instance?.destroy();
    this.instance = null;
  }

  // ─── Rendering (the only chart.js-aware code) ────────────────────────────

  private render(
    canvas: HTMLCanvasElement,
    block: IReportChart,
    palette: readonly string[]
  ): void {
    this.instance?.destroy();
    this.instance = new Chart(canvas, toChartConfig(block, palette));
  }
}
