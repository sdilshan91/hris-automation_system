import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { AttendanceSettingsComponent } from './attendance-settings.component';
import { AttendanceService } from '../../services/attendance.service';
import { IAttendanceSettings } from '../../models/attendance.models';

/**
 * US-ATT-011 AC-3/AC-5 (ISSUE-438) — attendance policy configuration form.
 *
 * `AttendanceService` is mocked (no HttpClient), matching the sibling late-policy spec.
 * The assertions concentrate on what is actually risky about this screen:
 *
 *  1. FULL-REPLACE COMPLETENESS. Saving PUTs the whole policy, so a field the form fails to
 *     carry is not "unchanged" — it is RESET to the DTO default server-side. Several tests
 *     assert on the object handed to `updateAttendanceSettings`, field by field.
 *  2. THE TWO MONEY FIELDS. `fteScaledOvertimeBase` and `weekdayOvertimeMultiplier` decide
 *     what employees are paid, so both must be reachable, editable and round-tripped.
 *  3. NEVER SAVE A POLICY WE NEVER READ. If the GET fails the form holds defaults, and
 *     saving those over a live policy would silently rewrite pay settings.
 */
describe('AttendanceSettingsComponent (US-ATT-011 AC-3/AC-5 / ISSUE-438)', () => {
  let fixture: ComponentFixture<AttendanceSettingsComponent>;
  let component: AttendanceSettingsComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  /** A fully-populated, non-default policy, so a dropped field shows up as a value change. */
  const settings: IAttendanceSettings = {
    locationId: null,
    locationName: null,
    requireGeolocation: true,
    geoFenceEnabled: true,
    geoFenceLatitude: 6.9271,
    geoFenceLongitude: 79.8612,
    geoFenceRadiusMeters: 150,
    geoFenceLocations: [
      { name: 'HQ', latitude: 6.9271, longitude: 79.8612, radiusMeters: 150 },
    ],
    ipAllowlistEnabled: true,
    ipAllowlist: ['203.0.113.7'],
    requirePhoto: true,
    gracePeriodMinutes: 10,
    standardWorkMinutes: 500,
    minimumWorkMinutes: 250,
    autoBreakMinutes: 45,
    autoBreakThresholdMinutes: 300,
    overtimeThresholdMinutes: 15,
    regularizationLookbackDays: 14,
    overtimeMinimumThresholdMinutes: 20,
    weekdayOvertimeMultiplier: 2,
    weekendOvertimeMultiplier: 2.25,
    holidayOvertimeMultiplier: 3,
    maxDailyOvertimeMinutes: 200,
    maxWeeklyOvertimeMinutes: 1000,
    requireOvertimePreApproval: true,
    fteScaledOvertimeBase: true,
    halfDayEnabled: true,
    absenteeismThresholdDays: 2.5,
  };

  function setup(getRes = of(settings)): void {
    attendanceSpy.getAttendanceSettings.and.returnValue(getRes);
    fixture = TestBed.createComponent(AttendanceSettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  /** The policy object handed to the service on the most recent save. */
  function savedPolicy(): IAttendanceSettings {
    return attendanceSpy.updateAttendanceSettings.calls.mostRecent()
      .args[0] as IAttendanceSettings;
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getAttendanceSettings',
      'updateAttendanceSettings',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [AttendanceSettingsComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  // ── Load ──────────────────────────────────────────────────────────────────

  it('loads the tenant policy into the form (AC-3)', () => {
    setup();
    expect(component.isLoading()).toBeFalse();
    expect(component.form.controls.weekdayOvertimeMultiplier.value).toBe(2);
    expect(component.form.controls.fteScaledOvertimeBase.value).toBeTrue();
    expect(component.form.controls.standardWorkMinutes.value).toBe(500);
    expect(component.geoFenceLocations.length).toBe(1);
    expect(component.ipAllowlist.length).toBe(1);
  });

  it('renders a control for every editable policy field', () => {
    setup();
    // The point of ISSUE-438 was that the whole DTO was unreachable, so the crisp check is
    // that each field has a real control in the DOM — not merely a key in the form model.
    const editable = Object.keys(component.form.controls).filter(
      (k) => k !== 'geoFenceLocations' && k !== 'ipAllowlist'
    );
    const missing = editable.filter(
      (k) => !fixture.debugElement.query(By.css(`[data-test="${k}"]`))
    );
    expect(missing)
      .withContext(`policy fields with no control rendered: ${missing.join(', ')}`)
      .toEqual([]);
  });

  it('surfaces both money-affecting settings with explanatory help text, not a bare toggle', () => {
    setup();
    const fte = fixture.debugElement.query(By.css('[data-test="fteScaledOvertimeBase"]'));
    const weekday = fixture.debugElement.query(
      By.css('[data-test="weekdayOvertimeMultiplier"]')
    );
    expect(fte).withContext('FTE-scaled overtime base control (ISSUE-438)').toBeTruthy();
    expect(weekday).withContext('weekday overtime multiplier control (BUG-456)').toBeTruthy();

    // a11y + comprehension: each is described by help text that actually exists in the DOM.
    for (const el of [fte, weekday]) {
      const describedBy = el.nativeElement.getAttribute('aria-describedby');
      expect(describedBy).toBeTruthy();
      const hint = fixture.nativeElement.querySelector(`#${describedBy}`);
      expect(hint).withContext(`aria-describedby="${describedBy}" resolves`).toBeTruthy();
      expect(hint.textContent.trim().length).toBeGreaterThan(40);
    }
  });

  it('labels every rendered control, and marks money fields with text, not colour alone', () => {
    setup();
    const ids = ['fteScaledOvertimeBase', 'weekdayOvertimeMultiplier', 'standardWorkMinutes'];
    ids.forEach((id) => {
      const label = fixture.nativeElement.querySelector(`label[for="${id}"]`);
      expect(label).withContext(`<label for="${id}">`).toBeTruthy();
    });

    // "Affects pay" is a text badge; a colour-only cue would fail WCAG 1.4.1.
    const badges = fixture.nativeElement.querySelectorAll('.badge-money');
    expect(badges.length).toBeGreaterThan(0);
    expect(badges[0].textContent.trim()).toBe('Affects pay');
  });

  // ── Save ──────────────────────────────────────────────────────────────────

  it('saves the complete policy, carrying untouched fields through unchanged (full replace)', () => {
    setup();
    attendanceSpy.updateAttendanceSettings.and.returnValue(of(settings));

    component.form.patchValue({ weekdayOvertimeMultiplier: 1.75 });
    component.onSubmit();

    const sent = savedPolicy();
    expect(sent.weekdayOvertimeMultiplier).toBe(1.75);
    // Everything the admin did NOT touch must be sent at its loaded value. Under full-replace
    // semantics an omitted or defaulted field silently resets that setting server-side.
    expect(sent.weekendOvertimeMultiplier).toBe(2.25);
    expect(sent.holidayOvertimeMultiplier).toBe(3);
    expect(sent.fteScaledOvertimeBase).toBeTrue();
    expect(sent.standardWorkMinutes).toBe(500);
    expect(sent.autoBreakThresholdMinutes).toBe(300);
    expect(sent.regularizationLookbackDays).toBe(14);
    expect(sent.absenteeismThresholdDays).toBe(2.5);
    expect(sent.ipAllowlist).toEqual(['203.0.113.7']);
    expect(sent.geoFenceLocations).toEqual([
      { name: 'HQ', latitude: 6.9271, longitude: 79.8612, radiusMeters: 150 },
    ]);
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('turns the FTE-scaled overtime base off and sends that exact value (AC-5)', () => {
    setup();
    attendanceSpy.updateAttendanceSettings.and.returnValue(
      of({ ...settings, fteScaledOvertimeBase: false })
    );

    component.form.patchValue({ fteScaledOvertimeBase: false });
    component.onSubmit();

    expect(savedPolicy().fteScaledOvertimeBase).toBeFalse();
  });

  it('refuses to save when the policy was never successfully read', () => {
    // Without a GET the form holds DTO defaults. Saving them would overwrite a live policy —
    // including both pay settings — with values the admin never saw.
    setup(throwError(() => new Error('boom')));
    expect(toastrSpy.error).toHaveBeenCalled();

    component.onSubmit();

    expect(attendanceSpy.updateAttendanceSettings).not.toHaveBeenCalled();
  });

  it('blocks a save when a multiplier is outside the range the backend accepts', () => {
    setup();
    // The backend rule is InclusiveBetween(1.0, 10): below 1.0 pays overtime less than
    // regular time. Catching it here turns a 400 into an inline error.
    component.form.patchValue({ weekdayOvertimeMultiplier: 0.5 });
    component.onSubmit();
    expect(attendanceSpy.updateAttendanceSettings).not.toHaveBeenCalled();

    component.form.patchValue({ weekdayOvertimeMultiplier: 11 });
    component.onSubmit();
    expect(attendanceSpy.updateAttendanceSettings).not.toHaveBeenCalled();
  });

  it('blocks a save when minimum work minutes exceed standard work minutes', () => {
    setup();
    // Mirrors the backend's cross-field rule: with minimum > standard every worked day is
    // flagged SHORT_DAY.
    component.form.patchValue({ minimumWorkMinutes: 600, standardWorkMinutes: 480 });
    fixture.detectChanges();

    expect(component.form.hasError('minimumAboveStandard')).toBeTrue();
    component.onSubmit();
    expect(attendanceSpy.updateAttendanceSettings).not.toHaveBeenCalled();
    expect(
      fixture.debugElement.query(By.css('[data-test="minimum-above-standard-error"]'))
    ).toBeTruthy();
  });

  it('reports a failed save and leaves the form editable', () => {
    setup();
    attendanceSpy.updateAttendanceSettings.and.returnValue(throwError(() => new Error('x')));

    component.onSubmit();

    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isSaving()).toBeFalse();
  });

  // ── Geo-fence + IP list editing ───────────────────────────────────────────

  it('adds and removes an allowed clock-in location, and sends the resulting list', () => {
    setup();
    attendanceSpy.updateAttendanceSettings.and.returnValue(of(settings));

    component.addGeofenceLocation();
    component.geoFenceLocations.at(1).patchValue({
      name: 'Branch',
      latitude: 7.29,
      longitude: 80.63,
      radiusMeters: 80,
    });
    component.onSubmit();

    expect(savedPolicy().geoFenceLocations.length).toBe(2);
    expect(savedPolicy().geoFenceLocations[1]).toEqual({
      name: 'Branch',
      latitude: 7.29,
      longitude: 80.63,
      radiusMeters: 80,
    });

    component.removeGeofenceLocation(1);
    component.onSubmit();
    expect(savedPolicy().geoFenceLocations.length).toBe(1);
  });

  it('sends a CLEARED allowlist as an empty array rather than dropping the field', () => {
    setup();
    attendanceSpy.updateAttendanceSettings.and.returnValue(of(settings));

    component.removeIpEntry(0);
    component.onSubmit();

    // Omitting the key would ALSO clear the list server-side, but by accident. Sending [] is
    // the admin's explicit instruction, and keeps the payload complete.
    const sent = savedPolicy();
    expect(sent.ipAllowlist).toEqual([]);
    expect('ipAllowlist' in sent).toBeTrue();
  });

  it('blocks a save while an incomplete allowed-location row is present', () => {
    setup();
    component.addGeofenceLocation();
    component.onSubmit();
    expect(attendanceSpy.updateAttendanceSettings).not.toHaveBeenCalled();
  });

  it('only requires a geo-fence radius while the fence is enabled', () => {
    setup();
    const radius = component.form.controls.geoFenceRadiusMeters;

    component.form.controls.geoFenceEnabled.setValue(false);
    radius.setValue(0);
    expect(radius.valid).withContext('radius of 0 is fine with the fence off').toBeTrue();

    component.form.controls.geoFenceEnabled.setValue(true);
    expect(radius.valid).withContext('radius of 0 is invalid with the fence on').toBeFalse();
  });

  // ── Full-replace disclosure ───────────────────────────────────────────────

  it('tells the admin that saving replaces the whole policy', () => {
    setup();
    const notice = fixture.debugElement.query(By.css('[data-test="full-replace-notice"]'));
    expect(notice)
      .withContext('the full-replace contract must be visible, not just documented')
      .toBeTruthy();
  });
});
