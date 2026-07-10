/**
 * US-TRN-001: Training Catalog & Course Enrollment models.
 *
 * These interfaces mirror the pinned backend DTO contract EXACTLY (the backend
 * serializes camelCase JSON). Enums are string unions matching the C# enum
 * member names. `DateOnly?` fields are serialized as `yyyy-MM-dd` strings;
 * `DateTime?` fields as ISO-8601 strings.
 */

// --- Enums (string unions matching HRM.Domain/Enums) -------------

/** TrainingMode { InPerson, Online, Hybrid } */
export type TrainingMode = 'InPerson' | 'Online' | 'Hybrid';

/** CourseStatus { Draft, Open, Closed, Cancelled, Completed } */
export type CourseStatus = 'Draft' | 'Open' | 'Closed' | 'Cancelled' | 'Completed';

/** EnrollmentStatus { Enrolled, Waitlisted, Cancelled, Completed, NoShow } */
export type EnrollmentStatus =
  | 'Enrolled'
  | 'Waitlisted'
  | 'Cancelled'
  | 'Completed'
  | 'NoShow';

/** Selectable training modes (create/edit form). */
export const TRAINING_MODES: readonly TrainingMode[] = [
  'InPerson',
  'Online',
  'Hybrid',
];

/** All course statuses (display + transition helpers). */
export const COURSE_STATUSES: readonly CourseStatus[] = [
  'Draft',
  'Open',
  'Closed',
  'Cancelled',
  'Completed',
];

// --- DTOs (mirror HRM.Application/Features/Training/DTOs) ---------

/** CourseDto — a catalog course with computed enrollment counts. */
export interface ICourse {
  id: string;
  title: string;
  description: string | null;
  mode: TrainingMode;
  /** null = unlimited capacity (never waitlists). */
  capacity: number | null;
  location: string | null;
  instructor: string | null;
  /** DateOnly? → 'yyyy-MM-dd' */
  startDate: string | null;
  /** DateOnly? → 'yyyy-MM-dd' */
  endDate: string | null;
  durationHours: number | null;
  status: CourseStatus;
  enrolledCount: number;
  waitlistCount: number;
  createdAt: string;
  updatedAt: string | null;
}

/** CreateCourseRequest — creates a Draft course. */
export interface ICreateCourse {
  title: string;
  description?: string | null;
  mode: TrainingMode;
  capacity?: number | null;
  location?: string | null;
  instructor?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  durationHours?: number | null;
}

/** UpdateCourseRequest — same fields as create (metadata edit, not status). */
export type IUpdateCourse = ICreateCourse;

/** ChangeCourseStatusRequest — target status (server validates the transition). */
export interface IChangeCourseStatus {
  status: CourseStatus;
}

/** EnrollRequest — null employeeId self-enrolls the current user's employee. */
export interface IEnrollRequest {
  employeeId?: string | null;
}

/** CompleteEnrollmentRequest — optional certificate reference + score. */
export interface ICompleteEnrollment {
  certificateReference?: string | null;
  score?: number | null;
}

/** EnrollmentDto — one employee's enrollment on a course. */
export interface IEnrollment {
  id: string;
  courseId: string;
  courseTitle: string;
  employeeId: string;
  employeeName: string;
  status: EnrollmentStatus;
  waitlistPosition: number | null;
  enrolledAt: string;
  completedAt: string | null;
  certificateReference: string | null;
  score: number | null;
}

/** Error response shape from the training endpoints. */
export interface ITrainingErrorResponse {
  message: string;
  code?:
    | 'already_enrolled'
    | 'course_not_open'
    | 'invalid_status_transition'
    | string;
}
