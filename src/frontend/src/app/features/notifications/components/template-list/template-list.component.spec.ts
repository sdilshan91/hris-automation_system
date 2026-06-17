import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { TemplateListComponent } from './template-list.component';
import { NotificationTemplateService } from '../../services/notification-template.service';
import { ITemplateListItem } from '../../models/notification-template.models';

/**
 * US-NTF-002 AC-1: the template list renders one card per event with its status
 * badge and routes to the editor on click.
 */
describe('TemplateListComponent', () => {
  let fixture: ComponentFixture<TemplateListComponent>;
  let component: TemplateListComponent;
  let router: Router;

  const rows: ITemplateListItem[] = [
    {
      eventKey: 'leave_approved',
      eventName: 'Leave Approved',
      language: 'en',
      isCustom: false,
      lastModified: null,
    },
    {
      eventKey: 'onboarding_welcome',
      eventName: 'Onboarding Welcome',
      language: 'en',
      isCustom: true,
      lastModified: '2026-05-01T10:00:00Z',
    },
  ];

  const svcStub = {
    templates: signal<ITemplateListItem[]>(rows),
    loadingList: signal(false),
    loadTemplates: jasmine.createSpy('loadTemplates'),
  };

  beforeEach(async () => {
    svcStub.loadTemplates.calls.reset();
    svcStub.templates.set(rows);
    svcStub.loadingList.set(false);

    await TestBed.configureTestingModule({
      imports: [TemplateListComponent],
      providers: [
        provideRouter([]),
        provideTranslateService(),
        { provide: NotificationTemplateService, useValue: svcStub },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TemplateListComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  it('loads templates on init', () => {
    expect(svcStub.loadTemplates).toHaveBeenCalledWith('en');
  });

  it('renders one card per template', () => {
    const cards = fixture.nativeElement.querySelectorAll('li[role="listitem"]');
    expect(cards.length).toBe(2);
  });

  it('derives the correct badge label per row', () => {
    expect(component['badge'](rows[0])).toBe('Default');
    expect(component['badge'](rows[1])).toBe('Custom');
  });

  it('shows a skeleton while loading', () => {
    svcStub.loadingList.set(true);
    fixture.detectChanges();
    const skeleton = fixture.nativeElement.querySelector('.animate-pulse');
    expect(skeleton).toBeTruthy();
  });

  it('shows an empty state when there are no templates', () => {
    svcStub.templates.set([]);
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('.border-dashed');
    expect(empty).toBeTruthy();
  });

  it('navigates to the editor with the language query param on click', () => {
    const navSpy = spyOn(router, 'navigate');
    component['open'](rows[1]);
    expect(navSpy).toHaveBeenCalledWith(
      ['/admin/notification-templates', 'onboarding_welcome'],
      { queryParams: { language: 'en' } },
    );
  });
});
