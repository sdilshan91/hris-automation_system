import { Component, signal } from '@angular/core';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { HasPermissionDirective } from './has-permission.directive';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  standalone: true,
  imports: [HasPermissionDirective],
  template: `
    <div *appHasPermission="permission" id="guarded">Visible</div>
    <div id="always">Always Visible</div>
  `,
})
class TestHostComponent {
  permission: string | string[] = 'Admin.Roles.Manage';
}

describe('HasPermissionDirective', () => {
  let fixture: ComponentFixture<TestHostComponent>;

  function createFixture(perms: string[]): void {
    const permissionsSignal = signal(perms);

    TestBed.configureTestingModule({
      imports: [TestHostComponent],
      providers: [
        {
          provide: AuthService,
          useValue: { permissions: permissionsSignal },
        },
      ],
    });

    fixture = TestBed.createComponent(TestHostComponent);
    fixture.detectChanges();
  }

  it('should show element when user has permission', () => {
    createFixture(['Admin.Roles.Manage', 'Admin.View']);

    const guarded = fixture.nativeElement.querySelector('#guarded');
    expect(guarded).toBeTruthy();
    expect(guarded.textContent).toContain('Visible');
  });

  it('should hide element when user lacks permission', () => {
    createFixture(['Employee.View.All']);

    const guarded = fixture.nativeElement.querySelector('#guarded');
    expect(guarded).toBeNull();
  });

  it('should always show non-guarded elements', () => {
    createFixture([]);

    const always = fixture.nativeElement.querySelector('#always');
    expect(always).toBeTruthy();
  });
});
