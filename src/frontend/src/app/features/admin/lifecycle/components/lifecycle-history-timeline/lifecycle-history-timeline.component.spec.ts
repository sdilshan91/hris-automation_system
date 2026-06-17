import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LifecycleHistoryTimelineComponent } from './lifecycle-history-timeline.component';
import { ITenantLifecycleEvent } from '../../models/lifecycle.models';

describe('LifecycleHistoryTimelineComponent', () => {
  let fixture: ComponentFixture<LifecycleHistoryTimelineComponent>;
  let component: LifecycleHistoryTimelineComponent;

  const events: ITenantLifecycleEvent[] = [
    {
      id: 'e-1',
      tenantId: 't-1',
      eventType: 'suspended',
      detailJson: '{"reason":"Payment failure"}',
      createdAt: '2026-06-17T10:00:00Z',
      createdBy: 'admin@yourhrm.com',
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LifecycleHistoryTimelineComponent],
      providers: [provideTranslateService()],
    });

    fixture = TestBed.createComponent(LifecycleHistoryTimelineComponent);
    component = fixture.componentInstance;
  });

  it('renders an event row per event', () => {
    fixture.componentRef.setInput('events', events);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="history-list"]')).not.toBeNull();
    expect(el.querySelectorAll('li').length).toBe(1);
  });

  it('shows the empty state when there are no events', () => {
    fixture.componentRef.setInput('events', []);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="history-empty"]')).not.toBeNull();
  });

  it('extracts the reason from the detail JSON', () => {
    expect(component.reasonOf(events[0])).toBe('Payment failure');
    expect(
      component.reasonOf({ ...events[0], detailJson: null }),
    ).toBeNull();
    expect(
      component.reasonOf({ ...events[0], detailJson: 'not json' }),
    ).toBeNull();
  });
});
