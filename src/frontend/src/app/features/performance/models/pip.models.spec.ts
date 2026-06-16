import {
  IPip,
  IPipCheckpoint,
  addDaysIso,
  canEscalate,
  canRecordCheckpoint,
  canSetOutcome,
  checkpointMarkerPosition,
  isPipHrRole,
  pipDurationDays,
  pipDurationValid,
  pipIsClosed,
  pipTimelineProgress,
  PIP_MIN_DURATION_DAYS,
} from './pip.models';

function makePip(overrides: Partial<IPip> = {}): IPip {
  return {
    pipId: 'pip-1',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    managerName: 'Sam Lead',
    status: 'Active',
    reason: 'Below threshold on Q1 review',
    startDate: '2026-06-01',
    endDate: '2026-08-30',
    mentorName: 'Pat Coach',
    objectives: [],
    escalationAction: 'TerminationRecommendation',
    escalation: null,
    acknowledgement: 'Pending',
    acknowledgedSignature: null,
    outcome: null,
    ...overrides,
  };
}

describe('pip.models helpers', () => {
  describe('pipDurationDays / pipDurationValid', () => {
    it('computes whole-day duration between two dates', () => {
      expect(pipDurationDays('2026-06-01', '2026-07-01')).toBe(30);
    });

    it('returns 0 for missing or reversed dates', () => {
      expect(pipDurationDays(null, '2026-07-01')).toBe(0);
      expect(pipDurationDays('2026-07-01', '2026-06-01')).toBe(0);
    });

    it('BR-3: rejects a duration under the 30-day minimum', () => {
      expect(pipDurationValid('2026-06-01', '2026-06-20')).toBeFalse();
    });

    it('BR-3: accepts a duration at exactly 30 days', () => {
      const end = addDaysIso('2026-06-01', PIP_MIN_DURATION_DAYS);
      expect(pipDurationValid('2026-06-01', end)).toBeTrue();
    });
  });

  describe('addDaysIso', () => {
    it('adds whole days and returns yyyy-MM-dd', () => {
      expect(addDaysIso('2026-06-01', 60)).toBe('2026-07-31');
    });

    it('returns empty string for an invalid date', () => {
      expect(addDaysIso('not-a-date', 30)).toBe('');
    });
  });

  describe('isPipHrRole (BR-1)', () => {
    it('is true for HR / Tenant Admin roles', () => {
      expect(isPipHrRole(['HR Officer'])).toBeTrue();
      expect(isPipHrRole(['HR Manager'])).toBeTrue();
      expect(isPipHrRole(['Tenant Admin'])).toBeTrue();
    });

    it('is false for a plain Manager (cannot close/extend, BR-1)', () => {
      expect(isPipHrRole(['Manager'])).toBeFalse();
      expect(isPipHrRole(['Employee'])).toBeFalse();
    });
  });

  describe('canSetOutcome (AC-4)', () => {
    it('allows HR to set outcome on an Active or Extended PIP', () => {
      expect(canSetOutcome('Active', true)).toBeTrue();
      expect(canSetOutcome('Extended', true)).toBeTrue();
    });

    it('forbids a manager (non-HR) from setting outcome (BR-1)', () => {
      expect(canSetOutcome('Active', false)).toBeFalse();
    });

    it('forbids setting outcome on a closed PIP', () => {
      expect(canSetOutcome('SuccessfullyCompleted', true)).toBeFalse();
    });
  });

  describe('canEscalate (AC-5)', () => {
    it('allows HR to escalate a NotMet PIP with no prior escalation', () => {
      expect(canEscalate(makePip({ status: 'NotMet' }), true)).toBeTrue();
    });

    it('forbids escalation by a non-HR user', () => {
      expect(canEscalate(makePip({ status: 'NotMet' }), false)).toBeFalse();
    });

    it('forbids escalation when one is already recorded', () => {
      const pip = makePip({
        status: 'NotMet',
        escalation: {
          action: 'Demotion',
          note: null,
          confirmedBy: 'HR',
          confirmedOn: '2026-09-01',
        },
      });
      expect(canEscalate(pip, true)).toBeFalse();
    });

    it('forbids escalation when the PIP is not NotMet', () => {
      expect(canEscalate(makePip({ status: 'Active' }), true)).toBeFalse();
    });
  });

  describe('canRecordCheckpoint (AC-3)', () => {
    const pending: IPipCheckpoint = {
      checkpointId: 'cp-1',
      dueDate: '2026-07-01',
      recordedOn: null,
      status: null,
      notes: null,
      recordedBy: null,
      attachmentName: null,
      overdue: false,
    };

    it('allows recording an unrecorded checkpoint on an active PIP', () => {
      expect(canRecordCheckpoint(makePip(), pending)).toBeTrue();
    });

    it('forbids recording an already-recorded checkpoint', () => {
      expect(
        canRecordCheckpoint(makePip(), {
          ...pending,
          recordedOn: '2026-07-01',
        }),
      ).toBeFalse();
    });

    it('forbids recording on a closed PIP', () => {
      expect(
        canRecordCheckpoint(makePip({ status: 'NotMet' }), pending),
      ).toBeFalse();
    });
  });

  describe('pipIsClosed', () => {
    it('is true for terminal statuses', () => {
      expect(pipIsClosed('SuccessfullyCompleted')).toBeTrue();
      expect(pipIsClosed('NotMet')).toBeTrue();
      expect(pipIsClosed('Cancelled')).toBeTrue();
    });

    it('is false for in-progress statuses', () => {
      expect(pipIsClosed('Active')).toBeFalse();
      expect(pipIsClosed('Extended')).toBeFalse();
    });
  });

  describe('timeline geometry (§8)', () => {
    const pip = makePip({ startDate: '2026-06-01', endDate: '2026-07-01' });

    it('pipTimelineProgress clamps to [0,1]', () => {
      expect(pipTimelineProgress(pip, new Date('2026-05-01'))).toBe(0);
      expect(pipTimelineProgress(pip, new Date('2026-08-01'))).toBe(1);
      expect(pipTimelineProgress(pip, new Date('2026-06-16'))).toBeCloseTo(
        0.5,
        1,
      );
    });

    it('checkpointMarkerPosition places a midpoint checkpoint at ~0.5', () => {
      expect(
        checkpointMarkerPosition(pip, { dueDate: '2026-06-16' }),
      ).toBeCloseTo(0.5, 1);
    });

    it('checkpointMarkerPosition clamps an out-of-range due date', () => {
      expect(checkpointMarkerPosition(pip, { dueDate: '2026-09-01' })).toBe(1);
    });
  });
});
