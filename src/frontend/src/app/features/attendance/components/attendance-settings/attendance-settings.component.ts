import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AbstractControl,
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import {
  IAttendanceSettings,
  IGeofenceLocation,
  ATTENDANCE_SETTINGS_DEFAULTS,
} from '../../models/attendance.models';

/** One allowed clock-in location row (DF-23 multi-location geofence). */
type GeofenceLocationGroup = FormGroup<{
  name: FormControl<string>;
  latitude: FormControl<number | null>;
  longitude: FormControl<number | null>;
  radiusMeters: FormControl<number | null>;
}>;

/**
 * US-ATT-002 BR-3/BR-4 read together: a full day cannot be shorter than the minimum that
 * qualifies as one, or every worked day is flagged SHORT_DAY. Mirrors the backend's
 * `MinimumWorkMinutes <= StandardWorkMinutes` rule so the admin sees it before the 400.
 */
export function minimumNotAboveStandardValidator(
  group: AbstractControl,
): ValidationErrors | null {
  const min = group.get('minimumWorkMinutes')?.value;
  const std = group.get('standardWorkMinutes')?.value;
  if (typeof min !== 'number' || typeof std !== 'number') {
    return null;
  }
  return min > std ? { minimumAboveStandard: true } : null;
}

/**
 * US-ATT-011 AC-3/AC-5 (ISSUE-438) — tenant attendance policy configuration.
 *
 * Before this screen existed the ENTIRE `AttendanceSettingsDto` was unreachable from the
 * product: it had a complete backend read/write path and no Angular service, route or form,
 * so a tenant admin could only change policy by hand-crafting an API call. Two of the fields
 * here decide what employees are PAID (`weekdayOvertimeMultiplier`, `fteScaledOvertimeBase`),
 * which is why they carry explanatory help text rather than a bare toggle.
 *
 * ⚠ SAVING IS A FULL REPLACE of the tenant policy (BUG-117 class). The form is therefore
 * hydrated from a GET and submitted whole — never partially — and the banner tells the admin
 * so. `toAttendanceSettingsWire` enforces the "whole object" part at compile time.
 *
 * Access: the route carries `permissionGuard(['Attendance.ConfigurePolicy'])`, the same
 * permission the controller requires, so nav visibility and route access cannot drift from
 * the backend's answer (BUG-493 / ISSUE-210).
 */
@Component({
  selector: 'app-attendance-settings',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <div class="mb-6">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
          Attendance Policy
        </h1>
        <p class="text-sm text-neutral-500 mt-1">
          The tenant-wide rules for clock-in enforcement, work-hour calculation, overtime pay
          and absenteeism reporting.
        </p>
      </div>

      @if (isLoading()) {
        <div class="card-notion space-y-4" aria-busy="true" data-test="skeleton">
          @for (_ of [1, 2, 3, 4, 5, 6]; track $index) {
            <div class="skeleton-line h-12 w-full"></div>
          }
        </div>
      } @else {
        <!-- Full-replace contract: the admin is editing the WHOLE policy, not a patch. -->
        <div class="notice" role="note" data-test="full-replace-notice">
          <span class="notice-tag">Saves everything</span>
          <p>
            Saving replaces the complete attendance policy with what is on this page. Every
            setting below is submitted together, so review the whole form before saving.
          </p>
        </div>

        <form
          class="space-y-5 mt-5"
          [formGroup]="form"
          (ngSubmit)="onSubmit()"
          novalidate
          @fadeIn
        >
          <!-- ══ Clock-in enforcement ══════════════════════════════════ -->
          <section class="card-notion" aria-labelledby="sec-clockin">
            <h2 id="sec-clockin" class="section-title">Clock-in enforcement</h2>
            <p class="section-hint">
              What an employee must provide before a punch is accepted.
            </p>

            <div class="space-y-5 mt-5">
              <div class="toggle-row">
                <div>
                  <label class="field-label" for="requireGeolocation">
                    Require geolocation
                  </label>
                  <p class="field-hint" id="requireGeolocation-hint">
                    Latitude and longitude must be supplied on every clock-in.
                  </p>
                </div>
                <label class="toggle">
                  <input
                    id="requireGeolocation"
                    type="checkbox"
                    formControlName="requireGeolocation"
                    aria-describedby="requireGeolocation-hint"
                    data-test="requireGeolocation"
                  />
                  <span class="toggle-track"><span class="toggle-thumb"></span></span>
                </label>
              </div>

              <div class="toggle-row">
                <div>
                  <label class="field-label" for="requirePhoto">Require selfie photo</label>
                  <p class="field-hint" id="requirePhoto-hint">
                    A photo must be captured before the clock-in is accepted.
                  </p>
                </div>
                <label class="toggle">
                  <input
                    id="requirePhoto"
                    type="checkbox"
                    formControlName="requirePhoto"
                    aria-describedby="requirePhoto-hint"
                    data-test="requirePhoto"
                  />
                  <span class="toggle-track"><span class="toggle-thumb"></span></span>
                </label>
              </div>

              <div class="max-w-xs">
                <label class="field-label" for="gracePeriodMinutes">
                  Grace period (minutes)
                </label>
                <p class="field-hint" id="gracePeriodMinutes-hint">
                  Minutes after shift start within which a clock-in is not marked late.
                </p>
                <input
                  id="gracePeriodMinutes"
                  type="number"
                  min="0"
                  step="1"
                  class="field"
                  formControlName="gracePeriodMinutes"
                  aria-describedby="gracePeriodMinutes-hint"
                  [attr.aria-invalid]="showError('gracePeriodMinutes') ? 'true' : null"
                  data-test="gracePeriodMinutes"
                />
                @if (showError('gracePeriodMinutes')) {
                  <p class="field-error" role="alert" data-test="gracePeriodMinutes-error">
                    Enter 0 or more minutes.
                  </p>
                }
              </div>
            </div>
          </section>

          <!-- ══ Geo-fence ═════════════════════════════════════════════ -->
          <section class="card-notion" aria-labelledby="sec-geofence">
            <h2 id="sec-geofence" class="section-title">Geo-fence</h2>
            <p class="section-hint">
              Restrict clock-in to approved coordinates. When allowed locations are listed
              below, a punch passes if it falls inside any one of them; otherwise the single
              centre point is used.
            </p>

            <div class="space-y-5 mt-5">
              <div class="toggle-row">
                <div>
                  <label class="field-label" for="geoFenceEnabled">Enforce geo-fence</label>
                  <p class="field-hint" id="geoFenceEnabled-hint">
                    Validate the supplied coordinates against the allowed location(s).
                  </p>
                </div>
                <label class="toggle">
                  <input
                    id="geoFenceEnabled"
                    type="checkbox"
                    formControlName="geoFenceEnabled"
                    aria-describedby="geoFenceEnabled-hint"
                    data-test="geoFenceEnabled"
                  />
                  <span class="toggle-track"><span class="toggle-thumb"></span></span>
                </label>
              </div>

              <div class="grid grid-cols-1 sm:grid-cols-3 gap-5">
                <div>
                  <label class="field-label" for="geoFenceLatitude">Centre latitude</label>
                  <p class="field-hint" id="geoFenceLatitude-hint">Between -90 and 90.</p>
                  <input
                    id="geoFenceLatitude"
                    type="number"
                    step="any"
                    class="field"
                    formControlName="geoFenceLatitude"
                    aria-describedby="geoFenceLatitude-hint"
                    [attr.aria-invalid]="showError('geoFenceLatitude') ? 'true' : null"
                    data-test="geoFenceLatitude"
                  />
                  @if (showError('geoFenceLatitude')) {
                    <p class="field-error" role="alert" data-test="geoFenceLatitude-error">
                      Latitude must be between -90 and 90.
                    </p>
                  }
                </div>

                <div>
                  <label class="field-label" for="geoFenceLongitude">Centre longitude</label>
                  <p class="field-hint" id="geoFenceLongitude-hint">Between -180 and 180.</p>
                  <input
                    id="geoFenceLongitude"
                    type="number"
                    step="any"
                    class="field"
                    formControlName="geoFenceLongitude"
                    aria-describedby="geoFenceLongitude-hint"
                    [attr.aria-invalid]="showError('geoFenceLongitude') ? 'true' : null"
                    data-test="geoFenceLongitude"
                  />
                  @if (showError('geoFenceLongitude')) {
                    <p class="field-error" role="alert" data-test="geoFenceLongitude-error">
                      Longitude must be between -180 and 180.
                    </p>
                  }
                </div>

                <div>
                  <label class="field-label" for="geoFenceRadiusMeters">Radius (metres)</label>
                  <p class="field-hint" id="geoFenceRadiusMeters-hint">
                    Permitted distance from the centre point.
                  </p>
                  <input
                    id="geoFenceRadiusMeters"
                    type="number"
                    min="1"
                    step="1"
                    class="field"
                    formControlName="geoFenceRadiusMeters"
                    aria-describedby="geoFenceRadiusMeters-hint"
                    [attr.aria-invalid]="showError('geoFenceRadiusMeters') ? 'true' : null"
                    data-test="geoFenceRadiusMeters"
                  />
                  @if (showError('geoFenceRadiusMeters')) {
                    <p class="field-error" role="alert" data-test="geoFenceRadiusMeters-error">
                      Radius must be at least 1 metre while the geo-fence is enabled.
                    </p>
                  }
                </div>
              </div>

              <!-- Allowed locations (DF-23 / ISSUE-068) -->
              <div class="border-t border-neutral-100 pt-5">
                <div class="flex items-center justify-between gap-4">
                  <div>
                    <h3 class="field-label">Allowed clock-in locations</h3>
                    <p class="field-hint">
                      Optional. Listing locations here replaces the single centre point above.
                    </p>
                  </div>
                  <button
                    type="button"
                    class="btn-secondary text-sm"
                    (click)="addGeofenceLocation()"
                    data-test="add-location"
                  >
                    Add location
                  </button>
                </div>

                @if (geoFenceLocations.length === 0) {
                  <p class="empty-hint" data-test="no-locations">
                    No allowed locations. The single centre point above is used.
                  </p>
                } @else {
                  <ul class="space-y-4 mt-4" formArrayName="geoFenceLocations">
                    @for (row of geoFenceLocations.controls; track $index) {
                      <li
                        class="row-card"
                        [formGroupName]="$index"
                        [attr.data-test]="'location-' + $index"
                      >
                        <div class="grid grid-cols-1 sm:grid-cols-4 gap-4">
                          <div class="sm:col-span-2">
                            <label class="field-label" [attr.for]="'loc-name-' + $index">
                              Name
                            </label>
                            <input
                              [id]="'loc-name-' + $index"
                              type="text"
                              maxlength="100"
                              class="field"
                              formControlName="name"
                              [attr.data-test]="'location-name-' + $index"
                            />
                            @if (showRowError($index, 'name')) {
                              <p class="field-error" role="alert">
                                A name is required (max 100 characters).
                              </p>
                            }
                          </div>
                          <div>
                            <label class="field-label" [attr.for]="'loc-lat-' + $index">
                              Latitude
                            </label>
                            <input
                              [id]="'loc-lat-' + $index"
                              type="number"
                              step="any"
                              class="field"
                              formControlName="latitude"
                              [attr.data-test]="'location-latitude-' + $index"
                            />
                            @if (showRowError($index, 'latitude')) {
                              <p class="field-error" role="alert">Must be between -90 and 90.</p>
                            }
                          </div>
                          <div>
                            <label class="field-label" [attr.for]="'loc-lng-' + $index">
                              Longitude
                            </label>
                            <input
                              [id]="'loc-lng-' + $index"
                              type="number"
                              step="any"
                              class="field"
                              formControlName="longitude"
                              [attr.data-test]="'location-longitude-' + $index"
                            />
                            @if (showRowError($index, 'longitude')) {
                              <p class="field-error" role="alert">
                                Must be between -180 and 180.
                              </p>
                            }
                          </div>
                        </div>
                        <div class="flex items-end justify-between gap-4 mt-4">
                          <div class="max-w-[12rem]">
                            <label class="field-label" [attr.for]="'loc-radius-' + $index">
                              Radius (metres)
                            </label>
                            <input
                              [id]="'loc-radius-' + $index"
                              type="number"
                              min="1"
                              step="1"
                              class="field"
                              formControlName="radiusMeters"
                              [attr.data-test]="'location-radius-' + $index"
                            />
                            @if (showRowError($index, 'radiusMeters')) {
                              <p class="field-error" role="alert">
                                Radius must be greater than 0.
                              </p>
                            }
                          </div>
                          <button
                            type="button"
                            class="btn-danger text-sm"
                            (click)="removeGeofenceLocation($index)"
                            [attr.aria-label]="'Remove allowed location ' + ($index + 1)"
                            [attr.data-test]="'remove-location-' + $index"
                          >
                            Remove
                          </button>
                        </div>
                      </li>
                    }
                  </ul>
                }
              </div>
            </div>
          </section>

          <!-- ══ IP allowlist ══════════════════════════════════════════ -->
          <section class="card-notion" aria-labelledby="sec-ip">
            <h2 id="sec-ip" class="section-title">IP allowlist</h2>
            <p class="section-hint">
              Restrict clock-in to known networks. Each entry is an exact IP address or a CIDR
              range.
            </p>

            <div class="space-y-5 mt-5">
              <div class="toggle-row">
                <div>
                  <label class="field-label" for="ipAllowlistEnabled">
                    Enforce IP allowlist
                  </label>
                  <p class="field-hint" id="ipAllowlistEnabled-hint">
                    The request source IP must appear in the list below.
                  </p>
                </div>
                <label class="toggle">
                  <input
                    id="ipAllowlistEnabled"
                    type="checkbox"
                    formControlName="ipAllowlistEnabled"
                    aria-describedby="ipAllowlistEnabled-hint"
                    data-test="ipAllowlistEnabled"
                  />
                  <span class="toggle-track"><span class="toggle-thumb"></span></span>
                </label>
              </div>

              <div class="border-t border-neutral-100 pt-5">
                <div class="flex items-center justify-between gap-4">
                  <div>
                    <h3 class="field-label">Allowed addresses</h3>
                    <p class="field-hint">For example 203.0.113.7 or 203.0.113.0/24.</p>
                  </div>
                  <button
                    type="button"
                    class="btn-secondary text-sm"
                    (click)="addIpEntry()"
                    data-test="add-ip"
                  >
                    Add address
                  </button>
                </div>

                @if (ipAllowlist.length === 0) {
                  <p class="empty-hint" data-test="no-ips">
                    No addresses listed. Enabling the allowlist with an empty list would block
                    every clock-in.
                  </p>
                } @else {
                  <ul class="space-y-3 mt-4" formArrayName="ipAllowlist">
                    @for (entry of ipAllowlist.controls; track $index) {
                      <li class="flex items-end gap-3">
                        <div class="flex-1">
                          <label class="sr-only" [attr.for]="'ip-' + $index">
                            Allowed address {{ $index + 1 }}
                          </label>
                          <input
                            [id]="'ip-' + $index"
                            type="text"
                            class="field"
                            [formControlName]="$index"
                            [attr.data-test]="'ip-' + $index"
                          />
                          @if (showIpError($index)) {
                            <p class="field-error" role="alert">An address is required.</p>
                          }
                        </div>
                        <button
                          type="button"
                          class="btn-danger text-sm"
                          (click)="removeIpEntry($index)"
                          [attr.aria-label]="'Remove allowed address ' + ($index + 1)"
                          [attr.data-test]="'remove-ip-' + $index"
                        >
                          Remove
                        </button>
                      </li>
                    }
                  </ul>
                }
              </div>
            </div>
          </section>

          <!-- ══ Work hours ════════════════════════════════════════════ -->
          <section class="card-notion" aria-labelledby="sec-hours">
            <h2 id="sec-hours" class="section-title">Work hours</h2>
            <p class="section-hint">
              How a day's worked time is calculated and when a day counts as short.
            </p>

            <div class="grid grid-cols-1 sm:grid-cols-2 gap-5 mt-5">
              <div>
                <label class="field-label" for="standardWorkMinutes">
                  Standard work minutes
                </label>
                <p class="field-hint" id="standardWorkMinutes-hint">
                  Scheduled minutes in a full day. 480 is 8 hours.
                </p>
                <input
                  id="standardWorkMinutes"
                  type="number"
                  min="0"
                  step="1"
                  class="field"
                  formControlName="standardWorkMinutes"
                  aria-describedby="standardWorkMinutes-hint"
                  [attr.aria-invalid]="showError('standardWorkMinutes') ? 'true' : null"
                  data-test="standardWorkMinutes"
                />
                @if (showError('standardWorkMinutes')) {
                  <p class="field-error" role="alert" data-test="standardWorkMinutes-error">
                    Enter 0 or more minutes.
                  </p>
                }
              </div>

              <div>
                <label class="field-label" for="minimumWorkMinutes">
                  Minimum work minutes
                </label>
                <p class="field-hint" id="minimumWorkMinutes-hint">
                  Net minutes below which the day is flagged as a short day.
                </p>
                <input
                  id="minimumWorkMinutes"
                  type="number"
                  min="0"
                  step="1"
                  class="field"
                  formControlName="minimumWorkMinutes"
                  aria-describedby="minimumWorkMinutes-hint"
                  [attr.aria-invalid]="showError('minimumWorkMinutes') ? 'true' : null"
                  data-test="minimumWorkMinutes"
                />
                @if (showError('minimumWorkMinutes')) {
                  <p class="field-error" role="alert" data-test="minimumWorkMinutes-error">
                    Enter 0 or more minutes.
                  </p>
                }
              </div>

              <div>
                <label class="field-label" for="autoBreakMinutes">Auto-break minutes</label>
                <p class="field-hint" id="autoBreakMinutes-hint">
                  Break time deducted automatically from gross worked time.
                </p>
                <input
                  id="autoBreakMinutes"
                  type="number"
                  min="0"
                  step="1"
                  class="field"
                  formControlName="autoBreakMinutes"
                  aria-describedby="autoBreakMinutes-hint"
                  [attr.aria-invalid]="showError('autoBreakMinutes') ? 'true' : null"
                  data-test="autoBreakMinutes"
                />
                @if (showError('autoBreakMinutes')) {
                  <p class="field-error" role="alert" data-test="autoBreakMinutes-error">
                    Enter 0 or more minutes.
                  </p>
                }
              </div>

              <div>
                <label class="field-label" for="autoBreakThresholdMinutes">
                  Auto-break threshold (minutes)
                </label>
                <p class="field-hint" id="autoBreakThresholdMinutes-hint">
                  Gross minutes above which the auto-break is deducted. 360 is 6 hours.
                </p>
                <input
                  id="autoBreakThresholdMinutes"
                  type="number"
                  min="0"
                  step="1"
                  class="field"
                  formControlName="autoBreakThresholdMinutes"
                  aria-describedby="autoBreakThresholdMinutes-hint"
                  [attr.aria-invalid]="showError('autoBreakThresholdMinutes') ? 'true' : null"
                  data-test="autoBreakThresholdMinutes"
                />
                @if (showError('autoBreakThresholdMinutes')) {
                  <p
                    class="field-error"
                    role="alert"
                    data-test="autoBreakThresholdMinutes-error"
                  >
                    Enter 0 or more minutes.
                  </p>
                }
              </div>

              <div>
                <label class="field-label" for="overtimeThresholdMinutes">
                  Overtime tolerance (minutes)
                </label>
                <p class="field-hint" id="overtimeThresholdMinutes-hint">
                  Minutes beyond the standard day tolerated before the excess counts as
                  overtime.
                </p>
                <input
                  id="overtimeThresholdMinutes"
                  type="number"
                  min="0"
                  step="1"
                  class="field"
                  formControlName="overtimeThresholdMinutes"
                  aria-describedby="overtimeThresholdMinutes-hint"
                  [attr.aria-invalid]="showError('overtimeThresholdMinutes') ? 'true' : null"
                  data-test="overtimeThresholdMinutes"
                />
                @if (showError('overtimeThresholdMinutes')) {
                  <p class="field-error" role="alert" data-test="overtimeThresholdMinutes-error">
                    Enter 0 or more minutes.
                  </p>
                }
              </div>

              <div>
                <label class="field-label" for="regularizationLookbackDays">
                  Regularization lookback (days)
                </label>
                <p class="field-hint" id="regularizationLookbackDays-hint">
                  How far back an employee may request a correction to a punch.
                </p>
                <input
                  id="regularizationLookbackDays"
                  type="number"
                  min="0"
                  step="1"
                  class="field"
                  formControlName="regularizationLookbackDays"
                  aria-describedby="regularizationLookbackDays-hint"
                  [attr.aria-invalid]="showError('regularizationLookbackDays') ? 'true' : null"
                  data-test="regularizationLookbackDays"
                />
                @if (showError('regularizationLookbackDays')) {
                  <p
                    class="field-error"
                    role="alert"
                    data-test="regularizationLookbackDays-error"
                  >
                    Enter 0 or more days.
                  </p>
                }
              </div>
            </div>

            @if (form.hasError('minimumAboveStandard')) {
              <p class="field-error mt-4" role="alert" data-test="minimum-above-standard-error">
                Minimum work minutes cannot exceed standard work minutes — every worked day
                would be flagged as short.
              </p>
            }
          </section>

          <!-- ══ Overtime & pay ════════════════════════════════════════ -->
          <section class="card-notion" aria-labelledby="sec-overtime">
            <h2 id="sec-overtime" class="section-title">Overtime and pay</h2>
            <p class="section-hint">
              Overtime detection limits, and the multipliers payroll applies to approved
              overtime.
            </p>

            <div class="space-y-5 mt-5">
              <!-- ── Weekday multiplier: MONEY (BUG-456) ── -->
              <div class="money-field" data-test="weekday-multiplier-field">
                <div class="flex items-center gap-2">
                  <label class="field-label" for="weekdayOvertimeMultiplier">
                    Weekday overtime multiplier
                  </label>
                  <span class="badge-money">Affects pay</span>
                </div>
                <p class="field-hint" id="weekdayOvertimeMultiplier-hint">
                  The rate paid for approved overtime worked on a normal weekday, as a
                  multiple of the employee's hourly base. 1.5 pays time-and-a-half; 1.0 pays
                  overtime at the ordinary hourly rate. Must be between 1.0 and 10.
                </p>
                <input
                  id="weekdayOvertimeMultiplier"
                  type="number"
                  min="1"
                  max="10"
                  step="0.1"
                  class="field max-w-xs"
                  formControlName="weekdayOvertimeMultiplier"
                  aria-describedby="weekdayOvertimeMultiplier-hint"
                  [attr.aria-invalid]="showError('weekdayOvertimeMultiplier') ? 'true' : null"
                  data-test="weekdayOvertimeMultiplier"
                />
                @if (showError('weekdayOvertimeMultiplier')) {
                  <p
                    class="field-error"
                    role="alert"
                    data-test="weekdayOvertimeMultiplier-error"
                  >
                    Enter a multiplier between 1.0 and 10.
                  </p>
                }
              </div>

              <div class="grid grid-cols-1 sm:grid-cols-2 gap-5">
                <div>
                  <div class="flex items-center gap-2">
                    <label class="field-label" for="weekendOvertimeMultiplier">
                      Weekend overtime multiplier
                    </label>
                    <span class="badge-money">Affects pay</span>
                  </div>
                  <p class="field-hint" id="weekendOvertimeMultiplier-hint">
                    Rate for overtime on a rest day. Between 1.0 and 10.
                  </p>
                  <input
                    id="weekendOvertimeMultiplier"
                    type="number"
                    min="1"
                    max="10"
                    step="0.1"
                    class="field"
                    formControlName="weekendOvertimeMultiplier"
                    aria-describedby="weekendOvertimeMultiplier-hint"
                    [attr.aria-invalid]="showError('weekendOvertimeMultiplier') ? 'true' : null"
                    data-test="weekendOvertimeMultiplier"
                  />
                  @if (showError('weekendOvertimeMultiplier')) {
                    <p
                      class="field-error"
                      role="alert"
                      data-test="weekendOvertimeMultiplier-error"
                    >
                      Enter a multiplier between 1.0 and 10.
                    </p>
                  }
                </div>

                <div>
                  <div class="flex items-center gap-2">
                    <label class="field-label" for="holidayOvertimeMultiplier">
                      Holiday overtime multiplier
                    </label>
                    <span class="badge-money">Affects pay</span>
                  </div>
                  <p class="field-hint" id="holidayOvertimeMultiplier-hint">
                    Rate for overtime on a public holiday. Between 1.0 and 10.
                  </p>
                  <input
                    id="holidayOvertimeMultiplier"
                    type="number"
                    min="1"
                    max="10"
                    step="0.1"
                    class="field"
                    formControlName="holidayOvertimeMultiplier"
                    aria-describedby="holidayOvertimeMultiplier-hint"
                    [attr.aria-invalid]="showError('holidayOvertimeMultiplier') ? 'true' : null"
                    data-test="holidayOvertimeMultiplier"
                  />
                  @if (showError('holidayOvertimeMultiplier')) {
                    <p
                      class="field-error"
                      role="alert"
                      data-test="holidayOvertimeMultiplier-error"
                    >
                      Enter a multiplier between 1.0 and 10.
                    </p>
                  }
                </div>

                <div>
                  <label class="field-label" for="overtimeMinimumThresholdMinutes">
                    Minimum overtime (minutes)
                  </label>
                  <p class="field-hint" id="overtimeMinimumThresholdMinutes-hint">
                    Extra minutes needed before an overtime record is raised at all.
                  </p>
                  <input
                    id="overtimeMinimumThresholdMinutes"
                    type="number"
                    min="0"
                    step="1"
                    class="field"
                    formControlName="overtimeMinimumThresholdMinutes"
                    aria-describedby="overtimeMinimumThresholdMinutes-hint"
                    [attr.aria-invalid]="
                      showError('overtimeMinimumThresholdMinutes') ? 'true' : null
                    "
                    data-test="overtimeMinimumThresholdMinutes"
                  />
                  @if (showError('overtimeMinimumThresholdMinutes')) {
                    <p
                      class="field-error"
                      role="alert"
                      data-test="overtimeMinimumThresholdMinutes-error"
                    >
                      Enter 0 or more minutes.
                    </p>
                  }
                </div>

                <div>
                  <label class="field-label" for="maxDailyOvertimeMinutes">
                    Maximum daily overtime (minutes)
                  </label>
                  <p class="field-hint" id="maxDailyOvertimeMinutes-hint">
                    Upper limit per day. 240 is 4 hours.
                  </p>
                  <input
                    id="maxDailyOvertimeMinutes"
                    type="number"
                    min="0"
                    step="1"
                    class="field"
                    formControlName="maxDailyOvertimeMinutes"
                    aria-describedby="maxDailyOvertimeMinutes-hint"
                    [attr.aria-invalid]="showError('maxDailyOvertimeMinutes') ? 'true' : null"
                    data-test="maxDailyOvertimeMinutes"
                  />
                  @if (showError('maxDailyOvertimeMinutes')) {
                    <p
                      class="field-error"
                      role="alert"
                      data-test="maxDailyOvertimeMinutes-error"
                    >
                      Enter 0 or more minutes.
                    </p>
                  }
                </div>

                <div>
                  <label class="field-label" for="maxWeeklyOvertimeMinutes">
                    Maximum weekly overtime (minutes)
                  </label>
                  <p class="field-hint" id="maxWeeklyOvertimeMinutes-hint">
                    Upper limit per week. 1200 is 20 hours.
                  </p>
                  <input
                    id="maxWeeklyOvertimeMinutes"
                    type="number"
                    min="0"
                    step="1"
                    class="field"
                    formControlName="maxWeeklyOvertimeMinutes"
                    aria-describedby="maxWeeklyOvertimeMinutes-hint"
                    [attr.aria-invalid]="showError('maxWeeklyOvertimeMinutes') ? 'true' : null"
                    data-test="maxWeeklyOvertimeMinutes"
                  />
                  @if (showError('maxWeeklyOvertimeMinutes')) {
                    <p
                      class="field-error"
                      role="alert"
                      data-test="maxWeeklyOvertimeMinutes-error"
                    >
                      Enter 0 or more minutes.
                    </p>
                  }
                </div>
              </div>

              <div class="toggle-row border-t border-neutral-100 pt-5">
                <div>
                  <label class="field-label" for="requireOvertimePreApproval">
                    Require overtime pre-approval
                  </label>
                  <p class="field-hint" id="requireOvertimePreApproval-hint">
                    Overtime must be requested and approved in advance to be counted.
                  </p>
                </div>
                <label class="toggle">
                  <input
                    id="requireOvertimePreApproval"
                    type="checkbox"
                    formControlName="requireOvertimePreApproval"
                    aria-describedby="requireOvertimePreApproval-hint"
                    data-test="requireOvertimePreApproval"
                  />
                  <span class="toggle-track"><span class="toggle-thumb"></span></span>
                </label>
              </div>

              <!-- ── FTE-scaled overtime base: MONEY (ISSUE-438) ── -->
              <div class="money-field" data-test="fte-scaled-field">
                <div class="toggle-row">
                  <div>
                    <div class="flex items-center gap-2">
                      <label class="field-label" for="fteScaledOvertimeBase">
                        Scale the overtime base by FTE
                      </label>
                      <span class="badge-money">Affects pay</span>
                    </div>
                    <p class="field-hint" id="fteScaledOvertimeBase-hint">
                      Off (the default): the hourly overtime base is derived from the monthly
                      basic over a full-time month, and the employee's FTE is ignored. On: the
                      base is divided by the employee's contracted hours instead, so a 0.5-FTE
                      employee on the same monthly basic earns roughly double the hourly
                      overtime rate — because that basic buys half the hours. Turning this on
                      raises overtime pay for part-time employees.
                    </p>
                  </div>
                  <label class="toggle">
                    <input
                      id="fteScaledOvertimeBase"
                      type="checkbox"
                      formControlName="fteScaledOvertimeBase"
                      aria-describedby="fteScaledOvertimeBase-hint"
                      data-test="fteScaledOvertimeBase"
                    />
                    <span class="toggle-track"><span class="toggle-thumb"></span></span>
                  </label>
                </div>
              </div>
            </div>
          </section>

          <!-- ══ Reporting ═════════════════════════════════════════════ -->
          <section class="card-notion" aria-labelledby="sec-reporting">
            <h2 id="sec-reporting" class="section-title">Summary and reporting</h2>
            <p class="section-hint">How monthly attendance is summarised and flagged.</p>

            <div class="space-y-5 mt-5">
              <div class="toggle-row">
                <div>
                  <label class="field-label" for="halfDayEnabled">Count half days</label>
                  <p class="field-hint" id="halfDayEnabled-hint">
                    A qualifying short day counts as 0.5 of a present day in the monthly
                    summary.
                  </p>
                </div>
                <label class="toggle">
                  <input
                    id="halfDayEnabled"
                    type="checkbox"
                    formControlName="halfDayEnabled"
                    aria-describedby="halfDayEnabled-hint"
                    data-test="halfDayEnabled"
                  />
                  <span class="toggle-track"><span class="toggle-thumb"></span></span>
                </label>
              </div>

              <div class="max-w-xs">
                <label class="field-label" for="absenteeismThresholdDays">
                  Absenteeism threshold (days)
                </label>
                <p class="field-hint" id="absenteeismThresholdDays-hint">
                  Average loss-of-pay days per month above which an employee is flagged.
                </p>
                <input
                  id="absenteeismThresholdDays"
                  type="number"
                  min="0"
                  step="0.5"
                  class="field"
                  formControlName="absenteeismThresholdDays"
                  aria-describedby="absenteeismThresholdDays-hint"
                  [attr.aria-invalid]="showError('absenteeismThresholdDays') ? 'true' : null"
                  data-test="absenteeismThresholdDays"
                />
                @if (showError('absenteeismThresholdDays')) {
                  <p class="field-error" role="alert" data-test="absenteeismThresholdDays-error">
                    Enter 0 or more days.
                  </p>
                }
              </div>
            </div>
          </section>

          <div class="flex justify-end gap-3">
            <button
              type="submit"
              class="btn-primary text-sm"
              [disabled]="isSaving()"
              data-test="save"
            >
              @if (isSaving()) {
                <span class="btn-spinner"></span> Saving...
              } @else {
                Save policy
              }
            </button>
          </div>
        </form>
      }
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .page-container {
        @apply max-w-4xl mx-auto;
      }
      .card-notion {
        @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5 sm:p-6;
      }
      .section-title {
        @apply text-base font-semibold text-neutral-900;
      }
      .section-hint {
        @apply mt-1 text-sm text-neutral-500;
      }
      .toggle-row {
        @apply flex items-start justify-between gap-4;
      }
      .row-card {
        @apply rounded-lg border border-neutral-200 p-4;
      }
      .empty-hint {
        @apply mt-4 rounded-lg bg-neutral-50 px-4 py-3 text-xs text-neutral-500;
      }
      .notice {
        @apply rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900;
      }
      .notice-tag {
        @apply inline-block rounded-md bg-amber-200 px-2 py-0.5 text-xs font-semibold
          uppercase tracking-wide text-amber-900;
      }
      .notice p {
        @apply mt-1.5;
      }
      /* Money-affecting settings get a visible outline AND a text badge — never colour alone. */
      .money-field {
        @apply rounded-lg border border-neutral-200 bg-neutral-50/60 p-4;
      }
      .badge-money {
        @apply inline-block rounded-md border border-amber-300 bg-amber-100 px-1.5 py-0.5
          text-[11px] font-semibold uppercase tracking-wide text-amber-900;
      }
      .field-label {
        @apply block text-sm font-medium text-neutral-800;
      }
      .field-hint {
        @apply mt-0.5 text-xs text-neutral-500;
      }
      .field-error {
        @apply mt-1 text-xs text-red-600;
      }
      .field {
        @apply mt-2 block w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
          text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1
          focus:ring-brand-500 outline-none;
      }
      .skeleton-line {
        @apply rounded-lg bg-neutral-200;
        animation: shimmer 1.5s ease-in-out infinite;
      }
      @keyframes shimmer {
        0%,
        100% {
          opacity: 1;
        }
        50% {
          opacity: 0.4;
        }
      }
      .btn-primary {
        @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
          text-sm font-semibold text-white shadow-sm transition-all duration-200
          hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
      }
      .btn-secondary {
        @apply inline-flex items-center justify-center rounded-lg border border-neutral-200
          bg-white px-3 py-1.5 font-medium text-neutral-700 shadow-sm transition-colors
          duration-200 hover:bg-neutral-50;
      }
      .btn-danger {
        @apply inline-flex items-center justify-center rounded-lg border border-red-200
          bg-white px-3 py-1.5 font-medium text-red-700 transition-colors duration-200
          hover:bg-red-50;
      }
      .btn-spinner {
        @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
        animation: spin 0.6s linear infinite;
      }
      @keyframes spin {
        to {
          transform: rotate(360deg);
        }
      }
      /* Notion-style toggle. The input stays focusable (sr-only, not display:none). */
      .toggle {
        @apply relative inline-flex cursor-pointer items-center;
      }
      .toggle input {
        @apply sr-only;
      }
      .toggle-track {
        @apply block h-6 w-11 rounded-full bg-neutral-200 transition-colors duration-200;
      }
      .toggle input:checked + .toggle-track {
        @apply bg-brand-600;
      }
      .toggle input:focus-visible + .toggle-track {
        @apply ring-2 ring-brand-500/40;
      }
      .toggle-thumb {
        @apply absolute left-0.5 top-0.5 h-5 w-5 rounded-full bg-white shadow
          transition-transform duration-200;
      }
      .toggle input:checked ~ .toggle-track .toggle-thumb {
        transform: translateX(20px);
      }
    `,
  ],
})
export class AttendanceSettingsComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly fb = inject(FormBuilder);
  private readonly destroy$ = new Subject<void>();

  readonly isLoading = signal(true);
  readonly isSaving = signal(false);

  /**
   * The policy as last read from the server. Every field the form does not own
   * (`locationId`/`locationName`, which are read-only) is carried from here into the save,
   * so the full-replace payload is always complete.
   */
  private loaded: IAttendanceSettings | null = null;

  private readonly d = ATTENDANCE_SETTINGS_DEFAULTS;

  readonly form = this.fb.group(
    {
      // Clock-in enforcement
      requireGeolocation: this.fb.nonNullable.control(false),
      requirePhoto: this.fb.nonNullable.control(false),
      gracePeriodMinutes: this.fb.control<number | null>(this.d.gracePeriodMinutes, [
        Validators.required,
        Validators.min(0),
      ]),

      // Geo-fence
      geoFenceEnabled: this.fb.nonNullable.control(false),
      geoFenceLatitude: this.fb.control<number | null>(null, [
        Validators.min(-90),
        Validators.max(90),
      ]),
      geoFenceLongitude: this.fb.control<number | null>(null, [
        Validators.min(-180),
        Validators.max(180),
      ]),
      geoFenceRadiusMeters: this.fb.control<number | null>(this.d.geoFenceRadiusMeters, [
        Validators.required,
        Validators.min(0),
      ]),
      geoFenceLocations: this.fb.array<GeofenceLocationGroup>([]),

      // IP allowlist
      ipAllowlistEnabled: this.fb.nonNullable.control(false),
      ipAllowlist: this.fb.array<FormControl<string>>([]),

      // Work hours
      standardWorkMinutes: this.fb.control<number | null>(this.d.standardWorkMinutes, [
        Validators.required,
        Validators.min(0),
      ]),
      minimumWorkMinutes: this.fb.control<number | null>(this.d.minimumWorkMinutes, [
        Validators.required,
        Validators.min(0),
      ]),
      autoBreakMinutes: this.fb.control<number | null>(this.d.autoBreakMinutes, [
        Validators.required,
        Validators.min(0),
      ]),
      autoBreakThresholdMinutes: this.fb.control<number | null>(
        this.d.autoBreakThresholdMinutes,
        [Validators.required, Validators.min(0)],
      ),
      overtimeThresholdMinutes: this.fb.control<number | null>(
        this.d.overtimeThresholdMinutes,
        [Validators.required, Validators.min(0)],
      ),
      regularizationLookbackDays: this.fb.control<number | null>(
        this.d.regularizationLookbackDays,
        [Validators.required, Validators.min(0)],
      ),

      // Overtime & pay
      overtimeMinimumThresholdMinutes: this.fb.control<number | null>(
        this.d.overtimeMinimumThresholdMinutes,
        [Validators.required, Validators.min(0)],
      ),
      weekdayOvertimeMultiplier: this.fb.control<number | null>(
        this.d.weekdayOvertimeMultiplier,
        [Validators.required, Validators.min(1), Validators.max(10)],
      ),
      weekendOvertimeMultiplier: this.fb.control<number | null>(
        this.d.weekendOvertimeMultiplier,
        [Validators.required, Validators.min(1), Validators.max(10)],
      ),
      holidayOvertimeMultiplier: this.fb.control<number | null>(
        this.d.holidayOvertimeMultiplier,
        [Validators.required, Validators.min(1), Validators.max(10)],
      ),
      maxDailyOvertimeMinutes: this.fb.control<number | null>(this.d.maxDailyOvertimeMinutes, [
        Validators.required,
        Validators.min(0),
      ]),
      maxWeeklyOvertimeMinutes: this.fb.control<number | null>(
        this.d.maxWeeklyOvertimeMinutes,
        [Validators.required, Validators.min(0)],
      ),
      requireOvertimePreApproval: this.fb.nonNullable.control(false),
      fteScaledOvertimeBase: this.fb.nonNullable.control(false),

      // Reporting
      halfDayEnabled: this.fb.nonNullable.control(false),
      absenteeismThresholdDays: this.fb.control<number | null>(
        this.d.absenteeismThresholdDays,
        [Validators.required, Validators.min(0)],
      ),
    },
    { validators: [minimumNotAboveStandardValidator] },
  );

  get geoFenceLocations(): FormArray<GeofenceLocationGroup> {
    return this.form.controls.geoFenceLocations;
  }

  get ipAllowlist(): FormArray<FormControl<string>> {
    return this.form.controls.ipAllowlist;
  }

  ngOnInit(): void {
    this.load();
    // The backend only enforces a radius while the fence is on; mirror that so an admin
    // with the fence off is not blocked by a field that has no effect.
    this.form.controls.geoFenceEnabled.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.applyRadiusRule());
    this.applyRadiusRule();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.isLoading.set(true);
    this.attendanceService
      .getAttendanceSettings()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (settings) => {
          this.loaded = settings;
          this.patchForm(settings);
          this.isLoading.set(false);
        },
        error: () => {
          // Leave `loaded` null: without a server read we cannot honour the full-replace
          // contract, and saving form defaults over an unread policy could silently reset
          // live pay settings. onSubmit() refuses to save in that state.
          this.isLoading.set(false);
          this.toastr.error('Could not load the attendance policy.');
        },
      });
  }

  onSubmit(): void {
    if (this.isSaving()) {
      return;
    }
    if (!this.loaded) {
      // Guard the full-replace contract: never PUT a policy we never successfully GET'd.
      this.toastr.error('Reload the page before saving — the current policy is unknown.');
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.attendanceService
      .updateAttendanceSettings(this.toSettings(this.loaded))
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (saved) => {
          this.loaded = saved;
          this.patchForm(saved);
          this.isSaving.set(false);
          this.toastr.success('Attendance policy saved.');
        },
        error: () => {
          this.isSaving.set(false);
          this.toastr.error('Could not save the attendance policy.');
        },
      });
  }

  addGeofenceLocation(): void {
    this.geoFenceLocations.push(this.newGeofenceGroup());
  }

  removeGeofenceLocation(index: number): void {
    this.geoFenceLocations.removeAt(index);
  }

  addIpEntry(): void {
    this.ipAllowlist.push(this.fb.nonNullable.control('', [Validators.required]));
  }

  removeIpEntry(index: number): void {
    this.ipAllowlist.removeAt(index);
  }

  showError(control: string): boolean {
    const c = this.form.get(control);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  showRowError(index: number, control: string): boolean {
    const c = this.geoFenceLocations.at(index)?.get(control);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  showIpError(index: number): boolean {
    const c = this.ipAllowlist.at(index);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  // ── internals ────────────────────────────────────────────────────────────

  private newGeofenceGroup(location?: IGeofenceLocation): GeofenceLocationGroup {
    return this.fb.group({
      name: this.fb.nonNullable.control(location?.name ?? '', [
        Validators.required,
        Validators.maxLength(100),
      ]),
      latitude: this.fb.control<number | null>(location?.latitude ?? null, [
        Validators.required,
        Validators.min(-90),
        Validators.max(90),
      ]),
      longitude: this.fb.control<number | null>(location?.longitude ?? null, [
        Validators.required,
        Validators.min(-180),
        Validators.max(180),
      ]),
      // The backend rule is `> 0`, so min(1) is the integer equivalent for a metre count.
      radiusMeters: this.fb.control<number | null>(location?.radiusMeters ?? null, [
        Validators.required,
        Validators.min(1),
      ]),
    });
  }

  private patchForm(s: IAttendanceSettings): void {
    this.form.patchValue({
      requireGeolocation: s.requireGeolocation,
      requirePhoto: s.requirePhoto,
      gracePeriodMinutes: s.gracePeriodMinutes,
      geoFenceEnabled: s.geoFenceEnabled,
      geoFenceLatitude: s.geoFenceLatitude,
      geoFenceLongitude: s.geoFenceLongitude,
      geoFenceRadiusMeters: s.geoFenceRadiusMeters,
      ipAllowlistEnabled: s.ipAllowlistEnabled,
      standardWorkMinutes: s.standardWorkMinutes,
      minimumWorkMinutes: s.minimumWorkMinutes,
      autoBreakMinutes: s.autoBreakMinutes,
      autoBreakThresholdMinutes: s.autoBreakThresholdMinutes,
      overtimeThresholdMinutes: s.overtimeThresholdMinutes,
      regularizationLookbackDays: s.regularizationLookbackDays,
      overtimeMinimumThresholdMinutes: s.overtimeMinimumThresholdMinutes,
      weekdayOvertimeMultiplier: s.weekdayOvertimeMultiplier,
      weekendOvertimeMultiplier: s.weekendOvertimeMultiplier,
      holidayOvertimeMultiplier: s.holidayOvertimeMultiplier,
      maxDailyOvertimeMinutes: s.maxDailyOvertimeMinutes,
      maxWeeklyOvertimeMinutes: s.maxWeeklyOvertimeMinutes,
      requireOvertimePreApproval: s.requireOvertimePreApproval,
      fteScaledOvertimeBase: s.fteScaledOvertimeBase,
      halfDayEnabled: s.halfDayEnabled,
      absenteeismThresholdDays: s.absenteeismThresholdDays,
    });

    this.geoFenceLocations.clear();
    s.geoFenceLocations.forEach((l) => this.geoFenceLocations.push(this.newGeofenceGroup(l)));

    this.ipAllowlist.clear();
    s.ipAllowlist.forEach((ip) =>
      this.ipAllowlist.push(this.fb.nonNullable.control(ip, [Validators.required])),
    );

    this.applyRadiusRule();
  }

  /**
   * Build the complete policy to save: the server-read `base` supplies the read-only scope
   * fields, and every editable field comes from the form. Numbers are coerced because an
   * emptied number input yields null at runtime even on a typed control — the `required`
   * validators block that path, and the `?? base.x` fallback keeps the payload complete
   * rather than sending a null the backend would coerce to 0.
   */
  private toSettings(base: IAttendanceSettings): IAttendanceSettings {
    const v = this.form.getRawValue();
    return {
      locationId: base.locationId,
      locationName: base.locationName,

      requireGeolocation: v.requireGeolocation,
      requirePhoto: v.requirePhoto,
      gracePeriodMinutes: v.gracePeriodMinutes ?? base.gracePeriodMinutes,

      geoFenceEnabled: v.geoFenceEnabled,
      // Genuinely nullable: "not configured" is a real state the backend accepts.
      geoFenceLatitude: v.geoFenceLatitude ?? null,
      geoFenceLongitude: v.geoFenceLongitude ?? null,
      geoFenceRadiusMeters: v.geoFenceRadiusMeters ?? base.geoFenceRadiusMeters,
      geoFenceLocations: v.geoFenceLocations.map((l) => ({
        name: l.name,
        latitude: l.latitude ?? 0,
        longitude: l.longitude ?? 0,
        radiusMeters: l.radiusMeters ?? 0,
      })),

      ipAllowlistEnabled: v.ipAllowlistEnabled,
      ipAllowlist: v.ipAllowlist,

      standardWorkMinutes: v.standardWorkMinutes ?? base.standardWorkMinutes,
      minimumWorkMinutes: v.minimumWorkMinutes ?? base.minimumWorkMinutes,
      autoBreakMinutes: v.autoBreakMinutes ?? base.autoBreakMinutes,
      autoBreakThresholdMinutes:
        v.autoBreakThresholdMinutes ?? base.autoBreakThresholdMinutes,
      overtimeThresholdMinutes: v.overtimeThresholdMinutes ?? base.overtimeThresholdMinutes,

      regularizationLookbackDays:
        v.regularizationLookbackDays ?? base.regularizationLookbackDays,

      overtimeMinimumThresholdMinutes:
        v.overtimeMinimumThresholdMinutes ?? base.overtimeMinimumThresholdMinutes,
      // MONEY: falling back to the server-read value keeps an unedited multiplier exactly
      // as it was; it must never become 0 or a hardcoded guess.
      weekdayOvertimeMultiplier:
        v.weekdayOvertimeMultiplier ?? base.weekdayOvertimeMultiplier,
      weekendOvertimeMultiplier:
        v.weekendOvertimeMultiplier ?? base.weekendOvertimeMultiplier,
      holidayOvertimeMultiplier:
        v.holidayOvertimeMultiplier ?? base.holidayOvertimeMultiplier,
      maxDailyOvertimeMinutes: v.maxDailyOvertimeMinutes ?? base.maxDailyOvertimeMinutes,
      maxWeeklyOvertimeMinutes: v.maxWeeklyOvertimeMinutes ?? base.maxWeeklyOvertimeMinutes,
      requireOvertimePreApproval: v.requireOvertimePreApproval,
      fteScaledOvertimeBase: v.fteScaledOvertimeBase,

      halfDayEnabled: v.halfDayEnabled,
      absenteeismThresholdDays:
        v.absenteeismThresholdDays ?? base.absenteeismThresholdDays,
    };
  }

  /** The radius is only enforced while the geo-fence is on (mirrors the backend's `.When`). */
  private applyRadiusRule(): void {
    const radius = this.form.controls.geoFenceRadiusMeters;
    const validators = this.form.controls.geoFenceEnabled.value
      ? [Validators.required, Validators.min(1)]
      : [Validators.required, Validators.min(0)];
    radius.setValidators(validators);
    radius.updateValueAndValidity({ emitEvent: false });
  }
}
