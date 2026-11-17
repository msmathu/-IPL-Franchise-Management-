import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { Router, RouterLink } from '@angular/router';
import { ChartConfiguration } from 'chart.js';
import { of } from 'rxjs';
import { Api, Paged } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Toast, WordsPipe } from '../../core/services';
import { ChartView, Monogram, PALETTE, PageHeader, StatCard, StateView, StatusChip } from '../../shared/ui';

@Component({
  selector: 'app-franchises',
  imports: [PageHeader, StateView, Monogram, RouterLink, InrPipe, MatButtonModule, MatIconModule, ReactiveFormsModule, MatFormFieldModule, MatInputModule],
  template: `
    <div class="page">
      <app-page-header title="Franchises" subtitle="Every franchise in the league">
        @if (auth.has(P.franchisesManage)) { <button mat-flat-button (click)="creating.set(!creating())"><mat-icon>add</mat-icon>New franchise</button> }
      </app-page-header>
      @if (creating()) {
        <section class="card"><h2>Create franchise</h2>
          <form [formGroup]="form" (ngSubmit)="create()" class="form-grid">
            @for (f of fields; track f.key) {
              <mat-form-field><mat-label>{{ f.label }}</mat-label><input matInput [formControlName]="f.key" [type]="f.type ?? 'text'" /></mat-form-field>
            }
            <div class="row"><button mat-flat-button [disabled]="form.invalid || busy()">Create</button><button mat-button type="button" (click)="creating.set(false)">Cancel</button></div>
          </form>
          <p class="muted small">Use your own branding. Official league or team logos must not be used without permission.</p>
        </section>
      }
      <app-state [loading]="res.isLoading()" [error]="res.error()" [empty]="!res.value()?.length" (retry)="res.reload()">
        <div class="grid cols-3">
          @for (f of res.value(); track f.id) {
            <a class="card fr" [routerLink]="['/franchises', f.id]" [style.border-top-color]="f.primaryColor">
              <div class="row"><app-monogram [name]="f.name" [color]="f.primaryColor" [size]="44" />
                <div><h2>{{ f.name }}</h2><span class="muted">{{ f.city }} · {{ f.homeGround }}</span></div></div>
              <div class="row small">
                <span>🏆 {{ f.titles }} title{{ f.titles === 1 ? '' : 's' }}</span><span>{{ f.squadSize }} players</span>
                @if (f.purseRemaining !== null) { <span>Purse {{ f.purseRemaining | inr }}</span> }
                @if (!f.isActive) { <span class="bad-text">Inactive</span> }
                @if (f.isDemo) { <span class="tag">Demo</span> }
              </div>
            </a>
          }
        </div>
      </app-state>
    </div>
  `,
  styles: `.fr { display: flex; flex-direction: column; gap: 12px; border-top: 4px solid; color: var(--text); text-decoration: none !important; } .fr:hover { background: var(--card-2); } .fr h2 { margin: 0; }`,
})
export class FranchisesPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly router = inject(Router);
  readonly res = rxResource({ stream: () => this.api.get<any[]>('franchises', { includeInactive: this.auth.has(P.franchisesManage) }) });
  readonly creating = signal(false);
  readonly busy = signal(false);
  readonly fields = FRANCHISE_FIELDS;
  readonly form = franchiseForm(inject(FormBuilder));

  create(): void {
    this.busy.set(true);
    this.api.post<{ id: string }>('franchises', this.form.getRawValue()).subscribe({
      next: (r) => { this.toast.ok('Franchise created with this season\'s purse.'); void this.router.navigate(['/franchises', r.id]); },
      error: (e) => { this.toast.error(e); this.busy.set(false); },
    });
  }
}

const FRANCHISE_FIELDS: { key: string; label: string; type?: string }[] = [
  { key: 'code', label: 'Code (2–5 letters)' }, { key: 'name', label: 'Name' }, { key: 'shortName', label: 'Short name' }, { key: 'city', label: 'City' },
  { key: 'homeGround', label: 'Home ground' }, { key: 'ownerName', label: 'Owner' }, { key: 'ownerCompany', label: 'Owner company' }, { key: 'ownerEmail', label: 'Owner e-mail', type: 'email' },
  { key: 'foundedYear', label: 'Founded', type: 'number' }, { key: 'primaryColor', label: 'Primary colour', type: 'color' }, { key: 'secondaryColor', label: 'Secondary colour', type: 'color' },
  { key: 'logoUrl', label: 'Logo URL (https, licensed)' },
];

function franchiseForm(fb: FormBuilder) {
  return fb.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z]{2,5}$/)]], name: ['', Validators.required], shortName: ['', Validators.required],
    city: ['', Validators.required], homeGround: ['', Validators.required], ownerName: ['', Validators.required], ownerCompany: [''], ownerEmail: ['', Validators.email],
    foundedYear: [null as number | null], primaryColor: ['#1d4ed8'], secondaryColor: ['#f59e0b'], logoUrl: [''],
  });
}

@Component({
  selector: 'app-franchise-detail',
  imports: [PageHeader, StateView, StatCard, Monogram, StatusChip, ChartView, RouterLink, InrPipe, WordsPipe, DatePipe, DecimalPipe, MatTabsModule, MatButtonModule,
    MatIconModule, ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <div class="page">
      <app-state [loading]="res.isLoading()" [error]="res.error()" (retry)="res.reload()">
        @if (res.value(); as f) {
          <div class="row">
            <app-monogram [name]="f.name" [color]="f.primaryColor" [size]="56" />
            <app-page-header [title]="f.name" [subtitle]="f.city + ' · ' + f.homeGround + (f.foundedYear ? ' · since ' + f.foundedYear : '')" />
            <span class="spacer"></span>
            @if (f.isDemo) { <span class="tag">Fictional demo franchise</span> }
            @if (canEdit()) { <button mat-stroked-button (click)="startEdit(f)"><mat-icon>edit</mat-icon>Edit</button> }
            @if (auth.has(P.franchisesManage)) {
              <button mat-stroked-button (click)="setActive(!f.isActive)">{{ f.isActive ? 'Deactivate' : 'Activate' }}</button>
            }
          </div>
          @if (editing()) {
            <section class="card"><h2>Edit franchise</h2>
              <form [formGroup]="form" (ngSubmit)="save()" class="form-grid">
                @for (field of fields; track field.key) { <mat-form-field><mat-label>{{ field.label }}</mat-label><input matInput [formControlName]="field.key" [type]="field.type ?? 'text'" /></mat-form-field> }
                <div class="row"><button mat-flat-button [disabled]="form.invalid">Save</button><button mat-button type="button" (click)="editing.set(false)">Cancel</button></div>
              </form></section>
          }
          <div class="grid stats">
            <app-stat label="Championships" [value]="f.championships" />
            <app-stat label="Play-off finishes" [value]="f.playoffs" />
            <app-stat label="Owner" [value]="f.ownerName" [hint]="f.ownerCompany" />
            @if (f.currentSeason?.purseRemaining !== null && f.currentSeason) {
              <app-stat label="Purse remaining" [value]="f.currentSeason.purseRemaining | inr" [hint]="'of ' + (f.currentSeason.purseTotal | inr)" />
            }
          </div>
          <mat-tab-group>
            <mat-tab label="History">
              <div class="grid cols-2 tabpad">
                <section class="card"><h2>Season results</h2>
                  <table class="data"><tr><th>Season</th><th class="num">Pos</th><th class="num">W</th><th class="num">L</th><th class="num">Pts</th><th class="num">NRR</th><th></th></tr>
                    @for (s of f.history; track s.seasonId) {
                      <tr><td>{{ s.seasonName }}</td><td class="num">{{ s.finalPosition ?? '—' }}</td><td class="num">{{ s.won }}</td><td class="num">{{ s.lost }}</td><td class="num">{{ s.points }}</td>
                        <td class="num">{{ s.netRunRate | number: '1.3-3' }}</td><td>@if (s.isChampion) { 🏆 Champions } @else if (s.isRunnerUp) { Runners-up }</td></tr>
                    }</table></section>
                <section class="card"><h2>Finishing position by season</h2><app-chart [config]="historyChart()" /></section>
              </div>
            </mat-tab>
            <mat-tab label="Management & staff">
              <div class="grid cols-2 tabpad">
                <section class="card"><h2>Staff</h2>
                  @for (s of f.staff; track s.id) {
                    <div class="list-item"><strong>{{ s.name }}</strong><span class="muted">{{ s.position }}</span><span class="spacer"></span><app-chip [text]="s.category" />
                      @if (canEdit()) { <button mat-icon-button (click)="removeStaff(s.id)" aria-label="Remove"><mat-icon>delete</mat-icon></button> }</div>
                  }
                  @if (canEdit()) {
                    <form [formGroup]="staffForm" (ngSubmit)="addStaff()" class="row">
                      <mat-form-field><mat-label>Name</mat-label><input matInput formControlName="name" /></mat-form-field>
                      <mat-form-field><mat-label>Position</mat-label><input matInput formControlName="position" /></mat-form-field>
                      <mat-form-field><mat-label>Category</mat-label><mat-select formControlName="category">
                        <mat-option value="Management">Management</mat-option><mat-option value="Coaching">Coaching</mat-option><mat-option value="Support">Support</mat-option></mat-select></mat-form-field>
                      <button mat-stroked-button [disabled]="staffForm.invalid">Add</button>
                    </form>
                  }
                </section>
                <section class="card"><h2>Platform users</h2>
                  @for (m of f.members; track m.userId) {
                    <div class="list-item"><strong>{{ m.fullName }}</strong><span class="muted small">{{ m.jobTitle }}</span><span class="spacer"></span><span class="small">{{ m.roles.join(', ') }}</span></div>
                  } @empty { <p class="muted">Only visible to the franchise and the league.</p> }
                </section>
              </div>
            </mat-tab>
            <mat-tab label="Squad">
              <div class="tabpad"><a mat-stroked-button [routerLink]="['/players']" [queryParams]="{ franchiseId: f.id }">Open squad in the player database</a></div>
            </mat-tab>
            @if (auth.has(P.auditRead) && f.canViewConfidential) {
              <mat-tab label="Activity">
                <section class="card tabpad">
                  @for (a of activity.value()?.items ?? []; track $index) {
                    <div class="list-item"><span class="small muted">{{ a.at | date: 'd MMM HH:mm' }}</span><span>{{ a.action | words }}</span><span class="spacer"></span><span class="small muted">{{ a.userEmail }}</span></div>
                  } @empty { <p class="muted">No activity yet.</p> }
                </section>
              </mat-tab>
            }
          </mat-tab-group>
        }
      </app-state>
    </div>
  `,
  styles: `.tabpad { margin-top: 16px; }`,
})
export class FranchiseDetailPage {
  readonly id = input.required<string>();
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly fb = inject(FormBuilder);
  readonly res = rxResource({ params: () => this.id(), stream: ({ params }) => this.api.get<any>(`franchises/${params}`) });
  readonly activity = rxResource({
    params: () => (this.res.value()?.canViewConfidential && this.auth.has(P.auditRead) ? this.id() : null),
    stream: ({ params }) => (params ? this.api.get<Paged<any>>(`franchises/${params}/activity`, { pageSize: 30 }) : of(null)),
  });
  readonly editing = signal(false);
  readonly fields = FRANCHISE_FIELDS;
  readonly form = franchiseForm(this.fb);
  readonly staffForm = this.fb.nonNullable.group({ name: ['', Validators.required], position: ['', Validators.required], category: ['Coaching'] });
  readonly canEdit = computed(() => this.auth.has(P.franchisesManage) || (this.auth.has(P.franchisesEdit) && this.auth.user()?.activeFranchiseId === this.id()));

  readonly historyChart = computed<ChartConfiguration>(() => {
    const h = [...(this.res.value()?.history ?? [])].reverse();
    return {
      type: 'line',
      data: { labels: h.map((s: any) => s.year), datasets: [{ label: 'Position', data: h.map((s: any) => s.finalPosition), borderColor: PALETTE[0], backgroundColor: PALETTE[0], tension: 0.3 }] },
      options: { scales: { y: { reverse: true, min: 1, ticks: { stepSize: 1 } } }, plugins: { legend: { display: false } } },
    };
  });

  startEdit(f: any): void {
    this.form.patchValue({ ...f, ownerCompany: f.ownerCompany ?? '', ownerEmail: f.ownerEmail ?? '', logoUrl: f.logoUrl ?? '' });
    this.editing.set(true);
  }

  save(): void {
    this.api.put(`franchises/${this.id()}`, this.form.getRawValue()).subscribe({
      next: () => { this.toast.ok('Saved.'); this.editing.set(false); this.res.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  setActive(active: boolean): void {
    this.api.post(`franchises/${this.id()}/${active ? 'activate' : 'deactivate'}`).subscribe({ next: () => this.res.reload(), error: (e) => this.toast.error(e) });
  }

  addStaff(): void {
    this.api.post(`franchises/${this.id()}/staff`, this.staffForm.getRawValue()).subscribe({
      next: () => { this.staffForm.reset({ name: '', position: '', category: 'Coaching' }); this.res.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  removeStaff(staffId: string): void {
    this.api.delete(`franchises/${this.id()}/staff/${staffId}`).subscribe({ next: () => this.res.reload(), error: (e) => this.toast.error(e) });
  }
}
