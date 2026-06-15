import {
  IInterview,
  endTimeLabel,
  groupByDate,
  interviewStatusBadge,
  interviewStatusLabel,
  interviewTypeBadge,
  interviewTypeLabel,
  isPastDateTime,
  nameInitials,
  todayIsoDate,
} from './interview.models';

/** US-REC-005: pure interview-model helpers. */
describe('interview.models helpers', () => {
  const iv = (over: Partial<IInterview> = {}): IInterview => ({
    id: 'iv-1',
    applicantId: 'app-1',
    vacancyId: 'vac-1',
    roundNumber: 1,
    interviewType: 'Video',
    scheduledDate: '2026-07-01',
    startTime: '09:00',
    durationMinutes: 60,
    location: null,
    videoLink: 'https://meet.example.com/x',
    notes: null,
    status: 'Scheduled',
    interviewers: [],
    ...over,
  });

  describe('label / badge maps', () => {
    it('maps interview types to labels', () => {
      expect(interviewTypeLabel('InPerson')).toBe('In-person');
      expect(interviewTypeLabel('Video')).toBe('Video');
      expect(interviewTypeLabel('Phone')).toBe('Phone');
      // Unknown values pass through.
      expect(interviewTypeLabel('zoom' as never)).toBe('zoom');
    });

    it('maps NoShow status to a friendly label', () => {
      expect(interviewStatusLabel('NoShow')).toBe('No-show');
      expect(interviewStatusLabel('Scheduled')).toBe('Scheduled');
    });

    it('color-codes status badges (Scheduled=blue, Completed=green, Cancelled=gray, NoShow=amber)', () => {
      expect(interviewStatusBadge('Scheduled')).toContain('blue');
      expect(interviewStatusBadge('Completed')).toContain('green');
      expect(interviewStatusBadge('Cancelled')).toContain('neutral');
      expect(interviewStatusBadge('NoShow')).toContain('amber');
      // Unknown falls back to the Scheduled style.
      expect(interviewStatusBadge('???')).toBe(interviewStatusBadge('Scheduled'));
    });

    it('tints type badges and falls back for unknown', () => {
      expect(interviewTypeBadge('Video')).toContain('sky');
      expect(interviewTypeBadge('???')).toBe(interviewTypeBadge('InPerson'));
    });
  });

  describe('nameInitials', () => {
    it('takes first + last initials', () => {
      expect(nameInitials('Ada Lovelace')).toBe('AL');
    });
    it('handles a single name', () => {
      expect(nameInitials('Ada')).toBe('A');
    });
    it('handles empty input', () => {
      expect(nameInitials('')).toBe('');
    });
  });

  describe('isPastDateTime (BR-3 / NFR-6)', () => {
    const now = new Date(2026, 5, 15, 12, 0, 0); // 2026-06-15 12:00 local

    it('flags a date+time in the past', () => {
      expect(isPastDateTime('2026-06-15', '11:00', now)).toBeTrue();
      expect(isPastDateTime('2026-06-14', '23:59', now)).toBeTrue();
    });

    it('allows a future date+time', () => {
      expect(isPastDateTime('2026-06-15', '13:00', now)).toBeFalse();
      expect(isPastDateTime('2026-06-20', '08:00', now)).toBeFalse();
    });

    it('treats missing/unparseable input as not-past (defers to server)', () => {
      expect(isPastDateTime('', '09:00', now)).toBeFalse();
      expect(isPastDateTime(null, null, now)).toBeFalse();
      expect(isPastDateTime('not-a-date', '09:00', now)).toBeFalse();
    });
  });

  describe('todayIsoDate', () => {
    it('formats the date as yyyy-MM-dd (local)', () => {
      expect(todayIsoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
    });
  });

  describe('endTimeLabel', () => {
    it('adds the duration to the start time', () => {
      expect(endTimeLabel('09:00', 60)).toBe('10:00');
      expect(endTimeLabel('09:45', 30)).toBe('10:15');
    });
    it('returns empty for an unparseable start', () => {
      expect(endTimeLabel('', 60)).toBe('');
    });
  });

  describe('groupByDate (FR-5 agenda)', () => {
    it('groups by date, sorts groups ascending and interviews by start time', () => {
      const groups = groupByDate([
        iv({ id: 'b', scheduledDate: '2026-07-02', startTime: '11:00' }),
        iv({ id: 'a2', scheduledDate: '2026-07-01', startTime: '14:00' }),
        iv({ id: 'a1', scheduledDate: '2026-07-01', startTime: '09:00' }),
      ]);
      expect(groups.map((g) => g.date)).toEqual(['2026-07-01', '2026-07-02']);
      // First day: sorted by start time -> a1 (09:00) before a2 (14:00).
      expect(groups[0].interviews.map((i) => i.id)).toEqual(['a1', 'a2']);
    });

    it('returns an empty array for no interviews', () => {
      expect(groupByDate([])).toEqual([]);
    });
  });
});
