import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { of } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Toast } from '../../core/services';
import { PageHeader, StateView } from '../../shared/ui';

@Component({
  selector: 'app-settings',
  imports: [PageHeader, StateView, DatePipe, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatTabsModule],
  template: `
    <div class="page">
      <app-page-header title="Settings" />
      <mat-tab-group>
        <mat-tab label="Account & security">
          <div class="grid cols-2 tabpad">
            <section class="card"><h2>Change password</h2>
              <mat-form-field class="full"><mat-label>Current password</mat-label><input matInput type="password" [(ngModel)]="current" autocomplete="current-password" /></mat-form-field>
              <mat-form-field class="full"><mat-label>New password</mat-label><input matInput type="password" [(ngModel)]="next" autocomplete="new-password" />
                <mat-hint>10+ characters with upper and lower case letters and a number</mat-hint></mat-form-field>
              <button mat-flat-button [disabled]="!current || !next" (click)="changePassword()">Change password</button>
              <p class="muted small">Changing your password signs you out on every device.</p></section>
            <section class="card"><h2>Active sessions</h2>
              @for (s of sessions.value() ?? []; track s.id) {
                <div class="list-item"><div><strong>{{ s.isCurrent ? 'This device' : (s.userAgent?.slice(0, 50) ?? 'Unknown device') }}</strong>
                  <div class="small muted">{{ s.ipAddress }} · last used {{ s.lastUsedAt | date: 'd MMM HH:mm' }}</div></div>
                  <span class="spacer"></span>@if (!s.isCurrent) { <button mat-button (click)="revoke(s.id)">Sign out</button> }</div>
              }
              <h3>Recent sign-ins</h3>
              @for (h of logins.value() ?? []; track $index) { <div class="small">{{ h.at | date: 'd MMM HH:mm' }} · {{ h.succeeded ? 'Success' : 'Failed' }} · {{ h.ipAddress }}</div> }
            </section>
          </div>
        </mat-tab>
        <mat-tab label="Notifications">
          <section class="card tabpad">
            <table class="data"><tr><th>Notification</th><th>In the app</th><th>E-mail</th></tr>
              @for (p of prefs(); track p.type) {
                <tr><td>{{ p.label }}</td><td><mat-checkbox [(ngModel)]="p.inApp" /></td><td><mat-checkbox [(ngModel)]="p.email" /></td></tr>
              }</table>
            <button mat-flat-button (click)="savePrefs()">Save preferences</button>
            <p class="muted small">E-mails are sent when the platform's SMTP server is configured.</p>
          </section>
        </mat-tab>
        <mat-tab label="Seasons & rules">
          <div class="stack tabpad">
            <section class="card row">
              <mat-form-field><mat-label>Season</mat-label><mat-select [ngModel]="seasonId()" (ngModelChange)="seasonId.set($event)">
                @for (s of seasons.value() ?? []; track s.id) { <mat-option [value]="s.id">{{ s.name }}{{ s.isCurrent ? ' (current)' : '' }}</mat-option> }</mat-select></mat-form-field>
              @if (auth.has(P.seasonsManage) && seasonId() && !currentSeason()?.isCurrent) { <button mat-stroked-button (click)="makeCurrent()">Make current season</button> }
              @if (auth.has(P.seasonsManage)) {
                <span class="spacer"></span>
                <mat-form-field><mat-label>New season year</mat-label><input matInput type="number" [(ngModel)]="newYear" /></mat-form-field>
                <button mat-stroked-button [disabled]="!newYear" (click)="createSeason()">Create (copy rules)</button>
              }
            </section>
            <section class="card">
              <app-state [loading]="rules.isLoading()" [error]="rules.error()" (retry)="rules.reload()">
                <table class="data"><tr><th>Category</th><th>Rule</th><th>Value</th><th>Updated</th></tr>
                  @for (r of rules.value() ?? []; track r.key) {
                    <tr><td>{{ r.category }}</td><td>{{ r.description }}<div class="small muted">{{ r.key }} · {{ r.type }}</div></td>
                      <td>@if (auth.has(P.settingsManage)) { <div class="row"><input class="cell" [value]="r.value" #v /><button mat-button (click)="saveRule(r.key, v.value)">Save</button></div> } @else { <code>{{ r.value }}</code> }</td>
                      <td class="small muted">{{ r.updatedAt | date: 'd MMM yyyy' }}</td></tr>
                  }</table>
                <p class="disclaimer">Season rules drive purse checks, auctions, retentions, trades and Playing XI validation. Set them from the official regulations for each season — nothing is hard-coded.</p>
              </app-state>
            </section>
          </div>
        </mat-tab>
        <mat-tab label="Rating model">
          <section class="card tabpad">
            <p class="muted">Weights of the explainable player rating (they are normalised by their total).</p>
            <div class="form-grid">
              @for (k of weightKeys; track k) { <mat-form-field><mat-label>{{ k }}</mat-label><input matInput type="number" step="0.05" [(ngModel)]="weights[k]" [disabled]="!auth.has(P.settingsManage)" /></mat-form-field> }
            </div>
            @if (auth.has(P.settingsManage)) { <button mat-flat-button (click)="saveWeights()">Save weights</button> }
          </section>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: `.tabpad { margin-top: 16px; } input.cell { background: var(--bg-2); color: var(--text); border: 1px solid var(--line); border-radius: 6px; padding: 6px 8px; width: 100%; min-width: 180px; font-family: monospace; }`,
})
export class SettingsPage {
  readonly tab = input<string>();
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly sessions = rxResource({ stream: () => this.api.get<any[]>('auth/sessions') });
  readonly logins = rxResource({ stream: () => this.api.get<any[]>('auth/login-history') });
  readonly prefRes = rxResource({ stream: () => this.api.get<any[]>('notifications/preferences') });
  readonly prefs = computed(() => (this.prefRes.value() ?? []).map((p) => ({ ...p })));
  readonly seasons = rxResource({ stream: () => this.api.get<any[]>('settings/seasons') });
  readonly seasonId = signal<number | null>(null);
  readonly currentSeason = computed(() => (this.seasons.value() ?? []).find((s) => s.id === this.seasonId()));
  readonly rules = rxResource({ params: () => this.seasonId(), stream: ({ params }) => (params ? this.api.get<any[]>(`settings/seasons/${params}/rules`) : of([])) });
  readonly weightRes = rxResource({ stream: () => this.api.get<any>('settings/rating-weights') });
  readonly weightKeys = ['recentForm', 'career', 'age', 'consistency', 'competitionLevel', 'availability', 'potential'];
  weights: Record<string, number> = {};
  current = '';
  next = '';
  newYear: number | null = null;

  constructor() {
    effect(() => {
      const s = this.seasons.value();
      if (s?.length && this.seasonId() === null) this.seasonId.set((s.find((x) => x.isCurrent) ?? s[0]).id);
    });
    effect(() => {
      const w = this.weightRes.value();
      if (w) this.weights = { ...w };
    });
  }

  changePassword(): void {
    this.api.post('auth/change-password', { currentPassword: this.current, newPassword: this.next }).subscribe({
      next: () => { this.toast.ok('Password changed. Please sign in again.'); this.auth.logout(); },
      error: (e) => this.toast.error(e),
    });
  }

  revoke(id: string): void {
    this.api.delete(`auth/sessions/${id}`).subscribe({ next: () => this.sessions.reload(), error: (e) => this.toast.error(e) });
  }

  savePrefs(): void {
    this.api.put('notifications/preferences', this.prefs()).subscribe({ next: () => this.toast.ok('Preferences saved.'), error: (e) => this.toast.error(e) });
  }

  saveRule(key: string, value: string): void {
    this.api.put(`settings/seasons/${this.seasonId()}/rules/${key}`, { value }).subscribe({ next: () => { this.toast.ok('Rule saved and audited.'); this.rules.reload(); }, error: (e) => this.toast.error(e) });
  }

  makeCurrent(): void {
    this.api.post(`settings/seasons/${this.seasonId()}/current`).subscribe({ next: () => { this.toast.ok('Current season changed.'); this.seasons.reload(); }, error: (e) => this.toast.error(e) });
  }

  createSeason(): void {
    const y = Number(this.newYear);
    this.api.post<any>('settings/seasons', { year: y, name: `Season ${y}`, startDate: `${y}-03-22`, endDate: `${y}-05-28`, status: 'Planning', copyRulesFromSeasonId: this.seasonId() }).subscribe({
      next: (r) => { this.toast.ok('Season created with copied rules.'); this.seasons.reload(); this.seasonId.set(r.id); },
      error: (e) => this.toast.error(e),
    });
  }

  saveWeights(): void {
    this.api.put('settings/rating-weights', this.weights).subscribe({ next: () => this.toast.ok('Weights saved.'), error: (e) => this.toast.error(e) });
  }
}
