import { DatePipe } from '@angular/common';
import { PlayerName } from '../../shared/media';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CdkDrag, CdkDragDrop, CdkDragPreview, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { Router } from '@angular/router';
import { P } from '../../core/services';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { of } from 'rxjs';
import { Auth } from '../../core/auth';
import { teamVars } from '../../core/team-theme';
import { PlayerCard } from '../../shared/player-card';
import { rxResource } from '../../core/resource';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { RouterLink } from '@angular/router';
import { ChartConfiguration } from 'chart.js';
import { Api } from '../../core/api';
import { InrPipe, WordsPipe } from '../../core/services';
import { ChartView, PALETTE, PageHeader, PlayerAvatar, StatCard, StateView, StatusChip } from '../../shared/ui';

@Component({
  selector: 'app-squad',
  imports: [CdkDropListGroup, CdkDropList, CdkDrag, CdkDragPreview, PlayerName, StateView, StatCard, StatusChip, ChartView, PlayerCard, InrPipe, WordsPipe, DatePipe, MatButtonToggleModule,
    MatIconModule, MatSelectModule, FormsModule],
  template: `
    <div class="page">
      @if (pickable()) {
        <div class="row">
          <mat-form-field class="team-pick"><mat-label>Team</mat-label>
            <mat-select [ngModel]="chosen()" (ngModelChange)="chosen.set($event)">
              @for (f of franchises.value() ?? []; track f.id) { <mat-option [value]="f.id">{{ f.name }}</mat-option> }
            </mat-select></mat-form-field>
        </div>
      }
      <app-state [loading]="res.isLoading()" [error]="res.error()" (retry)="res.reload()">
        @if (res.value(); as s) {
          <div class="stack" [style]="vars()">
          <section class="team-hero">
            <div class="eyebrow">{{ s.season }} · Squad</div>
            <h1>{{ s.franchiseName }}</h1>
            <div class="hero-facts">
              <div class="fact"><mat-icon>groups</mat-icon><div><b>Players</b><span>{{ s.composition.total }} / {{ s.composition.maxSquad }}</span></div></div>
              <div class="fact"><mat-icon>flight</mat-icon><div><b>Overseas</b><span>{{ s.composition.overseas }} / {{ s.composition.maxOverseas }}</span></div></div>
              <div class="fact"><mat-icon>workspace_premium</mat-icon><div><b>Captain</b><span>{{ captain() ?? '—' }}</span></div></div>
              <div class="fact"><mat-icon>bolt</mat-icon><div><b>Team strength (model)</b><span>{{ s.strength.strength }}</span></div></div>
              @if (s.purseRemaining) { <div class="fact"><mat-icon>account_balance_wallet</mat-icon><div><b>Purse left</b><span>{{ s.purseRemaining | inr }}</span></div></div> }
            </div>
          </section>
          <div class="row">
            <mat-button-toggle-group [value]="view()" (change)="view.set($event.value)">
              <mat-button-toggle value="cards"><mat-icon>grid_view</mat-icon> Cards</mat-button-toggle>
              <mat-button-toggle value="table"><mat-icon>table_rows</mat-icon> Table &amp; details</mat-button-toggle>
            </mat-button-toggle-group>
            <span class="spacer"></span>
            @for (sec of sections(); track sec.key) { <a mat-button class="small" (click)="jump(sec.key)"><mat-icon class="role-ico">{{ sec.icon }}</mat-icon>{{ sec.title }}</a> }
          </div>
          @if (view() === 'cards') {
            <div cdkDropListGroup>
            @for (sec of sections(); track sec.key) {
              <h2 class="section-title" [id]="'sec-' + sec.key"><mat-icon>{{ sec.icon }}</mat-icon>{{ sec.title }} <span class="count">{{ sec.players.length }}</span></h2>
              <div class="pcards" cdkDropList cdkDropListSortingDisabled cdkDropListAutoScrollDisabled [cdkDropListData]="sec.key" [cdkDropListConnectedTo]="trayIds()">
                @for (p of sec.players; track p.playerId) {
                  <div cdkDrag [cdkDragData]="p" class="drag-card"><app-player-card [player]="p" />
                    <div class="pcard-drag-preview" *cdkDragPreview><app-pname [id]="p.playerId" [name]="p.name" [size]="34" [link]="false" /></div></div>
                }
              </div>
            }
            <div class="tray">
              <span class="tray-title"><mat-icon>pan_tool_alt</mat-icon>Drag player cards here:</span>
              @for (t of trays(); track t.key) {
                <div class="tray-zone" cdkDropList cdkDropListAutoScrollDisabled [id]="'tray-' + t.key" [cdkDropListData]="t.key" (cdkDropListDropped)="trayDrop($event, t.key)">
                  <div class="tray-head"><mat-icon>{{ t.icon }}</mat-icon><strong>{{ t.label }}</strong><span class="muted small">{{ t.hint }}</span></div>
                  <div class="tray-chips">
                    @for (p of tray()[t.key]; track p.playerId) {
                      <span class="chip"><app-pname [id]="p.playerId" [name]="p.name" [size]="20" [link]="false" /><button mat-icon-button (click)="untray(t.key, p.playerId)" aria-label="Remove"><mat-icon>close</mat-icon></button></span>
                    } @empty { <span class="muted small">Drop here</span> }
                  </div>
                  <button mat-flat-button [disabled]="tray()[t.key].length < t.min" (click)="go(t.key)">{{ t.action }} ({{ tray()[t.key].length }})</button>
                </div>
              }
            </div>
            </div>
          } @else {
          <div class="grid stats">
            <app-stat label="Players" [value]="s.composition.total + ' / ' + s.composition.maxSquad" [hint]="s.composition.slotsRemaining + ' slots left · min ' + s.composition.minSquad" />
            <app-stat label="Overseas" [value]="s.composition.overseas + ' / ' + s.composition.maxOverseas" [hint]="s.composition.overseasSlotsRemaining + ' overseas slots left'" />
            <app-stat label="Batters · bowlers" [value]="s.composition.batters + ' · ' + s.composition.bowlers" />
            <app-stat label="All-rounders · keepers" [value]="s.composition.allRounders + ' · ' + s.composition.wicketKeepers" />
            <app-stat label="Uncapped" [value]="s.composition.uncapped" />
            <app-stat label="Squad value" [value]="s.squadValue ? (s.squadValue | inr) : 'Not recorded'" [hint]="'Purse left ' + (s.purseRemaining | inr)" />
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
                @for (p of s.departed; track p.playerId) { <tr><td><app-pname [id]="p.playerId" [name]="p.name" [size]="24" /></td><td><app-chip [text]="p.status | words" /></td><td>{{ p.leftAt | date: 'd MMM yyyy' }}</td></tr> }
                @empty { <tr><td colspan="3" class="muted">No departures this season.</td></tr> }</table>
            } @else {
              <table class="data"><tr><th>Player</th><th>Role</th><th class="num">Age</th><th>Acquired</th><th class="num">Price</th><th>Availability</th><th class="num">Rating</th><th>Form</th></tr>
                @for (p of shown(); track p.playerId) {
                  <tr><td><app-pname [id]="p.playerId" [name]="p.name" [size]="28" />@if (p.isOverseas) { <span class="tag os">OS</span> }@if (p.isWicketKeeper) { <span class="tag">WK</span> }@if (!p.isCapped) { <span class="tag">Uncapped</span> }</td>
                    <td>{{ p.role }}</td><td class="num">{{ p.age ?? '—' }}</td><td>{{ p.acquisition === 'Initial' ? 'Squad 2026' : (p.acquisition | words) }}</td><td class="num">{{ p.price ? (p.price | inr) : '—' }}</td>
                    <td><app-chip [text]="p.availability" /></td><td class="num">{{ p.rating ?? '—' }}</td>
                    <td [class.good-text]="p.formTrend === 'Rising'" [class.bad-text]="p.formTrend === 'Falling'">{{ p.formTrend }}</td></tr>
                } @empty { <tr><td colspan="8" class="muted">No players in this group.</td></tr> }
              </table>
            }
          </section>
          }
          </div>
        }
      </app-state>
    </div>
  `,
})
export class SquadPage {
  private readonly router = inject(Router);
  /** Players dropped on each action tray. */
  readonly tray = signal<Record<string, any[]>>({ compare: [], trade: [], xi: [] });
  readonly trays = computed(() => {
    const own = !!this.auth.activeFranchise() && (!this.res.value() || this.res.value()?.franchiseId === this.auth.activeFranchise()?.id);
    return [
      { key: 'compare', label: 'Compare', icon: 'compare_arrows', hint: '2–5 players', action: 'Compare', min: 2, max: 5, show: this.auth.has(P.comparisonsRead) },
      { key: 'trade', label: 'Offer in a trade', icon: 'swap_horiz', hint: 'your players', action: 'Start trade offer', min: 1, max: 10, show: own && this.auth.has(P.tradesCreate) },
      { key: 'xi', label: 'Playing XI', icon: 'sports_cricket', hint: 'up to 11', action: 'Open in XI builder', min: 1, max: 11, show: own && this.auth.has(P.xiManage) },
    ].filter((t) => t.show);
  });
  readonly trayIds = computed(() => this.trays().map((t) => 'tray-' + t.key));

  trayDrop(e: CdkDragDrop<string>, key: string): void {
    const p = e.item.data;
    const t = this.trays().find((x) => x.key === key)!;
    this.tray.update((all) => {
      const list = all[key];
      if (list.some((x) => x.playerId === p.playerId) || list.length >= t.max) return all;
      return { ...all, [key]: [...list, p] };
    });
  }

  untray(key: string, id: string): void {
    this.tray.update((all) => ({ ...all, [key]: all[key].filter((x) => x.playerId !== id) }));
  }

  go(key: string): void {
    const ids = this.tray()[key].map((p) => p.playerId).join(',');
    if (key === 'compare') void this.router.navigate(['/comparison'], { queryParams: { ids } });
    if (key === 'trade') void this.router.navigate(['/trading/new'], { queryParams: { offer: ids } });
    if (key === 'xi') void this.router.navigate(['/playing-xi'], { queryParams: { add: ids } });
  }
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  /** ?franchise=<id> shows another team's squad (league staff, or browsing a rival). */
  readonly franchise = input<string>();
  readonly chosen = signal<string | null>(null);
  readonly view = signal<'cards' | 'table'>('cards');
  /** League staff without an active franchise pick a team here. */
  readonly pickable = computed(() => !this.auth.activeFranchise() || !!this.franchise());
  readonly franchises = rxResource({ stream: () => (this.pickable() ? this.api.get<any[]>('franchises') : of([])) });
  private readonly target = computed(() => this.chosen() ?? this.franchise() ?? (this.auth.activeFranchise() ? null : this.franchises.value()?.[0]?.id ?? undefined));
  readonly res = rxResource({
    params: () => ({ id: this.target() }),
    stream: ({ params }) => (params.id === undefined ? of(undefined as any) : this.api.get<any>(params.id ? `franchises/${params.id}/squad` : 'squad')),
  });
  readonly vars = computed(() => teamVars(this.res.value()?.primaryColor, this.res.value()?.secondaryColor));
  readonly captain = computed(() => (this.res.value()?.players ?? []).find((p: any) => p.isCaptain)?.name ?? null);
  readonly sections = computed(() => {
    const players: any[] = this.res.value()?.players ?? [];
    const order = (a: any, b: any) => Number(b.isCaptain) - Number(a.isCaptain) || (b.matches ?? 0) - (a.matches ?? 0) || a.name.localeCompare(b.name);
    const keeper = (p: any) => p.isWicketKeeper || p.roleCode === 'wk_batter';
    return [
      { key: 'wk', title: 'Wicketkeepers', icon: 'back_hand', players: players.filter(keeper).sort(order) },
      { key: 'bat', title: 'Batters', icon: 'sports_cricket', players: players.filter((p) => !keeper(p) && p.roleCode === 'batter').sort(order) },
      { key: 'ar', title: 'All-rounders', icon: 'auto_awesome', players: players.filter((p) => !keeper(p) && p.roleCode === 'allrounder').sort(order) },
      { key: 'bowl', title: 'Bowlers', icon: 'sports_baseball', players: players.filter((p) => !keeper(p) && p.roleCode === 'bowler').sort(order) },
    ].filter((s) => s.players.length > 0);
  });

  jump(key: string): void {
    if (this.view() !== 'cards') this.view.set('cards');
    setTimeout(() => document.getElementById('sec-' + key)?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }
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
