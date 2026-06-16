import {
  DISPUTE_COMMENT_MIN,
  IReviewSignoff,
  SIGNOFF_CONFIRM_MESSAGE,
  buildMeetingNotesTemplate,
  buildSignoffTimeline,
  canSubmitDispute,
  employeeCanRespond,
  notesAreEmpty,
  notesEditable,
  signoffIsLocked,
} from './review-signoff.models';

function makeRecord(overrides: Partial<IReviewSignoff> = {}): IReviewSignoff {
  return {
    reviewId: 'rv-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    managerName: 'Sam Lead',
    status: 'NotesDraft',
    ratingScaleMax: 5,
    finalScore: 87,
    meetingNotesHtml: '',
    managerSignature: null,
    employeeSignature: null,
    disputeComments: null,
    disputedOn: null,
    employeeViewed: false,
    goals: [{ goalId: 'g-1', title: 'Improve NPS', weight: 100, managerRating: 4 }],
    exportAvailable: true,
    ...overrides,
  };
}

describe('review-signoff.models helpers', () => {
  describe('buildMeetingNotesTemplate', () => {
    it('includes all four template sections + goal titles and ratings', () => {
      const html = buildMeetingNotesTemplate(
        [{ goalId: 'g-1', title: 'Improve NPS', weight: 100, managerRating: 4 }],
        5,
      );
      expect(html).toContain('Key strengths');
      expect(html).toContain('Areas for improvement');
      expect(html).toContain('Agreed development actions');
      expect(html).toContain('Overall discussion summary');
      expect(html).toContain('Improve NPS');
      expect(html).toContain('4/5');
    });

    it('escapes goal titles and marks unrated goals', () => {
      const html = buildMeetingNotesTemplate(
        [{ goalId: 'g-1', title: '<b>x</b>', weight: 100, managerRating: null }],
        5,
      );
      expect(html).toContain('&lt;b&gt;x&lt;/b&gt;');
      expect(html).toContain('not rated');
    });
  });

  describe('notesAreEmpty', () => {
    it('treats null / blank / empty markup as empty', () => {
      expect(notesAreEmpty(null)).toBeTrue();
      expect(notesAreEmpty('')).toBeTrue();
      expect(notesAreEmpty('<p></p>')).toBeTrue();
      expect(notesAreEmpty('<br>')).toBeTrue();
      expect(notesAreEmpty('<p>&nbsp;</p>')).toBeTrue();
    });

    it('treats real content as non-empty', () => {
      expect(notesAreEmpty('<p>Discussed performance.</p>')).toBeFalse();
    });
  });

  describe('canSubmitDispute', () => {
    it('requires at least DISPUTE_COMMENT_MIN trimmed chars', () => {
      expect(canSubmitDispute('')).toBeFalse();
      expect(canSubmitDispute('   ')).toBeFalse();
      expect(canSubmitDispute('a'.repeat(DISPUTE_COMMENT_MIN - 1))).toBeFalse();
      expect(canSubmitDispute('a'.repeat(DISPUTE_COMMENT_MIN))).toBeTrue();
    });
  });

  describe('status gates', () => {
    it('notesEditable only in NotesDraft', () => {
      expect(notesEditable('NotesDraft')).toBeTrue();
      expect(notesEditable('PendingEmployeeSignOff')).toBeFalse();
      expect(notesEditable('SignedOff')).toBeFalse();
    });

    it('employeeCanRespond only when pending the employee sign-off', () => {
      expect(employeeCanRespond('PendingEmployeeSignOff')).toBeTrue();
      expect(employeeCanRespond('NotesDraft')).toBeFalse();
      expect(employeeCanRespond('SignedOff')).toBeFalse();
    });

    it('signoffIsLocked for terminal statuses', () => {
      expect(signoffIsLocked('SignedOff')).toBeTrue();
      expect(signoffIsLocked('Completed')).toBeTrue();
      expect(signoffIsLocked('NoResponse')).toBeTrue();
      expect(signoffIsLocked('PendingEmployeeSignOff')).toBeFalse();
    });
  });

  describe('buildSignoffTimeline', () => {
    it('prefers a server-provided timeline', () => {
      const server = [
        { key: 'NotesAdded' as const, label: 'X', occurredOn: null, done: true },
      ];
      const out = buildSignoffTimeline(makeRecord({ timeline: server }));
      expect(out).toBe(server);
    });

    it('derives a signed-off timeline ending in Completed', () => {
      const out = buildSignoffTimeline(
        makeRecord({
          status: 'Completed',
          managerSignature: { name: 'Sam', signedOn: '2026-06-10T00:00:00Z' },
          employeeSignature: { name: 'Alex', signedOn: '2026-06-12T00:00:00Z' },
        }),
      );
      const keys = out.map((s) => s.key);
      expect(keys).toEqual([
        'NotesAdded',
        'SignOffRequested',
        'EmployeeSigned',
        'Completed',
      ]);
      expect(out.every((s) => s.done)).toBeTrue();
    });

    it('derives a Disputed branch instead of EmployeeSigned', () => {
      const out = buildSignoffTimeline(
        makeRecord({
          status: 'Disputed',
          managerSignature: { name: 'Sam', signedOn: '2026-06-10T00:00:00Z' },
          disputeComments: 'Disagree',
          disputedOn: '2026-06-11T00:00:00Z',
        }),
      );
      const disputed = out.find((s) => s.key === 'Disputed');
      expect(disputed).toBeTruthy();
      expect(disputed?.done).toBeTrue();
      expect(out.find((s) => s.key === 'EmployeeSigned')).toBeUndefined();
    });
  });

  it('exposes the verbatim confirmation copy', () => {
    expect(SIGNOFF_CONFIRM_MESSAGE).toBe(
      'By signing, you acknowledge this review has been discussed.',
    );
  });
});
