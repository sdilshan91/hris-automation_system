import {
  slugifyFieldKey,
  fieldTypeHasOptions,
  fieldTypeToInputType,
  fieldTypeIcon,
  CustomFieldType,
} from './custom-field.models';

/**
 * US-CHR-012: Pure function tests for custom field models.
 * Separate describe block -- no TestBed, no httpMock.verify().
 */
describe('Custom field model utilities', () => {
  describe('slugifyFieldKey', () => {
    it('should convert a simple name to a slug', () => {
      expect(slugifyFieldKey('T-Shirt Size')).toBe('t_shirt_size');
    });

    it('should handle hyphens by converting to underscores', () => {
      expect(slugifyFieldKey('T-Shirt')).toBe('t_shirt');
    });

    it('should handle leading/trailing whitespace', () => {
      expect(slugifyFieldKey('  Hello World  ')).toBe('hello_world');
    });

    it('should collapse consecutive special chars', () => {
      expect(slugifyFieldKey('Employee  ID #')).toBe('employee_id');
    });

    it('should handle empty string', () => {
      expect(slugifyFieldKey('')).toBe('');
    });

    it('should handle only special characters', () => {
      expect(slugifyFieldKey('---')).toBe('');
    });

    it('should handle already valid keys', () => {
      expect(slugifyFieldKey('my_field')).toBe('my_field');
    });

    it('should handle numbers', () => {
      expect(slugifyFieldKey('Field 123')).toBe('field_123');
    });
  });

  describe('fieldTypeHasOptions', () => {
    it('should return true for dropdown', () => {
      expect(fieldTypeHasOptions('dropdown')).toBeTrue();
    });

    it('should return true for multi_select', () => {
      expect(fieldTypeHasOptions('multi_select')).toBeTrue();
    });

    it('should return false for text', () => {
      expect(fieldTypeHasOptions('text')).toBeFalse();
    });

    it('should return false for number', () => {
      expect(fieldTypeHasOptions('number')).toBeFalse();
    });

    it('should return false for all non-option types', () => {
      const nonOptionTypes: CustomFieldType[] = ['text', 'textarea', 'number', 'date', 'checkbox', 'email', 'phone', 'url'];
      for (const t of nonOptionTypes) {
        expect(fieldTypeHasOptions(t)).toBeFalse();
      }
    });
  });

  describe('fieldTypeToInputType', () => {
    it('should map text to text', () => {
      expect(fieldTypeToInputType('text')).toBe('text');
    });

    it('should map textarea to textarea', () => {
      expect(fieldTypeToInputType('textarea')).toBe('textarea');
    });

    it('should map number to number', () => {
      expect(fieldTypeToInputType('number')).toBe('number');
    });

    it('should map date to date', () => {
      expect(fieldTypeToInputType('date')).toBe('date');
    });

    it('should map email to email', () => {
      expect(fieldTypeToInputType('email')).toBe('email');
    });

    it('should map phone to tel', () => {
      expect(fieldTypeToInputType('phone')).toBe('tel');
    });

    it('should map url to url', () => {
      expect(fieldTypeToInputType('url')).toBe('url');
    });

    it('should map checkbox to checkbox', () => {
      expect(fieldTypeToInputType('checkbox')).toBe('checkbox');
    });

    it('should map dropdown to select', () => {
      expect(fieldTypeToInputType('dropdown')).toBe('select');
    });

    it('should map multi_select to multi-select', () => {
      expect(fieldTypeToInputType('multi_select')).toBe('multi-select');
    });
  });

  describe('fieldTypeIcon', () => {
    it('should return Aa for text', () => {
      expect(fieldTypeIcon('text')).toBe('Aa');
    });

    it('should return # for number', () => {
      expect(fieldTypeIcon('number')).toBe('#');
    });

    it('should return @ for email', () => {
      expect(fieldTypeIcon('email')).toBe('@');
    });
  });
});
