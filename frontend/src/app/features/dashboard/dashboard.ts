import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { rxResource } from '../../core/resource';
import { ChartConfiguration } from 'chart.js';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { InrPipe, WordsPipe, formatInr } from '../../core/services';
import { ChartView, PALETTE, PageHeader, StatCard, StateView, StatusChip } from '../../shared/ui';

@Component({
  selector: 'app-dashboard',
  imports: [PageHeader, StateView, StatCard, ChartView, StatusChip, RouterLink, InrPipe, WordsPipe, DatePipe, DecimalPipe],
  template: `
    <div class="page">
      <app-page-header [title]="d()?.isLeagueView ? 'League overview' : (d()?.franchise ?? 'Dashboard')" [subtitle]="d()?.season" />
      <app-state [loading]="res.isLoading()" [error]="res.error()" (retry)="res.reload()">
        @if (d(); as d) {
          @if (d.league; as l) {
            <div class="grid stats">
              <app-stat label="Players in database" [value]="l.players" />
              <app-stat label="Free agents for auction" [value]="l.freeAgents" />
              <app-stat label="Trades awaiting league approval" [value]="l.pendingLeagueApprovals" [tone]="l.pendingLeagueApprovals ? 'warn' : 'normal'" />
            </div>
            <div class="grid cols-2">
              <section class="card"><h2>Standings (latest completed season)</h2>
                <div class="table-wrap"><table class="data"><tr><th>#</th><th>Franchise</th><th class="num">P</th><th class="num">W</th><th class="num">L</th><th class="num">Pts</th><th class="num">NRR</th></tr>
                @for (s of l.standings; track s.franchiseId; let i = $index) {
                  <tr><td>{{ i + 1 }}</td><td><a [routerLink]="['/franchises', s.franchiseId]">{{ s.franchise }}</a></td><td class="num">{{ s.played }}</td><td class="num">{{ s.won }}</td>
                    <td class="num">{{ s.lost }}</td><td class="num"><strong>{{ s.points }}</strong></td><td class="num">{{ s.netRunRate | number: '1.3-3' }}</td></tr>
                }</table></div></section>
              <section class="card"><h2>Auction spend this season</h2><app-chart [config]="auctionChart()" /></section>
            </div>
          }
          @if (d.owner; as o) {
            <div class="grid stats">
              <app-stat label="Squad value" [value]="o.squadValue | inr" />
              <app-stat label="Auction purse left" [value]="o.purseRemaining | inr" [hint]="'of ' + (o.purseTotal | inr)" />
              <app-stat label="Team strength" [value]="o.teamStrength" [hint]="'Rank ' + o.strengthRank + ' in the league (model)'" />
              <app-stat label="Open trades" [value]="o.openTrades" [hint]="o.awaitingMyResponse + ' awaiting your response'" [tone]="o.awaitingMyResponse ? 'warn' : 'normal'" />
              <app-stat label="Retention" [value]="o.retentionSubmitted ? 'Submitted' : 'Not submitted'" [hint]="o.retentionDeadline ? 'Deadline ' + (o.retentionDeadline | date: 'd MMM') : ''" [tone]="o.retentionSubmitted ? 'good' : 'warn'" />
            </div>
            <div class="grid cols-2">
              <section class="card"><h2>Approval requests</h2>
                @for (a of o.approvalRequests; track a.tradeId) {
                  <div class="list-item"><a [routerLink]="['/trading', a.tradeId]">{{ a.reference }}</a><span>with {{ a.counterparty }}</span><span class="spacer"></span><app-chip [text]="a.type | words" /></div>
                } @empty { <p class="muted">Nothing needs your approval.</p> }
              </section>
              <section class="card"><h2>Upcoming deadlines</h2>
                @for (dl of o.deadlines; track dl.title) {
                  <div class="list-item"><a [routerLink]="dl.link">{{ dl.title }}</a><span class="spacer"></span><span>{{ dl.date | date: 'd MMM yyyy' }}</span>
                    <app-chip [text]="dl.daysLeft === 0 ? 'Today' : dl.daysLeft + ' days'" /></div>
                } @empty { <p class="muted">No upcoming deadlines.</p> }
              </section>
            </div>
          }
          @if (d.director; as dr) {
            <div class="grid cols-3">
              <section class="card"><h2>Squad composition</h2>
                <app-chart [config]="compositionChart()" [height]="220" />
                <p class="muted small">{{ dr.composition.total }} players · {{ dr.composition.overseas }} overseas · {{ dr.composition.slotsRemaining }} slots left</p></section>
              <section class="card"><h2>Squad needs</h2>
                @for (n of dr.needs; track n.need) { <div class="list-item"><app-chip [text]="n.severity >= 3 ? 'Urgent' : n.severity === 2 ? 'Important' : 'Nice to have'" /><strong>{{ n.need }}</strong><span class="muted small">{{ n.detail }}</span></div> }
                @empty { <p class="muted">The squad is balanced.</p> }</section>
              <section class="card"><h2>Top rated players</h2>
                @for (p of dr.topRated; track p.playerId) { <div class="list-item"><a [routerLink]="['/players', p.playerId]">{{ p.name }}</a><span class="muted small">{{ p.role }}</span><span class="spacer"></span><strong>{{ p.rating }}</strong></div> }</section>
            </div>
            <div class="grid cols-3">
              <section class="card"><h2>Auction targets</h2>
                @for (t of dr.auctionTargets; track t.playerId) { <div class="list-item"><a [routerLink]="['/players', t.playerId]">{{ t.name }}</a><span class="muted small">{{ t.fitReason }}</span><span class="spacer"></span><span class="small">{{ t.low | inr }}–{{ t.high | inr }}</span></div> }
                @empty { <p class="muted">No targets yet.</p> }<p class="disclaimer">Model estimates, not official prices.</p></section>
              <section class="card"><h2>Trade opportunities</h2>
                @for (g of dr.tradeOpportunities; track g.playerId) { <div class="list-item"><a [routerLink]="['/players', g.playerId]">{{ g.name }}</a><span class="muted small">{{ g.franchise }}</span><span class="spacer"></span><span class="good-text small">+{{ g.gap | inr }} vs price</span></div> }
                @empty { <p class="muted">No clear undervalued players.</p> }</section>
              <section class="card"><h2>Scouting recommendations</h2>
                @for (r of dr.scoutingRecommendations; track r.id) { <div class="list-item"><a [routerLink]="['/scouting/reports', r.id]">{{ r.player }}</a><span class="muted small">by {{ r.scout }}</span><span class="spacer"></span><app-chip [text]="r.recommendation | words" /></div> }
                @empty { <p class="muted">No recommendations yet.</p> }</section>
            </div>
          }
          @if (d.coach; as c) {
            <div class="grid stats">
              <app-stat label="Available" [value]="c.available" tone="good" />
              <app-stat label="Injured" [value]="c.injured" [tone]="c.injured ? 'warn' : 'normal'" />
              <app-stat label="Doubtful" [value]="c.doubtful" />
              <app-stat label="Unavailable" [value]="c.unavailable" />
              <app-stat label="Preferred Playing XI" [value]="c.preferredXI ?? 'None saved'" [hint]="c.xiWarnings.length ? c.xiWarnings.length + ' warning(s)' : ''" />
            </div>
            <div class="grid cols-3">
              <section class="card"><h2>Top run scorers (last season)</h2><app-chart [config]="runsChart()" [height]="220" /></section>
              <section class="card"><h2>In form</h2>
                @for (p of c.inForm; track p.playerId) { <div class="list-item"><a [routerLink]="['/players', p.playerId]">{{ p.name }}</a><span class="spacer"></span><span class="good-text">▲ {{ p.rating }}</span></div> }
                @empty { <p class="muted">No players clearly above their career level.</p> }</section>
              <section class="card"><h2>Out of form</h2>
                @for (p of c.outOfForm; track p.playerId) { <div class="list-item"><a [routerLink]="['/players', p.playerId]">{{ p.name }}</a><span class="spacer"></span><span class="bad-text">▼ {{ p.rating }}</span></div> }
                @empty { <p class="muted">No players clearly below their career level.</p> }</section>
            </div>
          }
          @if (d.finance; as f) {
            <div class="grid stats">
              <app-stat label="Total spent" [value]="f.totalSpent | inr" />
              <app-stat label="Purse remaining" [value]="f.purseRemaining | inr" />
              <app-stat label="Contract commitments" [value]="f.contractCommitments | inr" />
              <app-stat label="Trade consideration (net)" [value]="f.tradeConsiderationNet | inr" />
            </div>
            <div class="grid cols-2">
              <section class="card"><h2>Spending by month</h2><app-chart [config]="spendChart()" /></section>
              <section class="card"><h2>Recent transactions</h2>
                @for (t of f.recentTransactions; track t.id) { <div class="list-item"><span class="small muted">{{ t.createdAt | date: 'd MMM' }}</span><span>{{ t.description }}</span><span class="spacer"></span>
                  <span [class.bad-text]="t.direction === 'Debit'" [class.good-text]="t.direction === 'Credit'">{{ t.direction === 'Debit' ? '−' : '+' }}{{ t.amount | inr }}</span></div> }</section>
            </div>
          }
          @if (!d.owner && !d.director && !d.coach && !d.finance && !d.league) { <p class="muted">Your role has no dashboard widgets. Use the menu to open your areas.</p> }
        }
      </app-state>
    </div>
  `,
})
export class DashboardPage {
  private readonly api = inject(Api);
  readonly res = rxResource({ stream: () => this.api.get<any>('reports/dashboard') });
  readonly d = computed(() => this.res.value());

  readonly auctionChart = computed<ChartConfiguration>(() => {
    const rows = this.d()?.league?.auctionSpend ?? [];
    return { type: 'bar', data: { labels: rows.map((r: any) => r.franchise), datasets: [{ label: 'Spent', data: rows.map((r: any) => r.spent / 1e7), backgroundColor: PALETTE[0] }] },
      options: { plugins: { legend: { display: false }, tooltip: { callbacks: { label: (c) => `₹${c.parsed.y} Cr` } } } } };
  });

  readonly compositionChart = computed<ChartConfiguration>(() => {
    const c = this.d()?.director?.composition;
    return { type: 'doughnut', data: { labels: ['Batters', 'Bowlers', 'All-rounders', 'Keepers'], datasets: [{ data: c ? [c.batters, c.bowlers, c.allRounders, c.wicketKeepers] : [], backgroundColor: PALETTE }] },
      options: { plugins: { legend: { position: 'right' } } } };
  });

  readonly runsChart = computed<ChartConfiguration>(() => {
    const rows = this.d()?.coach?.topRunScorers ?? [];
    return { type: 'bar', data: { labels: rows.map((r: any) => r.name.split(' ')[1] ?? r.name), datasets: [{ label: 'Runs', data: rows.map((r: any) => r.runs), backgroundColor: PALETTE[1] }] },
      options: { indexAxis: 'y', plugins: { legend: { display: false } } } };
  });

  readonly spendChart = computed<ChartConfiguration>(() => {
    const rows: any[] = this.d()?.finance?.monthlySpend ?? [];
    const months = [...new Set(rows.map((r) => `${r.year}-${String(r.month).padStart(2, '0')}`))];
    const cats = [...new Set(rows.map((r) => r.category))];
    return {
      type: 'bar',
      data: { labels: months, datasets: cats.map((c, i) => ({ label: c, backgroundColor: PALETTE[i], data: months.map((m) => rows.filter((r) => r.category === c && `${r.year}-${String(r.month).padStart(2, '0')}` === m).reduce((s, r) => s + r.total, 0) / 1e7) })) },
      options: { scales: { x: { stacked: true }, y: { stacked: true, ticks: { callback: (v) => `₹${v} Cr` } } }, plugins: { tooltip: { callbacks: { label: (c) => `${c.dataset.label}: ${formatInr(Number(c.parsed.y) * 1e7)}` } } } },
    };
  });
}
