import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { NotificationBellComponent } from './notification-bell.component';
import { NotificationService } from '../../services/notification.service';
import { badgeText } from '../../models/notification.models';

/** Minimal NotificationService stub exposing the signals/methods the bell uses. */
class FakeNotificationService {
  notifications = signal([]);
  unreadCount = signal(0);
  loading = signal(false);
  liveAnnouncement = signal('');
  badge = signal('');
  connectionState = signal<'connected'>('connected');
  start = jasmine.createSpy('start').and.resolveTo();
  loadFirstPage = jasmine.createSpy('loadFirstPage');

  /** Test helper to drive the unread count + derived badge together. */
  setUnread(n: number): void {
    this.unreadCount.set(n);
    this.badge.set(badgeText(n));
  }
}

describe('NotificationBellComponent', () => {
  let fixture: ComponentFixture<NotificationBellComponent>;
  let component: NotificationBellComponent;
  let svc: FakeNotificationService;

  beforeEach(async () => {
    svc = new FakeNotificationService();
    await TestBed.configureTestingModule({
      imports: [NotificationBellComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: NotificationService, useValue: svc },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationBellComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('creates and starts the realtime connection on init (AC-1)', () => {
    expect(component).toBeTruthy();
    expect(svc.start).toHaveBeenCalled();
  });

  it('hides the badge when there are no unread notifications', () => {
    const badge = fixture.nativeElement.querySelector('.badge');
    expect(badge).toBeNull();
  });

  it('shows the unread count on the badge (FR-5)', () => {
    svc.setUnread(5);
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('.badge');
    expect(badge?.textContent.trim()).toBe('5');
  });

  it('caps the badge at 99+ (FR-5)', () => {
    svc.setUnread(123);
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('.badge');
    expect(badge?.textContent.trim()).toBe('99+');
  });

  it('toggles the panel open and refreshes the first page on open', () => {
    expect(component.open()).toBeFalse();
    component.toggle();
    expect(component.open()).toBeTrue();
    expect(svc.loadFirstPage).toHaveBeenCalled();
    component.toggle();
    expect(component.open()).toBeFalse();
  });

  it('renders the panel when open', () => {
    component.toggle();
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('app-notification-panel'),
    ).toBeTruthy();
  });

  it('closes on Escape (keyboard a11y, NFR-6)', () => {
    component.toggle();
    expect(component.open()).toBeTrue();
    component.onEscape();
    expect(component.open()).toBeFalse();
  });

  it('exposes a polite ARIA live region for new arrivals (NFR-6)', () => {
    const region = fixture.nativeElement.querySelector('[aria-live="polite"]');
    expect(region).toBeTruthy();
    svc.liveAnnouncement.set('Leave approved. Your leave was approved.');
    fixture.detectChanges();
    expect(region.textContent).toContain('Leave approved');
  });
});
