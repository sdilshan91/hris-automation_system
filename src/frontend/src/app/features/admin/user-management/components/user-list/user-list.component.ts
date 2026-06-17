import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { UserManagementService } from '../../services/user-management.service';
import {
  IUserSummary,
  IUserListParams,
  IAssignableRole,
  UserMembershipStatus,
  IInvitation,
} from '../../models/user-management.models';
import { InviteUsersModalComponent } from '../invite-users-modal/invite-users-modal.component';
import { EditRolesPanelComponent } from '../edit-roles-panel/edit-roles-panel.component';

type ActionKind = 'deactivate' | 'force-reset' | 'end-sessions';

@Component({
  selector: 'app-user-list',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    TranslateModule,
    InviteUsersModalComponent,
    EditRolesPanelComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('150ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
  ],
  templateUrl: './user-list.component.html',
  styles: [`
    .action-btn {
      @apply w-8 h-8 rounded-md flex items-center justify-center text-neutral-400
        hover:text-neutral-700 hover:bg-neutral-100 transition-colors
        disabled:opacity-40 disabled:cursor-not-allowed;
    }
    .action-btn-danger {
      @apply hover:text-red-600 hover:bg-red-50;
    }
    .page-btn {
      @apply rounded-md px-3 py-1.5 text-xs font-medium text-neutral-600
        ring-1 ring-inset ring-neutral-200 transition-colors
        hover:bg-neutral-50 disabled:opacity-40 disabled:cursor-not-allowed;
    }
  `],
})
export class UserListComponent implements OnInit {
  private readonly service = inject(UserManagementService);
  private readonly toastr = inject(ToastrService);

  readonly pageSize = 20;

  // ─── Tab ─────────────────────────────────────────────────
  readonly activeTab = signal<'users' | 'invitations'>('users');

  // ─── User list state ─────────────────────────────────────
  readonly users = signal<IUserSummary[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly loading = signal(true);
  readonly error = signal('');

  readonly searchTerm = signal('');
  readonly statusFilter = signal<UserMembershipStatus | ''>('');
  readonly roleFilter = signal('');

  readonly roles = signal<IAssignableRole[]>([]);

  // ─── Invitations state ───────────────────────────────────
  readonly invitations = signal<IInvitation[]>([]);
  readonly invitationsLoading = signal(false);
  readonly invitationsError = signal('');

  // ─── Modals / panels ─────────────────────────────────────
  readonly showInvite = signal(false);
  readonly editTarget = signal<IUserSummary | null>(null);

  // ─── Confirm dialog ──────────────────────────────────────
  readonly confirmAction = signal<{ kind: ActionKind; user: IUserSummary } | null>(null);
  readonly confirmBusy = signal(false);

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.pageSize))
  );

  readonly rangeStart = computed(() =>
    this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize + 1
  );
  readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize, this.total())
  );

  private readonly search$ = new Subject<string>();

  constructor() {
    this.search$
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        takeUntilDestroyed()
      )
      .subscribe((term) => {
        this.searchTerm.set(term);
        this.page.set(1);
        this.loadUsers();
      });
  }

  ngOnInit(): void {
    this.loadRoles();
    this.loadUsers();
  }

  // ─── Data loading ────────────────────────────────────────

  loadRoles(): void {
    this.service.getAssignableRoles().subscribe({
      next: (roles) => this.roles.set(roles),
      error: () => this.roles.set([]),
    });
  }

  loadUsers(): void {
    this.loading.set(true);
    this.error.set('');
    const params: IUserListParams = {
      page: this.page(),
      pageSize: this.pageSize,
      search: this.searchTerm() || undefined,
      status: this.statusFilter() || undefined,
      roleId: this.roleFilter() || undefined,
    };
    this.service.getUsers(params).subscribe({
      next: (res) => {
        this.users.set(res.items);
        this.total.set(res.total);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load users. Please try again.');
        this.loading.set(false);
      },
    });
  }

  loadInvitations(): void {
    this.invitationsLoading.set(true);
    this.invitationsError.set('');
    this.service.getInvitations().subscribe({
      next: (inv) => {
        this.invitations.set(inv);
        this.invitationsLoading.set(false);
      },
      error: () => {
        this.invitationsError.set('Failed to load invitations.');
        this.invitationsLoading.set(false);
      },
    });
  }

  // ─── Tabs ────────────────────────────────────────────────

  selectTab(tab: 'users' | 'invitations'): void {
    this.activeTab.set(tab);
    if (tab === 'invitations' && this.invitations().length === 0) {
      this.loadInvitations();
    }
  }

  // ─── Search & filters ────────────────────────────────────

  onSearchInput(value: string): void {
    this.search$.next(value);
  }

  onStatusChange(value: string): void {
    this.statusFilter.set(value as UserMembershipStatus | '');
    this.page.set(1);
    this.loadUsers();
  }

  onRoleChange(value: string): void {
    this.roleFilter.set(value);
    this.page.set(1);
    this.loadUsers();
  }

  // ─── Pagination ──────────────────────────────────────────

  prevPage(): void {
    if (this.page() > 1) {
      this.page.update((p) => p - 1);
      this.loadUsers();
    }
  }

  nextPage(): void {
    if (this.page() < this.totalPages()) {
      this.page.update((p) => p + 1);
      this.loadUsers();
    }
  }

  // ─── Invite modal ────────────────────────────────────────

  openInvite(): void {
    this.showInvite.set(true);
  }

  closeInvite(): void {
    this.showInvite.set(false);
  }

  onInviteDone(): void {
    this.showInvite.set(false);
    this.loadUsers();
    // Refresh pending invitations so the tab reflects the new entries.
    this.loadInvitations();
  }

  // ─── Edit roles ──────────────────────────────────────────

  openEditRoles(user: IUserSummary): void {
    this.editTarget.set(user);
  }

  closeEditRoles(): void {
    this.editTarget.set(null);
  }

  onRolesSaved(): void {
    this.editTarget.set(null);
    this.loadUsers();
  }

  // ─── Lifecycle action confirmations ──────────────────────

  askDeactivate(user: IUserSummary): void {
    this.confirmAction.set({ kind: 'deactivate', user });
  }

  askForceReset(user: IUserSummary): void {
    this.confirmAction.set({ kind: 'force-reset', user });
  }

  askEndSessions(user: IUserSummary): void {
    this.confirmAction.set({ kind: 'end-sessions', user });
  }

  cancelConfirm(): void {
    this.confirmAction.set(null);
  }

  confirm(): void {
    const action = this.confirmAction();
    if (!action) {
      return;
    }
    this.confirmBusy.set(true);
    const { kind, user } = action;
    const call$ =
      kind === 'deactivate'
        ? this.service.deactivateUser(user.userTenantId)
        : kind === 'force-reset'
          ? this.service.forcePasswordReset(user.userTenantId)
          : this.service.endAllSessions(user.userTenantId);

    call$.subscribe({
      next: () => {
        this.confirmBusy.set(false);
        this.confirmAction.set(null);
        this.toastr.success(this.successMessage(kind, user.displayName));
        if (kind === 'deactivate') {
          this.loadUsers();
        }
      },
      error: () => {
        this.confirmBusy.set(false);
        this.toastr.error('Action failed. Please try again.');
      },
    });
  }

  private successMessage(kind: ActionKind, name: string): string {
    switch (kind) {
      case 'deactivate':
        return `${name} has been deactivated.`;
      case 'force-reset':
        return `Password reset email sent to ${name}.`;
      default:
        return `All sessions for ${name} ended.`;
    }
  }

  // ─── Invitation actions ──────────────────────────────────

  resendInvitation(inv: IInvitation): void {
    this.service.resendInvitation(inv.id).subscribe({
      next: () => this.toastr.success(`Invitation resent to ${inv.email}.`),
      error: () => this.toastr.error('Failed to resend invitation.'),
    });
  }

  revokeInvitation(inv: IInvitation): void {
    this.service.revokeInvitation(inv.id).subscribe({
      next: () => {
        this.toastr.success(`Invitation to ${inv.email} revoked.`);
        this.invitations.update((list) => list.filter((i) => i.id !== inv.id));
      },
      error: () => this.toastr.error('Failed to revoke invitation.'),
    });
  }

  // ─── Helpers ─────────────────────────────────────────────

  initials(name: string): string {
    const parts = (name || '').trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return (name || '?').substring(0, 2).toUpperCase();
  }

  statusClass(status: string): string {
    switch (status) {
      case 'active':
        return 'bg-emerald-50 text-emerald-700';
      case 'invited':
        return 'bg-amber-50 text-amber-700';
      default:
        return 'bg-neutral-100 text-neutral-600';
    }
  }
}
