import { PERMISSION_CATALOG, ALL_PERMISSION_KEYS } from './permission-catalog';

describe('PermissionCatalog', () => {
  it('should have permission groups', () => {
    expect(PERMISSION_CATALOG.length).toBeGreaterThan(0);
  });

  it('should have unique permission keys', () => {
    const keys = new Set(ALL_PERMISSION_KEYS);
    expect(keys.size).toBe(ALL_PERMISSION_KEYS.length);
  });

  it('should follow Module.Action pattern', () => {
    for (const key of ALL_PERMISSION_KEYS) {
      const parts = key.split('.');
      expect(parts.length).toBeGreaterThanOrEqual(2);
    }
  });

  it('should have required modules', () => {
    const modules = PERMISSION_CATALOG.map((g) => g.module);
    expect(modules).toContain('Employee');
    expect(modules).toContain('Leave');
    expect(modules).toContain('Attendance');
    expect(modules).toContain('Payroll');
    expect(modules).toContain('Recruitment');
    expect(modules).toContain('Performance');
    expect(modules).toContain('Admin');
  });

  it('should have Admin.Roles.Manage permission', () => {
    expect(ALL_PERMISSION_KEYS).toContain('Admin.Roles.Manage');
  });

  it('each group should have label and permissions', () => {
    for (const group of PERMISSION_CATALOG) {
      expect(group.label).toBeTruthy();
      expect(group.module).toBeTruthy();
      expect(group.permissions.length).toBeGreaterThan(0);
      for (const perm of group.permissions) {
        expect(perm.key).toBeTruthy();
        expect(perm.label).toBeTruthy();
        expect(perm.description).toBeTruthy();
      }
    }
  });
});
