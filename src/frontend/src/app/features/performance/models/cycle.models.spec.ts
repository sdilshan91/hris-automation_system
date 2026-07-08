import {
  CYCLE_STATUS_BADGE,
  CycleStatus,
  IPhaseDates,
  IPhaseStat,
  allowedTransitions,
  phaseCompletionPercent,
  phasesAreValid,
  progressFillClass,
  validatePhaseSequencing,
} from './cycle.models';

describe('cycle.models pure helpers', () => {
  describe('validatePhaseSequencing (FR-2 / BR-3)', () => {
    const cycleStart = '2026-01-01';
    const cycleEnd = '2026-12-31';

    const validPhases: IPhaseDates[] = [
      { phaseType: 'GoalSetting', startDate: '2026-01-05', endDate: '2026-01-20' },
      { phaseType: 'SelfAssessment', startDate: '2026-01-25', endDate: '2026-02-10' },
      { phaseType: 'ManagerReview', startDate: '2026-02-15', endDate: '2026-03-01' },
      { phaseType: 'Publish', startDate: '2026-03-05', endDate: '2026-03-10' },
    ];

    it('accepts sequential, non-overlapping, in-window phases', () => {
      expect(validatePhaseSequencing(validPhases, cycleStart, cycleEnd)).toEqual(
        [],
      );
      expect(phasesAreValid(validPhases, cycleStart, cycleEnd)).toBeTrue();
    });

    it('is order-independent (sorts into canonical phase order)', () => {
      const shuffled = [...validPhases].reverse();
      expect(validatePhaseSequencing(shuffled, cycleStart, cycleEnd)).toEqual([]);
    });

    it('rejects overlapping phases', () => {
      const overlap: IPhaseDates[] = [
        { phaseType: 'GoalSetting', startDate: '2026-01-05', endDate: '2026-02-01' },
        // starts before goal-setting ends → overlap
        {
          phaseType: 'SelfAssessment',
          startDate: '2026-01-20',
          endDate: '2026-02-10',
        },
      ];
      const errors = validatePhaseSequencing(overlap, cycleStart, cycleEnd);
      expect(errors.length).toBeGreaterThan(0);
      expect(errors.some((e) => e.includes('cannot overlap'))).toBeTrue();
      expect(phasesAreValid(overlap, cycleStart, cycleEnd)).toBeFalse();
    });

    it('rejects a phase that starts before the cycle window', () => {
      const outOfRange: IPhaseDates[] = [
        { phaseType: 'GoalSetting', startDate: '2025-12-20', endDate: '2026-01-10' },
      ];
      const errors = validatePhaseSequencing(outOfRange, cycleStart, cycleEnd);
      expect(errors.some((e) => e.includes('within the cycle window'))).toBeTrue();
    });

    it('rejects a phase that ends after the cycle window', () => {
      const outOfRange: IPhaseDates[] = [
        { phaseType: 'GoalSetting', startDate: '2026-12-20', endDate: '2027-01-10' },
      ];
      const errors = validatePhaseSequencing(outOfRange, cycleStart, cycleEnd);
      expect(errors.some((e) => e.includes('within the cycle window'))).toBeTrue();
    });

    it('rejects a reversed phase range (start after end)', () => {
      const reversed: IPhaseDates[] = [
        { phaseType: 'GoalSetting', startDate: '2026-02-01', endDate: '2026-01-01' },
      ];
      const errors = validatePhaseSequencing(reversed, cycleStart, cycleEnd);
      expect(
        errors.some((e) => e.includes('end date must be on or after')),
      ).toBeTrue();
    });

    it('rejects a missing phase date', () => {
      const missing: IPhaseDates[] = [
        { phaseType: 'GoalSetting', startDate: '', endDate: '2026-01-10' },
      ];
      const errors = validatePhaseSequencing(missing, cycleStart, cycleEnd);
      expect(
        errors.some((e) => e.includes('both start and end dates are required')),
      ).toBeTrue();
    });

    it('flags a reversed cycle window itself', () => {
      const errors = validatePhaseSequencing(validPhases, cycleEnd, cycleStart);
      expect(
        errors.some((e) => e.includes('on or after the start date')),
      ).toBeTrue();
    });
  });

  describe('allowedTransitions (FR-7 status machine)', () => {
    it('Draft → Activate, Cancel', () => {
      expect(allowedTransitions('Draft')).toEqual(['Activate', 'Cancel']);
    });
    it('Active → Pause, Complete, Cancel', () => {
      expect(allowedTransitions('Active')).toEqual([
        'Pause',
        'Complete',
        'Cancel',
      ]);
    });
    it('Paused → Resume, Complete, Cancel', () => {
      expect(allowedTransitions('Paused')).toEqual([
        'Resume',
        'Complete',
        'Cancel',
      ]);
    });
    it('Completed and Cancelled are terminal', () => {
      expect(allowedTransitions('Completed')).toEqual([]);
      expect(allowedTransitions('Cancelled')).toEqual([]);
    });
  });

  describe('status badge mapping (§8)', () => {
    it('maps every status to a distinct color class', () => {
      const statuses: CycleStatus[] = [
        'Draft',
        'Active',
        'Paused',
        'Completed',
        'Cancelled',
      ];
      for (const s of statuses) {
        expect(CYCLE_STATUS_BADGE[s]).toBeTruthy();
      }
      // §8 spec colors: green Active, amber Paused, blue Completed, red Cancelled.
      expect(CYCLE_STATUS_BADGE.Active).toContain('emerald');
      expect(CYCLE_STATUS_BADGE.Paused).toContain('amber');
      expect(CYCLE_STATUS_BADGE.Completed).toContain('sky');
      expect(CYCLE_STATUS_BADGE.Cancelled).toContain('rose');
      expect(CYCLE_STATUS_BADGE.Draft).toContain('neutral');
    });
  });

  describe('phaseCompletionPercent (AC-3)', () => {
    const stat = (
      completed: number,
      total: number,
    ): Pick<IPhaseStat, 'completedCount' | 'totalCount'> => ({
      completedCount: completed,
      totalCount: total,
    });

    it('returns a rounded percentage', () => {
      expect(phaseCompletionPercent(stat(1, 3))).toBe(33);
      expect(phaseCompletionPercent(stat(2, 4))).toBe(50);
      expect(phaseCompletionPercent(stat(4, 4))).toBe(100);
    });
    it('returns 0 when there are no participants', () => {
      expect(phaseCompletionPercent(stat(0, 0))).toBe(0);
    });
    it('clamps to 0-100', () => {
      expect(phaseCompletionPercent(stat(5, 4))).toBe(100);
    });
  });

  describe('progressFillClass (§8)', () => {
    it('greens at 100, blues at >=50, ambers above 0, gray at 0', () => {
      expect(progressFillClass(100)).toContain('emerald');
      expect(progressFillClass(60)).toContain('sky');
      expect(progressFillClass(10)).toContain('amber');
      expect(progressFillClass(0)).toContain('neutral');
    });
  });
});
