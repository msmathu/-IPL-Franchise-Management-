import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { filter, of } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Realtime, Toast, WordsPipe, formatInr } from '../../core/services';
import { Monogram, PageHeader, StateView, StatusChip, confirmAction } from '../../shared/ui';
import { SquadSelect, TradeImpact } from './trade-editor';

@Component({
  selector: 'app-trade-detail',
  imports: [PageHeader, StateView, StatusChip, Monogram, TradeImpact, SquadSelect, RouterLink, InrPipe, WordsPipe, DatePipe, FormsModule, MatButtonModule,
    MatIconModule, MatFormFieldModule, MatInputModule, MatCheckboxModule],
  template: `
    <div class="page">
      <app-state [loading]="res.isLoading() && !res.value()" [error]="res.error()" (retry)="res.reload()">
        @if (res.value(); as t) {
          <div class="row">
            <app-page-header [title]="'Trade ' + t.reference" [subtitle]="(t.type | words) + ' · revision ' + t.revisionNo" />
            <span class="spacer"></span><app-chip [text]="t.status | words" />
          </div>
          @if (t.awaitingFranchiseId && t.awaitingFranchiseId === auth.user()?.activeFranchiseId) { <div class="demo-banner">It's your turn: accept, counter or reject this offer.</div> }
          <div class="grid cols-2">
            @for (side of sides(); track side.f.id) {
              <section class="card">
                <div class="row"><app-monogram [name]="side.f.name" [color]="side.f.primaryColor" /><h2>{{ side.f.name }} gives</h2></div>
                @for (p of side.players; track p.playerId) {
                  <div class="list-item"><a [routerLink]="['/players', p.playerId]">{{ p.name }}</a>@if (p.isOverseas) { <span class="tag os">OS</span> }
                    <span class="muted small">{{ p.role }}</span><span class="spacer"></span><span class="small">salary {{ p.contractSalary | inr }}</span>
                    @if (p.availability !== 'Available') { <app-chip [text]="p.availability" /> }</div>
                } @empty { <p class="muted">No players.</p> }
                @if (t.cashConsideration && t.cashPayerFranchiseId === side.f.id) { <p>+ trade consideration <strong>{{ t.cashConsideration | inr }}</strong></p> }
              </section>
            }
          </div>
          @if (t.conditions) { <p><strong>Conditions:</strong> {{ t.conditions }}</p> }
          <p class="muted small">@if (t.submittedAt) { Submitted {{ t.submittedAt | date: 'd MMM yyyy HH:mm' }} · } @if (t.expiresAt) { expires {{ t.expiresAt | date: 'd MMM HH:mm' }} · } @if (t.completedAt) { completed {{ t.completedAt | date: 'd MMM yyyy' }} }</p>

          <section class="card row">
            @if (can('submit')) { <button mat-flat-button (click)="act('submit', 'Submit this offer?')">Submit</button> }
            @if (can('accept')) { <button mat-flat-button (click)="act('accept', 'Accept these terms in principle? Approvals will then be requested.')">Accept</button> }
            @if (can('counter')) { <button mat-stroked-button (click)="startCounter(t)">Counteroffer</button> }
            @if (can('reject')) { <button mat-stroked-button (click)="act('reject', 'Reject this offer?', true)">Reject</button> }
            @if (can('withdraw')) { <button mat-stroked-button (click)="act('withdraw', 'Withdraw this trade?', true)">Withdraw</button> }
            @if (can('complete')) { <button mat-flat-button (click)="complete(t)"><mat-icon>verified</mat-icon>Complete transfer</button> }
            <span class="spacer"></span>
            <button mat-button (click)="showImpact.set(!showImpact())">{{ showImpact() ? 'Hide' : 'Show' }} impact</button>
          </section>

          @if (countering()) {
            <section class="card"><h2>Counteroffer</h2>
              <div class="grid cols-2">
                <div><h3>You give</h3><app-squad-select [players]="mySquad.value()?.players ?? []" [selected]="cOffered()" (changed)="cOffered.set($event)" /></div>
                <div><h3>You receive</h3><app-squad-select [players]="theirSquad.value()?.players ?? []" [selected]="cRequested()" (changed)="cRequested.set($event)" /></div>
              </div>
              <div class="row">
                <mat-form-field><mat-label>Consideration (₹ lakh)</mat-label><input matInput type="number" [(ngModel)]="cCash" /></mat-form-field>
                <mat-checkbox [(ngModel)]="cIPay">We pay it</mat-checkbox>
                <mat-form-field class="wide"><mat-label>Message</mat-label><input matInput [(ngModel)]="cMessage" /></mat-form-field>
                <button mat-flat-button (click)="sendCounter(t)">Send counteroffer</button><button mat-button (click)="countering.set(false)">Cancel</button>
              </div>
            </section>
          }
          @if (showImpact()) { <app-trade-impact [impact]="impact.value()" /> }

          <div class="grid cols-2">
            <section class="card"><h2>Approvals</h2>
              @for (a of t.approvals; track a.id) {
                <div class="list-item">
                  <span>{{ a.type | words }}@if (a.playerName) { — {{ a.playerName }} }@if (a.franchiseName && !a.playerName) { — {{ a.franchiseName }} }</span>
                  <span class="spacer"></span>
                  @if (a.decidedBy) { <span class="small muted">{{ a.decidedBy }} · {{ a.decidedAt | date: 'd MMM HH:mm' }}</span> }
                  <app-chip [text]="a.decision" />
                  @if (a.canDecide) {
                    <button mat-stroked-button (click)="decide(a, true)">Approve</button>
                    <button mat-button (click)="decide(a, false)">Reject</button>
                  }
                </div>
                @if (a.comment) { <p class="small muted">“{{ a.comment }}”</p> }
              } @empty { <p class="muted">Approvals start when both franchises accept in principle.</p> }
              <p class="disclaimer">Order: both franchises authorise → each player consents → league approves → transfer completed.</p>
            </section>
            <section class="card"><h2>Negotiation</h2>
              <div class="timeline">
                @for (n of t.negotiations; track $index) {
                  <div class="event" [class.internal]="n.isInternal">
                    <div class="small muted">{{ n.at | date: 'd MMM HH:mm' }} · {{ n.byFranchise }}@if (n.byUser) { · {{ n.byUser }} } · rev {{ n.revisionNo }}</div>
                    <div><strong>{{ n.action | words }}</strong>@if (n.isInternal) { <span class="tag os">Internal note</span> }@if (n.message) { — {{ n.message }} }</div>
                  </div>
                }
              </div>
              @if (can('message') || can('note')) {
                <div class="row">
                  <mat-form-field class="full"><mat-label>Message</mat-label><input matInput [(ngModel)]="msg" /></mat-form-field>
                  @if (can('message')) { <button mat-stroked-button [disabled]="!msg" (click)="send(false)">Send to both</button> }
                  @if (can('note')) { <button mat-button [disabled]="!msg" (click)="send(true)">Internal note</button> }
                </div>
              }
            </section>
          </div>
        }
      </app-state>
    </div>
  `,
})
export class TradeDetailPage {
  readonly id = input.required<string>();
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly dialog = inject(MatDialog);
  readonly res = rxResource({ params: () => this.id(), stream: ({ params }) => this.api.get<any>(`trades/${params}`) });
  readonly showImpact = signal(false);
  readonly impact = rxResource({ params: () => (this.showImpact() ? this.id() : null), stream: ({ params }) => (params ? this.api.get<any>(`trades/${params}/impact`) : of(null)) });
  readonly countering = signal(false);
  readonly mySquad = rxResource({ params: () => this.countering(), stream: ({ params }) => (params ? this.api.get<any>('squad') : of(null)) });
  readonly otherId = computed(() => {
    const t = this.res.value();
    const me = this.auth.user()?.activeFranchiseId;
    return t ? (t.initiator.id === me ? t.counterparty.id : t.initiator.id) : null;
  });
  readonly theirSquad = rxResource({ params: () => (this.countering() ? this.otherId() : null), stream: ({ params }) => (params ? this.api.get<any>(`franchises/${params}/squad`) : of(null)) });
  readonly sides = computed(() => {
    const t = this.res.value();
    if (!t) return [];
    return [t.initiator, t.counterparty].map((f: any) => ({ f, players: t.players.filter((p: any) => p.fromFranchiseId === f.id) }));
  });
  readonly cOffered = signal<string[]>([]);
  readonly cRequested = signal<string[]>([]);
  cCash = 0;
  cIPay = true;
  cMessage = '';
  msg = '';

  constructor() {
    inject(Realtime).events.pipe(filter((e) => e.name === 'trade.updated' && e.payload?.id === this.id()), takeUntilDestroyed()).subscribe(() => this.res.reload());
  }

  can(action: string): boolean {
    return (this.res.value()?.allowedActions ?? []).includes(action);
  }

  private run(path: string, body: unknown = {}, ok?: string): void {
    this.api.post<any>(`trades/${this.id()}/${path}`, body).subscribe({
      next: (t) => { this.res.set(t); if (ok) this.toast.ok(ok); },
      error: (e) => this.toast.error(e),
    });
  }

  act(action: string, question: string, danger = false): void {
    confirmAction(this.dialog, { title: question, message: 'This is recorded in the trade history and the other franchise is notified.', danger, confirmText: action[0].toUpperCase() + action.slice(1) })
      .subscribe((yes) => yes && this.run(action, action === 'submit' ? {} : { message: null }, 'Done.'));
  }

  startCounter(t: any): void {
    const me = this.auth.user()?.activeFranchiseId;
    this.cOffered.set(t.players.filter((p: any) => p.fromFranchiseId === me).map((p: any) => p.playerId));
    this.cRequested.set(t.players.filter((p: any) => p.fromFranchiseId !== me).map((p: any) => p.playerId));
    this.cCash = t.cashConsideration / 1e5;
    this.cIPay = t.cashPayerFranchiseId === me;
    this.countering.set(true);
  }

  sendCounter(t: any): void {
    const me = this.auth.user()!.activeFranchiseId!;
    const cash = Math.round((Number(this.cCash) || 0) * 1e5);
    this.run('counter', {
      playersOffered: this.cOffered(), playersRequested: this.cRequested(), cashConsideration: cash,
      cashPayerFranchiseId: cash > 0 ? (this.cIPay ? me : this.otherId()) : null, conditions: t.conditions, message: this.cMessage || null,
    }, 'Counteroffer sent.');
    this.countering.set(false);
  }

  decide(a: any, approve: boolean): void {
    const comment = approve ? null : prompt('Reason for rejecting (required):');
    if (!approve && !comment) return;
    confirmAction(this.dialog, {
      title: approve ? 'Record approval?' : 'Record rejection?',
      message: approve ? `You are approving: ${a.type}${a.playerName ? ' for ' + a.playerName : ''}. This decision is audited.` : 'Rejecting stops the trade.',
      danger: !approve, confirmText: approve ? 'Approve' : 'Reject',
    }).subscribe((yes) => yes && this.run(`approvals/${a.id}`, { approve, comment }, 'Decision recorded.'));
  }

  complete(t: any): void {
    confirmAction(this.dialog, {
      title: 'Complete the transfer?',
      message: `Players and contracts move between squads and money is booked${t.cashConsideration ? ` (consideration ${formatInr(t.cashConsideration)})` : ''}. Every rule is re-checked first. This cannot be undone.`,
      confirmText: 'Complete transfer',
    }).subscribe((yes) => yes && this.run('complete', {}, 'Trade completed — the transfer is official.'));
  }

  send(internal: boolean): void {
    this.run('messages', { message: this.msg, internal }, internal ? 'Note saved.' : 'Message sent.');
    this.msg = '';
  }
}
