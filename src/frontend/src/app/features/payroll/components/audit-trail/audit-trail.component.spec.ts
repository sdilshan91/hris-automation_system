import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { AuditTrailComponent } from './audit-trail.component';
import { AuditService } from '../../services/audit.service';
import { IAuditEntry, IPage } from '../../models/audit.models';

describe('AuditTrailComponent', () => {
  let fixture: ComponentFixture<AuditTrailComponent>;
  let component: AuditTrailComponent;
  let audit: jasmine.SpyObj<AuditService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const updateEntry: IAuditEntry = {
    id: 'a-1',
    timestamp: '2026-05-31T10:00:00Z',
    action: 'SalaryComponent.Updated',
    resourceType: 'SalaryComponent',
    resourceId: 'sc-1',
    actorUserId: 'u-1',
    actorEmployeeNo: 'EMP001',
    before: '{"rate":10}',
    after: '{"rate":12}',
    ipAddress: '10.0.0.1',
    userAgent: 'jasmine',
    traceId: 't-1',
  };
  const noDiffEntry: IAuditEntry = {
    id: 'a-2',
    timestamp: '2026-05-31T11:00:00Z',
    action: 'PayslipEmail.Sent',
    resourceType: 'PayrollRun',
    resourceId: 'r-1',
    actorUserId: null,
    actorEmployeeNo: null,
    before: null,
    after: null,
    ipAddress: null,
    userAgent: null,
    traceId: null,
  };

  function page(entries: IAuditEntry[]): IPage<IAuditEntry> {
    return { items: entries, totalCount: entries.length, page: 1, pageSize: 50 };
  }

  function makeService(entries: IAuditEntry[]): void {
    audit = jasmine.createSpyObj<AuditService>('AuditService', [
      'getHistory',
      'getAuditTrail',
      'getRunAuditTrail',
      'exportAuditTrail',
    ]);
    audit.getAuditTrail.and.returnValue(of(page(entries)));
    audit.getRunAuditTrail.and.returnValue(of(entries));
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
  }

  function setup(
    entries: IAuditEntry[] = [updateEntry, noDiffEntry],
    runId: string | null = null,
  ): void {
    makeService(entries);
    TestBed.configureTestingModule({
      imports: [AuditTrailComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AuditService, useValue: audit },
        { provide: ToastrService, useValue: toastr },
      ],
    });
    fixture = TestBed.createComponent(AuditTrailComponent);
    component = fixture.componentInstance;
    if (runId !== null) {
      fixture.componentRef.setInput('runId', runId);
    }
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  describe('standalone (no runId)', () => {
    it('loads the filtered audit trail on init', () => {
      setup();
      expect(audit.getAuditTrail).toHaveBeenCalled();
      expect(audit.getRunAuditTrail).not.toHaveBeenCalled();
      expect(component.entries().length).toBe(2);
      expect(component.loading()).toBeFalse();
    });

    it('sets an error when the trail fails to load', () => {
      setup();
      audit.getAuditTrail.and.returnValue(throwError(() => new Error('boom')));
      component.load();
      expect(component.error()).toBe('Could not load the audit trail.');
    });

    it('applyFilters snapshots the filter-bar model and reloads', () => {
      setup();
      component.filters = {
        fromUtc: '2026-05-01',
        toUtc: null,
        action: 'PayrollRun.Finalized',
        actorUserId: 'u-2',
        resourceType: null,
        resourceId: null,
      };
      component.applyFilters();
      const lastArg = audit.getAuditTrail.calls.mostRecent().args[0];
      expect(lastArg.fromUtc).toBe('2026-05-01');
      expect(lastArg.action).toBe('PayrollRun.Finalized');
      expect(lastArg.actorUserId).toBe('u-2');
    });

    it('resetFilters clears the model and reloads with empty filters', () => {
      setup();
      component.filters = {
        fromUtc: '2026-05-01',
        toUtc: null,
        action: null,
        actorUserId: 'u-2',
        resourceType: null,
        resourceId: null,
      };
      component.applyFilters();
      component.resetFilters();
      expect(component.filters.actorUserId).toBeNull();
      const lastArg = audit.getAuditTrail.calls.mostRecent().args[0];
      expect(lastArg.actorUserId).toBeNull();
      expect(lastArg.fromUtc).toBeNull();
    });

    it('renders the filter bar and export buttons', () => {
      setup();
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('[data-test="apply"]')).toBeTruthy();
      expect(el.querySelector('[data-test="export-csv"]')).toBeTruthy();
      expect(el.querySelector('[data-test="export-xlsx"]')).toBeTruthy();
    });
  });

  describe('per-run mode (runId set)', () => {
    it('loads the per-run timeline and not the filtered trail', () => {
      setup([updateEntry], 'r-9');
      expect(audit.getRunAuditTrail).toHaveBeenCalledWith('r-9');
      expect(audit.getAuditTrail).not.toHaveBeenCalled();
    });

    it('hides the filter bar + export in per-run mode', () => {
      setup([updateEntry], 'r-9');
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('[data-test="apply"]')).toBeNull();
      expect(el.querySelector('[data-test="export-csv"]')).toBeNull();
    });
  });

  describe('diff expand/collapse (FR-8)', () => {
    it('offers a diff toggle only for entries with before/after JSON', () => {
      setup();
      expect(component.canDiff(updateEntry)).toBeTrue();
      expect(component.canDiff(noDiffEntry)).toBeFalse();
    });

    it('toggles an entry expanded then collapsed', () => {
      setup();
      expect(component.isExpanded('a-1')).toBeFalse();
      component.toggle('a-1');
      expect(component.isExpanded('a-1')).toBeTrue();
      component.toggle('a-1');
      expect(component.isExpanded('a-1')).toBeFalse();
    });

    it('builds the diff rows for an entry', () => {
      setup();
      const rows = component.diffRows(updateEntry);
      expect(rows[0].field).toBe('rate');
      expect(rows[0].kind).toBe('modified');
    });

    it('renders the desktop diff table only after expanding', () => {
      setup();
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('[data-test="diff-table"]')).toBeNull();
      component.toggle('a-1');
      fixture.detectChanges();
      expect(el.querySelector('[data-test="diff-table"]')).toBeTruthy();
      // mobile note is always present alongside the desktop table (CSS-hidden)
      expect(el.querySelector('[data-test="diff-mobile-note"]')).toBeTruthy();
    });
  });

  describe('export (FR-5)', () => {
    let clickSpy: jasmine.Spy;

    beforeEach(() => {
      clickSpy = spyOn(
        HTMLAnchorElement.prototype,
        'click',
      ).and.stub();
    });

    it('exports CSV: requests the blob and triggers a download', () => {
      setup();
      const blob = new Blob(['a,b'], { type: 'text/csv' });
      audit.exportAuditTrail.and.returnValue(
        of(
          new HttpResponse<Blob>({
            body: blob,
            headers: undefined,
          }),
        ),
      );
      component.exportTrail('csv');
      expect(audit.exportAuditTrail).toHaveBeenCalledWith(
        jasmine.any(Object),
        'csv',
      );
      expect(clickSpy).toHaveBeenCalled();
      expect(component.exporting()).toBeFalse();
    });

    it('does not download when the export body is null', () => {
      setup();
      audit.exportAuditTrail.and.returnValue(
        of(new HttpResponse<Blob>({ body: null })),
      );
      component.exportTrail('xlsx');
      expect(clickSpy).not.toHaveBeenCalled();
      expect(component.exporting()).toBeFalse();
    });

    it('toasts an error when the export fails', () => {
      setup();
      audit.exportAuditTrail.and.returnValue(
        throwError(() => new Error('boom')),
      );
      component.exportTrail('csv');
      expect(toastr.error).toHaveBeenCalled();
      expect(component.exporting()).toBeFalse();
    });
  });

  describe('display helpers', () => {
    it('labels and colours actions', () => {
      setup();
      expect(component.actionLabel('PayrollRun.Finalized')).toBe(
        'Run finalized',
      );
      expect(component.dotClass('PayrollRun.Finalized')).toBe('bg-violet-500');
    });

    it('shows the empty state when there are no entries', () => {
      setup([]);
      expect(
        fixture.nativeElement.querySelector('[data-test="empty"]'),
      ).toBeTruthy();
    });
  });
});
