import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { PipListComponent } from './pip-list.component';
import { PipService } from '../../services/pip.service';
import { IPipSummary } from '../../models/pip.models';

function row(overrides: Partial<IPipSummary> = {}): IPipSummary {
  return {
    pipId: 'pip-1',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    status: 'Active',
    startDate: '2026-06-01',
    endDate: '2026-08-30',
    objectiveCount: 2,
    checkpointsRecorded: 1,
    checkpointsTotal: 3,
    acknowledgement: 'Acknowledged',
    ...overrides,
  };
}

describe('PipListComponent (AC-1 list)', () => {
  let fixture: ComponentFixture<PipListComponent>;
  let component: PipListComponent;
  let serviceSpy: jasmine.SpyObj<PipService>;

  async function setup(rows: IPipSummary[]): Promise<void> {
    serviceSpy = jasmine.createSpyObj<PipService>('PipService', ['listPips']);
    serviceSpy.listPips.and.returnValue(of(rows));

    await TestBed.configureTestingModule({
      imports: [PipListComponent],
      providers: [
        provideRouter([]),
        { provide: PipService, useValue: serviceSpy },
        provideNoopAnimations(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PipListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders a confidentiality banner and a Create PIP CTA (§8/AC-1)', async () => {
    await setup([row()]);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="confidential-banner"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="create-pip"]')).toBeTruthy();
  });

  it('renders one card per PIP', async () => {
    await setup([row(), row({ pipId: 'pip-2', employeeName: 'Jo Q' })]);
    expect(
      fixture.nativeElement.querySelectorAll('[data-testid="pip-card"]').length,
    ).toBe(2);
  });

  it('filters by status', async () => {
    await setup([
      row({ pipId: 'a', status: 'Active' }),
      row({ pipId: 'b', status: 'NotMet' }),
    ]);
    expect(component.visiblePips().length).toBe(2);
    component.statusFilter.set('NotMet');
    expect(component.visiblePips().length).toBe(1);
    expect(component.visiblePips()[0].pipId).toBe('b');
  });

  it('shows an empty state when there are no PIPs', async () => {
    await setup([]);
    expect(
      (fixture.nativeElement as HTMLElement).textContent,
    ).toContain('No PIPs yet');
  });

  it('shows an error state with retry when loading fails', async () => {
    serviceSpy = jasmine.createSpyObj<PipService>('PipService', ['listPips']);
    serviceSpy.listPips.and.returnValue(throwError(() => new Error('boom')));
    await TestBed.configureTestingModule({
      imports: [PipListComponent],
      providers: [
        provideRouter([]),
        { provide: PipService, useValue: serviceSpy },
        provideNoopAnimations(),
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(PipListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.error()).toBeTruthy();
    expect(component.loading()).toBeFalse();
  });
});
