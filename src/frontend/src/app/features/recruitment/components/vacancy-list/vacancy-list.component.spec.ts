import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { VacancyListComponent } from './vacancy-list.component';
import { VacancyService } from '../../services/vacancy.service';
import { IVacancy, IVacancyPage } from '../../models/vacancy.models';

describe('VacancyListComponent', () => {
  let component: VacancyListComponent;
  let fixture: ComponentFixture<VacancyListComponent>;
  let serviceSpy: jasmine.SpyObj<VacancyService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const v = (over: Partial<IVacancy> = {}): IVacancy => ({
    id: 'vac-1',
    referenceNumber: 'VAC-2026-0001',
    title: 'Senior Engineer',
    departmentId: 'dept-1',
    departmentName: 'Engineering',
    jobTitleId: 'jt-1',
    jobTitleName: 'Engineer',
    employmentType: 'FullTime',
    locationId: 'loc-1',
    locationName: 'HQ',
    hiringManagerId: 'emp-1',
    hiringManagerName: 'Jane Doe',
    headcount: 2,
    filledCount: 0,
    salaryMin: null,
    salaryMax: null,
    currency: null,
    description: null,
    qualifications: null,
    applicationDeadline: null,
    status: 'Draft',
    slug: null,
    createdAt: '2026-06-01T00:00:00Z',
    ...over,
  });

  const page = (data: IVacancy[]): IVacancyPage => ({
    data,
    total: data.length,
    page: 1,
    pageSize: 20,
  });

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('VacancyService', [
      'listVacancies',
      'changeStatus',
    ]);
    serviceSpy.listVacancies.and.returnValue(of(page([v()])));
    serviceSpy.changeStatus.and.returnValue(of(v({ status: 'Open' })));

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [VacancyListComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: VacancyService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VacancyListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads vacancies on init', () => {
    expect(serviceSpy.listVacancies).toHaveBeenCalled();
    expect(component.vacancies().length).toBe(1);
    expect(component.loading()).toBeFalse();
  });

  it('renders a color-coded status badge', () => {
    const badge = fixture.nativeElement.querySelector('.badge') as HTMLElement;
    expect(badge.textContent?.trim()).toBe('Draft');
    expect(component.badgeClass('Open')).toContain('green');
    expect(component.badgeClass('OnHold')).toContain('amber');
    expect(component.badgeClass('Closed')).toContain('red');
    expect(component.badgeClass('Cancelled')).toContain('red');
  });

  it('setStatusFilter re-queries with the chosen status', () => {
    component.setStatusFilter('Open');
    expect(component.statusFilter()).toBe('Open');
    expect(serviceSpy.listVacancies).toHaveBeenCalledWith({ status: 'Open' });
  });

  it('setStatusFilter(null) queries with no status', () => {
    component.setStatusFilter(null);
    expect(serviceSpy.listVacancies).toHaveBeenCalledWith({});
  });

  it('shows the error toast when the list fails', () => {
    serviceSpy.listVacancies.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ error: { message: 'nope' }, status: 500 }),
      ),
    );
    component.load();
    expect(toastrSpy.error).toHaveBeenCalledWith('nope');
    expect(component.loading()).toBeFalse();
  });

  describe('onInlineStatusChange', () => {
    it('calls changeStatus and updates the row', () => {
      const target = { value: 'Open' } as HTMLSelectElement;
      component.onInlineStatusChange(v(), { target } as unknown as Event);
      expect(serviceSpy.changeStatus).toHaveBeenCalledWith('vac-1', 'Open');
      expect(component.vacancies()[0].status).toBe('Open');
      expect(toastrSpy.success).toHaveBeenCalled();
    });

    it('does nothing when the status is unchanged', () => {
      const target = { value: 'Draft' } as HTMLSelectElement;
      component.onInlineStatusChange(v(), { target } as unknown as Event);
      expect(serviceSpy.changeStatus).not.toHaveBeenCalled();
    });

    it('shows an error toast and keeps the list on failure', () => {
      serviceSpy.changeStatus.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({ error: { message: 'blocked' }, status: 409 }),
        ),
      );
      const target = { value: 'Closed' } as HTMLSelectElement;
      component.onInlineStatusChange(v(), { target } as unknown as Event);
      expect(toastrSpy.error).toHaveBeenCalledWith('blocked');
      expect(component.vacancies()[0].status).toBe('Draft');
    });
  });

  describe('form drawer', () => {
    it('openCreate opens the form with no editing target', () => {
      component.openCreate();
      expect(component.showForm()).toBeTrue();
      expect(component.editing()).toBeNull();
    });

    it('openEdit opens the form with the chosen vacancy', () => {
      const target = v({ id: 'vac-9' });
      component.openEdit(target);
      expect(component.showForm()).toBeTrue();
      expect(component.editing()?.id).toBe('vac-9');
    });

    it('onSaved closes the drawer and reloads', () => {
      serviceSpy.listVacancies.calls.reset();
      component.openCreate();
      component.onSaved();
      expect(component.showForm()).toBeFalse();
      expect(serviceSpy.listVacancies).toHaveBeenCalled();
    });

    it('closeForm hides the drawer', () => {
      component.openCreate();
      component.closeForm();
      expect(component.showForm()).toBeFalse();
      expect(component.editing()).toBeNull();
    });
  });

  it('toggles between table and grid view', () => {
    expect(component.viewMode()).toBe('table');
    component.viewMode.set('grid');
    fixture.detectChanges();
    expect(component.viewMode()).toBe('grid');
  });
});
