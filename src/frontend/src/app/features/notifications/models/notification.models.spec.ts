import {
  badgeText,
  notificationRoute,
  notificationVisual,
  relativeTime,
} from './notification.models';

describe('notification.models helpers', () => {
  describe('badgeText (FR-5)', () => {
    it('returns "" for zero/negative', () => {
      expect(badgeText(0)).toBe('');
      expect(badgeText(-3)).toBe('');
    });
    it('returns the number for 1..99', () => {
      expect(badgeText(1)).toBe('1');
      expect(badgeText(99)).toBe('99');
    });
    it('caps at "99+" above 99', () => {
      expect(badgeText(100)).toBe('99+');
      expect(badgeText(5000)).toBe('99+');
    });
  });

  describe('notificationVisual (§8 colour coding)', () => {
    it('maps approvals/completions to green check', () => {
      expect(notificationVisual('leave_approved')).toEqual({
        icon: 'check_circle',
        tone: 'green',
      });
      expect(notificationVisual('payroll_completed').tone).toBe('green');
    });
    it('maps rejections/failures to red', () => {
      expect(notificationVisual('leave_rejected').tone).toBe('red');
    });
    it('maps reminders to amber', () => {
      expect(notificationVisual('overdue_reminder').tone).toBe('amber');
    });
    it('maps tasks/onboarding to purple', () => {
      expect(notificationVisual('task_assigned').tone).toBe('purple');
    });
    it('falls back to neutral notifications icon for unknown types', () => {
      expect(notificationVisual('something_unknown')).toEqual({
        icon: 'notifications',
        tone: 'neutral',
      });
    });
  });

  describe('notificationRoute (FR-8)', () => {
    it('returns null when there is no resource type', () => {
      expect(notificationRoute(null, null)).toBeNull();
    });
    it('routes a LeaveRequest to the leave detail (AC-4)', () => {
      expect(notificationRoute('LeaveRequest', 'lr-9')).toEqual([
        '/leave',
        'requests',
        'lr-9',
      ]);
    });
    it('routes a payslip to my-payslips', () => {
      expect(notificationRoute('Payslip', 'p1')).toEqual(['/my-payslips']);
    });
    it('routes an onboarding task to the checklist', () => {
      expect(notificationRoute('OnboardingTask', 't1')).toEqual([
        '/onboarding',
        'my-checklist',
      ]);
    });
    it('falls back to the notifications list for unknown types', () => {
      expect(notificationRoute('Mystery', 'x')).toEqual(['/notifications']);
    });
  });

  describe('relativeTime (§8)', () => {
    const now = new Date('2026-06-17T12:00:00Z').getTime();
    it('says "just now" within 45s', () => {
      const t = new Date(now - 10_000).toISOString();
      expect(relativeTime(t, now)).toBe('just now');
    });
    it('formats minutes', () => {
      const t = new Date(now - 2 * 60_000).toISOString();
      expect(relativeTime(t, now)).toBe('2 min ago');
    });
    it('formats hours', () => {
      const t = new Date(now - 3 * 3_600_000).toISOString();
      expect(relativeTime(t, now)).toBe('3 hr ago');
    });
    it('formats single and plural days', () => {
      expect(relativeTime(new Date(now - 24 * 3_600_000).toISOString(), now)).toBe(
        '1 day ago',
      );
      expect(
        relativeTime(new Date(now - 3 * 24 * 3_600_000).toISOString(), now),
      ).toBe('3 days ago');
    });
    it('returns "" for an invalid date', () => {
      expect(relativeTime('not-a-date', now)).toBe('');
    });
  });
});
