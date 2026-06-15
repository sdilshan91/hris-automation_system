import { Routes } from '@angular/router';

/**
 * US-REC-001: Recruiter-facing recruitment routes.
 *
 * Lazy-loaded under the 'recruitment' path in app.routes.ts. The parent route
 * applies roleGuard(['Recruiter', 'HR Officer', 'HR Manager', 'Tenant Admin']) —
 * vacancy management requires the Recruitment.Create.All / Manage.All capability
 * (BR-1); the backend enforces the actual permission, the route guard scopes by
 * role. This is the first Recruitment screen; later stories add sibling child
 * routes here (applicants, pipeline, etc.).
 */
export const RECRUITMENT_ROUTES: Routes = [
  {
    // US-REC-001: vacancy list + create/edit slide-over.
    path: 'vacancies',
    loadComponent: () =>
      import('./components/vacancy-list/vacancy-list.component').then(
        (m) => m.VacancyListComponent,
      ),
  },
  {
    // US-REC-003: recruiter applicant pipeline (Kanban) for a vacancy.
    path: 'vacancies/:vacancyId/pipeline',
    loadComponent: () =>
      import(
        './components/applicant-pipeline/applicant-pipeline.component'
      ).then((m) => m.ApplicantPipelineComponent),
  },
  {
    // US-REC-005: tenant-wide interviews agenda/calendar (FR-5).
    path: 'interviews',
    loadComponent: () =>
      import(
        './components/interview-agenda/interview-agenda.component'
      ).then((m) => m.InterviewAgendaComponent),
  },
  {
    // US-REC-009: recruitment dashboard & analytics (KPIs, funnel, sources, trends).
    path: 'dashboard',
    loadComponent: () =>
      import(
        './components/recruitment-dashboard/recruitment-dashboard.component'
      ).then((m) => m.RecruitmentDashboardComponent),
  },
  { path: '', redirectTo: 'vacancies', pathMatch: 'full' },
];
