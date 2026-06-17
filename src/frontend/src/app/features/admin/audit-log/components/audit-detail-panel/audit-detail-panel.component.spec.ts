import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { AuditDetailPanelComponent } from './audit-detail-panel.component';
import { IAuditLogDetail } from '../../models/audit-log.models';

describe('AuditDetailPanelComponent', () => {
  let fixture: ComponentFixture<AuditDetailPanelComponent>;
  let component: AuditDetailPanelComponent;

  const baseRecord: IAuditLogDetail = {
    id: 'a-1',
    timestamp: '2026-06-16T10:00:00Z',
    actorName: 'Jane Doe',
    actorEmail: 'jane@acme.com',
    action: 'Employee.Update',
    resourceType: 'Employee',
    resourceId: 'e-1',
    ipAddress: '10.0.0.1',
    summary: 'Renamed employee',
    userAgent: 'Mozilla/5.0',
    traceId: 'trace-123',
    before: '{"name":"John","bankAccount":"***REDACTED***"}',
    after: '{"name":"Jane","bankAccount":"***REDACTED***"}',
  };

  function create(record: IAuditLogDetail): void {
    fixture = TestBed.createComponent(AuditDetailPanelComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('record', record);
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AuditDetailPanelComponent],
      providers: [provideAnimationsAsync(), provideTranslateService()],
    });
  });

  it('computes the diff and renders only changed rows', () => {
    create(baseRecord);
    // name modified; bankAccount unchanged (both redacted) → 1 visible row.
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="diff-row"]');
    expect(rows.length).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('name');
    expect(fixture.nativeElement.textContent).toContain('John');
    expect(fixture.nativeElement.textContent).toContain('Jane');
  });

  it('renders pre-masked sensitive values as-is (added field stays masked)', () => {
    create({
      ...baseRecord,
      before: '{"name":"John"}',
      // bankAccount is newly present and already masked server-side → added row.
      after: '{"name":"John","bankAccount":"***REDACTED***"}',
    });
    // The masked value is rendered verbatim — never unmasked in the UI.
    expect(fixture.nativeElement.textContent).toContain('***REDACTED***');
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="diff-row"]');
    expect(rows.length).toBe(1);
  });

  it('classes rows by change type', () => {
    create(baseRecord);
    expect(component.rowClass('added')).toContain('emerald');
    expect(component.rowClass('removed')).toContain('red');
    expect(component.rowClass('modified')).toContain('amber');
  });

  it('shows a no-changes message when before == after', () => {
    create({
      ...baseRecord,
      before: '{"name":"John"}',
      after: '{"name":"John"}',
    });
    expect(component.hasChanges()).toBeFalse();
    expect(fixture.nativeElement.querySelectorAll('[data-testid="diff-row"]').length).toBe(0);
  });

  it('emits closed when the close button is clicked', () => {
    create(baseRecord);
    const spy = jasmine.createSpy('closed');
    component.closed.subscribe(spy);
    component.close();
    expect(spy).toHaveBeenCalled();
  });
});
