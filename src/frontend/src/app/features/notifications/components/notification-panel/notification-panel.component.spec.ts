import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { NotificationPanelComponent } from './notification-panel.component';
import { NotificationService } from '../../services/notification.service';
import { INotification } from '../../models/notification.models';

function note(over: Partial<INotification> = {}): INotification {
  return {
    id: over.id ?? 'n1',
    type: over.type ?? 'leave_approved',
    title: over.title ?? 'Leave approved',
    message: over.message ?? 'Your leave was approved.',
    resourceType: over.resourceType ?? 'LeaveRequest',
    resourceId: over.resourceId ?? 'lr-1',
    isRead: over.isRead ?? false,
    createdAt: over.createdAt ?? new Date().toISOString(),
  };
}

class FakeNotificationService {
  notifications = signal<INotification[]>([]);
  unreadCount = signal(0);
  loading = signal(false);
  hasMore = signal(false);
  markRead = jasmine.createSpy('markRead').and.returnValue(of({}));
  markAllRead = jasmine
    .createSpy('markAllRead')
    .and.returnValue(of({ updated: 0 }));
  loadNextPage = jasmine.createSpy('loadNextPage');
}

describe('NotificationPanelComponent', () => {
  let fixture: ComponentFixture<NotificationPanelComponent>;
  let component: NotificationPanelComponent;
  let svc: FakeNotificationService;
  let router: Router;

  beforeEach(async () => {
    svc = new FakeNotificationService();
    await TestBed.configureTestingModule({
      imports: [NotificationPanelComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService(),
        { provide: NotificationService, useValue: svc },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationPanelComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
  });

  it('shows the empty state when there are no notifications', () => {
    expect(fixture.nativeElement.querySelector('.empty')).toBeTruthy();
  });

  it('renders notification items (AC-3)', () => {
    svc.notifications.set([note({ id: 'a' }), note({ id: 'b' })]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.item').length).toBe(2);
  });

  it('marks unread items with the unread class + blue dot (AC-3)', () => {
    svc.notifications.set([note({ id: 'a', isRead: false })]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.item.unread')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.unread-dot')).toBeTruthy();
  });

  it('only shows Mark All as Read when there are unread items (AC-5)', () => {
    expect(fixture.nativeElement.querySelector('.mark-all-btn')).toBeNull();
    svc.unreadCount.set(2);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.mark-all-btn')).toBeTruthy();
  });

  it('clicking an unread item marks it read and navigates (AC-4)', () => {
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);
    component.open(note({ id: 'x', isRead: false, resourceType: 'LeaveRequest', resourceId: 'lr-7' }));
    expect(svc.markRead).toHaveBeenCalledWith('x');
    expect(closedSpy).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/leave', 'requests', 'lr-7']);
  });

  it('does not re-mark an already-read item', () => {
    component.open(note({ id: 'y', isRead: true }));
    expect(svc.markRead).not.toHaveBeenCalled();
  });

  it('Mark All as Read calls the service (AC-5)', () => {
    component.markAllRead();
    expect(svc.markAllRead).toHaveBeenCalled();
  });

  it('View All emits closed and navigates to the full list', () => {
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);
    component.viewAll();
    expect(closedSpy).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/notifications']);
  });
});
