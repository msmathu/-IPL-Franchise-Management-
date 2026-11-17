import { Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { Api, Paged } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Toast, WordsPipe } from '../../core/services';
import { Monogram, PageHeader, PlayerAvatar, StateView, StatusChip } from '../../shared/ui';

export interface Lookups {
  countries: { id: number; code: string; name: string }[];
  roles: { id: number; code: string; name: string }[];
  competitions: { id: number; code: string; name: string; group: string }[];
  categories: string[];
  bowlingTypes: string[];
  statuses: string[];
  availabilityStatuses: string[];
}

@Component({
  selector: 'app-players',
  imports: [PageHeader, StateView, StatusChip, Monogram, PlayerAvatar, InrPipe, WordsPipe, FormsModule, ReactiveFormsModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatButtonModule, MatIconModule, MatPaginatorModule, MatCheckboxModule],
  template: `
    <div class="page">
      <app-page-header title="Player database" subtitle="Search every player by role, style, age, team, price, performance and availability">
        @if (auth.has(P.playersManage)) {
          <button mat-stroked-button (click)="file.click()"><mat-icon>upload</mat-icon>Import CSV</button>
          <input #file type="file" accept=".csv" hidden (change)="import(file)" />
          <button mat-flat-button (click)="editing.set(!editing())"><mat-icon>person_add</mat-icon>Add player</button>
        }
      </app-page-header>
      @if (editing() && lookups.value(); as l) {
        <section class="card"><h2>New player</h2>
          <form [formGroup]="form" (ngSubmit)="create()" class="form-grid">
            <mat-form-field><mat-label>Full name</mat-label><input matInput formControlName="fullName" /></mat-form-field>
            <mat-form-field><mat-label>Display name</mat-label><input matInput formControlName="displayName" /></mat-form-field>
            <mat-form-field><mat-label>Date of birth</mat-label><input matInput type="date" formControlName="dateOfBirth" /></mat-form-field>
            <mat-form-field><mat-label>Country</mat-label><mat-select formControlName="countryId">@for (c of l.countries; track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Primary role</mat-label><mat-select formControlName="primaryRoleId">@for (r of l.roles; track r.id) { <mat-option [value]="r.id">{{ r.name }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Batting</mat-label><mat-select formControlName="battingHand"><mat-option value="RightHand">Right-handed</mat-option><mat-option value="LeftHand">Left-handed</mat-option></mat-select></mat-form-field>
            <mat-form-field><mat-label>Bowling type</mat-label><mat-select formControlName="bowlingType">@for (b of l.bowlingTypes; track b) { <mat-option [value]="b">{{ b | words }}</mat-option> }</mat-select></mat-form-field>
            <mat-form-field><mat-label>Bowling style</mat-label><input matInput formControlName="bowlingStyle" placeholder="Right-arm fast-medium" /></mat-form-field>
            <mat-form-field><mat-label>Data source</mat-label><input matInput formControlName="dataSource" placeholder="Provider / licence reference" /></mat-form-field>
            <mat-checkbox formControlName="isWicketKeeper">Wicketkeeper</mat-checkbox>
            <mat-checkbox formControlName="isCapped">Capped international</mat-checkbox>
            <div class="row"><button mat-flat-button [disabled]="form.invalid">Save</button><button mat-button type="button" (click)="editing.set(false)">Cancel</button></div>
          </form>
          <p class="muted small">Only enter data from authorised sources. Photos must be https links you have the rights to use.</p>
        </section>
      }
      <section class="card">
        <div class="filters">
          <mat-form-field class="wide"><mat-label>Search name or code</mat-label><input matInput [ngModel]="f().search" (ngModelChange)="set('search', $event)" /><mat-icon matSuffix>search</mat-icon></mat-form-field>
          <mat-form-field><mat-label>Category</mat-label><mat-select [ngModel]="f().category" (ngModelChange)="set('category', $event)"><mat-option [value]="null">Any</mat-option>
            @for (c of lookups.value()?.categories ?? []; track c) { <mat-option [value]="c">{{ c }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Country</mat-label><mat-select [ngModel]="f().countryId" (ngModelChange)="set('countryId', $event)"><mat-option [value]="null">Any</mat-option>
            @for (c of lookups.value()?.countries ?? []; track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Batting</mat-label><mat-select [ngModel]="f().battingHand" (ngModelChange)="set('battingHand', $event)"><mat-option [value]="null">Any</mat-option>
            <mat-option value="RightHand">Right-handed</mat-option><mat-option value="LeftHand">Left-handed</mat-option></mat-select></mat-form-field>
          <mat-form-field><mat-label>Bowling</mat-label><mat-select [ngModel]="f().bowlingType" (ngModelChange)="set('bowlingType', $event)"><mat-option [value]="null">Any</mat-option>
            @for (b of lookups.value()?.bowlingTypes ?? []; track b) { <mat-option [value]="b">{{ b | words }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Team</mat-label><mat-select [ngModel]="f().franchiseId" (ngModelChange)="set('franchiseId', $event)"><mat-option [value]="null">Any</mat-option>
            @for (fr of franchises.value() ?? []; track fr.id) { <mat-option [value]="fr.id">{{ fr.name }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Status</mat-label><mat-select [ngModel]="f().status" (ngModelChange)="set('status', $event)"><mat-option [value]="null">Any</mat-option>
            @for (s of lookups.value()?.statuses ?? []; track s) { <mat-option [value]="s">{{ s | words }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Availability</mat-label><mat-select [ngModel]="f().availability" (ngModelChange)="set('availability', $event)"><mat-option [value]="null">Any</mat-option>
            @for (s of lookups.value()?.availabilityStatuses ?? []; track s) { <mat-option [value]="s">{{ s | words }}</mat-option> }</mat-select></mat-form-field>
          <mat-form-field><mat-label>Age from</mat-label><input matInput type="number" [ngModel]="f().ageMin" (ngModelChange)="set('ageMin', $event)" /></mat-form-field>
          <mat-form-field><mat-label>Age to</mat-label><input matInput type="number" [ngModel]="f().ageMax" (ngModelChange)="set('ageMax', $event)" /></mat-form-field>
          <mat-form-field><mat-label>Min price (₹ Cr)</mat-label><input matInput type="number" [ngModel]="f().priceMinCr" (ngModelChange)="set('priceMinCr', $event)" /></mat-form-field>
          <mat-form-field><mat-label>Max price (₹ Cr)</mat-label><input matInput type="number" [ngModel]="f().priceMaxCr" (ngModelChange)="set('priceMaxCr', $event)" /></mat-form-field>
          <mat-form-field><mat-label>Min T20 runs</mat-label><input matInput type="number" [ngModel]="f().minRuns" (ngModelChange)="set('minRuns', $event)" /></mat-form-field>
          <mat-form-field><mat-label>Min T20 wickets</mat-label><input matInput type="number" [ngModel]="f().minWickets" (ngModelChange)="set('minWickets', $event)" /></mat-form-field>
          <mat-checkbox [ngModel]="f().overseas" (ngModelChange)="set('overseas', $event || null)">Overseas only</mat-checkbox>
          <mat-checkbox [ngModel]="f().freeAgentsOnly" (ngModelChange)="set('freeAgentsOnly', $event || null)">Unsigned only</mat-checkbox>
          <button mat-button (click)="reset()">Clear filters</button>
        </div>
        <app-state [loading]="res.isLoading()" [error]="res.error()" [empty]="res.value()?.items?.length === 0" emptyText="No players match these filters." (retry)="res.reload()">
          <div class="table-wrap"><table class="data">
            <tr>
              <th (click)="sortBy('name')" class="sortable">Player</th><th (click)="sortBy('age')" class="sortable">Age</th><th (click)="sortBy('country')" class="sortable">Country</th>
              <th>Role</th><th>Style</th><th>Team</th><th>Status</th><th (click)="sortBy('price')" class="sortable num">Price</th>
              <th class="num">T20 M</th><th (click)="sortBy('runs')" class="sortable num">Runs</th><th class="num">SR</th><th (click)="sortBy('wickets')" class="sortable num">Wkts</th><th class="num">Econ</th>
            </tr>
            @for (p of res.value()?.items ?? []; track p.id) {
              <tr class="clickable" (click)="open(p.id)">
                <td><span class="player-cell"><app-avatar [name]="p.fullName" [photoUrl]="p.photoUrl" [color]="p.franchise?.primaryColor ?? '#334155'" [size]="30" /><strong>{{ p.fullName }}</strong></span>@if (p.isOverseas) { <span class="tag os">OS</span> }@if (p.isWicketKeeper) { <span class="tag">WK</span> }@if (p.isDemo) { <span class="tag">Demo</span> }</td>
                <td>{{ p.age ?? '—' }}</td><td>{{ p.country }}</td><td>{{ p.role }}</td>
                <td class="small muted">{{ p.battingHand === 'LeftHand' ? 'LHB' : p.battingHand === 'RightHand' ? 'RHB' : '—' }}{{ p.bowlingStyle ? ' · ' + p.bowlingStyle : '' }}</td>
                <td>@if (p.franchise; as fr) { <span class="row"><app-monogram [name]="fr.name" [code]="fr.code" [logoUrl]="fr.logoUrl" [color]="fr.primaryColor" [size]="26" />{{ fr.code }}</span> } @else { <span class="muted">Unsigned</span> }</td>
                <td><app-chip [text]="p.availability !== 'Available' ? p.availability : (p.status | words)" /></td>
                <td class="num">{{ p.currentPrice ? (p.currentPrice | inr) : '—' }}</td><td class="num">{{ p.t20Matches }}</td><td class="num">{{ p.t20Runs }}</td>
                <td class="num">{{ p.t20StrikeRate ?? '—' }}</td><td class="num">{{ p.t20Wickets }}</td><td class="num">{{ p.t20Economy ?? '—' }}</td>
              </tr>
            }
          </table></div>
          <mat-paginator [length]="res.value()?.total ?? 0" [pageSize]="pageSize()" [pageIndex]="page() - 1" [pageSizeOptions]="[25, 50, 100]" (page)="paged($event)" />
          <p class="muted small">Headline numbers are franchise T20 career totals. Each player's statistics page shows the source and last-updated date of every dataset.</p>
        </app-state>
      </section>
    </div>
  `,
  styles: `.sortable { cursor: pointer; } .sortable:hover { color: var(--text); }`,
})
export class PlayersPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly router = inject(Router);
  readonly franchiseId = input<string>();
  readonly f = signal<any>({});
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly sort = signal<{ sort: string; desc: boolean }>({ sort: 'name', desc: false });
  readonly editing = signal(false);
  readonly lookups = rxResource({ stream: () => this.api.get<Lookups>('players/lookups') });
  readonly franchises = rxResource({ stream: () => this.api.get<any[]>('franchises') });
  private readonly query = computed(() => {
    const f = { ...this.f() };
    const toRupees = (cr: unknown) => (cr === null || cr === undefined || cr === '' ? null : Number(cr) * 1e7);
    return {
      ...f, priceMin: toRupees(f['priceMinCr']), priceMax: toRupees(f['priceMaxCr']), priceMinCr: null, priceMaxCr: null,
      franchiseId: f['franchiseId'] ?? this.franchiseId() ?? null, page: this.page(), pageSize: this.pageSize(), ...this.sort(),
    };
  });
  readonly res = rxResource({ params: () => this.query(), stream: ({ params }) => this.api.get<Paged<any>>('players', params as any) });
  readonly form = inject(FormBuilder).nonNullable.group({
    fullName: ['', Validators.required], displayName: [''], dateOfBirth: ['', Validators.required], countryId: [1, Validators.required],
    primaryRoleId: [1, Validators.required], secondaryRoleId: [null as number | null], battingHand: ['RightHand'], bowlingType: ['None'], bowlingStyle: [''],
    isWicketKeeper: [false], isCapped: [false], status: ['Active'], careerStatus: ['Active'], dataSource: ['', Validators.required], photoUrl: [''],
  });

  set(key: string, value: unknown): void {
    this.f.update((f) => ({ ...f, [key]: value === '' ? null : value }));
    this.page.set(1);
  }

  reset(): void {
    this.f.set({});
    this.page.set(1);
  }

  sortBy(sort: string): void {
    this.sort.update((s) => ({ sort, desc: s.sort === sort ? !s.desc : sort !== 'name' && sort !== 'country' }));
  }

  paged(e: PageEvent): void {
    this.page.set(e.pageIndex + 1);
    this.pageSize.set(e.pageSize);
  }

  open(id: string): void {
    void this.router.navigate(['/players', id]);
  }

  create(): void {
    const v = this.form.getRawValue();
    this.api.post<{ id: string }>('players', { ...v, displayName: v.displayName || v.fullName, photoUrl: v.photoUrl || null }).subscribe({
      next: (r) => { this.toast.ok('Player created.'); this.open(r.id); },
      error: (e) => this.toast.error(e),
    });
  }

  import(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.api.upload<any>('players/import', file).subscribe({
      next: (r) => {
        this.toast.ok(`Imported: ${r.created} new, ${r.updated} updated, ${r.skipped} skipped.${r.errors.length ? ' First problem: ' + r.errors[0] : ''}`);
        this.res.reload();
      },
      error: (e) => this.toast.error(e),
    });
  }
}
