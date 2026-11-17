import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { of } from 'rxjs';
import { Api, Paged } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Toast } from '../../core/services';
import { PageHeader, StateView, StatusChip, confirmAction } from '../../shared/ui';

@Component({
  selector: 'app-users',
  imports: [PageHeader, StateView, StatusChip, DatePipe, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatPaginatorModule],
  template: `
    <div class="page">
      <app-page-header title="User management" [subtitle]="auth.has(P.usersAdmin) ? 'All platform users' : 'Users of ' + (auth.activeFranchise()?.name ?? 'your franchise')">
        <button mat-flat-button (click)="inviting.set(!inviting())"><mat-icon>person_add</mat-icon>Invite user</button>
      </app-page-header>
      @if (inviting()) {
        <section class="card">
          <div class="row">
            <mat-form-field><mat-label>E-mail</mat-label><input matInput type="email" [(ngModel)]="email" /></mat-form-field>
            <mat-form-field><mat-label>Full name</mat-label><input matInput [(ngModel)]="fullName" /></mat-form-field>
            <mat-form-field><mat-label>Job title</mat-label><input matInput [(ngModel)]="jobTitle" /></mat-form-field>
            @if (auth.has(P.usersAdmin)) {
              <mat-form-field><mat-label>Franchise</mat-label><mat-select [(ngModel)]="franchiseId"><mat-option [value]="null">None (league/platform role)</mat-option>
                @for (f of franchises.value() ?? []; track f.id) { <mat-option [value]="f.id">{{ f.name }}</mat-option> }</mat-select></mat-form-field>
            }
            <mat-form-field class="wide"><mat-label>Roles</mat-label><mat-select multiple [(ngModel)]="roleCodes">
              @for (r of assignableRoles(); track r.code) { <mat-option [value]="r.code">{{ r.name }}</mat-option> }</mat-select></mat-form-field>
            <button mat-flat-button [disabled]="!email || !fullName || !roleCodes.length" (click)="invite()">Send invitation</button>
          </div>
          @if (inviteLink()) { <p class="small">Invitation link (also e-mailed when e-mail is configured): <code>{{ inviteLink() }}</code></p> }
          <p class="muted small">You can only give roles whose permissions you hold yourself.</p>
        </section>
      }
      <section class="card">
        <mat-form-field class="wide"><mat-label>Search</mat-label><input matInput [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" /></mat-form-field>
        <app-state [loading]="res.isLoading()" [error]="res.error()" [empty]="res.value()?.items?.length === 0" (retry)="res.reload()">
          <table class="data"><tr><th>Name</th><th>E-mail</th><th>Title</th><th>Roles</th><th>Status</th><th>Last sign-in</th><th></th></tr>
            @for (u of res.value()?.items ?? []; track u.id) {
              <tr><td><strong>{{ u.fullName }}</strong></td><td>{{ u.email }}</td><td>{{ u.jobTitle }}</td>
                <td class="small">@for (r of u.roles; track $index) { <div>{{ r.roleName }}{{ r.franchiseName ? ' · ' + r.franchiseName : '' }}</div> }</td>
                <td><app-chip [text]="!u.isActive ? 'Deactivated' : u.isActivated ? 'Active' : 'Invited'" /></td><td class="small">{{ u.lastLoginAt | date: 'd MMM HH:mm' }}</td>
                <td>
                  <button mat-button (click)="history(u)">Sign-ins</button>
                  @if (u.id !== auth.user()?.id) { <button mat-button (click)="toggle(u)">{{ u.isActive ? 'Deactivate' : 'Activate' }}</button> }
                </td></tr>
              @if (historyFor() === u.id) {
                <tr><td colspan="7">@for (h of logins.value() ?? []; track $index) { <div class="small">{{ h.at | date: 'd MMM HH:mm' }} · {{ h.succeeded ? 'Success' : 'Failed (' + h.failureReason + ')' }} · {{ h.ipAddress }}</div> }
                  @empty { <span class="muted small">No sign-ins.</span> }</td></tr>
              }
            }</table>
          <mat-paginator [length]="res.value()?.total ?? 0" [pageSize]="25" [pageIndex]="page() - 1" (page)="paged($event)" />
        </app-state>
      </section>
      <section class="card"><h2>Roles & permissions</h2>
        @for (r of roles.value() ?? []; track r.code) {
          <details><summary><strong>{{ r.name }}</strong> <span class="muted small">— {{ r.description }} ({{ r.permissions.length }} permissions)</span></summary>
            <p class="small muted">{{ r.permissions.join(', ') }}</p></details>
        }
      </section>
    </div>
  `,
})
export class UsersPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly dialog = inject(MatDialog);
  readonly search = signal('');
  readonly page = signal(1);
  readonly inviting = signal(false);
  readonly inviteLink = signal<string | null>(null);
  readonly historyFor = signal<string | null>(null);
  readonly res = rxResource({ params: () => ({ search: this.search(), page: this.page() }), stream: ({ params }) => this.api.get<Paged<any>>('users', { ...params, pageSize: 25 }) });
  readonly roles = rxResource({ stream: () => this.api.get<any[]>('users/roles') });
  readonly franchises = rxResource({ stream: () => this.api.get<any[]>('franchises') });
  readonly logins = rxResource({ params: () => this.historyFor(), stream: ({ params }) => (params ? this.api.get<any[]>(`users/${params}/login-history`) : of([])) });
  readonly assignableRoles = computed(() => (this.roles.value() ?? []).filter((r) => (this.auth.has(P.usersAdmin) ? true : r.scope === 'Franchise' && r.permissions.every((p: string) => this.auth.has(p)))));
  email = '';
  fullName = '';
  jobTitle = '';
  franchiseId: string | null = this.auth.user()?.activeFranchiseId ?? null;
  roleCodes: string[] = [];

  paged(e: PageEvent): void {
    this.page.set(e.pageIndex + 1);
  }

  invite(): void {
    this.api.post<any>('users/invite', { email: this.email, fullName: this.fullName, franchiseId: this.franchiseId, jobTitle: this.jobTitle, roleCodes: this.roleCodes }).subscribe({
      next: (r) => { this.inviteLink.set(r.invitationLink); this.toast.ok('Invitation created.'); this.email = this.fullName = this.jobTitle = ''; this.roleCodes = []; this.res.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  toggle(u: any): void {
    confirmAction(this.dialog, {
      title: `${u.isActive ? 'Deactivate' : 'Activate'} ${u.fullName}?`,
      message: u.isActive ? 'They are signed out everywhere immediately and cannot sign in.' : 'They can sign in again.', danger: u.isActive,
    }).subscribe((yes) => yes && this.api.post(`users/${u.id}/${u.isActive ? 'deactivate' : 'activate'}`).subscribe({ next: () => this.res.reload(), error: (e) => this.toast.error(e) }));
  }

  history(u: any): void {
    this.historyFor.set(this.historyFor() === u.id ? null : u.id);
  }
}
