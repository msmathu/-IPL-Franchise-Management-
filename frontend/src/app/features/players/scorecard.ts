import { DatePipe } from '@angular/common';
import { PlayerName } from '../../shared/media';
import { Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { rxResource } from '../../core/resource';
import { teamVars } from '../../core/team-theme';
import { Monogram, StateView } from '../../shared/ui';

/** Full scorecard of one match (batting, bowling, extras, fall of wickets), from the ball-by-ball data. */
@Component({
  selector: 'app-scorecard',
  imports: [PlayerName, StateView, Monogram, RouterLink, DatePipe, MatButtonModule, MatIconModule],
  template: `
    <div class="page">
      <a mat-button routerLink="/season"><mat-icon>arrow_back</mat-icon>Fixtures &amp; Table</a>
      <app-state [loading]="res.isLoading()" [error]="res.error()" (retry)="res.reload()">
        @if (res.value(); as s) {
          @let m = s.match;
          <section class="team-hero" [style]="vars(winnerTeam())">
            <div class="eyebrow">{{ m.stage ?? (m.matchNumber ? 'Match ' + m.matchNumber : 'Match') }} · {{ m.date | date: 'd MMMM yyyy' }}</div>
            <div class="sc-head">
              @for (side of [m.team1, m.team2]; track $index) {
                <div class="sc-team">
                  <app-monogram [name]="side.team.name" [code]="side.team.code" [color]="side.team.primaryColor ?? '#475569'" [size]="56" />
                  <div><strong>{{ side.team.name }}</strong>
                    <div class="sc-score">{{ side.runs !== null ? side.runs + '/' + side.wickets : 'Did not bat' }} @if (side.overs) { <small>({{ side.overs }} ov)</small> }</div></div>
                </div>
                @if ($index === 0) { <span class="vs">VS</span> }
              }
            </div>
            <div class="hero-facts">
              <div class="fact"><mat-icon>emoji_events</mat-icon><div><b>Result</b><span>{{ m.result ?? 'Not recorded' }}</span></div></div>
              <div class="fact"><mat-icon>location_on</mat-icon><div><b>Venue</b><span>{{ m.venue }}</span></div></div>
              @if (s.card.toss) { <div class="fact"><mat-icon>toll</mat-icon><div><b>Toss</b><span>{{ s.card.toss }}</span></div></div> }
              @if (s.card.playerOfMatch?.length) { <div class="fact"><mat-icon>star</mat-icon><div><b>Player of the match</b><span>{{ s.card.playerOfMatch.join(', ') }}</span></div></div> }
            </div>
          </section>

          @for (inn of s.card.innings; track $index) {
            <section class="card">
              <h2 class="inn-title">{{ inn.team }} <span>{{ inn.runs }}/{{ inn.wickets }} ({{ inn.overs }} ov)</span></h2>
              <div class="table-wrap"><table class="data">
                <tr><th>Batter</th><th></th><th class="num">R</th><th class="num">B</th><th class="num">4s</th><th class="num">6s</th><th class="num">SR</th></tr>
                @for (b of inn.batting; track b.id) {
                  <tr [class.top]="b.runs >= 50">
                    <td><app-pname [id]="s.linkedPlayers[b.id]" [name]="b.name" [size]="22" /></td>
                    <td class="muted small">{{ b.how }}</td>
                    <td class="num"><strong>{{ b.runs }}</strong></td><td class="num">{{ b.balls }}</td><td class="num">{{ b.fours }}</td><td class="num">{{ b.sixes }}</td>
                    <td class="num">{{ b.strikeRate ?? '—' }}</td>
                  </tr>
                }
                <tr class="muted"><td>Extras</td><td class="small">b {{ inn.extras.byes }}, lb {{ inn.extras.legByes }}, w {{ inn.extras.wides }}, nb {{ inn.extras.noBalls }}@if (inn.extras.penalty) { , pen {{ inn.extras.penalty }} }</td><td class="num">{{ inn.extras.total }}</td><td colspan="4"></td></tr>
                <tr><td><strong>Total</strong></td><td class="small">{{ inn.wickets }} wickets, {{ inn.overs }} overs</td><td class="num"><strong>{{ inn.runs }}</strong></td><td colspan="4"></td></tr>
              </table></div>
              @if (inn.didNotBat.length) { <p class="small muted">Did not bat: {{ inn.didNotBat.join(', ') }}</p> }
              @if (inn.fallOfWickets.length) { <p class="small"><strong>Fall of wickets:</strong> {{ inn.fallOfWickets.join(' · ') }}</p> }
              <div class="table-wrap"><table class="data">
                <tr><th>Bowler</th><th class="num">O</th><th class="num">M</th><th class="num">R</th><th class="num">W</th><th class="num">Econ</th><th class="num">Dots</th><th class="num">Wd</th><th class="num">Nb</th></tr>
                @for (w of inn.bowling; track w.id) {
                  <tr [class.top]="w.wickets >= 3">
                    <td><app-pname [id]="s.linkedPlayers[w.id]" [name]="w.name" [size]="22" /></td>
                    <td class="num">{{ w.overs }}</td><td class="num">{{ w.maidens }}</td><td class="num">{{ w.runs }}</td><td class="num"><strong>{{ w.wickets }}</strong></td>
                    <td class="num">{{ w.economy ?? '—' }}</td><td class="num">{{ w.dots }}</td><td class="num">{{ w.wides }}</td><td class="num">{{ w.noBalls }}</td>
                  </tr>
                }
              </table></div>
            </section>
          }
          <p class="muted small">Source: {{ s.match.dataSource }}. Names as recorded on the scorecard; linked players open their profile.</p>
        }
      </app-state>
    </div>
  `,
  styles: `
    .sc-head { display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: 18px; margin-top: 10px; }
    .sc-team { display: flex; gap: 14px; align-items: center; }
    .sc-team strong { font-size: 18px; }
    .sc-score { font-size: 34px; font-weight: 900; } .sc-score small { font-size: 15px; font-weight: 600; opacity: 0.85; }
    .vs { font-weight: 900; padding: 4px 8px; border-radius: 8px; background: rgba(0,0,0,0.25); }
    .inn-title { display: flex; justify-content: space-between; color: var(--team-accent); font-size: 18px !important; }
    .inn-title span { color: var(--text); }
    tr.top td { background: var(--team-soft); }
  `,
})
export class ScorecardPage {
  readonly id = input.required<string>();
  private readonly api = inject(Api);
  readonly res = rxResource({ params: () => this.id(), stream: ({ params }) => this.api.get<any>(`season-centre/matches/${params}/scorecard`) });
  readonly winnerTeam = computed(() => {
    const m = this.res.value()?.match;
    if (!m) return null;
    return m.winner === m.team2.team.name ? m.team2.team : m.team1.team;
  });
  readonly vars = (t: any) => teamVars(t?.primaryColor ?? '#334155', t?.secondaryColor ?? '#1e293b');
}
