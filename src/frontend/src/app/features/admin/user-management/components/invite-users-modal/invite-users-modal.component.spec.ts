import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr } from 'ngx-toastr';
import { provideTranslateService } from '@ngx-translate/core';
import { InviteUsersModalComponent } from './invite-users-modal.component';
import { IAssignableRole } from '../../models/user-management.models';
import { environment } from '../../../../../../environments/environment';

describe('InviteUsersModalComponent', () => {
  let component: InviteUsersModalComponent;
  let fixture: ComponentFixture<InviteUsersModalComponent>;
  let httpMock: HttpTestingController;

  const usersUrl = `${environment.apiBaseUrl}/users`;

  const mockRoles: IAssignableRole[] = [
    { id: 'r-1', name: 'Employee', description: 'Standard access' },
    { id: 'r-2', name: 'Manager', description: 'Team lead' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InviteUsersModalComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        provideTranslateService(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(InviteUsersModalComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('roles', mockRoles);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('adds multiple emails as tags', () => {
    component.emailDraft.set('a@acme.com');
    component.addEmailFromDraft();
    component.emailDraft.set('b@acme.com');
    component.addEmailFromDraft();
    expect(component.emails()).toEqual(['a@acme.com', 'b@acme.com']);
    expect(component.emailDraft()).toBe('');
  });

  it('rejects an invalid email', () => {
    component.emailDraft.set('not-an-email');
    component.addEmailFromDraft();
    expect(component.emails().length).toBe(0);
    expect(component.emailError()).toBeTruthy();
  });

  it('does not add a duplicate email', () => {
    component.emailDraft.set('a@acme.com');
    component.addEmailFromDraft();
    component.emailDraft.set('a@acme.com');
    component.addEmailFromDraft();
    expect(component.emails().length).toBe(1);
  });

  it('removes an email tag', () => {
    component.emails.set(['a@acme.com', 'b@acme.com']);
    component.removeEmail('a@acme.com');
    expect(component.emails()).toEqual(['b@acme.com']);
  });

  it('toggles role selection', () => {
    component.toggleRole('r-1');
    component.toggleRole('r-2');
    expect(component.selectedRoleIds()).toEqual(['r-1', 'r-2']);
    component.toggleRole('r-1');
    expect(component.selectedRoleIds()).toEqual(['r-2']);
  });

  it('canSubmit requires at least one email and one role in email mode', () => {
    expect(component.canSubmit()).toBeFalse();
    component.emails.set(['a@acme.com']);
    expect(component.canSubmit()).toBeFalse();
    component.selectedRoleIds.set(['r-1']);
    expect(component.canSubmit()).toBeTrue();
  });

  it('submits emails + roleIds and shows per-email results', () => {
    component.emails.set(['a@acme.com', 'b@acme.com']);
    component.selectedRoleIds.set(['r-1']);

    component.submit();

    const req = httpMock.expectOne(`${usersUrl}/invite`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      emails: ['a@acme.com', 'b@acme.com'],
      roleIds: ['r-1'],
    });
    req.flush([
      { email: 'a@acme.com', status: 'invited' },
      { email: 'b@acme.com', status: 'error', error: 'Already a member' },
    ]);

    expect(component.results()?.length).toBe(2);
    expect(component.invitedCount()).toBe(1);
    expect(component.errorCount()).toBe(1);

    fixture.detectChanges();
    const errorRow = fixture.nativeElement.querySelector(
      '[data-testid="invite-row-error"]'
    );
    expect(errorRow.textContent).toContain('Already a member');
  });

  it('surfaces the plan-limit rejection on a 409', () => {
    component.emails.set(['a@acme.com']);
    component.selectedRoleIds.set(['r-1']);

    component.submit();

    const req = httpMock.expectOne(`${usersUrl}/invite`);
    req.flush(
      { message: 'User limit reached for your plan.' },
      { status: 409, statusText: 'Conflict' }
    );

    expect(component.planLimitError()).toBeTrue();
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid="plan-limit-error"]')
    ).toBeTruthy();
  });

  it('parses an uploaded CSV into rows and submits them', () => {
    component.mode.set('csv');
    // Simulate parsed rows (FileReader is async; the parser is unit-tested
    // separately in the service spec).
    component.csvRows.set([
      { email: 'x@acme.com', roleNames: ['Employee'] },
      { email: 'y@acme.com', roleNames: ['Manager'] },
    ]);
    expect(component.canSubmit()).toBeTrue();

    component.submit();

    const req = httpMock.expectOne(`${usersUrl}/invite/csv`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      rows: [
        { email: 'x@acme.com', roleNames: ['Employee'] },
        { email: 'y@acme.com', roleNames: ['Manager'] },
      ],
    });
    req.flush([
      { email: 'x@acme.com', status: 'invited' },
      { email: 'y@acme.com', status: 'invited' },
    ]);
    expect(component.invitedCount()).toBe(2);
  });

  it('blocks email submit with no roles selected', () => {
    component.emails.set(['a@acme.com']);
    component.submit();
    httpMock.verify(); // no request issued
    expect(component.roleError()).toBeTruthy();
  });

  it('reset clears all state', () => {
    component.emails.set(['a@acme.com']);
    component.selectedRoleIds.set(['r-1']);
    component.results.set([{ email: 'a@acme.com', status: 'invited' }]);
    component.reset();
    expect(component.emails().length).toBe(0);
    expect(component.selectedRoleIds().length).toBe(0);
    expect(component.results()).toBeNull();
  });
});
