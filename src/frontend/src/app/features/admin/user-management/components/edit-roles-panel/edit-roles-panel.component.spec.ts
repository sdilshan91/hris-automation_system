import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr } from 'ngx-toastr';
import { provideTranslateService } from '@ngx-translate/core';
import { EditRolesPanelComponent } from './edit-roles-panel.component';
import {
  IAssignableRole,
  IUserSummary,
} from '../../models/user-management.models';
import { environment } from '../../../../../../environments/environment';

describe('EditRolesPanelComponent', () => {
  let component: EditRolesPanelComponent;
  let fixture: ComponentFixture<EditRolesPanelComponent>;
  let httpMock: HttpTestingController;

  const usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  const roles: IAssignableRole[] = [
    { id: 'r-1', name: 'Employee', description: '' },
    { id: 'r-2', name: 'Manager', description: '' },
    { id: 'r-3', name: 'HR Officer', description: '' },
  ];

  const user: IUserSummary = {
    userTenantId: 'ut-1',
    userId: 'u-1',
    displayName: 'Jane Doe',
    email: 'jane@acme.com',
    roles: [{ id: 'r-1', name: 'Employee' }],
    status: 'active',
    lastLoginAt: null,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EditRolesPanelComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        provideTranslateService(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EditRolesPanelComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('user', user);
    fixture.componentRef.setInput('roles', roles);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('seeds selection from the user current roles', () => {
    expect(component.selected()).toEqual(['r-1']);
  });

  it('saves the complete new role-id set', () => {
    // Add Manager + HR Officer, keep Employee.
    component.toggle('r-2');
    component.toggle('r-3');

    component.save();

    const req = httpMock.expectOne(`${usersUrl}/roles`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      userTenantId: 'ut-1',
      roleIds: ['r-1', 'r-2', 'r-3'],
    });
    req.flush(null);
  });

  it('emits saved after a successful save', () => {
    const savedSpy = jasmine.createSpy('saved');
    component.saved.subscribe(savedSpy);

    component.save();
    httpMock.expectOne(`${usersUrl}/roles`).flush(null);

    expect(savedSpy).toHaveBeenCalled();
    expect(component.saving()).toBeFalse();
  });

  it('removing a role drops it from the submitted set', () => {
    component.toggle('r-1'); // remove the only current role
    component.save();
    const req = httpMock.expectOne(`${usersUrl}/roles`);
    expect(req.request.body).toEqual({ userTenantId: 'ut-1', roleIds: [] });
    req.flush(null);
  });
});
