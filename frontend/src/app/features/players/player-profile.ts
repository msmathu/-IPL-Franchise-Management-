import { DatePipe } from '@angular/common';
import { teamVars } from '../../core/team-theme';
import { roleIcon } from '../../shared/player-card';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { ChartConfiguration } from 'chart.js';
import { debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { Api, Paged } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Toast, WordsPipe } from '../../core/services';
import { ChartView, Monogram, PALETTE, PageHeader, PlayerAvatar, StatCard, StateView, StatusChip } from '../../shared/ui';

/** Autocomplete search over the player database. */
@Component({
  selector: 'app-player-picker',
  imports: [MatFormFieldModule, MatInputModule, MatAutocompleteModule, FormsModule],
  template: `
    <mat-form-field [class.full]="full()">
      <mat-label>{{ label() }}</mat-label>
      <input matInput [ngModel]="term()" (ngModelChange)="term.set($event)" [matAutocomplete]="auto" />
      <mat-autocomplete #auto="matAutocomplete" (optionSelected)="choose($event.option.value)" [displayWith]="display">
        @for (p of results().items ?? []; track p.id) {
          <mat-option [value]="p">{{ p.fullName }} <span class="muted small">· {{ p.role }} · {{ p.franchise?.shortName ?? 'Unsigned' }}</span></mat-option>
        }
      </mat-autocomplete>
    </mat-form-field>
  `,
})
export class PlayerPicker {
  private readonly api = inject(Api);
  readonly label = input('Find a player');
  readonly franchiseId = input<string | null>(null);
  readonly full = input(false);
  readonly picked = output<any>();
  readonly term = signal('');
  readonly results = toSignal(
    toObservable(this.term).pipe(debounceTime(250), distinctUntilChanged(), switchMap((t) =>
      typeof t === 'string' && t.length >= 2 ? this.api.get<Paged<any>>('players', { search: t, pageSize: 12, franchiseId: this.franchiseId() }) : of({ items: [] } as any)),
    ), { initialValue: { items: [] } as any },
  );
  display = (p: any) => (p && typeof p === 'object' ? p.fullName : p);
  choose(p: any): void {
    this.picked.emit(p);
    this.term.set('');
  }
}

/** Career and season tables with data provenance. */
@Component({
  selector: 'app-player-stats',
  imports: [StateView, DatePipe],
  template: `
    <app-state [loading]="res.isLoading()" [error]="res.error()" [empty]="!res.value()?.careers?.length" emptyText="No statistics recorded for this player." (retry)="res.reload()">
      @if (res.value(); as s) {
        @if (s.lastPrimaryT20Season; as last) {
          <p class="muted">Last franchise T20 season ({{ last.year }}, {{ last.team }}): <strong>{{ last.line.runs }} runs</strong> at SR {{ last.line.strikeRate ?? '—' }},
            <strong>{{ last.line.wickets }} wickets</strong> at economy {{ last.line.economy ?? '—' }}.</p>
        }
        @for (group of groups(); track group.type) {
          <section class="card"><h2>{{ group.label }}</h2>
            <div class="table-wrap"><table class="data">
              <tr><th>Competition</th><th>Years</th><th class="num">M</th><th class="num">Inn</th><th class="num">Runs</th><th class="num">Avg</th><th class="num">SR</th><th class="num">HS</th>
                <th class="num">100s</th><th class="num">50s</th><th class="num">4s</th><th class="num">6s</th><th class="num">Wkts</th><th class="num">Avg</th><th class="num">Econ</th><th class="num">SR</th>
                <th class="num">Best</th><th class="num">5w</th><th class="num">Ct</th><th class="num">St</th></tr>
              @for (c of group.rows; track c.competitionCode) {
                <tr><td>{{ c.competition }}</td><td>{{ c.firstYear }}–{{ c.lastYear }}</td><td class="num">{{ c.line.matches }}</td><td class="num">{{ c.line.innings }}</td>
                  <td class="num">{{ c.line.runs }}</td><td class="num">{{ c.line.battingAverage ?? '—' }}</td><td class="num">{{ c.line.strikeRate ?? '—' }}</td>
                  <td class="num">{{ c.line.highestScore }}{{ c.line.highestNotOut ? '*' : '' }}</td><td class="num">{{ c.line.hundreds }}</td><td class="num">{{ c.line.fifties }}</td>
                  <td class="num">{{ c.line.fours }}</td><td class="num">{{ c.line.sixes }}</td><td class="num">{{ c.line.wickets }}</td><td class="num">{{ c.line.bowlingAverage ?? '—' }}</td>
                  <td class="num">{{ c.line.economy ?? '—' }}</td><td class="num">{{ c.line.bowlingStrikeRate ?? '—' }}</td><td class="num">{{ c.line.bestBowling ?? '—' }}</td>
                  <td class="num">{{ c.line.fiveWicketHauls }}</td><td class="num">{{ c.line.catches }}</td><td class="num">{{ c.line.stumpings }}</td></tr>
              }
            </table></div>
            <p class="muted small">Source: {{ sources(group.rows) }} · last updated {{ group.rows[0].lastUpdatedAt | date: 'd MMM yyyy' }}</p>
          </section>
        }
        <section class="card"><h2>Season by season</h2>
          <div class="table-wrap"><table class="data">
            <tr><th>Year</th><th>Competition</th><th>Team</th><th class="num">M</th><th class="num">Runs</th><th class="num">Avg</th><th class="num">SR</th><th class="num">Wkts</th><th class="num">Econ</th><th>Source</th></tr>
            @for (r of s.seasons; track $index) {
              <tr><td>{{ r.year }}</td><td>{{ r.competition }}</td><td>{{ r.team }}</td><td class="num">{{ r.line.matches }}</td><td class="num">{{ r.line.runs }}</td>
                <td class="num">{{ r.line.battingAverage ?? '—' }}</td><td class="num">{{ r.line.strikeRate ?? '—' }}</td><td class="num">{{ r.line.wickets }}</td>
                <td class="num">{{ r.line.economy ?? '—' }}</td><td class="small muted">{{ r.dataSource }}</td></tr>
            }
          </table></div>
        </section>
      }
    </app-state>
  `,
})
export class PlayerStats {
  private readonly api = inject(Api);
  readonly playerId = input.required<string>();
  readonly res = rxResource({ params: () => this.playerId(), stream: ({ params }) => this.api.get<any>(`players/${params}/statistics`) });
  readonly groups = computed(() => {
    const careers: any[] = this.res.value()?.careers ?? [];
    return [
      { type: 'Franchise', label: 'Franchise T20 leagues' },
      { type: 'Domestic', label: 'Domestic cricket' },
      { type: 'International', label: 'International cricket' },
    ].map((g) => ({ ...g, rows: careers.filter((c) => c.type === g.type) })).filter((g) => g.rows.length);
  });
  sources(rows: any[]): string {
    return [...new Set(rows.map((r) => r.dataSource))].join('; ');
  }
}

/** Match-level performance analysis with charts. */
@Component({
  selector: 'app-player-analysis',
  imports: [StateView, StatCard, ChartView, MatFormFieldModule, MatSelectModule, FormsModule, DatePipe],
  template: `
    <div class="row">
      <mat-form-field><mat-label>Competition</mat-label>
        <mat-select [ngModel]="competitionId()" (ngModelChange)="competitionId.set($event)">
          <mat-option [value]="null">All T20 cricket</mat-option>
          @for (c of competitions(); track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }
        </mat-select></mat-form-field>
    </div>
    <app-state [loading]="res.isLoading()" [error]="res.error()" [empty]="res.value()?.matchesAnalysed === 0" emptyText="No match-level data for this selection." (retry)="res.reload()">
      @if (res.value(); as a) {
        <p class="muted small">{{ a.matchesAnalysed }} matches · source: {{ a.dataSources.join('; ') }}@if (a.lastUpdatedAt) { · updated {{ a.lastUpdatedAt | date: 'd MMM yyyy' }} }</p>
        <div class="grid stats">
          <app-stat label="Recent form" [value]="a.formTrend" [tone]="a.formTrend === 'Improving' ? 'good' : a.formTrend === 'Declining' ? 'warn' : 'normal'" />
          <app-stat label="Last 5" [value]="a.last5.runs + ' runs · ' + a.last5.wickets + ' wkts'" [hint]="'SR ' + (a.last5.strikeRate ?? '—') + ' · econ ' + (a.last5.economy ?? '—')" />
          <app-stat label="Last 10" [value]="a.last10.runs + ' runs · ' + a.last10.wickets + ' wkts'" [hint]="'Avg ' + (a.last10.battingAverage ?? '—') + ' · SR ' + (a.last10.strikeRate ?? '—')" />
          <app-stat label="Last 20" [value]="a.last20.runs + ' runs · ' + a.last20.wickets + ' wkts'" [hint]="'Avg ' + (a.last20.battingAverage ?? '—') + ' · econ ' + (a.last20.economy ?? '—')" />
          <app-stat label="Boundary %" [value]="(a.boundaryPercentage ?? '—') + '%'" hint="Share of runs from 4s and 6s" />
          <app-stat label="Dot-ball %" [value]="(a.dotBallPercentage ?? '—') + '%'" hint="Bowling" />
        </div>
        <div class="grid cols-2">
          <section class="card"><h2>Year-wise performance</h2><app-chart [config]="yearChart()" /></section>
          <section class="card"><h2>Phase of innings</h2><app-chart [config]="phaseChart()" />
            <table class="data small"><tr><th>Phase</th><th class="num">Bat SR</th><th class="num">Runs</th><th class="num">Bowl econ</th><th class="num">Wkts</th></tr>
              @for (p of a.phases; track p.phase) { <tr><td>{{ p.phase }}</td><td class="num">{{ p.strikeRate ?? '—' }}</td><td class="num">{{ p.runs }}</td><td class="num">{{ p.economy ?? '—' }}</td><td class="num">{{ p.wickets }}</td></tr> }</table>
          </section>
          <section class="card"><h2>Pace vs spin · home vs away</h2>
            <table class="data"><tr><th></th><th class="num">Runs</th><th class="num">Balls</th><th class="num">SR</th></tr>
              <tr><td>Against pace</td><td class="num">{{ a.runsVsPace }}</td><td class="num">{{ a.ballsVsPace }}</td><td class="num">{{ a.strikeRateVsPace ?? '—' }}</td></tr>
              <tr><td>Against spin</td><td class="num">{{ a.runsVsSpin }}</td><td class="num">{{ a.ballsVsSpin }}</td><td class="num">{{ a.strikeRateVsSpin ?? '—' }}</td></tr></table>
            <table class="data"><tr><th></th><th class="num">M</th><th class="num">Runs</th><th class="num">Avg</th><th class="num">SR</th><th class="num">Wkts</th><th class="num">Econ</th></tr>
              @for (s of [a.home, a.away]; track s.label) { <tr><td>{{ s.label }}</td><td class="num">{{ s.matches }}</td><td class="num">{{ s.runs }}</td><td class="num">{{ s.average ?? '—' }}</td><td class="num">{{ s.strikeRate ?? '—' }}</td><td class="num">{{ s.wickets }}</td><td class="num">{{ s.economy ?? '—' }}</td></tr> }</table>
            <p class="small muted">Wickets by batting hand: {{ a.wicketsVsRightHand }} right-handers, {{ a.wicketsVsLeftHand }} left-handers.</p>
          </section>
          <section class="card"><h2>Dismissal pattern</h2><app-chart [config]="dismissalChart()" [height]="220" /></section>
          <section class="card"><h2>Against opponents</h2>
            <table class="data"><tr><th>Opponent</th><th class="num">M</th><th class="num">Runs</th><th class="num">SR</th><th class="num">Wkts</th><th class="num">Econ</th></tr>
              @for (s of a.byOpponent; track s.label) { <tr><td>{{ s.label }}</td><td class="num">{{ s.matches }}</td><td class="num">{{ s.runs }}</td><td class="num">{{ s.strikeRate ?? '—' }}</td><td class="num">{{ s.wickets }}</td><td class="num">{{ s.economy ?? '—' }}</td></tr> }</table></section>
          <section class="card"><h2>By venue</h2>
            <table class="data"><tr><th>Venue</th><th class="num">M</th><th class="num">Runs</th><th class="num">SR</th><th class="num">Wkts</th><th class="num">Econ</th></tr>
              @for (s of a.byVenue; track s.label) { <tr><td>{{ s.label }}</td><td class="num">{{ s.matches }}</td><td class="num">{{ s.runs }}</td><td class="num">{{ s.strikeRate ?? '—' }}</td><td class="num">{{ s.wickets }}</td><td class="num">{{ s.economy ?? '—' }}</td></tr> }</table></section>
        </div>
        <section class="card"><h2>Recent matches</h2>
          <div class="table-wrap"><table class="data"><tr><th>Date</th><th>Competition</th><th>Opponent</th><th>Venue</th><th class="num">Batting</th><th class="num">Bowling</th></tr>
            @for (m of a.recentMatches; track $index) {
              <tr><td>{{ m.date | date: 'd MMM yyyy' }}</td><td>{{ m.competition }}</td><td>{{ m.opponent }}</td><td>{{ m.venue }}{{ m.isHome ? ' (H)' : '' }}</td>
                <td class="num">{{ m.batted ? m.runs + (m.notOut ? '*' : '') + ' (' + m.balls + ')' : '—' }}</td>
                <td class="num">{{ m.bowled ? m.wickets + '/' + m.runsConceded + ' (' + (m.ballsBowled / 6).toFixed(1) + ' ov)' : '—' }}</td></tr>
            }</table></div></section>
      }
    </app-state>
  `,
})
export class PlayerAnalysis {
  private readonly api = inject(Api);
  readonly playerId = input.required<string>();
  readonly competitions = input<any[]>([]);
  readonly competitionId = signal<number | null>(null);
  readonly res = rxResource({
    params: () => ({ id: this.playerId(), c: this.competitionId() }),
    stream: ({ params }) => this.api.get<any>(`players/${params.id}/analysis`, { competitionId: params.c }),
  });
  readonly yearChart = computed<ChartConfiguration>(() => {
    const years: any[] = this.res.value()?.byYear ?? [];
    return {
      type: 'bar',
      data: { labels: years.map((y) => y.label), datasets: [
        { type: 'bar', label: 'Runs', data: years.map((y) => y.runs), backgroundColor: PALETTE[0], yAxisID: 'y' },
        { type: 'line', label: 'Wickets', data: years.map((y) => y.wickets), borderColor: PALETTE[1], backgroundColor: PALETTE[1], yAxisID: 'y1' } as any,
      ] },
      options: { scales: { y: { position: 'left' }, y1: { position: 'right', grid: { drawOnChartArea: false } } } },
    };
  });
  readonly phaseChart = computed<ChartConfiguration>(() => {
    const p: any[] = this.res.value()?.phases ?? [];
    return { type: 'bar', data: { labels: p.map((x) => x.phase), datasets: [
      { label: 'Batting SR', data: p.map((x) => x.strikeRate ?? 0), backgroundColor: PALETTE[0] },
      { label: 'Economy × 10', data: p.map((x) => (x.economy ?? 0) * 10), backgroundColor: PALETTE[1] },
    ] }, options: { plugins: { tooltip: { callbacks: { label: (c) => c.datasetIndex === 1 ? `Economy ${(Number(c.parsed.y) / 10).toFixed(2)}` : `SR ${c.parsed.y}` } } } } };
  });
  readonly dismissalChart = computed<ChartConfiguration>(() => {
    const d: any[] = this.res.value()?.dismissals ?? [];
    return { type: 'doughnut', data: { labels: d.map((x) => x.label), datasets: [{ data: d.map((x) => x.matches), backgroundColor: PALETTE }] }, options: { plugins: { legend: { position: 'right' } } } };
  });
}

@Component({
  selector: 'app-player-profile',
  imports: [StateView, StatusChip, Monogram, PlayerStats, PlayerAnalysis, InrPipe, WordsPipe, DatePipe, MatTabsModule,
    MatButtonModule, MatIconModule, MatFormFieldModule, MatSelectModule, MatInputModule, FormsModule],
  template: `
    <div class="page">
      <app-state [loading]="res.isLoading()" [error]="res.error()" (retry)="res.reload()">
        @if (res.value(); as p) {
          <section class="team-hero" [style]="heroVars()">
            <div class="player-hero">
              <div class="portrait">
                @if (p.photoUrl && !photoFailed()) { <img [src]="p.photoUrl" [alt]="p.fullName" (error)="photoFailed.set(true)" /> }
                @else { <span class="initials">{{ initials(p.fullName) }}</span> }
              </div>
              <div>
                <div class="first">{{ nameParts(p.fullName).first }}</div>
                <div class="last">{{ nameParts(p.fullName).last }}</div>
                <div class="hero-facts">
                  <div class="fact"><mat-icon>{{ roleIcon(p.roleCode, p.isWicketKeeper) }}</mat-icon><div><b>Role</b><span>{{ p.role }}{{ p.isWicketKeeper && p.roleCode !== 'wk_batter' ? ' · keeper' : '' }}</span></div></div>
                  <div class="fact"><mat-icon>sports_cricket</mat-icon><div><b>Batting</b><span>{{ p.battingHand === 'LeftHand' ? 'Left-handed' : p.battingHand === 'RightHand' ? 'Right-handed' : 'Not recorded' }}</span></div></div>
                  <div class="fact"><mat-icon>sports_baseball</mat-icon><div><b>Bowling</b><span>{{ p.bowlingStyle || (p.bowlingType === 'Unknown' ? 'Not recorded' : 'Does not bowl') }}</span></div></div>
                  <div class="fact"><mat-icon>{{ p.isOverseas ? 'flight' : 'home' }}</mat-icon><div><b>Country</b><span>{{ p.country }} · {{ p.isCapped ? 'Capped' : 'Uncapped' }}</span></div></div>
                  <div class="fact"><mat-icon>cake</mat-icon><div><b>Born</b><span>{{ p.dateOfBirth ? (p.dateOfBirth | date: 'd MMM yyyy') : 'Not recorded' }}</span></div></div>
                  <div class="fact"><mat-icon>currency_rupee</mat-icon><div><b>Price</b><span>{{ p.currentPrice ? (p.currentPrice | inr) : 'Not recorded' }}</span></div></div>
                </div>
              </div>
              <div class="side">
                @if (p.franchise; as fr) {
                  <span class="row"><app-monogram [name]="fr.name" [code]="fr.code" [logoUrl]="fr.logoUrl" [color]="fr.secondaryColor ?? fr.primaryColor" [size]="40" /><strong>{{ fr.name }}</strong></span>
                } @else { <strong>Unsigned</strong> }
                @if (p.isDemo) { <span class="chip">Fictional demo player</span> }
                <span class="chip">{{ p.status | words }}</span><span class="chip">{{ p.availability.status }}</span>
              </div>
            </div>
          </section>
          @if (p.photoCredit) {
            <p class="small muted">{{ p.photoCredit }}@if (p.photoSourceUrl) { · <a [href]="p.photoSourceUrl" target="_blank" rel="noopener">source &amp; licence</a> }</p>
          }
          <mat-tab-group>
            <mat-tab label="Statistics"><div class="stack tabpad"><app-player-stats [playerId]="p.id" /></div></mat-tab>
            <mat-tab label="Performance analysis"><div class="stack tabpad"><app-player-analysis [playerId]="p.id" [competitions]="competitions()" /></div></mat-tab>
            <mat-tab label="Rating & value">
              <div class="grid cols-2 tabpad">
                <section class="card"><h2>Player potential rating</h2>
                  <app-state [loading]="rating.isLoading()" [error]="rating.error()" (retry)="rating.reload()">
                    @if (rating.value(); as r) {
                      @if (r.confidence === 'None') { <p class="warn-text">No statistics are loaded for this player, so the rating below is only a neutral placeholder. Import licensed statistics to rate this player.</p> }
                      <div class="row"><span class="big">{{ r.confidence === 'None' ? '—' : r.rating }}</span><span class="muted">/ 100 · confidence {{ r.confidence }} ({{ r.matchesUsed }} matches)</span></div>
                      <table class="data"><tr><th>Factor</th><th class="num">Score</th><th class="num">Weight</th><th class="num">Points</th><th>How it was calculated</th></tr>
                        @for (f of r.factors; track f.factor) { <tr><td>{{ f.factor }}</td><td class="num">{{ f.score }}</td><td class="num">{{ f.weight }}</td><td class="num">{{ f.contribution }}</td><td class="small">{{ f.explanation }}</td></tr> }</table>
                      <p class="disclaimer">{{ r.method }}</p>
                    }
                  </app-state>
                </section>
                @if (auth.has(P.valuationRead)) {
                  <section class="card"><h2>Estimated value</h2>
                    <app-state [loading]="valuation.isLoading()" [error]="valuation.error()" (retry)="valuation.reload()">
                      @if (valuation.value(); as v) {
                        <div class="row"><span class="big">{{ v.mid | inr }}</span><span class="muted">range {{ v.low | inr }} – {{ v.high | inr }}</span></div>
                        <p>Demand: <strong>{{ v.demandLabel }}</strong> ({{ v.demandIndex }}/100)@if (v.lastKnownPrice) { · last price {{ v.lastKnownPrice | inr }} }</p>
                        @for (f of v.factors; track f.factor) { <div class="list-item"><strong>{{ f.factor }}</strong><span class="muted small">{{ f.effect }}</span><span class="spacer"></span><span class="small">{{ f.explanation }}</span></div> }
                        <p class="disclaimer">{{ v.disclaimer }} Model {{ v.modelVersion }}, {{ v.generatedAt | date: 'd MMM HH:mm' }}.</p>
                        @if (auth.has(P.valuationRun)) { <button mat-stroked-button (click)="rerun()">Re-run valuation</button> }
                      }
                    </app-state>
                  </section>
                }
              </div>
            </mat-tab>
            <mat-tab label="Career & availability">
              <div class="grid cols-2 tabpad">
                <section class="card"><h2>Franchise history</h2>
                  @for (h of p.teamHistory; track $index) { <div class="list-item"><strong>{{ h.year }}</strong><span>{{ h.franchise }}</span><span class="spacer"></span><span class="muted small">{{ h.acquisition }}</span><span>{{ h.price | inr }}</span></div> }
                  @empty { <p class="muted">No franchise history recorded.</p> }
                </section>
                <section class="card"><h2>Availability</h2>
                  <p><app-chip [text]="p.availability.status" /> {{ p.availability.publicNote }}@if (p.availability.expectedReturn) { · expected back {{ p.availability.expectedReturn | date: 'd MMM' }} }</p>
                  <p class="muted small">Detailed medical information is only visible to authorised medical staff.</p>
                  @if (auth.has(P.availabilityManage)) {
                    <div class="row">
                      <mat-form-field><mat-label>Status</mat-label><mat-select [(ngModel)]="availStatus">@for (s of ['Available','Doubtful','Injured','Unavailable','NationalDuty','Personal']; track s) { <mat-option [value]="s">{{ s | words }}</mat-option> }</mat-select></mat-form-field>
                      <mat-form-field><mat-label>Public note</mat-label><input matInput [(ngModel)]="availNote" /></mat-form-field>
                      <mat-form-field><mat-label>Expected return</mat-label><input matInput type="date" [(ngModel)]="availReturn" /></mat-form-field>
                      <button mat-stroked-button (click)="saveAvailability()">Update</button>
                    </div>
                  }
                </section>
              </div>
              @if (auth.has(P.playersManage)) {
                @if (!p.franchise && !p.isDemo) {
                  <section class="card"><h2>Auction pool</h2>
                    <div class="row">
                      @if (p.status === 'AvailableForAuction') {
                        <span class="chip" data-tone="good">Available for auction</span>
                        <span class="small muted">The auctioneer can add this player to an auction (Auction Centre → Add players / Import eligible).</span>
                        <span class="spacer"></span><button mat-button (click)="setStatus(p, 'Active')">Remove from pool</button>
                      } @else {
                        <span class="small muted">This player has no team and is not in the auction pool, so no franchise can sign them yet.</span>
                        <span class="spacer"></span><button mat-flat-button (click)="setStatus(p, 'AvailableForAuction')"><mat-icon>gavel</mat-icon>Make available for auction</button>
                      }
                    </div>
                  </section>
                }
                <section class="card"><h2>Photo</h2>
                  <div class="row"><mat-form-field class="wide"><mat-label>Photo URL (https, licensed image)</mat-label><input matInput [(ngModel)]="photoUrl" /></mat-form-field>
                    <button mat-stroked-button (click)="savePhoto(p)">Save photo</button></div>
                  @if (p.photoCredit) { <p class="small">Current: {{ p.photoCredit }} @if (p.photoSourceUrl) { · <a [href]="p.photoSourceUrl" target="_blank" rel="noopener">source &amp; licence</a> }</p> }
                  <p class="muted small">Use an image you have the rights to (your own photographer, an agency licence, or a Wikimedia Commons file under its licence). Official league or franchise photos need permission.</p>
                </section>
              }
              @if (auth.has(P.statsImport) && !p.isDemo) {
                <section class="card"><h2>Cricsheet link</h2>
                  <div class="row"><mat-form-field><mat-label>Cricsheet id</mat-label><input matInput [(ngModel)]="cricsheetId" [placeholder]="p.cricsheetId ?? 'e.g. ba607b88'" /></mat-form-field>
                    <button mat-stroked-button (click)="saveCricsheet()">Save link</button>
                    @if (p.cricsheetId) { <span class="small muted">Linked to <a [href]="'https://cricsheet.org/register/' + p.cricsheetId" target="_blank" rel="noopener">{{ p.cricsheetId }}</a></span> }
                    @else { <span class="small muted">Not linked: no statistics are loaded for this player.</span> }</div>
                  <p class="muted small">Links are set automatically by the Cricsheet import. Set one by hand only for a player it could not identify; leave the field empty and save to remove the link. Then run the import again (Player Statistics page).</p>
                </section>
              }
              <p class="muted small">Profile source: {{ p.dataSource }} · updated {{ p.updatedAt | date: 'd MMM yyyy' }}</p>
            </mat-tab>
          </mat-tab-group>
        }
      </app-state>
    </div>
  `,
  styles: `.tabpad { margin-top: 16px; } .big { font-size: 30px; font-weight: 700; }`,
})
export class PlayerProfilePage {
  readonly id = input.required<string>();
  readonly photoFailed = signal(false);
  readonly roleIcon = roleIcon;
  readonly heroVars = computed(() => teamVars(this.res.value()?.franchise?.primaryColor ?? '#334155', this.res.value()?.franchise?.secondaryColor ?? '#1e293b'));

  initials(name: string): string {
    return name.replace(/\(.*\)/, '').split(/\s+/).filter(Boolean).map((w) => w[0]).slice(0, 2).join('').toUpperCase();
  }

  nameParts(name: string): { first: string; last: string } {
    const words = name.trim().split(/\s+/);
    return words.length < 2 ? { first: '', last: name } : { first: words.slice(0, -1).join(' '), last: words[words.length - 1] };
  }
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly res = rxResource({ params: () => this.id(), stream: ({ params }) => this.api.get<any>(`players/${params}`) });
  readonly rating = rxResource({ params: () => this.id(), stream: ({ params }) => this.api.get<any>(`players/${params}/rating`) });
  readonly valuation = rxResource({ params: () => this.id(), stream: ({ params }) => (this.auth.has(P.valuationRead) ? this.api.get<any>(`players/${params}/valuation`) : of(null)) });
  private readonly lookups = rxResource({ stream: () => this.api.get<any>('players/lookups') });
  readonly competitions = computed(() => (this.lookups.value()?.competitions ?? []).filter((c: any) => /T20/.test(c.group)));
  availStatus = 'Available';
  photoUrl = '';
  cricsheetId = '';

  saveCricsheet(): void {
    this.api.put(`players/${this.id()}/cricsheet-id`, { cricsheetId: this.cricsheetId.trim() || null }).subscribe({
      next: () => { this.toast.ok('Cricsheet link saved. Run the Cricsheet import to load the matches.'); this.cricsheetId = ''; this.res.reload(); },
      error: (e) => this.toast.error(e),
    });
  }
  availNote = '';
  availReturn = '';

  savePhoto(p: any): void {
    this.api.put(`players/${this.id()}`, {
      fullName: p.fullName, displayName: p.displayName, dateOfBirth: p.dateOfBirth, countryId: p.countryId, photoUrl: this.photoUrl || null,
      battingHand: p.battingHand, bowlingStyle: p.bowlingStyle, bowlingType: p.bowlingType, primaryRoleId: p.primaryRoleId, secondaryRoleId: p.secondaryRoleId,
      isWicketKeeper: p.isWicketKeeper, isCapped: p.isCapped, status: p.status, careerStatus: p.careerStatus, dataSource: p.dataSource,
    }).subscribe({ next: () => { this.toast.ok('Photo saved.'); this.res.reload(); }, error: (e) => this.toast.error(e) });
  }

  setStatus(p: any, status: string): void {
    this.api.put(`players/${this.id()}`, {
      fullName: p.fullName, displayName: p.displayName, dateOfBirth: p.dateOfBirth, countryId: p.countryId, photoUrl: p.photoUrl,
      battingHand: p.battingHand, bowlingStyle: p.bowlingStyle, bowlingType: p.bowlingType, primaryRoleId: p.primaryRoleId, secondaryRoleId: p.secondaryRoleId,
      isWicketKeeper: p.isWicketKeeper, isCapped: p.isCapped, status, careerStatus: p.careerStatus, dataSource: p.dataSource,
    }).subscribe({
      next: () => { this.toast.ok(status === 'AvailableForAuction' ? 'Player added to the auction pool.' : 'Player removed from the auction pool.'); this.res.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  rerun(): void {
    this.api.post(`players/${this.id()}/valuation`).subscribe({ next: () => this.valuation.reload(), error: (e) => this.toast.error(e) });
  }

  saveAvailability(): void {
    this.api.put(`players/${this.id()}/availability`, { status: this.availStatus, publicNote: this.availNote || null, expectedReturn: this.availReturn || null }).subscribe({
      next: () => { this.toast.ok('Availability updated.'); this.res.reload(); },
      error: (e) => this.toast.error(e),
    });
  }
}
