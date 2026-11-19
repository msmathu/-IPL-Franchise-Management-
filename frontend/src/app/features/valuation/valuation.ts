import { Component, computed, inject, signal } from '@angular/core';
import { PlayerName, TeamName } from '../../shared/media';
import { rxResource } from '../../core/resource';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { ChartConfiguration } from 'chart.js';
import { Api } from '../../core/api';
import { InrPipe } from '../../core/services';
import { ChartView, PALETTE, PageHeader, StatCard, StateView, StatusChip } from '../../shared/ui';

@Component({
  selector: 'app-valuation',
  imports: [PlayerName, TeamName, PageHeader, StateView, StatCard, StatusChip, ChartView, InrPipe, MatTabsModule, MatButtonToggleModule],
  template: `
    <div class="page">
      <app-page-header title="Player valuation & recommendations" subtitle="Explainable model estimates — never official prices" />
      <mat-tab-group>
        <mat-tab label="Auction strategy">
          <div class="stack tabpad">
            <app-state [loading]="strategy.isLoading()" [error]="strategy.error()" (retry)="strategy.reload()">
              @if (strategy.value(); as s) {
                <div class="grid stats">
                  <app-stat label="Purse" [value]="s.purseRemaining | inr" /><app-stat label="Squad slots" [value]="s.slotsRemaining" /><app-stat label="Overseas slots" [value]="s.overseasSlotsRemaining" />
                </div>
                <div class="grid cols-2">
                  <section class="card"><h2>Squad requirements</h2>
                    @for (n of s.needs; track n.need) { <div class="list-item"><app-chip [text]="n.severity >= 3 ? 'Urgent' : n.severity === 2 ? 'Important' : 'Nice to have'" /><strong>{{ n.need }}</strong><span class="muted small">{{ n.detail }}</span></div> }
                    @empty { <p class="muted">No gaps found.</p> }
                    <h3>Suggestions</h3>
                    @for (t of s.suggestions; track t) { <p class="small">• {{ t }}</p> }</section>
                  <section class="card"><h2>Target value ranges</h2><app-chart [config]="targetChart()" [height]="320" /></section>
                </div>
                <section class="card table-wrap"><h2>Recommended auction targets</h2>
                  <table class="data"><tr><th>Player</th><th>Role</th><th class="num">Age</th><th class="num">Rating</th><th class="num">Base</th><th class="num">Model range</th><th>Why</th></tr>
                    @for (t of s.targets; track t.playerId) {
                      <tr><td><app-pname [id]="t.playerId" [name]="t.name" [size]="24" />@if (t.isOverseas) { <span class="tag os">OS</span> }</td><td>{{ t.role }}</td><td class="num">{{ t.age }}</td>
                        <td class="num">{{ t.rating }}</td><td class="num">{{ t.basePrice | inr }}</td><td class="num">{{ t.low | inr }} – {{ t.high | inr }}</td><td class="small">{{ t.fitReason }}</td></tr>
                    }</table>
                  <p class="disclaimer">{{ s.disclaimer }}</p></section>
              }
            </app-state>
          </div>
        </mat-tab>
        <mat-tab label="Undervalued / overvalued">
          <div class="stack tabpad">
            <mat-button-toggle-group [value]="kind()" (change)="kind.set($event.value)">
              <mat-button-toggle value="undervalued">Potentially undervalued</mat-button-toggle><mat-button-toggle value="overvalued">Potentially overvalued</mat-button-toggle>
            </mat-button-toggle-group>
            <section class="card table-wrap">
              <app-state [loading]="gaps.isLoading()" [error]="gaps.error()" [empty]="!gaps.value()?.length" (retry)="gaps.reload()">
                <table class="data"><tr><th>Player</th><th>Role</th><th>Team</th><th class="num">Rating</th><th class="num">Current price</th><th class="num">Model mid</th><th class="num">Difference</th></tr>
                  @for (g of gaps.value() ?? []; track g.playerId) {
                    <tr><td><app-pname [id]="g.playerId" [name]="g.name" [size]="24" /></td><td>{{ g.role }}</td><td><app-team [key]="g.franchise" [size]="20" /></td><td class="num">{{ g.rating }}</td>
                      <td class="num">{{ g.referencePrice | inr }}</td><td class="num">{{ g.modelMid | inr }}</td><td class="num" [class.good-text]="g.gap > 0" [class.bad-text]="g.gap < 0">{{ g.gap | inr }}</td></tr>
                  }</table>
                <p class="disclaimer">Comparisons use the model's mid estimate against the current squad price. Use them to start a discussion, not as a verdict.</p>
              </app-state>
            </section>
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: `.tabpad { margin-top: 16px; }`,
})
export class ValuationPage {
  private readonly api = inject(Api);
  readonly kind = signal('undervalued');
  readonly strategy = rxResource({ stream: () => this.api.get<any>('valuation/strategy') });
  readonly gaps = rxResource({ params: () => this.kind(), stream: ({ params }) => this.api.get<any[]>(`valuation/${params}`) });
  readonly targetChart = computed<ChartConfiguration>(() => {
    const t: any[] = (this.strategy.value()?.targets ?? []).slice(0, 10);
    return {
      type: 'bar',
      data: { labels: t.map((x) => x.name), datasets: [
        { label: 'Low', data: t.map((x) => x.low / 1e7), backgroundColor: PALETTE[3] },
        { label: 'Mid', data: t.map((x) => (x.mid - x.low) / 1e7), backgroundColor: PALETTE[0] },
        { label: 'High', data: t.map((x) => (x.high - x.mid) / 1e7), backgroundColor: PALETTE[1] },
      ] },
      options: { indexAxis: 'y', scales: { x: { stacked: true, title: { display: true, text: '₹ Cr' } }, y: { stacked: true } } },
    };
  });
}
