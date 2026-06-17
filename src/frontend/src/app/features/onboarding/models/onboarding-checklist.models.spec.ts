import {
  IChecklistTask,
  countResponsibleParties,
  notificationToast,
  sortTasksByDueDate,
} from './onboarding-checklist.models';

describe('onboarding-checklist.models helpers', () => {
  const task = (over: Partial<IChecklistTask> = {}): IChecklistTask => ({
    title: 'Task',
    dueOffsetDays: 0,
    dueDate: '2026-07-01',
    status: 'pending',
    isMandatory: false,
    sortOrder: 0,
    ...over,
  });

  describe('sortTasksByDueDate', () => {
    it('sorts ascending by dueDate, then sortOrder', () => {
      const input = [
        task({ title: 'C', dueDate: '2026-07-05', sortOrder: 2 }),
        task({ title: 'A', dueDate: '2026-07-01', sortOrder: 0 }),
        task({ title: 'B', dueDate: '2026-07-01', sortOrder: 1 }),
      ];
      expect(sortTasksByDueDate(input).map((t) => t.title)).toEqual([
        'A',
        'B',
        'C',
      ]);
    });

    it('does not mutate the input array', () => {
      const input = [task({ dueDate: '2026-07-05' }), task({ dueDate: '2026-07-01' })];
      const copy = [...input];
      sortTasksByDueDate(input);
      expect(input).toEqual(copy);
    });
  });

  describe('countResponsibleParties', () => {
    it('counts distinct roles', () => {
      const tasks = [
        task({ responsibleRole: 'HR' }),
        task({ responsibleRole: 'IT' }),
        task({ responsibleRole: 'HR' }),
      ];
      expect(countResponsibleParties(tasks)).toBe(2);
    });

    it('keys named users separately from roles', () => {
      const tasks = [
        task({ responsibleRole: 'Custom', responsibleUserId: 'u1' }),
        task({ responsibleRole: 'Custom', responsibleUserId: 'u2' }),
        task({ responsibleRole: 'HR' }),
      ];
      expect(countResponsibleParties(tasks)).toBe(3);
    });

    it('ignores tasks with no responsible party', () => {
      expect(countResponsibleParties([task(), task()])).toBe(0);
    });
  });

  describe('notificationToast', () => {
    it('uses the plural form for zero or many', () => {
      expect(notificationToast(0)).toContain('0 people');
      expect(notificationToast(5)).toContain('5 people');
    });

    it('uses the singular form for exactly one', () => {
      expect(notificationToast(1)).toBe(
        'Onboarding checklist assigned. Notifications sent to 1 person.',
      );
    });
  });
});
