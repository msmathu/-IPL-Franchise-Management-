import { Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { Api, Paged } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Toast } from '../../core/services';
import { PageHeader, StateView } from '../../shared/ui';
import { PlayerAnalysis, PlayerPicker, PlayerStats } from './player-profile';

@Component({
  selector: 'app-statistics',
  imports: [PageHeader, StateView, PlayerPicker, PlayerStats, PlayerAnalysis, RouterLink, MatButtonModule],
  template: `
    <div class="page">
      <app-page-header title="Player statistics & performance" subtitle="Franchise, domestic and international records with match-level analysis">
        @if (auth.has(P.statsImport)) {
          <button mat-stroked-button (click)="file.click()">Import season statistics (CSV)</button>
          <input #file type="file" accept=".csv" hidden (change)="import(file)" />
        }
      </app-page-header>
      <section class="card">
        <div class="row"><app-player-picker label="Choose a player" (picked)="player.set($event)" />
          <span class="muted small">or start from a leader:</span>
          @for (p of leaders.value()?.items ?? []; track p.id) { <button mat-button (click)="player.set(p)">{{ p.fullName }}</button> }
        </div>
        <p class="muted small">Only statistics from authorised sources should be imported. Every row keeps its source and last-updated date; nothing is estimated.</p>
      </section>
      @if (player(); as p) {
        <h2>{{ p.fullName }} <a class="small" [routerLink]="['/players', p.id]">open profile</a></h2>
        <app-player-analysis [playerId]="p.id" [competitions]="competitions()" />
        <app-player-stats [playerId]="p.id" />
      } @else {
        <app-state [empty]="true" emptyText="Choose a player to see their statistics." />
      }
    </div>
  `,
})
export class StatisticsPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly player = signal<any | null>(null);
  readonly leaders = rxResource({ stream: () => this.api.get<Paged<any>>('players', { sort: 'runs', desc: true, pageSize: 4 }) });
  private readonly lookups = rxResource({ stream: () => this.api.get<any>('players/lookups') });
  readonly competitions = computed(() => (this.lookups.value()?.competitions ?? []).filter((c: any) => /T20/.test(c.group)));

  import(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.api.upload<any>('statistics/import', file).subscribe({
      next: (r) => this.toast.ok(`Imported ${r.imported} rows, skipped ${r.skipped}.${r.errors.length ? ' ' + r.errors[0] : ''}`),
      error: (e) => this.toast.error(e),
    });
  }
}
