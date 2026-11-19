import { DatePipe } from '@angular/common';
import { PlayerName } from '../../shared/media';
import { Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { ChartConfiguration } from 'chart.js';
import { of } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Toast } from '../../core/services';
import { ChartView, PALETTE, PageHeader, StateView } from '../../shared/ui';
import { PlayerPicker } from './player-profile';

@Component({
  selector: 'app-comparison',
  imports: [PlayerName, PageHeader, StateView, ChartView, PlayerPicker, DatePipe, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatCheckboxModule],
  template: `
    <div class="page">
      <app-page-header title="Player comparison" subtitle="Compare 2–5 players side by side" />
      <section class="card">
        <div class="row">
          <app-player-picker label="Add a player" (picked)="add($event)" />
          @for (p of selected(); track p.id) { <span class="chip"><app-pname [id]="p.id" [name]="p.fullName" [size]="20" [link]="false" /> <button mat-icon-button (click)="remove(p.id)" aria-label="Remove"><mat-icon>close</mat-icon></button></span> }
        </div>
        @if (selected().length < 2) { <p class="muted small">Pick at least two players.</p> }
      </section>
      @if (selected().length >= 2) {
        <app-state [loading]="res.isLoading()" [error]="res.error()" (retry)="res.reload()">
          @if (res.value(); as c) {
            <div class="grid cols-2">
              <section class="card"><h2>Profile radar</h2><app-chart [config]="radar()" [height]="340" /></section>
              <section class="card"><h2>Rating vs estimated value</h2><app-chart [config]="bars()" [height]="340" /></section>
            </div>
            <section class="card table-wrap">
              <table class="data">
                <tr><th></th>@for (p of c.players; track p.playerId) { <th><app-pname [id]="p.playerId" [name]="p.name" [size]="24" /></th> }</tr>
                @for (row of rows; track row.label) {
                  <tr><td class="muted">{{ row.label }}</td>@for (p of c.players; track p.playerId) { <td [class.best]="isBest(row, p, c.players)">{{ row.get(p) ?? '—' }}</td> }</tr>
                }
              </table>
              <p class="disclaimer">{{ c.note }}</p>
            </section>
            @if (auth.has(P.comparisonsManage)) {
              <section class="card row">
                <mat-form-field class="wide"><mat-label>Save as</mat-label><input matInput [(ngModel)]="name" /></mat-form-field>
                <mat-form-field class="wide"><mat-label>Notes</mat-label><input matInput [(ngModel)]="notes" /></mat-form-field>
                <mat-checkbox [(ngModel)]="shared">Share with my franchise</mat-checkbox>
                <button mat-flat-button [disabled]="!name" (click)="save()">Save report</button>
              </section>
            }
          }
        </app-state>
      }
      <section class="card"><h2>Saved comparisons</h2>
        <app-state [loading]="saved.isLoading()" [error]="saved.error()" [empty]="!saved.value()?.length" emptyText="No saved comparisons." (retry)="saved.reload()">
          @for (s of saved.value() ?? []; track s.id) {
            <div class="list-item"><a href="" (click)="$event.preventDefault(); load(s)">{{ s.name }}</a><span class="muted small">{{ s.playerNames.join(' · ') }}</span>
              <span class="spacer"></span><span class="small muted">{{ s.isShared ? 'Shared' : 'Private' }} · {{ s.createdBy }} · {{ s.createdAt | date: 'd MMM' }}</span>
              @if (s.isMine) { <button mat-icon-button (click)="delete(s.id)" aria-label="Delete"><mat-icon>delete</mat-icon></button> }</div>
          }
        </app-state>
      </section>
    </div>
  `,
  styles: `td.best { color: var(--good); font-weight: 600; }`,
})
export class ComparisonPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly selected = signal<{ id: string; fullName: string }[]>([]);
  readonly res = rxResource({
    params: () => this.selected().map((p) => p.id),
    stream: ({ params }) => (params.length >= 2 ? this.api.post<any>('comparisons/compare', { playerIds: params }) : of(null)),
  });
  readonly saved = rxResource({ stream: () => this.api.get<any[]>('comparisons') });
  name = '';
  notes = '';
  shared = true;

  readonly rows: { label: string; get: (p: any) => any; higherBetter?: boolean; lowerBetter?: boolean }[] = [
    { label: 'Age', get: (p) => p.age },
    { label: 'Nationality', get: (p) => p.country },
    { label: 'Role', get: (p) => p.role },
    { label: 'Team', get: (p) => p.franchise ?? 'Unsigned' },
    { label: 'Availability', get: (p) => p.availability },
    { label: 'Franchise T20 experience (matches)', get: (p) => p.franchiseT20Matches, higherBetter: true },
    { label: 'Franchise T20 runs', get: (p) => p.t20Franchise?.runs, higherBetter: true },
    { label: 'Batting average', get: (p) => p.t20Franchise?.battingAverage, higherBetter: true },
    { label: 'Strike rate', get: (p) => p.t20Franchise?.strikeRate, higherBetter: true },
    { label: 'Franchise T20 wickets', get: (p) => p.t20Franchise?.wickets, higherBetter: true },
    { label: 'Bowling average', get: (p) => p.t20Franchise?.bowlingAverage, lowerBetter: true },
    { label: 'Economy', get: (p) => p.t20Franchise?.economy, lowerBetter: true },
    { label: 'Recent form (last 10)', get: (p) => `${p.recentForm.runs} r, ${p.recentForm.wickets} w` },
    { label: 'Domestic', get: (p) => (p.domestic ? `${p.domestic.matches} M · ${p.domestic.runs} r · ${p.domestic.wickets} w` : null) },
    { label: 'International', get: (p) => (p.international ? `${p.international.matches} M · ${p.international.runs} r · ${p.international.wickets} w` : null) },
    { label: 'Model rating', get: (p) => p.rating, higherBetter: true },
    { label: 'Estimated value', get: (p) => p.valueRange },
  ];

  readonly radar = computed<ChartConfiguration>(() => {
    const c = this.res.value();
    return {
      type: 'radar',
      data: { labels: c?.radarAxes ?? [], datasets: (c?.players ?? []).map((p: any, i: number) => ({
        label: p.name, data: (c.radarAxes as string[]).map((a) => p.radar[a] ?? 0), borderColor: PALETTE[i], backgroundColor: PALETTE[i] + '33', pointBackgroundColor: PALETTE[i],
      })) },
      options: { scales: { r: { min: 0, max: 100, ticks: { display: false }, grid: { color: 'rgba(159,176,204,0.2)' }, angleLines: { color: 'rgba(159,176,204,0.2)' } } } },
    };
  });

  readonly bars = computed<ChartConfiguration>(() => {
    const ps: any[] = this.res.value()?.players ?? [];
    return {
      type: 'bar',
      data: { labels: ps.map((p) => p.name), datasets: [
        { label: 'Rating', data: ps.map((p) => p.rating), backgroundColor: PALETTE[0], yAxisID: 'y' },
        { label: 'Estimated value (₹ Cr)', data: ps.map((p) => (p.estimatedValue ?? 0) / 1e7), backgroundColor: PALETTE[1], yAxisID: 'y1' },
      ] },
      options: { scales: { y: { min: 0, max: 100 }, y1: { position: 'right', grid: { drawOnChartArea: false } } } },
    };
  });

  isBest(row: any, p: any, all: any[]): boolean {
    if (!row.higherBetter && !row.lowerBetter) return false;
    const values = all.map((x) => row.get(x)).filter((v) => typeof v === 'number');
    const v = row.get(p);
    if (typeof v !== 'number' || values.length < 2) return false;
    return v === (row.higherBetter ? Math.max(...values) : Math.min(...values));
  }

  add(p: any): void {
    if (this.selected().some((x) => x.id === p.id)) return;
    if (this.selected().length >= 5) return this.toast.error(new Error('Compare at most 5 players.'));
    this.selected.update((s) => [...s, { id: p.id, fullName: p.fullName }]);
  }

  remove(id: string): void {
    this.selected.update((s) => s.filter((p) => p.id !== id));
  }

  load(s: any): void {
    this.selected.set(s.playerIds.map((id: string, i: number) => ({ id, fullName: s.playerNames[i] })));
  }

  save(): void {
    this.api.post('comparisons', { name: this.name, playerIds: this.selected().map((p) => p.id), notes: this.notes || null, isShared: this.shared }).subscribe({
      next: () => { this.toast.ok('Comparison saved.'); this.name = ''; this.saved.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  delete(id: string): void {
    this.api.delete(`comparisons/${id}`).subscribe({ next: () => this.saved.reload(), error: (e) => this.toast.error(e) });
  }
}
