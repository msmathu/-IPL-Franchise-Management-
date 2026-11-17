import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { RouterLink } from '@angular/router';
import { ChartConfiguration } from 'chart.js';
import { Api } from '../../core/api';
import { InrPipe, WordsPipe } from '../../core/services';
import { ChartView, PALETTE, PageHeader, StatCard, StateView, StatusChip } from '../../shared/ui';

@Component({
  selector: 'app-squad',
  imports: [PageHeader, StateView, StatCard, StatusChip, ChartView, RouterLink, InrPipe, WordsPipe, DatePipe, MatButtonToggleModule],
  template: `
    <div class="page">
      <app-state [loading]="res.isLoading()" [error]="res.error()" (retry)="res.reload()">
        @if (res.value(); as s) {
          <app-page-header [title]="s.franchiseName + ' squad'" [subtitle]="s.season + ' · strength ' + s.strength.strength + ' (model)'" />
          <div class="grid stats">
            <app-stat label="Players" [value]="s.composition.total + ' / ' + s.composition.maxSquad" [hint]="s.composition.slotsRemaining + ' slots left · min ' + s.composition.minSquad" />
            <app-stat label="Overseas" [value]="s.composition.overseas + ' / ' + s.composition.maxOverseas" [hint]="s.composition.overseasSlotsRemaining + ' overseas slots left'" />
            <app-stat label="Batters · bowlers" [value]="s.composition.batters + ' · ' + s.composition.bowlers" />
            <app-stat label="All-rounders · keepers" [value]="s.composition.allRounders + ' · ' + s.composition.wicketKeepers" />
            <app-stat label="Uncapped" [value]="s.composition.uncapped" />
            <app-stat label="Squad value" [value]="s.squadValue | inr" [hint]="'Purse left ' + (s.purseRemaining | inr)" />
          </div>
          @for (w of s.composition.warnings; track w) { <p class="warn-text small">⚠ {{ w }}</p> }
          @for (n of s.strength.notes; track n) { <p class="muted small">{{ n }}</p> }
          <div class="grid cols-2">
            <section class="card"><h2>Composition</h2><app-chart [config]="chart()" [height]="220" /></section>
            <section class="card"><h2>Availability</h2>
              @for (g of availability(); track g.status) { <div class="list-item"><app-chip [text]="g.status" /><span>{{ g.names }}</span></div> }</section>
          </div>
          <mat-button-toggle-group [value]="group()" (change)="group.set($event.value)">
            @for (g of groups; track g.key) { <mat-button-toggle [value]="g.key">{{ g.label }} ({{ count(g.key) }})</mat-button-toggle> }
          </mat-button-toggle-group>
          <section class="card table-wrap">
            @if (group() === 'departed') {
              <table class="data"><tr><th>Player</th><th>Status</th><th>Left</th></tr>
                @for (p of s.departed; track p.playerId) { <tr><td><a [routerLink]="['/players', p.playerId]">{{ p.name }}</a></td><td><app-chip [text]="p.status | words" /></td><td>{{ p.leftAt | date: 'd MMM yyyy' }}</td></tr> }
                @empty { <tr><td colspan="3" class="muted">No departures this season.</td></tr> }</table>
            } @else {
              <table class="data"><tr><th>Player</th><th>Role</th><th class="num">Age</th><th>Acquired</th><th class="num">Price</th><th>Availability</th><th class="num">Rating</th><th>Form</th></tr>
                @for (p of shown(); track p.playerId) {
                  <tr><td><a [routerLink]="['/players', p.playerId]">{{ p.name }}</a>@if (p.isOverseas) { <span class="tag os">OS</span> }@if (p.isWicketKeeper) { <span class="tag">WK</span> }@if (!p.isCapped) { <span class="tag">Uncapped</span> }</td>
                    <td>{{ p.role }}</td><td class="num">{{ p.age }}</td><td>{{ p.acquisition | words }}</td><td class="num">{{ p.price | inr }}</td>
                    <td><app-chip [text]="p.availability" /></td><td class="num">{{ p.rating }}</td>
                    <td [class.good-text]="p.formTrend === 'Rising'" [class.bad-text]="p.formTrend === 'Falling'">{{ p.formTrend }}</td></tr>
                } @empty { <tr><td colspan="8" class="muted">No players in this group.</td></tr> }
              </table>
            }
          </section>
        }
      </app-state>
    </div>
  `,
})
export class SquadPage {
  private readonly api = inject(Api);
  readonly res = rxResource({ stream: () => this.api.get<any>('squad') });
  readonly group = signal('all');
  readonly groups = [
    { key: 'all', label: 'All' }, { key: 'Retained', label: 'Retained' }, { key: 'Auction', label: 'Purchased' }, { key: 'Trade', label: 'Traded in' },
    { key: 'injured', label: 'Injured' }, { key: 'available', label: 'Available' }, { key: 'departed', label: 'Released / traded out' },
  ];
  readonly shown = computed(() => this.filter(this.group()));
  readonly availability = computed(() => {
    const players: any[] = this.res.value()?.players ?? [];
    const statuses = [...new Set(players.map((p) => p.availability))];
    return statuses.map((status) => ({ status, names: players.filter((p) => p.availability === status).map((p) => p.name).join(', ') }));
  });
  readonly chart = computed<ChartConfiguration>(() => {
    const c = this.res.value()?.composition;
    return {
      type: 'bar',
      data: { labels: ['Batters', 'Bowlers', 'All-rounders', 'Keepers', 'Overseas', 'Uncapped'],
        datasets: [{ data: c ? [c.batters, c.bowlers, c.allRounders, c.wicketKeepers, c.overseas, c.uncapped] : [], backgroundColor: PALETTE }] },
      options: { plugins: { legend: { display: false } } },
    };
  });

  private filter(key: string): any[] {
    const players: any[] = this.res.value()?.players ?? [];
    if (key === 'all') return players;
    if (key === 'injured') return players.filter((p) => p.availability === 'Injured');
    if (key === 'available') return players.filter((p) => p.availability === 'Available');
    return players.filter((p) => p.acquisition === key);
  }

  count(key: string): number {
    return key === 'departed' ? (this.res.value()?.departed?.length ?? 0) : this.filter(key).length;
  }
}
