import {
  ComponentFixture,
  TestBed,
  fakeAsync,
  tick,
} from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideRouter,
  Router,
  ActivatedRoute,
} from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { signal } from '@angular/core';
import { ToastrService } from 'ngx-toastr';
import { EmployeeListComponent } from './employee-list.component';
import { AuthService } from '../../../../../core/auth/auth.service';
import { environment } from '../../../../../../environments/environment';
import {
  IEmployee,
  IPaginatedResponse,
} from '../../models/employee.models';

describe('EmployeeListComponent', () => {
  let component: EmployeeListComponent;
  let fixture: ComponentFixture<EmployeeListComponent>;
  let httpMock: HttpTestingController;
  let router: Router;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const baseUrl = `${environment.apiBaseUrl}/employees`;

  const mockEmployee: IEmployee = {
    employeeId: 'emp-1',
    tenantId: 'tenant-1',
    employeeNo: 'EMP-0001',
    firstName: 'John',
    lastName: 'Doe',
    email: 'john.doe@company.com',
    phone: '+94771234567',
    dateOfBirth: '1990-01-15',
    gender: 'Male',
    dateOfJoining: '2026-06-01',
    departmentId: 'dept-1',
    departmentName: 'Engineering',
    jobTitleId: 'jt-1',
    jobTitleName: 'Software Engineer',
    employmentType: 'Full-Time',
    status: 'active',
    profilePhotoUrl: null,
    customFields: null,
    isActive: true,
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
  };

  function buildPaginatedResponse(
    employees: IEmployee[],
    total?: number,
    page?: number,
    pageSize?: number
  ): IPaginatedResponse<IEmployee> {
    return {
      data: employees,
      total: total ?? employees.length,
      page: page ?? 1,
      pageSize: pageSize ?? 20,
    };
  }

  /** Flush the initial directory load request */
  function flushInitialLoad(
    employees: IEmployee[] = [],
    total?: number
  ): void {
    const req = httpMock.expectOne((r) => r.url === baseUrl && r.method === 'GET');
    req.flush(buildPaginatedResponse(employees, total));
  }

  beforeEach(async () => {
    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'info',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [EmployeeListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          {
            path: 'employees',
            children: [
              { path: '', component: EmployeeListComponent },
              { path: 'new', component: EmployeeListComponent },
              { path: ':id', component: EmployeeListComponent },
            ],
          },
        ]),
        provideAnimationsAsync(),
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: AuthService,
          useValue: {
            permissions: signal([
              'Employee.View.All',
              'Employee.View.Team',
              'Employee.Export',
            ]),
            hasPermission: (_p: string) => true,
          },
        },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate');

    fixture = TestBed.createComponent(EmployeeListComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    httpMock.verify();
  });

  // ─── Basic rendering ──────────────────────────────────────

  it('should create the component', () => {
    fixture.detectChanges();
    flushInitialLoad();
    expect(component).toBeTruthy();
  });

  it('should load directory on init with default pagination', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(
      (r) => r.url === baseUrl && r.method === 'GET'
    );
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('20');
    expect(req.request.params.get('sort')).toBe('name');
    expect(req.request.params.get('sortDirection')).toBe('asc');
    req.flush(buildPaginatedResponse([mockEmployee], 1));
    fixture.detectChanges();

    expect(component.employees().length).toBe(1);
    expect(component.totalCount()).toBe(1);
    expect(component.isLoading()).toBeFalse();
  });

  it('should display empty state when no employees found', () => {
    fixture.detectChanges();
    flushInitialLoad([], 0);
    fixture.detectChanges();

    expect(component.employees().length).toBe(0);
    expect(component.isLoading()).toBeFalse();
  });

  it('should display correct initials', () => {
    fixture.detectChanges();
    flushInitialLoad();

    const result = component.getInitials({
      firstName: 'John',
      lastName: 'Doe',
    } as IEmployee);
    expect(result).toBe('JD');
  });

  // ─── Search with debounce (AC-2, NFR-2) ──────────────────

  it('should debounce search input by 300ms', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    // Type a search term
    component.onSearchInput('John');
    tick(100);
    // Not triggered yet
    httpMock.expectNone((r) => r.url === baseUrl && r.params.has('search'));

    tick(200); // Total 300ms
    // Now the debounced search should fire
    const req = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('search') === 'John'
    );
    req.flush(buildPaginatedResponse([mockEmployee], 1));

    expect(component.searchTerm()).toBe('John');
    expect(component.currentPage()).toBe(1);
  }));

  it('should not fire duplicate requests for same search term', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad();

    component.onSearchInput('test');
    tick(300);
    const req1 = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('search') === 'test'
    );
    req1.flush(buildPaginatedResponse([]));

    // Same term again
    component.onSearchInput('test');
    tick(300);
    httpMock.expectNone(
      (r) => r.url === baseUrl && r.params.get('search') === 'test'
    );
  }));

  it('should reset page to 1 on search', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 50);

    // Go to page 2 first
    component.goToPage(2);
    const pageReq = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('page') === '2'
    );
    pageReq.flush(buildPaginatedResponse([mockEmployee], 50, 2));

    expect(component.currentPage()).toBe(2);

    // Now search
    component.onSearchInput('Jane');
    tick(300);
    const searchReq = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('search') === 'Jane'
    );
    expect(searchReq.request.params.get('page')).toBe('1');
    searchReq.flush(buildPaginatedResponse([], 0));

    expect(component.currentPage()).toBe(1);
  }));

  // ─── Filter application + chips (FR-2, AC-3) ──────────────

  it('should apply department and status filters and generate chips', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    // Set filters
    component.filterDepartments.set(['Engineering']);
    component.filterStatuses.set(['active']);
    component.applyFilters();

    const req = httpMock.expectOne(
      (r) =>
        r.url === baseUrl &&
        r.params.get('departments') === 'Engineering' &&
        r.params.get('statuses') === 'active'
    );
    req.flush(buildPaginatedResponse([mockEmployee], 1));

    // Verify chips
    const chips = component.activeFilterChips();
    expect(chips.length).toBe(2);
    expect(chips[0].category).toBe('Department');
    expect(chips[0].label).toBe('Engineering');
    expect(chips[1].category).toBe('Status');
    expect(chips[1].label).toBe('active');
  });

  it('should remove a filter chip and reload', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.filterDepartments.set(['Engineering', 'Sales']);
    component.applyFilters();
    const req1 = httpMock.expectOne((r) => r.url === baseUrl);
    req1.flush(buildPaginatedResponse([mockEmployee], 1));

    // Remove "Engineering" chip
    component.removeFilterChip({
      category: 'Department',
      label: 'Engineering',
      value: 'Engineering',
      filterKey: 'departments',
    });

    const req2 = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('departments') === 'Sales'
    );
    req2.flush(buildPaginatedResponse([mockEmployee], 1));

    expect(component.filterDepartments()).toEqual(['Sales']);
  });

  it('should clear all filters', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.filterDepartments.set(['Engineering']);
    component.filterStatuses.set(['active']);
    component.filterLocation.set('NYC');
    component.clearFilters();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.flush(buildPaginatedResponse([], 0));

    expect(component.filterDepartments().length).toBe(0);
    expect(component.filterStatuses().length).toBe(0);
    expect(component.filterLocation()).toBe('');
    expect(component.searchTerm()).toBe('');
    expect(component.activeFilterChips().length).toBe(0);
  });

  it('should count active filters correctly', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.activeFilterCount()).toBe(0);

    component.filterDepartments.set(['Engineering']);
    expect(component.activeFilterCount()).toBe(1);

    component.filterStatuses.set(['active']);
    expect(component.activeFilterCount()).toBe(2);

    component.filterLocation.set('NYC');
    expect(component.activeFilterCount()).toBe(3);
  });

  // ─── View mode toggle (FR-3) ──────────────────────────────

  it('should default to card view', () => {
    fixture.detectChanges();
    flushInitialLoad();
    expect(component.viewMode()).toBe('card');
  });

  it('should toggle between card and table view modes', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.setViewMode('table');
    expect(component.viewMode()).toBe('table');

    component.setViewMode('card');
    expect(component.viewMode()).toBe('card');
  });

  // ─── Pagination (FR-5, AC-4) ──────────────────────────────

  it('should calculate pagination correctly', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.flush(buildPaginatedResponse([mockEmployee], 55, 1, 20));
    fixture.detectChanges();

    expect(component.totalCount()).toBe(55);
    expect(component.totalPages()).toBe(3);
    expect(component.showingFrom()).toBe(1);
    expect(component.showingTo()).toBe(20);
  });

  it('should navigate to next page', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 50);

    component.goToPage(2);
    const req = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('page') === '2'
    );
    req.flush(buildPaginatedResponse([mockEmployee], 50, 2, 20));

    expect(component.currentPage()).toBe(2);
    expect(component.showingFrom()).toBe(21);
    expect(component.showingTo()).toBe(40);
  });

  it('should not navigate to invalid pages', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 20);

    component.goToPage(0);
    component.goToPage(2); // Only 1 page available (20 items, pageSize=20)

    // Current page should remain 1
    expect(component.currentPage()).toBe(1);
  });

  it('should change page size and reset to page 1', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 50);

    component.goToPage(2);
    const pageReq = httpMock.expectOne((r) => r.params.get('page') === '2');
    pageReq.flush(buildPaginatedResponse([mockEmployee], 50, 2, 20));

    component.onPageSizeChange(50);
    const sizeReq = httpMock.expectOne(
      (r) =>
        r.url === baseUrl &&
        r.params.get('pageSize') === '50' &&
        r.params.get('page') === '1'
    );
    sizeReq.flush(buildPaginatedResponse([mockEmployee], 50, 1, 50));

    expect(component.pageSize()).toBe(50);
    expect(component.currentPage()).toBe(1);
  });

  it('should generate visible page numbers with ellipsis for many pages', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 200);

    // Navigate to page 5 so we're mid-range
    component.goToPage(5);
    const req = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('page') === '5'
    );
    req.flush(buildPaginatedResponse([mockEmployee], 200, 5, 20));
    fixture.detectChanges();

    // totalPages = ceil(200/20) = 10, currentPage = 5
    expect(component.currentPage()).toBe(5);
    const pages = component.visiblePages();
    expect(pages[0]).toBe(1);
    expect(pages).toContain(-1); // ellipsis
    expect(pages).toContain(5); // current page
    expect(pages[pages.length - 1]).toBe(10);
  });

  // ─── URL state sync (FR-6, AC-3) ──────────────────────────

  it('should sync filters to URL on apply', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.filterDepartments.set(['Engineering']);
    component.applyFilters();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.flush(buildPaginatedResponse([mockEmployee], 1));

    expect(router.navigate).toHaveBeenCalledWith(
      [],
      jasmine.objectContaining({
        queryParams: jasmine.objectContaining({
          departments: 'Engineering',
        }),
        queryParamsHandling: 'replace',
        replaceUrl: true,
      })
    );
  });

  it('should restore state from URL query params', () => {
    // Reconfigure with query params
    const route = TestBed.inject(ActivatedRoute);
    spyOn(route.snapshot.queryParamMap, 'has').and.callFake(
      (key: string) => ['search', 'page', 'view'].includes(key)
    );
    spyOn(route.snapshot.queryParamMap, 'get').and.callFake((key: string) => {
      switch (key) {
        case 'search':
          return 'John';
        case 'page':
          return '3';
        case 'view':
          return 'table';
        default:
          return null;
      }
    });

    component.restoreFromUrl();

    expect(component.searchTerm()).toBe('John');
    expect(component.currentPage()).toBe(3);
    expect(component.viewMode()).toBe('table');
  });

  // ─── Export (AC-5, FR-8) ──────────────────────────────────

  it('should export CSV and trigger file download', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    component.exportDirectory('csv');

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${baseUrl}/export` &&
        r.params.get('format') === 'csv'
    );
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['csv-data'], { type: 'text/csv' }));

    expect(component.isExporting()).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalledWith(
      'Employee directory exported as CSV.'
    );
  });

  it('should export Excel and trigger file download', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    component.exportDirectory('excel');

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${baseUrl}/export` &&
        r.params.get('format') === 'excel'
    );
    req.flush(
      new Blob(['excel-data'], {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      })
    );

    expect(toastrSpy.success).toHaveBeenCalledWith(
      'Employee directory exported as XLSX.'
    );
  });

  it('should show error toast on export failure', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    component.exportDirectory('csv');

    const req = httpMock.expectOne(
      (r) => r.url === `${baseUrl}/export`
    );
    req.error(new ProgressEvent('error'), { status: 500 });

    expect(toastrSpy.error).toHaveBeenCalledWith(
      'Failed to export employee directory.'
    );
    expect(component.isExporting()).toBeFalse();
  });

  it('should toggle export menu', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.showExportMenu()).toBeFalse();
    component.toggleExportMenu();
    expect(component.showExportMenu()).toBeTrue();
    component.toggleExportMenu();
    expect(component.showExportMenu()).toBeFalse();
  });

  // ─── Sorting (FR-4) ──────────────────────────────────────

  it('should change sort field and direction', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    component.onSortChange('date_of_joining_desc');

    const req = httpMock.expectOne(
      (r) =>
        r.url === baseUrl &&
        r.params.get('sort') === 'date_of_joining' &&
        r.params.get('sortDirection') === 'desc'
    );
    req.flush(buildPaginatedResponse([mockEmployee], 1));

    expect(component.sortField()).toBe('date_of_joining');
    expect(component.sortDirection()).toBe('desc');
  });

  it('should reset to page 1 on sort change', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 50);

    component.goToPage(2);
    const pageReq = httpMock.expectOne((r) => r.params.get('page') === '2');
    pageReq.flush(buildPaginatedResponse([mockEmployee], 50, 2));

    component.onSortChange('department_asc');
    const sortReq = httpMock.expectOne(
      (r) => r.params.get('sort') === 'department' && r.params.get('page') === '1'
    );
    sortReq.flush(buildPaginatedResponse([mockEmployee], 50, 1));

    expect(component.currentPage()).toBe(1);
  });

  // ─── Navigation ───────────────────────────────────────────

  it('should navigate to add employee', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.addEmployee();
    expect(router.navigate).toHaveBeenCalledWith(['/employees/new']);
  });

  it('should navigate to employee profile', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.viewEmployee('emp-1');
    expect(router.navigate).toHaveBeenCalledWith(['/employees', 'emp-1']);
  });

  // ─── Role-based visibility ────────────────────────────────

  it('should show archived toggle for users with Employee.View.All', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.canShowArchived()).toBeTrue();
    expect(component.isPrivilegedUser()).toBeTrue();
  });

  // ─── US-CHR-011: Bulk selection + assignment (AC-5) ─────

  it('should toggle employee selection', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    expect(component.selectedEmployeeIds().length).toBe(0);

    component.toggleSelect('emp-1');
    expect(component.selectedEmployeeIds()).toContain('emp-1');

    component.toggleSelect('emp-1');
    expect(component.selectedEmployeeIds()).not.toContain('emp-1');
  });

  it('should select/deselect all visible employees', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee, { ...mockEmployee, employeeId: 'emp-2' }], 2);

    expect(component.allVisibleSelected()).toBeFalse();

    component.toggleSelectAll();
    expect(component.selectedEmployeeIds().length).toBe(2);
    expect(component.allVisibleSelected()).toBeTrue();

    component.toggleSelectAll();
    expect(component.selectedEmployeeIds().length).toBe(0);
  });

  it('should clear selection', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    component.toggleSelect('emp-1');
    expect(component.selectedEmployeeIds().length).toBe(1);

    component.clearSelection();
    expect(component.selectedEmployeeIds().length).toBe(0);
  });

  it('should open and close bulk assign modal', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.toggleSelect('emp-1');
    component.openBulkAssignModal();
    expect(component.showBulkAssignModal()).toBeTrue();

    component.closeBulkAssignModal();
    expect(component.showBulkAssignModal()).toBeFalse();
  });

  it('should search for managers in bulk assign modal', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    component.openBulkAssignModal();
    component.onBulkManagerSearch('Al');
    tick(350);

    const searchReq = httpMock.expectOne(
      (r) => r.url === baseUrl &&
        r.params.get('search') === 'Al' &&
        r.params.get('statuses') === 'active'
    );
    searchReq.flush({
      data: [{ ...mockEmployee, employeeId: 'mgr-1', firstName: 'Alice', lastName: 'Boss' }],
      total: 1,
      page: 1,
      pageSize: 10,
    });
    tick();

    expect(component.bulkManagerSearchResults().length).toBe(1);
    expect(component.isBulkSearching()).toBeFalse();
  }));

  it('should confirm bulk assignment and show results', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    component.selectedEmployeeIds.set(['emp-1', 'emp-2']);
    component.openBulkAssignModal();
    component.bulkSelectedManagerId.set('mgr-1');
    component.bulkSelectedManagerName.set('Alice Boss');

    component.confirmBulkAssign();

    const req = httpMock.expectOne(`${baseUrl}/bulk-assign-manager`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.employeeIds).toEqual(['emp-1', 'emp-2']);
    expect(req.request.body.managerEmployeeId).toBe('mgr-1');

    req.flush({
      results: [
        { employeeId: 'emp-1', employeeName: 'John Doe', success: true, error: null },
        { employeeId: 'emp-2', employeeName: 'Jane Smith', success: true, error: null },
      ],
      totalSuccess: 2,
      totalFailed: 0,
    });
    tick();

    expect(component.bulkAssignResults().length).toBe(2);
    expect(component.isBulkAssigning()).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalledWith(
      'Manager assigned to 2 employees successfully.'
    );
  }));

  it('should show warning toast for partial bulk assignment failure', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    component.selectedEmployeeIds.set(['emp-1', 'emp-2']);
    component.openBulkAssignModal();
    component.bulkSelectedManagerId.set('mgr-1');

    component.confirmBulkAssign();

    const req = httpMock.expectOne(`${baseUrl}/bulk-assign-manager`);
    req.flush({
      results: [
        { employeeId: 'emp-1', employeeName: 'John', success: true, error: null },
        { employeeId: 'emp-2', employeeName: 'Jane', success: false, error: 'Circular chain' },
      ],
      totalSuccess: 1,
      totalFailed: 1,
    });
    tick();

    expect(toastrSpy.warning).toHaveBeenCalledWith(
      '1 assigned, 1 failed. See details.'
    );
  }));

  it('should show error toast on bulk assign API failure', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee], 1);

    component.selectedEmployeeIds.set(['emp-1']);
    component.openBulkAssignModal();
    component.bulkSelectedManagerId.set('mgr-1');

    component.confirmBulkAssign();

    const req = httpMock.expectOne(`${baseUrl}/bulk-assign-manager`);
    req.error(new ProgressEvent('error'), { status: 500 });
    tick();

    expect(toastrSpy.error).toHaveBeenCalledWith(
      'Failed to assign manager. Please try again.'
    );
    expect(component.isBulkAssigning()).toBeFalse();
  }));

  it('should compute someVisibleSelected correctly', () => {
    fixture.detectChanges();
    flushInitialLoad([mockEmployee, { ...mockEmployee, employeeId: 'emp-2' }], 2);

    expect(component.someVisibleSelected()).toBeFalse();

    component.toggleSelect('emp-1');
    expect(component.someVisibleSelected()).toBeTrue();
    expect(component.allVisibleSelected()).toBeFalse();
  });

  it('should return correct initials from getBulkInitials', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.getBulkInitials('Alice', 'Manager')).toBe('AM');
  });

  // ─── Error handling ───────────────────────────────────────

  it('should handle API errors gracefully', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.error(new ProgressEvent('error'), { status: 500 });
    fixture.detectChanges();

    expect(component.employees().length).toBe(0);
    expect(component.totalCount()).toBe(0);
    expect(component.isLoading()).toBeFalse();
  });
});
