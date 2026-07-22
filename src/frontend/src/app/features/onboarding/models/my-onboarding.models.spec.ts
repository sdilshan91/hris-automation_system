import {
  IMyOnboardingTask,
  validateUploadFile,
  formatUploadSize,
  isTaskOverdue,
  daysOverdue,
  categoryCompletedCount,
  isEmployeeActionable,
  myTaskStatusLabel,
  ringDashOffset,
  MAX_UPLOAD_BYTES,
} from './my-onboarding.models';

describe('my-onboarding.models helpers (US-ONB-003)', () => {
  const task = (over: Partial<IMyOnboardingTask> = {}): IMyOnboardingTask => ({
    taskInstanceId: 't1',
    title: 'Task',
    description: null,
    category: 'Documentation',
    responsibleRole: 'Employee',
    dueDate: '2026-07-10',
    status: 'pending',
    isMandatory: false,
    requiresDocument: false,
    completedAt: null,
    completedBy: null,
    comment: null,
    attachmentUrl: null,
    ...over,
  });

  function fakeFile(type: string, size: number): File {
    const f = new File(['x'], 'f', { type });
    Object.defineProperty(f, 'size', { value: size });
    return f;
  }

  // ─── validateUploadFile (FR-3 / NFR-6) ──────────────────────
  it('accepts a valid PDF under 10 MB', () => {
    expect(validateUploadFile(fakeFile('application/pdf', 1024))).toBeNull();
  });

  it('rejects an unsupported MIME type', () => {
    expect(validateUploadFile(fakeFile('application/zip', 1024))).toContain('Unsupported');
  });

  it('rejects a file over 10 MB', () => {
    expect(validateUploadFile(fakeFile('application/pdf', MAX_UPLOAD_BYTES + 1))).toContain('10 MB');
  });

  it('rejects an empty file', () => {
    expect(validateUploadFile(fakeFile('application/pdf', 0))).toContain('empty');
  });

  // ─── formatUploadSize ──────────────────────────────────────
  it('formats bytes/KB/MB', () => {
    expect(formatUploadSize(512)).toBe('512 B');
    expect(formatUploadSize(2048)).toBe('2.0 KB');
    expect(formatUploadSize(5 * 1024 * 1024)).toBe('5.0 MB');
  });

  // ─── isTaskOverdue / daysOverdue (AC-5) ────────────────────
  const today = new Date('2026-07-15T12:00:00');

  it('marks a server-stamped overdue task as overdue', () => {
    expect(isTaskOverdue(task({ status: 'overdue' }), today)).toBeTrue();
  });

  it('marks a pending past-due task as overdue and counts days', () => {
    const t = task({ dueDate: '2026-07-10', status: 'pending' });
    expect(isTaskOverdue(t, today)).toBeTrue();
    expect(daysOverdue(t, today)).toBe(5);
  });

  it('does not mark a completed past-due task as overdue', () => {
    expect(isTaskOverdue(task({ dueDate: '2026-07-10', status: 'completed' }), today)).toBeFalse();
  });

  it('does not mark a future-due task as overdue', () => {
    expect(isTaskOverdue(task({ dueDate: '2026-07-20', status: 'pending' }), today)).toBeFalse();
    expect(daysOverdue(task({ dueDate: '2026-07-20' }), today)).toBe(0);
  });

  // DF-60: the today-boundary — a task due *today* is not yet overdue; the day before is 1 day overdue.
  // (The existing arms use 5-days-past / future and skip the exact boundary.)
  it('treats a task due exactly today as not overdue', () => {
    expect(isTaskOverdue(task({ dueDate: '2026-07-15', status: 'pending' }), today)).toBeFalse();
    expect(daysOverdue(task({ dueDate: '2026-07-15' }), today)).toBe(0);
  });

  it('counts a task due yesterday as 1 day overdue (min-1 floor at the boundary)', () => {
    const t = task({ dueDate: '2026-07-14', status: 'pending' });
    expect(isTaskOverdue(t, today)).toBeTrue();
    expect(daysOverdue(t, today)).toBe(1);
  });

  // DF-60: binds the DF-1 (#416) wire-format contract. isTaskOverdue/daysOverdue parse dueDate as
  // `new Date(`${dueDate}T00:00:00`)`, which is correct ONLY for a bare `yyyy-MM-dd` string. If the BE
  // ever reverted onboarding date fields to full-DateTime serialization, that concat would yield an
  // Invalid Date and a genuinely past-due task would silently stop rendering as overdue. This arm pins
  // the contract: the bare date flags overdue; the same date as an ISO datetime string does not.
  it('requires the bare yyyy-MM-dd wire format — a datetime string silently defeats overdue detection', () => {
    const bare = task({ dueDate: '2026-07-10', status: 'pending' });
    expect(isTaskOverdue(bare, today)).toBeTrue();

    const datetime = task({ dueDate: '2026-07-10T10:30:00Z', status: 'pending' });
    expect(isTaskOverdue(datetime, today)).toBeFalse();
    expect(daysOverdue(datetime, today)).toBe(0);
  });

  // ─── categoryCompletedCount ────────────────────────────────
  it('counts completed tasks in a category', () => {
    const cat = {
      category: 'Documentation',
      tasks: [task({ status: 'completed' }), task({ status: 'pending' }), task({ status: 'completed' })],
    };
    expect(categoryCompletedCount(cat)).toBe(2);
  });

  // ─── isEmployeeActionable (BR-1 / FR-7) ────────────────────
  it('is actionable only for an open Employee-role task', () => {
    expect(isEmployeeActionable(task({ responsibleRole: 'Employee', status: 'pending' }))).toBeTrue();
    expect(isEmployeeActionable(task({ responsibleRole: 'IT', status: 'pending' }))).toBeFalse();
    expect(isEmployeeActionable(task({ responsibleRole: 'Employee', status: 'completed' }))).toBeFalse();
  });

  // ─── myTaskStatusLabel ─────────────────────────────────────
  it('labels every status', () => {
    expect(myTaskStatusLabel('pending')).toBe('Pending');
    expect(myTaskStatusLabel('in_progress')).toBe('In progress');
    expect(myTaskStatusLabel('completed')).toBe('Completed');
    expect(myTaskStatusLabel('cancelled')).toBe('Cancelled');
    expect(myTaskStatusLabel('overdue')).toBe('Overdue');
  });

  // ─── ringDashOffset ────────────────────────────────────────
  it('computes dash offset clamped to 0-100', () => {
    const c = 100;
    expect(ringDashOffset(0, c)).toBe(100);
    expect(ringDashOffset(50, c)).toBe(50);
    expect(ringDashOffset(100, c)).toBe(0);
    expect(ringDashOffset(150, c)).toBe(0); // clamped
    expect(ringDashOffset(-10, c)).toBe(100); // clamped
  });
});
