import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { Feedback360ConfigComponent } from './feedback-360-config.component';
import { Feedback360Service } from '../../services/feedback-360.service';
import {
  IReviewerConfig,
  ICompletionTracker,
  IEmployeeOption,
} from '../../models/feedback-360.models';

function makeConfig(overrides: Partial<IReviewerConfig> = {}): IReviewerConfig {
  return {
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    reviewers: [
      { reviewerId: 'e-1', name: 'Alex Doe', category: 'Self', locked: true },
      {
        reviewerId: 'mgr-1',
        name: 'Morgan Manager',
        category: 'Manager',
        locked: true,
      },
    ],
    suggestedPeers: [
      {
        employeeId: 'e-2',
        name: 'Pat Peer',
        jobTitle: 'Engineer',
        departmentId: 'd-1',
        departmentName: 'Engineering',
      },
    ],
    suggestedDirectReports: [],
    candidatePool: [
      {
        employeeId: 'e-2',
        name: 'Pat Peer',
        jobTitle: 'Engineer',
        departmentId: 'd-1',
        departmentName: 'Engineering',
      },
      {
        employeeId: 'e-3',
        name: 'Sam Sales',
        jobTitle: 'AE',
        departmentId: 'd-2',
        departmentName: 'Sales',
      },
      // Self must be filtered out of the pool (BR-2).
      {
        employeeId: 'e-1',
        name: 'Alex Doe',
        jobTitle: 'Eng',
        departmentId: 'd-1',
        departmentName: 'Engineering',
      },
    ],
    minimums: [
      { category: 'Peer', minimum: 2 },
      { category: 'DirectReport', minimum: 0 },
    ],
    anonymous: true,
    editable: true,
    ...overrides,
  };
}

function makeTracker(
  overrides: Partial<ICompletionTracker> = {},
): ICompletionTracker {
  return {
    employeeId: 'e-1',
    categories: [
      { category: 'Self', submitted: 1, pending: 0, overdue: 0, minimum: 1 },
      { category: 'Manager', submitted: 0, pending: 1, overdue: 0, minimum: 1 },
      { category: 'Peer', submitted: 1, pending: 0, overdue: 1, minimum: 2 },
      {
        category: 'DirectReport',
        submitted: 0,
        pending: 0,
        overdue: 0,
        minimum: 0,
      },
    ],
    ...overrides,
  };
}

describe('Feedback360ConfigComponent', () => {
  let fixture: ComponentFixture<Feedback360ConfigComponent>;
  let component: Feedback360ConfigComponent;
  let serviceSpy: jasmine.SpyObj<Feedback360Service>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(
    config: IReviewerConfig = makeConfig(),
    tracker: ICompletionTracker = makeTracker(),
  ): Promise<void> {
    serviceSpy = jasmine.createSpyObj<Feedback360Service>(
      'Feedback360Service',
      ['getReviewerConfig', 'saveReviewers', 'getTracker'],
    );
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getReviewerConfig.and.returnValue(of(config));
    serviceSpy.getTracker.and.returnValue(of(tracker));

    await TestBed.configureTestingModule({
      imports: [Feedback360ConfigComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: Feedback360Service, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'e-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Feedback360ConfigComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads the reviewer config and renders Self + Manager (AC-1)', async () => {
    await setup();
    expect(component.config()?.employeeName).toBe('Alex Doe');
    expect(component.reviewersFor('Self').length).toBe(1);
    expect(component.reviewersFor('Manager').length).toBe(1);
  });

  it('excludes self from the candidate pool (BR-2)', async () => {
    await setup();
    const poolIds = component.filteredPool().map((p) => p.employeeId);
    expect(poolIds).not.toContain('e-1');
    expect(poolIds).toContain('e-3');
  });

  it('adds a peer from the pool (AC-1)', async () => {
    await setup();
    const peer = component.config()!.candidatePool.find(
      (p) => p.employeeId === 'e-2',
    )!;
    component.addReviewer(peer, 'Peer');
    fixture.detectChanges();
    expect(component.reviewersFor('Peer').length).toBe(1);
    expect(component.reviewersFor('Peer')[0].reviewerId).toBe('e-2');
  });

  it('blocks adding self as a peer and toasts an error (BR-2)', async () => {
    await setup();
    const selfOption: IEmployeeOption = {
      employeeId: 'e-1',
      name: 'Alex Doe',
      departmentId: 'd-1',
      departmentName: 'Engineering',
    };
    component.addReviewer(selfOption, 'Peer');
    fixture.detectChanges();
    expect(component.reviewersFor('Peer').length).toBe(0);
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('removes a manual peer but not the locked Manager (AC-1)', async () => {
    await setup();
    const peer = component.config()!.candidatePool.find(
      (p) => p.employeeId === 'e-2',
    )!;
    component.addReviewer(peer, 'Peer');
    fixture.detectChanges();
    const added = component.reviewersFor('Peer')[0];
    component.removeReviewer(added);
    fixture.detectChanges();
    expect(component.reviewersFor('Peer').length).toBe(0);

    // Locked Manager cannot be removed.
    const manager = component.reviewersFor('Manager')[0];
    component.removeReviewer(manager);
    fixture.detectChanges();
    expect(component.reviewersFor('Manager').length).toBe(1);
  });

  it('save() sends ONLY manual Peer/DirectReport rows (full-replace)', async () => {
    await setup();
    serviceSpy.saveReviewers.and.returnValue(of(makeConfig()));
    const peer = component.config()!.candidatePool.find(
      (p) => p.employeeId === 'e-2',
    )!;
    component.addReviewer(peer, 'Peer');
    component.save();

    expect(serviceSpy.saveReviewers).toHaveBeenCalledWith('e-1', {
      reviewers: [{ reviewerId: 'e-2', category: 'Peer' }],
    });
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('computes the completion percentage per category (AC-3)', async () => {
    await setup();
    const peerProgress = component
      .tracker()!
      .categories.find((c) => c.category === 'Peer')!;
    // 1 submitted of (1 + 0 + 1) = 50%
    expect(component.percentFor(peerProgress)).toBe(50);
    expect(component.totalFor(peerProgress)).toBe(2);
  });

  it('flags categories below their minimum (BR-4)', async () => {
    await setup();
    // Peer has 2 reviewers (min 2) → not flagged; Manager has 1 (min 1) → not flagged.
    // Force a shortfall: tracker with 0 peers, min 2.
    component.tracker.set(
      makeTracker({
        categories: [
          {
            category: 'Peer',
            submitted: 0,
            pending: 0,
            overdue: 0,
            minimum: 2,
          },
        ],
      }),
    );
    fixture.detectChanges();
    expect(component.belowMinimumCategories()).toContain('Peers');
  });

  it('surfaces an error when the config load fails', async () => {
    serviceSpy = jasmine.createSpyObj<Feedback360Service>(
      'Feedback360Service',
      ['getReviewerConfig', 'saveReviewers', 'getTracker'],
    );
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getReviewerConfig.and.returnValue(
      throwError(() => new Error('boom')),
    );
    serviceSpy.getTracker.and.returnValue(of(makeTracker()));

    await TestBed.configureTestingModule({
      imports: [Feedback360ConfigComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: Feedback360Service, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'e-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Feedback360ConfigComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.error()).toBeTruthy();
  });
});
