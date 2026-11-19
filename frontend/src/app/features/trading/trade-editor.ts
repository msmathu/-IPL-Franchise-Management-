import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { PlayerName, TeamName } from '../../shared/media';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { debounceTime, of, Subject, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Toast, formatInr } from '../../core/services';
import { PageHeader, StateView, confirmAction } from '../../shared/ui';

export interface TradeTerms {
  counterpartyFranchiseId: string;
  playersOffered: string[];
  playersRequested: string[];
  cashConsideration: number;
  cashPayerFranchiseId: string | null;
  conditions: string | null;
}

/** Shows before/after squad impact for both franchises, keeping salary, consideration and purse separate. */
@Component({
  selector: 'app-trade-impact',
  imports: [TeamName, InrPipe],
  template: `
    @if (impact(); as i) {
      <div class="grid cols-2">
        @for (side of [i.initiator, i.counterparty]; track side.franchiseId) {
          <section class="card">
            <h2><app-team [key]="side.franchiseName" [size]="28" /></h2>
            <table class="data small">
              <tr><th></th><th class="num">Before</th><th class="num">After</th></tr>
              <tr><td>Squad size</td><td class="num">{{ side.before.total }}</td><td class="num" [class.bad-text]="side.after.total > side.after.maxSquad">{{ side.after.total }} / {{ side.after.maxSquad }}</td></tr>
              <tr><td>Overseas</td><td class="num">{{ side.before.overseas }}</td><td class="num" [class.bad-text]="side.after.overseas > side.after.maxOverseas">{{ side.after.overseas }} / {{ side.after.maxOverseas }}</td></tr>
              <tr><td>Batters · bowlers · all-rounders · keepers</td><td class="num">{{ side.before.batters }}·{{ side.before.bowlers }}·{{ side.before.allRounders }}·{{ side.before.wicketKeepers }}</td>
                <td class="num">{{ side.after.batters }}·{{ side.after.bowlers }}·{{ side.after.allRounders }}·{{ side.after.wicketKeepers }}</td></tr>
              <tr><td>Contract salaries</td><td class="num">{{ side.salaryBefore | inr }}</td><td class="num">{{ side.salaryAfter | inr }}</td></tr>
              <tr><td>Auction purse</td><td class="num">{{ side.purseBefore | inr }}</td><td class="num" [class.bad-text]="side.purseAfter < 0">{{ side.purseAfter | inr }}</td></tr>
              <tr><td>Trade consideration</td><td class="num" colspan="2">{{ side.tradeConsiderationChange === 0 ? '—' : (side.tradeConsiderationChange > 0 ? 'Receives ' : 'Pays ') + (abs(side.tradeConsiderationChange) | inr) }}</td></tr>
              <tr><td>Team strength (model)</td><td class="num">{{ side.strengthBefore }}</td><td class="num" [class.good-text]="side.strengthAfter > side.strengthBefore" [class.bad-text]="side.strengthAfter < side.strengthBefore">{{ side.strengthAfter }}</td></tr>
              <tr><td>Open slots after</td><td class="num" colspan="2">{{ side.after.slotsRemaining }} squad · {{ side.after.overseasSlotsRemaining }} overseas</td></tr>
            </table>
            @if (side.playersIn.length) { <p class="small">In: {{ side.playersIn.join(', ') }}</p> }
            @if (side.playersOut.length) { <p class="small">Out: {{ side.playersOut.join(', ') }}</p> }
            @for (v of side.violations; track v) { <p class="small bad-text">✖ {{ v }}</p> }
            @for (r of side.risks; track r) { <p class="small warn-text">⚠ {{ r }}</p> }
          </section>
        }
      </div>
      @for (n of i.ruleNotes; track n) { <p class="disclaimer">{{ n }}</p> }
    }
  `,
})
export class TradeImpact {
  readonly impact = input<any>();
  abs(v: number): number {
    return Math.abs(v);
  }
}

/** Player multi-select from a squad. */
@Component({
  selector: 'app-squad-select',
  imports: [PlayerName, MatCheckboxModule, InrPipe],
  template: `
    <div class="squad-list">
      @for (p of players(); track p.playerId) {
        <label class="list-item"><mat-checkbox [checked]="selected().includes(p.playerId)" (change)="toggle(p.playerId)" />
          <span><app-pname [id]="p.playerId ?? p.id" [name]="p.name" [size]="24" [link]="false" />@if (p.isOverseas) { <span class="tag os">OS</span> }</span><span class="muted small">{{ p.role }}@if (p.age !== null && p.age !== undefined) { · {{ p.age }}y }</span>
          <span class="spacer"></span><span class="small">{{ p.price | inr }}</span>
          @if (p.availability !== 'Available') { <span class="small warn-text">{{ p.availability }}</span> }</label>
      } @empty { <p class="muted">No players.</p> }
    </div>
  `,
  styles: `.squad-list { max-height: 360px; overflow: auto; } label { cursor: pointer; }`,
})
export class SquadSelect {
  readonly players = input<any[]>([]);
  readonly selected = input<string[]>([]);
  readonly changed = output<string[]>();
  toggle(id: string): void {
    const s = this.selected();
    this.changed.emit(s.includes(id) ? s.filter((x) => x !== id) : [...s, id]);
  }
}

@Component({
  selector: 'app-trade-editor',
  imports: [PageHeader, StateView, TradeImpact, SquadSelect, FormsModule, MatFormFieldModule, MatSelectModule, MatInputModule, MatButtonModule, MatIconModule, InrPipe],
  template: `
    <div class="page">
      <app-page-header title="New trade offer" subtitle="Choose what you give and what you want; the impact updates as you go" />
      <section class="card">
        <div class="row">
          <mat-form-field class="wide"><mat-label>Trade with</mat-label>
            <mat-select [ngModel]="counterparty()" (ngModelChange)="counterparty.set($event); requested.set([])">
              @for (f of others(); track f.id) { <mat-option [value]="f.id">{{ f.name }}</mat-option> }
            </mat-select></mat-form-field>
          <mat-form-field><mat-label>Trade consideration (₹ lakh)</mat-label><input matInput type="number" min="0" [ngModel]="cashLakh()" (ngModelChange)="cashLakh.set(+$event || 0)" /></mat-form-field>
          <mat-form-field><mat-label>Paid by</mat-label><mat-select [ngModel]="payer()" (ngModelChange)="payer.set($event)">
            <mat-option value="me">{{ auth.activeFranchise()?.name }}</mat-option><mat-option value="them">The other franchise</mat-option></mat-select></mat-form-field>
          <mat-form-field class="wide"><mat-label>Conditions</mat-label><input matInput [ngModel]="conditions()" (ngModelChange)="conditions.set($event)" placeholder="e.g. subject to medical clearance" /></mat-form-field>
        </div>
        <p class="muted small">The trade consideration is separate from contract salaries and from the auction purse; whether it affects the purse depends on the season's rules (shown below).</p>
      </section>
      <div class="grid cols-2">
        <section class="card"><h2>You give</h2>
          <app-state [loading]="mySquad.isLoading()" [error]="mySquad.error()" (retry)="mySquad.reload()">
            <app-squad-select [players]="mySquad.value()?.players ?? []" [selected]="offered()" (changed)="offered.set($event)" />
          </app-state></section>
        <section class="card"><h2>You receive</h2>
          @if (counterparty()) {
            <app-state [loading]="theirSquad.isLoading()" [error]="theirSquad.error()" (retry)="theirSquad.reload()">
              <app-squad-select [players]="theirSquad.value()?.players ?? []" [selected]="requested()" (changed)="requested.set($event)" />
            </app-state>
          } @else { <p class="muted">Choose a franchise first.</p> }
        </section>
      </div>
      @if (impactError()) { <p class="bad-text">{{ impactError() }}</p> }
      @if (impact()) {
        <h2>Trade impact</h2>
        <app-trade-impact [impact]="impact()" />
        @if (value(); as v) {
          <section class="card"><h2>Model value comparison</h2>
            <p>You give {{ v.initiatorGives | inr }} · you receive {{ v.initiatorReceives | inr }} → <strong>{{ v.verdict }}</strong></p>
            <p class="disclaimer">{{ v.disclaimer }}</p></section>
        }
      }
      <section class="card row">
        <mat-form-field class="wide"><mat-label>Message to the other franchise</mat-label><input matInput [ngModel]="message()" (ngModelChange)="message.set($event)" /></mat-form-field>
        <span class="spacer"></span>
        <button mat-stroked-button [disabled]="!valid() || busy()" (click)="save(false)">Save draft</button>
        <button mat-flat-button [disabled]="!valid() || busy() || impact()?.isValid === false" (click)="save(true)">Submit offer</button>
      </section>
    </div>
  `,
})
export class TradeEditorPage {
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  readonly franchises = rxResource({ stream: () => this.api.get<any[]>('franchises') });
  readonly others = computed(() => (this.franchises.value() ?? []).filter((f) => f.id !== this.auth.user()?.activeFranchiseId));
  readonly counterparty = signal<string | null>(null);
  readonly offered = signal<string[]>([]);
  /** ?offer=a,b (from the squad page's drag tray) preselects the players offered. */
  readonly offer = input<string>();
  private readonly preselectOffer = effect(() => {
    const ids = (this.offer() ?? '').split(',').filter(Boolean);
    if (ids.length) untracked(() => this.offered.set(ids));
  });
  readonly requested = signal<string[]>([]);
  readonly cashLakh = signal(0);
  readonly payer = signal<'me' | 'them'>('me');
  readonly conditions = signal('');
  readonly message = signal('');
  readonly busy = signal(false);
  readonly impact = signal<any>(null);
  readonly impactError = signal<string | null>(null);
  readonly value = signal<any>(null);
  readonly mySquad = rxResource({ stream: () => this.api.get<any>('squad') });
  readonly theirSquad = rxResource({ params: () => this.counterparty(), stream: ({ params }) => (params ? this.api.get<any>(`franchises/${params}/squad`) : of(null)) });
  readonly valid = computed(() => !!this.counterparty() && this.offered().length + this.requested().length > 0);
  private readonly preview$ = new Subject<TradeTerms>();

  constructor() {
    this.preview$.pipe(debounceTime(400), switchMap((t) => this.api.post<any>('trades/impact', t)), takeUntilDestroyed()).subscribe({
      next: (i) => { this.impact.set(i); this.impactError.set(null); },
      error: (e) => this.impactError.set(e?.error?.error?.message ?? 'Could not calculate the impact.'),
    });
    effect(() => {
      const terms = this.terms();
      if (!terms) { this.impact.set(null); return; }
      this.preview$.next(terms);
      if (this.auth.has(P.valuationRead)) {
        const cash = terms.cashConsideration;
        this.api.post<any>('trades/value', { gives: terms.playersOffered, receives: terms.playersRequested, cashPaid: this.payer() === 'me' ? cash : 0, cashReceived: this.payer() === 'them' ? cash : 0 })
          .subscribe({ next: (v) => this.value.set(v), error: () => this.value.set(null) });
      }
    });
  }

  readonly terms = computed<TradeTerms | null>(() => {
    if (!this.valid()) return null;
    const me = this.auth.user()!.activeFranchiseId!;
    const cash = Math.round(this.cashLakh() * 1e5);
    return {
      counterpartyFranchiseId: this.counterparty()!, playersOffered: this.offered(), playersRequested: this.requested(), cashConsideration: cash,
      cashPayerFranchiseId: cash > 0 ? (this.payer() === 'me' ? me : this.counterparty()) : null, conditions: this.conditions() || null,
    };
  });

  save(submit: boolean): void {
    const terms = this.terms();
    if (!terms) return;
    const go = () => {
      this.busy.set(true);
      this.api.post<any>('trades', { terms, submit, message: this.message() || null }).subscribe({
        next: (t) => { this.toast.ok(submit ? `Offer ${t.reference} sent.` : 'Draft saved.'); void this.router.navigate(['/trading', t.id]); },
        error: (e) => { this.toast.error(e); this.busy.set(false); },
      });
    };
    if (!submit) return go();
    confirmAction(this.dialog, {
      title: 'Submit trade offer?',
      message: `The other franchise will be notified and can accept, counter or reject.${terms.cashConsideration ? `\nTrade consideration: ${formatInr(terms.cashConsideration)}.` : ''}`,
      confirmText: 'Submit offer',
    }).subscribe((ok) => ok && go());
  }
}
