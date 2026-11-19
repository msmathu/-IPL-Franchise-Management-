import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { rxResource } from '../../core/resource';
import { teamVars } from '../../core/team-theme';
import { Monogram, StateView } from '../../shared/ui';

const TEAMS = ['CSK', 'DC', 'GT', 'KKR', 'LSG', 'MI', 'PBKS', 'RCB', 'RR', 'SRH'];

@Component({
  selector: 'app-season-centre',
  imports: [StateView, Monogram, RouterLink, DatePipe, DecimalPipe, NgTemplateOutlet, FormsModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatSelectModule, MatTooltipModule],
  template: `
    <div class="page">
      <section class="team-hero">
        <div class="eyebrow">Season centre</div>
        <h1>IPL {{ year() ?? '' }}</h1>
        <div class="hero-facts">
          <div class="fact"><mat-icon>sports_cricket</mat-icon><div><b>Matches</b><span>{{ fixtures.value()?.length ?? '—' }}{{ team() ? ' (' + team() + ')' : '' }}</span></div></div>
          @if (leader(); as l) { <div class="fact"><mat-icon>emoji_events</mat-icon><div><b>Top of the table</b><span>{{ l.team.name }} · {{ l.points }} pts</span></div></div> }
          @if (champion(); as c) { <div class="fact"><mat-icon>military_tech</mat-icon><div><b>Champions</b><span>{{ c }}</span></div></div> }
        </div>
      </section>

      <div class="row filters">
        <mat-button-toggle-group [value]="tab()" (change)="tab.set($event.value)">
          <mat-button-toggle value="results"><mat-icon>scoreboard</mat-icon> Results</mat-button-toggle>
          <mat-button-toggle value="table"><mat-icon>leaderboard</mat-icon> Points table</mat-button-toggle>
          <mat-button-toggle value="leaders"><mat-icon>workspace_premium</mat-icon> Leaders</mat-button-toggle>
          <mat-button-toggle value="records"><mat-icon>military_tech</mat-icon> Records</mat-button-toggle>
          <mat-button-toggle value="h2h"><mat-icon>compare_arrows</mat-icon> Head-to-head</mat-button-toggle>
        </mat-button-toggle-group>
        <span class="spacer"></span>
        @if (tab() === 'results') {
          <mat-form-field><mat-label>Filter by team</mat-label>
            <mat-select [ngModel]="team()" (ngModelChange)="team.set($event)">
              <mat-option [value]="null">All teams</mat-option>
              @for (t of teams; track t) { <mat-option [value]="t">{{ t }}</mat-option> }
            </mat-select></mat-form-field>
        }
        @if (tab() === 'h2h') {
          <mat-form-field><mat-label>Team</mat-label><mat-select [ngModel]="h2hA()" (ngModelChange)="h2hA.set($event)">@for (t of teams; track t) { <mat-option [value]="t">{{ t }}</mat-option> }</mat-select></mat-form-field>
          <span class="vs-txt">vs</span>
          <mat-form-field><mat-label>Opponent</mat-label><mat-select [ngModel]="h2hB()" (ngModelChange)="h2hB.set($event)">@for (t of teams; track t) { <mat-option [value]="t">{{ t }}</mat-option> }</mat-select></mat-form-field>
        }
        @if (tab() !== 'records' && tab() !== 'h2h') {
        <mat-form-field><mat-label>Season</mat-label>
          <mat-select [ngModel]="year()" (ngModelChange)="year.set($event)">
            @for (y of years.value()?.years ?? []; track y) { <mat-option [value]="y">{{ y }}</mat-option> }
          </mat-select></mat-form-field>
        }
      </div>

      @if (tab() === 'results') {
        <app-state [loading]="fixtures.isLoading()" [error]="fixtures.error()" [empty]="!fixtures.value()?.length" emptyText="No matches recorded for this season." (retry)="fixtures.reload()">
          <div class="fixtures">
            @for (f of fixtures.value() ?? []; track f.id) {
              <a class="fx" [routerLink]="['/season/match', f.id]">
                <header><span>{{ f.stage ?? (f.matchNumber ? 'Match ' + f.matchNumber : 'Match') }}</span><span>{{ f.noResult ? 'No result' : 'Result' }}</span></header>
                <div class="when">{{ f.date | date: 'd MMMM yyyy' }}</div>
                <div class="where"><mat-icon>location_on</mat-icon>{{ f.venue }}</div>
                <div class="teams">
                  @for (side of [f.team1, f.team2]; track $index) {
                    <div class="side" [class.won]="f.winner === side.team.name">
                      <span class="badge" [style]="vars(side.team)"><app-monogram [name]="side.team.name" [code]="side.team.code" [color]="side.team.primaryColor ?? '#475569'" [size]="58" /></span>
                      <strong class="code">{{ side.team.code }}</strong>
                      @if (side.runs !== null) {
                        <span class="score">{{ side.runs }}/{{ side.wickets }}</span><span class="overs">({{ side.overs }})</span>
                      } @else { <span class="score dnb">Did not bat</span> }
                    </div>
                    @if ($index === 0) { <span class="vs">VS</span> }
                  }
                </div>
                <footer>{{ f.result ?? 'Result not recorded' }}<span class="sc-link">Scorecard ›</span></footer>
              </a>
            }
          </div>
          <p class="muted small">Source: {{ years.value()?.source }}. Upcoming fixtures appear once the season schedule is published.</p>
        </app-state>
      } @else if (tab() === 'leaders') {
        <app-state [loading]="leaders.isLoading()" [error]="leaders.error()" [empty]="!leaders.value()?.length" (retry)="leaders.reload()">
          <div class="boards">@for (b of leaders.value() ?? []; track b.key) { <ng-container *ngTemplateOutlet="board; context: { $implicit: b }" /> }</div>
          <p class="muted small">IPL {{ year() }}, every player who played (also those not in a current squad). Source: {{ years.value()?.source }}.</p>
        </app-state>
      } @else if (tab() === 'records') {
        <app-state [loading]="records.isLoading()" [error]="records.error()" (retry)="records.reload()">
          @if (records.value(); as r) {
            <h2 class="section-title"><mat-icon>person</mat-icon>All-time player records</h2>
            <div class="boards">@for (b of r.players; track b.key) { <ng-container *ngTemplateOutlet="board; context: { $implicit: b }" /> }</div>
            <h2 class="section-title"><mat-icon>groups</mat-icon>Team totals</h2>
            <div class="grid cols-2">
              @for (t of [{ title: 'Highest totals', icon: 'trending_up', rows: r.highestTotals }, { title: 'Lowest completed totals', icon: 'trending_down', rows: r.lowestTotals }]; track t.title) {
                <section class="card board"><h3><mat-icon>{{ t.icon }}</mat-icon>{{ t.title }}</h3>
                  @for (x of t.rows; track $index) {
                    <a class="brow" [routerLink]="['/season/match', x.matchId]"><span class="rank">{{ $index + 1 }}</span><span class="who"><strong>{{ x.teamCode }}</strong> v {{ x.opponent }}</span>
                      <span class="val">{{ x.runs }}/{{ x.wickets }}</span><span class="det">{{ x.overs }} ov · {{ x.year }}</span></a>
                  }
                </section>
              }
            </div>
            <p class="muted small">IPL 2008 onwards. Source: {{ r.source }}.</p>
          }
        </app-state>
      } @else if (tab() === 'h2h') {
        <app-state [loading]="h2h.isLoading()" [error]="h2h.error()" (retry)="h2h.reload()">
          @if (h2h.value(); as h) {
            <section class="card h2h">
              <div class="h2h-side" [style]="vars(h.team1)"><app-monogram [name]="h.team1.name" [code]="h.team1.code" [color]="h.team1.primaryColor ?? '#475569'" [size]="64" /><strong>{{ h.team1.name }}</strong><span class="big">{{ h.team1Wins }}</span><span class="muted small">wins</span></div>
              <div class="h2h-mid"><span class="big">{{ h.played }}</span><span class="muted">matches</span>@if (h.noResult) { <span class="small muted">{{ h.noResult }} no result</span> }
                <div class="bar"><i [style.width.%]="h.played ? h.team1Wins * 100 / h.played : 50" [style.background]="h.team1.primaryColor"></i><i [style.width.%]="h.played ? h.team2Wins * 100 / h.played : 50" [style.background]="h.team2.primaryColor"></i></div></div>
              <div class="h2h-side" [style]="vars(h.team2)"><app-monogram [name]="h.team2.name" [code]="h.team2.code" [color]="h.team2.primaryColor ?? '#475569'" [size]="64" /><strong>{{ h.team2.name }}</strong><span class="big">{{ h.team2Wins }}</span><span class="muted small">wins</span></div>
            </section>
            <div class="grid cols-2">
              <section class="card board"><h3><mat-icon>trending_up</mat-icon>Highest totals in this fixture</h3>
                @for (x of [h.team1Highest, h.team2Highest]; track $index) { @if (x) {
                  <a class="brow norank" [routerLink]="['/season/match', x.matchId]"><span class="who"><strong>{{ x.teamCode }}</strong> v {{ x.opponent }}</span><span class="val">{{ x.runs }}/{{ x.wickets }}</span><span class="det">{{ x.year }}</span></a>
                } }
              </section>
              <section class="card board"><h3><mat-icon>history</mat-icon>Recent results</h3>
                @for (f of h.recent.slice(0, 6); track f.id) {
                  <a class="brow norank" [routerLink]="['/season/match', f.id]"><span class="who">{{ f.date | date: 'd MMM yyyy' }}</span><span class="det">{{ f.result }}</span></a>
                }
              </section>
              <section class="card board"><h3><mat-icon>sports_cricket</mat-icon>Most runs in this fixture</h3>
                @for (x of h.topRunScorers; track $index) { <ng-container *ngTemplateOutlet="row; context: { $implicit: x }" /> }</section>
              <section class="card board"><h3><mat-icon>sports_baseball</mat-icon>Most wickets in this fixture</h3>
                @for (x of h.topWicketTakers; track $index) { <ng-container *ngTemplateOutlet="row; context: { $implicit: x }" /> }</section>
            </div>
            <p class="muted small">All IPL meetings (former team names included). Source: {{ h.source }}.</p>
          }
        </app-state>
      } @else {
        <app-state [loading]="standings.isLoading()" [error]="standings.error()" [empty]="!standings.value()?.length" (retry)="standings.reload()">
          <section class="card table-wrap">
            <table class="data standings">
              <tr><th>Pos</th><th>Team</th><th class="num">P</th><th class="num">W</th><th class="num">L</th><th class="num">NR</th><th class="num">Pts</th><th class="num">NRR</th><th>Last 5</th></tr>
              @for (r of standings.value() ?? []; track r.team.name) {
                <tr [class.mine]="r.team.code === myCode()" [class.cut]="r.position === 4" [style]="r.team.code === myCode() ? vars(r.team) : null">
                  <td><span class="pos" [class.q]="r.position <= 4">{{ r.position }}</span></td>
                  <td><span class="player-cell"><app-monogram [name]="r.team.name" [code]="r.team.code" [color]="r.team.primaryColor ?? '#475569'" [size]="30" />
                    @if (r.team.franchiseId) { <a [routerLink]="['/franchises', r.team.franchiseId]">{{ r.team.name }}</a> } @else { {{ r.team.name }} }</span></td>
                  <td class="num">{{ r.played }}</td><td class="num">{{ r.won }}</td><td class="num">{{ r.lost }}</td><td class="num">{{ r.noResult }}</td>
                  <td class="num"><strong>{{ r.points }}</strong></td>
                  <td class="num">{{ r.netRunRate === null ? '—' : (r.netRunRate > 0 ? '+' : '') + (r.netRunRate | number: '1.3-3') }}</td>
                  <td><span class="form">@for (x of r.form; track $index) { <i [class]="x" [matTooltip]="x === 'W' ? 'Won' : x === 'L' ? 'Lost' : 'No result'">{{ x }}</i> }</span></td>
                </tr>
              }
            </table>
          </section>
          <p class="muted small">Calculated from the league-stage results: 2 points for a win, 1 for no result; the top four (marked) reach the play-offs.
            Net run rate counts a side bowled out as using its full overs; in rain-reduced matches it can differ slightly from the official figure. Source: {{ years.value()?.source }}.</p>
        </app-state>
      }
    </div>
    <ng-template #board let-b>
      <section class="card board"><h3><mat-icon>{{ b.icon }}</mat-icon>{{ b.title }}</h3>
        @for (x of b.rows; track $index) { <ng-container *ngTemplateOutlet="row; context: { $implicit: x }" /> }
        @if (!b.rows.length) { <p class="muted small">No data.</p> }
      </section>
    </ng-template>
    <ng-template #row let-x>
      <div class="brow" [class.first]="x.rank === 1"><span class="rank">{{ x.rank }}</span>
        <span class="who">@if (x.playerId) { <a [routerLink]="['/players', x.playerId]">{{ x.name }}</a> } @else { {{ x.name }} } <span class="tcode">{{ x.teamCode }}</span></span>
        <span class="val">{{ x.value }}</span><span class="det">{{ x.detail }}</span></div>
    </ng-template>
  `,
  styles: `
    .vs-txt { font-weight: 800; color: var(--muted); }
    .fx { color: var(--text); text-decoration: none !important; transition: transform 0.15s, box-shadow 0.15s; }
    .fx:hover { transform: translateY(-3px); box-shadow: 0 0 0 2px var(--team-line); }
    .sc-link { display: block; margin-top: 4px; color: var(--team-accent); font-weight: 800; font-size: 11px; letter-spacing: 0.05em; text-transform: uppercase; }
    .boards { display: grid; gap: 16px; grid-template-columns: repeat(auto-fill, minmax(330px, 1fr)); }
    .board h3 { display: flex; align-items: center; gap: 8px; color: var(--team-accent) !important; font-weight: 800 !important; font-size: 15px !important; }
    .brow { display: grid; grid-template-columns: 26px 1fr auto; grid-template-rows: auto auto; column-gap: 8px; padding: 7px 4px; border-bottom: 1px solid var(--line); color: var(--text); text-decoration: none !important; }
    .brow .rank { grid-row: span 2; font-weight: 800; color: var(--muted); align-self: center; }
    .brow .val { font-weight: 800; font-size: 16px; text-align: right; }
    .brow .det { grid-column: 2 / span 2; font-size: 11px; color: var(--muted); }
    .brow.norank { grid-template-columns: 1fr auto; } .brow.norank .det { grid-column: 1 / -1; }
    .brow.first { background: var(--team-soft); border-radius: 8px; }
    .brow.first .rank { color: var(--team-accent); }
    .tcode { font-size: 10px; font-weight: 800; padding: 1px 5px; border-radius: 4px; background: rgba(255,255,255,0.08); margin-left: 4px; }
    .h2h { display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: 20px; }
    .h2h-side, .h2h-mid { display: flex; flex-direction: column; align-items: center; gap: 4px; text-align: center; }
    .big { font-size: 40px; font-weight: 900; color: var(--team-accent); line-height: 1; }
    .h2h-mid .big { color: var(--text); }
    .bar { display: flex; width: 240px; height: 10px; border-radius: 99px; overflow: hidden; margin-top: 10px; background: var(--line); }
    .filters mat-form-field { width: 170px; }
    .fixtures { display: grid; gap: 18px; grid-template-columns: repeat(auto-fill, minmax(290px, 1fr)); }
    .fx { background: var(--card); border: 1px solid var(--line); border-radius: 14px; overflow: hidden; display: flex; flex-direction: column; }
    .fx header { display: flex; justify-content: space-between; padding: 9px 14px; font-weight: 800; font-size: 13px; color: var(--team-ink);
      background: linear-gradient(100deg, var(--team), color-mix(in srgb, var(--team) 70%, var(--team-2))); }
    .when { text-align: center; font-size: 12px; margin-top: 10px; font-weight: 600; }
    .where { text-align: center; font-size: 12px; color: var(--muted); display: flex; justify-content: center; align-items: center; gap: 2px; padding: 0 10px; }
    .where mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .teams { display: grid; grid-template-columns: 1fr auto 1fr; align-items: start; padding: 14px 10px 6px; }
    .side { display: flex; flex-direction: column; align-items: center; gap: 3px; }
    .badge { padding: 4px; border-radius: 50%; background: rgba(255,255,255,0.06); box-shadow: 0 0 0 2px var(--team-line, var(--line)); display: inline-flex; }
    .code { margin-top: 4px; font-size: 13px; letter-spacing: 0.06em; }
    .score { font-size: 24px; font-weight: 800; font-variant-numeric: tabular-nums; }
    .score.dnb { font-size: 14px; color: var(--muted); font-weight: 600; }
    .side.won .score { color: var(--team-accent); }
    .overs { font-size: 12px; color: var(--muted); }
    .vs { align-self: center; margin-top: -24px; font-size: 11px; font-weight: 800; padding: 3px 6px; border-radius: 6px; background: var(--team); color: var(--team-ink); }
    .fx footer { margin-top: auto; border-top: 1px solid var(--line); padding: 10px; text-align: center; font-size: 12px; font-weight: 600; }
    .standings .pos { display: inline-flex; width: 26px; height: 26px; border-radius: 50%; align-items: center; justify-content: center; font-weight: 800; }
    .standings .pos.q { background: var(--team-soft); color: var(--team-accent); }
    .standings tr.cut td { border-bottom: 2px dashed var(--team-line); }
    .standings tr.mine td { background: linear-gradient(90deg, var(--team), color-mix(in srgb, var(--team) 75%, var(--team-2))); color: var(--team-ink); font-weight: 700; }
    .standings tr.mine a { color: inherit; }
    .form { display: inline-flex; gap: 4px; }
    .form i { font-style: normal; width: 22px; height: 22px; border-radius: 50%; display: inline-flex; align-items: center; justify-content: center; font-size: 11px; font-weight: 800; color: #fff; }
    .form i.W { background: #16a34a; } .form i.L { background: #dc2626; } .form i.N { background: #64748b; }
    .standings tr.mine .form i { box-shadow: 0 0 0 2px rgba(255, 255, 255, 0.85); }
  `,
})
export class SeasonCentrePage {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  readonly teams = TEAMS;
  readonly tab = signal<'results' | 'table' | 'leaders' | 'records' | 'h2h'>('results');
  readonly h2hA = signal<string>(this.auth.activeFranchise()?.code ?? 'CSK');
  readonly h2hB = signal<string>((this.auth.activeFranchise()?.code ?? 'CSK') === 'MI' ? 'CSK' : 'MI');
  readonly leaders = rxResource({
    params: () => (this.tab() === 'leaders' ? this.year() : null),
    stream: ({ params }) => (params ? this.api.get<any[]>(`season-centre/${params}/leaders`) : of([])),
  });
  readonly records = rxResource({ params: () => this.tab() === 'records', stream: ({ params }) => (params ? this.api.get<any>('season-centre/records') : of(null)) });
  readonly h2h = rxResource({
    params: () => (this.tab() === 'h2h' && this.h2hA() !== this.h2hB() ? { a: this.h2hA(), b: this.h2hB() } : null),
    stream: ({ params }) => (params ? this.api.get<any>('season-centre/head-to-head', { team1: params.a, team2: params.b }) : of(null)),
  });
  readonly year = signal<number | null>(null);
  readonly myCode = computed(() => this.auth.activeFranchise()?.code ?? null);
  readonly team = signal<string | null>(this.auth.activeFranchise()?.code ?? null);
  readonly years = rxResource({ stream: () => this.api.get<any>('season-centre/years') });
  readonly fixtures = rxResource({
    params: () => ({ y: this.year(), t: this.team() }),
    stream: ({ params }) => (params.y ? this.api.get<any[]>(`season-centre/${params.y}/fixtures`, params.t ? { team: params.t } : undefined) : of([])),
  });
  readonly standings = rxResource({
    params: () => this.year(),
    stream: ({ params }) => (params ? this.api.get<any[]>(`season-centre/${params}/standings`) : of([])),
  });
  readonly leader = computed(() => this.standings.value()?.[0] ?? null);
  readonly champion = computed(() => {
    const final = (this.fixtures.value() ?? []).find((f: any) => f.stage === 'Final');
    return final?.winner ?? null;
  });
  readonly vars = (t: any) => teamVars(t?.primaryColor ?? '#475569', t?.secondaryColor ?? '#1e293b');

  constructor() {
    effect(() => {
      const ys = this.years.value()?.years;
      if (this.year() === null && ys?.length) this.year.set(ys[0]);
    });
  }
}
