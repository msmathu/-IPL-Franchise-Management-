import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnDestroy, signal } from '@angular/core';
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
  imports: [PageHeader, StateView, PlayerPicker, PlayerStats, PlayerAnalysis, RouterLink, MatButtonModule, DatePipe],
  template: `
    <div class="page">
      <app-page-header title="Player statistics & performance" subtitle="Franchise, domestic and international records with match-level analysis">
        @if (auth.has(P.statsImport)) {
          <button mat-stroked-button (click)="file.click()">Import season statistics (CSV)</button>
          <input #file type="file" accept=".csv" hidden (change)="import(file)" />
        }
      </app-page-header>
      @if (cricsheet.value(); as cs) {
        <section class="card">
          <div class="row"><h2>Ball-by-ball data: Cricsheet</h2><span class="spacer"></span>
            @if (cs.running) { <span class="chip" data-tone="warn">{{ cs.stage }}…</span> }
            @else if (auth.has(P.statsImport)) { <button mat-flat-button (click)="runCricsheet()">Import / update from Cricsheet</button> }
          </div>
          <p class="small">Every IPL match since 2008 and men's T20 internationals, from <a href="https://cricsheet.org" target="_blank" rel="noopener">Cricsheet</a>,
            used under the <a [href]="cs.licenceUrl" target="_blank" rel="noopener">{{ cs.licence }}</a>. Statistics are calculated from the ball-by-ball records.</p>
          @if (cs.lastRun; as r) {
            @if (r.succeeded) {
              <div class="grid cols-4">
                <div class="stat-card"><div class="label">Players with match data</div><div class="value">{{ r.playersWithData }} / {{ r.playersTotal }}</div></div>
                <div class="stat-card"><div class="label">Matches read</div><div class="value">{{ r.matchesRead }}</div></div>
                <div class="stat-card"><div class="label">Added in last run</div><div class="value">{{ r.matchesAdded }} matches · {{ r.performancesAdded }} lines</div></div>
                <div class="stat-card"><div class="label">Last run</div><div class="value small">{{ r.finishedAt | date: 'd MMM yyyy HH:mm' }}</div></div>
              </div>
              @if (r.unlinked.length) {
                <details><summary class="small">{{ r.unlinked.length }} squad players have no Cricsheet record yet (mostly players without an IPL or T20I appearance)</summary>
                  <p class="small muted">{{ r.unlinked.join(', ') }}</p>
                  <p class="small muted">If one of them has played, set their Cricsheet id on the player profile (Career &amp; availability tab) and run the import again.</p>
                </details>
              }
            } @else { <p class="warn-text">The last import failed: {{ r.error }}</p> }
          } @else { <p class="muted small">Not imported yet.</p> }
          @if (cs.nextAutomaticRun) { <p class="muted small">Next automatic update: {{ cs.nextAutomaticRun | date: 'd MMM yyyy' }}</p> }
        </section>
      }
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
export class StatisticsPage implements OnDestroy {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly player = signal<any | null>(null);
  readonly leaders = rxResource({ stream: () => this.api.get<Paged<any>>('players', { sort: 'runs', desc: true, pageSize: 4 }) });
  private readonly lookups = rxResource({ stream: () => this.api.get<any>('players/lookups') });
  readonly cricsheet = rxResource({ stream: () => this.api.get<any>('statistics/cricsheet') });
  private poll?: ReturnType<typeof setInterval>;

  runCricsheet(): void {
    this.api.post<any>('statistics/cricsheet/import').subscribe({
      next: () => {
        this.toast.ok('Cricsheet import started. It usually takes a minute or two.');
        this.cricsheet.reload();
        clearInterval(this.poll);
        this.poll = setInterval(() => {
          this.cricsheet.reload();
          const v = this.cricsheet.value();
          if (v && !v.running) { clearInterval(this.poll); this.leaders.reload(); }
        }, 4000);
      },
      error: (e) => this.toast.error(e),
    });
  }

  ngOnDestroy(): void { clearInterval(this.poll); }

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
