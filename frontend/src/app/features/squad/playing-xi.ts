import { CdkDrag, CdkDragDrop, CdkDragPlaceholder, CdkDropList, CdkDropListGroup, moveItemInArray } from '@angular/cdk/drag-drop';
import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Toast } from '../../core/services';
import { PlayerName } from '../../shared/media';
import { roleIcon } from '../../shared/player-card';
import { PageHeader, StateView, StatusChip } from '../../shared/ui';

interface Slot {
  playerId: string;
  battingOrder: number | null;
  isImpactSubstitute: boolean;
  isBowlingOption: boolean;
}

type ListId = 'squad' | 'xi' | 'subs';

@Component({
  selector: 'app-playing-xi',
  imports: [PageHeader, StateView, StatusChip, PlayerName, DatePipe, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatSelectModule, MatTooltipModule, CdkDropListGroup, CdkDropList, CdkDrag, CdkDragPlaceholder],
  template: `
    <div class="page">
      <app-page-header title="Playing XI builder" subtitle="Drag players into the XI and the batting order; the season rules are checked as you go">
        <button mat-stroked-button (click)="newXI()"><mat-icon>add</mat-icon>New combination</button>
      </app-page-header>
      <app-state [loading]="squad.isLoading()" [error]="squad.error()" (retry)="squad.reload()">
        <div class="xi-grid" cdkDropListGroup>
          <!-- Squad (drag source; drop here to remove) -->
          <section class="card">
            <div class="row"><h2>Squad</h2><span class="spacer"></span>
              @for (f of filters; track f.key) { <button mat-button class="small" [class.on]="filter() === f.key" (click)="filter.set(f.key)"><mat-icon class="role-ico">{{ f.icon }}</mat-icon>{{ f.label }}</button> }
            </div>
            <div class="drop squad-list" cdkDropList id="squad" [cdkDropListData]="'squad'" (cdkDropListDropped)="drop($event, 'squad')">
              @for (p of available(); track p.playerId) {
                <div class="xi-row" cdkDrag [cdkDragData]="p.playerId">
                  <mat-icon class="grip">drag_indicator</mat-icon>
                  <app-pname [id]="p.playerId" [name]="p.name" [size]="30" />
                  @if (p.isOverseas) { <span class="tag os">OS</span> }@if (p.isWicketKeeper) { <span class="tag">WK</span> }
                  <span class="muted small"><mat-icon class="role-ico">{{ icon(p) }}</mat-icon>{{ p.role }}{{ p.rating !== null ? ' · ' + p.rating : '' }}</span>
                  <span class="spacer"></span>
                  @if (p.availability !== 'Available') { <app-chip [text]="p.availability" /> }
                  <button mat-button [disabled]="starters().length >= 11" (click)="add(p, false)">+ XI</button>
                  <button mat-button (click)="add(p, true)">+ Sub</button>
                  <div class="xi-row placeholder" *cdkDragPlaceholder></div>
                </div>
              } @empty { <p class="muted small">Everyone in this filter is already picked.</p> }
            </div>
          </section>

          <!-- Selection -->
          <section class="card">
            <div class="row"><mat-form-field><mat-label>Name</mat-label><input matInput [(ngModel)]="name" /></mat-form-field>
              <mat-form-field><mat-label>Opponent</mat-label><input matInput [(ngModel)]="opponent" /></mat-form-field>
              <mat-form-field><mat-label>Venue</mat-label><input matInput [(ngModel)]="venue" /></mat-form-field>
              <mat-checkbox [(ngModel)]="preferred">Preferred</mat-checkbox></div>

            <h2 class="xi-title">Starting XI <span [class.full]="starters().length === 11">{{ starters().length }}/11</span></h2>
            <div class="drop xi-list" cdkDropList id="xi" [cdkDropListData]="'xi'" (cdkDropListDropped)="drop($event, 'xi')"
                 [cdkDropListEnterPredicate]="canEnterXI">
              @for (s of starters(); track s.playerId; let i = $index) {
                <div class="xi-row picked" cdkDrag [cdkDragData]="s.playerId">
                  <mat-icon class="grip">drag_indicator</mat-icon>
                  <span class="order">{{ i + 1 }}</span>
                  <app-pname [id]="s.playerId" [name]="nameOf(s.playerId)" [size]="30" />
                  @if (captainId === s.playerId) { <span class="role-badge c" matTooltip="Captain">C</span> }
                  @if (viceCaptainId === s.playerId) { <span class="role-badge" matTooltip="Vice-captain">VC</span> }
                  @if (keeperId === s.playerId) { <span class="role-badge" matTooltip="Wicketkeeper"><mat-icon>back_hand</mat-icon></span> }
                  @if (player(s.playerId)?.isOverseas) { <span class="tag os">OS</span> }
                  <span class="spacer"></span>
                  <mat-checkbox [checked]="s.isBowlingOption" (change)="toggleBowl(s.playerId)">Bowls</mat-checkbox>
                  <button mat-icon-button (click)="remove(s.playerId)" aria-label="Remove"><mat-icon>close</mat-icon></button>
                  <div class="xi-row placeholder" *cdkDragPlaceholder></div>
                </div>
              } @empty { <p class="drop-hint"><mat-icon>south</mat-icon>Drag players here (top to bottom = batting order)</p> }
            </div>

            <h3>Impact Player substitutes</h3>
            <div class="drop subs-list" cdkDropList id="subs" [cdkDropListData]="'subs'" (cdkDropListDropped)="drop($event, 'subs')">
              @for (s of subs(); track s.playerId) {
                <div class="xi-row picked" cdkDrag [cdkDragData]="s.playerId">
                  <mat-icon class="grip">drag_indicator</mat-icon>
                  <app-pname [id]="s.playerId" [name]="nameOf(s.playerId)" [size]="28" />
                  <span class="spacer"></span>
                  <button mat-icon-button (click)="remove(s.playerId)" aria-label="Remove"><mat-icon>close</mat-icon></button>
                  <div class="xi-row placeholder" *cdkDragPlaceholder></div>
                </div>
              } @empty { <p class="drop-hint"><mat-icon>swap_vert</mat-icon>Drag substitutes here</p> }
            </div>

            <div class="row">
              <mat-form-field><mat-label>Captain</mat-label><mat-select [(ngModel)]="captainId" (ngModelChange)="validate()">@for (s of starters(); track s.playerId) { <mat-option [value]="s.playerId">{{ nameOf(s.playerId) }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field><mat-label>Vice-captain</mat-label><mat-select [(ngModel)]="viceCaptainId" (ngModelChange)="validate()"><mat-option [value]="null">None</mat-option>@for (s of starters(); track s.playerId) { <mat-option [value]="s.playerId">{{ nameOf(s.playerId) }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field><mat-label>Wicketkeeper</mat-label><mat-select [(ngModel)]="keeperId" (ngModelChange)="validate()">@for (s of starters(); track s.playerId) { <mat-option [value]="s.playerId">{{ nameOf(s.playerId) }}</mat-option> }</mat-select></mat-form-field>
            </div>
            @if (check(); as c) {
              <p class="small">{{ c.overseas }} overseas · {{ c.bowlingOptions }} bowling options · average rating {{ c.strength }}</p>
              @for (e of c.errors; track e) { <p class="bad-text small">✖ {{ e }}</p> }
              @for (w of c.warnings; track w) { <p class="warn-text small">⚠ {{ w }}</p> }
            }
            @if (auth.has(P.xiManage)) { <button mat-flat-button [disabled]="!name || (check()?.errors?.length ?? 1) > 0" (click)="save()">{{ editingId() ? 'Update' : 'Save' }} combination</button> }
          </section>
        </div>
      </app-state>
      <section class="card"><h2>Saved combinations</h2>
        <app-state [loading]="xis.isLoading()" [error]="xis.error()" [empty]="!xis.value()?.length" emptyText="No combinations saved yet." (retry)="xis.reload()">
          @for (x of xis.value() ?? []; track x.id) {
            <div class="list-item"><strong>{{ x.name }}</strong>@if (x.isPreferred) { <span class="tag">Preferred</span> }
              <span class="muted small">{{ x.opponent ? 'vs ' + x.opponent : '' }} {{ x.venue ? '· ' + x.venue : '' }}</span>
              <span class="small">{{ x.players.length }} players</span>
              @if (x.warnings.length) { <span class="warn-text small">{{ x.warnings.length }} warning(s)</span> }
              <span class="spacer"></span><span class="small muted">{{ x.updatedAt | date: 'd MMM HH:mm' }}</span>
              <button mat-button (click)="load(x)">Open</button>
              @if (auth.has(P.xiManage)) { <button mat-icon-button (click)="delete(x.id)" aria-label="Delete"><mat-icon>delete</mat-icon></button> }
            </div>
          }
        </app-state>
      </section>
    </div>
  `,
  styles: `
    .xi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(460px, 1fr)); gap: 16px; align-items: start; }
    .drop { min-height: 64px; border-radius: 12px; padding: 4px; transition: background 0.15s, box-shadow 0.15s; }
    .drop.cdk-drop-list-receiving, .drop.cdk-drop-list-dragging { background: var(--team-soft); box-shadow: inset 0 0 0 2px var(--team-line); }
    .squad-list { max-height: 640px; overflow: auto; }
    .xi-row { display: flex; align-items: center; gap: 8px; padding: 6px 8px; border-radius: 10px; border-bottom: 1px solid var(--line); background: var(--card); cursor: grab; }
    .xi-row.picked { background: var(--card-2); margin-bottom: 4px; border: 1px solid var(--line); }
    .xi-row:active { cursor: grabbing; }
    .xi-row.placeholder { min-height: 44px; background: var(--team-soft); border: 2px dashed var(--team-line); }
    .grip { color: var(--muted); font-size: 18px; width: 18px; height: 18px; }
    .order { width: 24px; height: 24px; border-radius: 50%; display: inline-flex; align-items: center; justify-content: center; font-weight: 800; font-size: 12px;
      background: var(--team); color: var(--team-ink); }
    .role-badge { display: inline-flex; align-items: center; justify-content: center; min-width: 24px; height: 22px; padding: 0 5px; border-radius: 6px; font-size: 11px; font-weight: 900;
      background: rgba(255,255,255,0.1); }
    .role-badge.c { background: var(--team); color: var(--team-ink); }
    .role-badge mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .xi-title { display: flex; gap: 10px; align-items: center; } .xi-title span { color: var(--warn); } .xi-title span.full { color: var(--good); }
    .drop-hint { display: flex; gap: 6px; align-items: center; justify-content: center; color: var(--muted); padding: 18px; border: 2px dashed var(--line); border-radius: 10px; margin: 0; }
    button.on { background: var(--team-soft); }
  `,
})
export class PlayingXIPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly squad = rxResource({ stream: () => this.api.get<any>('squad') });
  readonly xis = rxResource({ stream: () => this.api.get<any[]>('squad/playing-xi') });
  readonly slots = signal<Slot[]>([]);
  readonly starters = computed(() => this.slots().filter((s) => !s.isImpactSubstitute));
  readonly subs = computed(() => this.slots().filter((s) => s.isImpactSubstitute));
  readonly check = signal<any>(null);
  readonly editingId = signal<string | null>(null);
  readonly filter = signal<string>('all');
  readonly filters = [
    { key: 'all', label: 'All', icon: 'groups' }, { key: 'bat', label: 'Bat', icon: 'sports_cricket' },
    { key: 'ar', label: 'AR', icon: 'auto_awesome' }, { key: 'bowl', label: 'Bowl', icon: 'sports_baseball' },
  ];
  readonly available = computed(() => {
    const f = this.filter();
    return (this.squad.value()?.players ?? []).filter((p: any) => !this.slots().some((s) => s.playerId === p.playerId)).filter((p: any) =>
      f === 'all' || (f === 'bat' && (p.roleCode === 'batter' || p.roleCode === 'wk_batter')) || (f === 'ar' && p.roleCode === 'allrounder') || (f === 'bowl' && p.roleCode === 'bowler'));
  });
  name = 'Match XI';
  opponent = '';
  venue = '';
  preferred = false;
  captainId: string | null = null;
  viceCaptainId: string | null = null;
  keeperId: string | null = null;

  constructor() {
    effect(() => {
      this.slots();
      this.validate();
    });
  }

  /** The XI list accepts a new player only while it has fewer than 11 (reordering inside it is always fine). */
  readonly canEnterXI = (drag: CdkDrag<string>, drop: CdkDropList<string>) =>
    drag.dropContainer === drop || this.starters().length < 11;

  icon(p: any): string { return roleIcon(p.roleCode, p.isWicketKeeper); }

  player(id: string): any { return this.squad.value()?.players.find((p: any) => p.playerId === id); }

  nameOf(id: string): string { return this.player(id)?.name ?? 'Unknown player'; }

  inXI(id: string): boolean { return this.slots().some((s) => s.playerId === id); }

  /** One handler for every drop: reorder, move between XI and substitutes, add from the squad, or drop back to remove. */
  drop(e: CdkDragDrop<string>, target: ListId): void {
    const id: string = e.item.data;
    const from = e.previousContainer.data;
    if (target === 'squad') { if (from !== 'squad') this.remove(id); return; }
    const p = this.player(id);
    this.slots.update((all) => {
      let starters = all.filter((s) => !s.isImpactSubstitute);
      let subs = all.filter((s) => s.isImpactSubstitute);
      const existing = all.find((s) => s.playerId === id);
      starters = starters.filter((s) => s.playerId !== id);
      subs = subs.filter((s) => s.playerId !== id);
      const slot: Slot = existing
        ? { ...existing, isImpactSubstitute: target === 'subs' }
        : { playerId: id, battingOrder: null, isImpactSubstitute: target === 'subs', isBowlingOption: !!p?.canBowl };
      if (target === 'xi') {
        if (from === 'xi') {
          const list = all.filter((s) => !s.isImpactSubstitute);
          moveItemInArray(list, e.previousIndex, e.currentIndex);
          return [...list, ...subs];
        }
        if (starters.length >= 11) { this.toast.error('The XI already has 11 players.'); return all; }
        starters.splice(e.currentIndex, 0, slot);
      } else {
        subs.splice(e.currentIndex, 0, slot);
      }
      return [...starters, ...subs];
    });
    if (target === 'xi' && p?.isWicketKeeper && !this.keeperId) this.keeperId = id;
    if (target === 'subs') for (const k of ['captainId', 'viceCaptainId', 'keeperId'] as const) if (this[k] === id) this[k] = null;
  }

  add(p: any, sub: boolean): void {
    this.slots.update((s) => [...s, { playerId: p.playerId, battingOrder: null, isImpactSubstitute: sub, isBowlingOption: p.canBowl }]);
    if (!sub && p.isWicketKeeper && !this.keeperId) this.keeperId = p.playerId;
  }

  remove(id: string): void {
    this.slots.update((s) => s.filter((x) => x.playerId !== id));
    for (const k of ['captainId', 'viceCaptainId', 'keeperId'] as const) if (this[k] === id) this[k] = null;
  }

  toggleBowl(id: string): void {
    this.slots.update((s) => s.map((x) => (x.playerId === id ? { ...x, isBowlingOption: !x.isBowlingOption } : x)));
  }

  private body() {
    return {
      name: this.name, opponent: this.opponent || null, venue: this.venue || null, captainId: this.captainId, viceCaptainId: this.viceCaptainId,
      wicketKeeperId: this.keeperId, isPreferred: this.preferred, notes: null,
      players: [...this.starters().map((s, i) => ({ ...s, battingOrder: i + 1 })), ...this.subs().map((s) => ({ ...s, battingOrder: null }))],
    };
  }

  validate(): void {
    if (this.slots().length === 0) return this.check.set(null);
    this.api.post<any>('squad/playing-xi/validate', this.body()).subscribe({ next: (c) => this.check.set(c), error: () => {} });
  }

  newXI(): void {
    this.slots.set([]);
    this.editingId.set(null);
    this.name = 'Match XI';
    this.opponent = this.venue = '';
    this.captainId = this.viceCaptainId = this.keeperId = null;
    this.preferred = false;
  }

  load(x: any): void {
    this.editingId.set(x.id);
    this.name = x.name;
    this.opponent = x.opponent ?? '';
    this.venue = x.venue ?? '';
    this.preferred = x.isPreferred;
    this.captainId = x.captainId;
    this.viceCaptainId = x.viceCaptainId;
    this.keeperId = x.wicketKeeperId;
    this.slots.set(x.players.map((p: any) => ({ playerId: p.playerId, battingOrder: p.battingOrder, isImpactSubstitute: p.isImpactSubstitute, isBowlingOption: p.isBowlingOption })));
  }

  save(): void {
    const id = this.editingId();
    const call = id ? this.api.put<any>(`squad/playing-xi/${id}`, this.body()) : this.api.post<any>('squad/playing-xi', this.body());
    call.subscribe({ next: (r) => { this.editingId.set(r.id); this.toast.ok('Combination saved.'); this.xis.reload(); }, error: (e) => this.toast.error(e) });
  }

  delete(id: string): void {
    this.api.delete(`squad/playing-xi/${id}`).subscribe({ next: () => { if (this.editingId() === id) this.newXI(); this.xis.reload(); }, error: (e) => this.toast.error(e) });
  }
}
