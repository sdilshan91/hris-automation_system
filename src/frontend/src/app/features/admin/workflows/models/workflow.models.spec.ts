import {
  IWorkflowSummary,
  approverNeedsIdentifier,
  groupByEntityType,
  parseCondition,
  serializeCondition,
} from './workflow.models';

describe('workflow.models helpers', () => {
  describe('serializeCondition', () => {
    it('serializes a numeric value as a number', () => {
      expect(
        serializeCondition({
          field: 'days_requested',
          operator: '>',
          value: '5',
        })
      ).toBe(
        JSON.stringify({ field: 'days_requested', operator: '>', value: 5 })
      );
    });

    it('keeps a non-numeric value as a string', () => {
      expect(
        serializeCondition({ field: 'type', operator: '==', value: 'Sick' })
      ).toBe(JSON.stringify({ field: 'type', operator: '==', value: 'Sick' }));
    });

    it('returns null for an empty or missing field', () => {
      expect(
        serializeCondition({ field: '', operator: '>', value: 1 })
      ).toBeNull();
      expect(serializeCondition(null)).toBeNull();
    });
  });

  describe('parseCondition', () => {
    it('round-trips a serialized condition', () => {
      const json = serializeCondition({
        field: 'days_requested',
        operator: '>=',
        value: '3',
      });
      expect(parseCondition(json)).toEqual({
        field: 'days_requested',
        operator: '>=',
        value: 3,
      });
    });

    it('returns null for null/invalid json', () => {
      expect(parseCondition(null)).toBeNull();
      expect(parseCondition('not json')).toBeNull();
      expect(parseCondition('{}')).toBeNull();
    });
  });

  describe('approverNeedsIdentifier', () => {
    it('is true for Role and NamedUser only', () => {
      expect(approverNeedsIdentifier('Role')).toBeTrue();
      expect(approverNeedsIdentifier('NamedUser')).toBeTrue();
      expect(approverNeedsIdentifier('LineManager')).toBeFalse();
      expect(approverNeedsIdentifier('DepartmentHead')).toBeFalse();
    });
  });

  describe('groupByEntityType', () => {
    const make = (id: string, et: IWorkflowSummary['entityType']): IWorkflowSummary => ({
      id,
      name: id,
      entityType: et,
      version: 1,
      status: 'Active',
      isDefault: false,
      stepCount: 1,
      updatedAt: '2026-06-01T00:00:00Z',
    });

    it('groups by entity type in display order and omits empty groups', () => {
      const groups = groupByEntityType([
        make('a', 'Expense'),
        make('b', 'Leave'),
        make('c', 'Leave'),
      ]);
      expect(groups.length).toBe(2);
      // Leave precedes Expense in display order
      expect(groups[0].entityType).toBe('Leave');
      expect(groups[0].workflows.length).toBe(2);
      expect(groups[1].entityType).toBe('Expense');
    });

    it('returns an empty array when there are no workflows', () => {
      expect(groupByEntityType([])).toEqual([]);
    });
  });
});
