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
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Toast } from '../../core/services';
import { PageHeader, StateView, StatusChip } from '../../shared/ui';

interface Slot {
  playerId: string;
  battingOrder: number | null;
  isImpactSubstitute: boolean;
  isBowlingOption: boolean;
}

@Component({
  selector: 'app-playing-xi',
  imports: [PageHeader, StateView, StatusChip, DatePipe, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule],
  template: `
    <div class="page">
      <app-page-header title="Playing XI builder" subtitle="Build and compare combinations; the season rules are checked as you go">
        <button mat-stroked-button (click)="newXI()"><mat-icon>add</mat-icon>New combination</button>
      </app-page-header>
      <app-state [loading]="squad.isLoading()" [error]="squad.error()" (retry)="squad.reload()">
        <div class="grid cols-2">
          <section class="card"><h2>Squad</h2>
            @for (p of squad.value()?.players ?? []; track p.playerId) {
              <div class="list-item">
                <span>{{ p.name }}@if (p.isOverseas) { <span class="tag os">OS</span> }@if (p.isWicketKeeper) { <span class="tag">WK</span> }</span>
                <span class="muted small">{{ p.role }} · {{ p.rating }}</span><span class="spacer"></span>
                @if (p.availability !== 'Available') { <app-chip [text]="p.availability" /> }
                @if (!inXI(p.playerId)) {
                  <button mat-button [disabled]="starters().length >= 11" (click)="add(p, false)">+ XI</button>
                  <button mat-button (click)="add(p, true)">+ Sub</button>
                }
              </div>
            }
          </section>
          <section class="card">
            <div class="row"><mat-form-field><mat-label>Name</mat-label><input matInput [(ngModel)]="name" /></mat-form-field>
              <mat-form-field><mat-label>Opponent</mat-label><input matInput [(ngModel)]="opponent" /></mat-form-field>
              <mat-form-field><mat-label>Venue</mat-label><input matInput [(ngModel)]="venue" /></mat-form-field>
              <mat-checkbox [(ngModel)]="preferred">Preferred</mat-checkbox></div>
            <h2>Starting XI ({{ starters().length }}/11)</h2>
            @for (s of starters(); track s.playerId; let i = $index) {
              <div class="list-item">
                <strong>{{ i + 1 }}</strong><span>{{ nameOf(s.playerId) }}</span><span class="spacer"></span>
                <mat-checkbox [checked]="s.isBowlingOption" (change)="toggleBowl(s.playerId)">Bowls</mat-checkbox>
                <button mat-icon-button (click)="move(s.playerId, -1)" [disabled]="i === 0" aria-label="Up"><mat-icon>arrow_upward</mat-icon></button>
                <button mat-icon-button (click)="move(s.playerId, 1)" [disabled]="i === starters().length - 1" aria-label="Down"><mat-icon>arrow_downward</mat-icon></button>
                <button mat-icon-button (click)="remove(s.playerId)" aria-label="Remove"><mat-icon>close</mat-icon></button>
              </div>
            } @empty { <p class="muted">Add 11 players from the squad.</p> }
            <h3>Impact Player substitutes</h3>
            @for (s of subs(); track s.playerId) { <div class="list-item"><span>{{ nameOf(s.playerId) }}</span><span class="spacer"></span><button mat-icon-button (click)="remove(s.playerId)" aria-label="Remove"><mat-icon>close</mat-icon></button></div> }
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

  nameOf(id: string): string {
    return this.squad.value()?.players.find((p: any) => p.playerId === id)?.name ?? 'Unknown player';
  }

  inXI(id: string): boolean {
    return this.slots().some((s) => s.playerId === id);
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

  move(id: string, dir: number): void {
    this.slots.update((all) => {
      const starters = all.filter((s) => !s.isImpactSubstitute);
      const i = starters.findIndex((s) => s.playerId === id);
      [starters[i], starters[i + dir]] = [starters[i + dir], starters[i]];
      return [...starters, ...all.filter((s) => s.isImpactSubstitute)];
    });
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
