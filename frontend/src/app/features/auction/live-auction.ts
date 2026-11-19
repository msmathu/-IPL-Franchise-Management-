import { DatePipe, DecimalPipe } from '@angular/common';
import { PlayerName, TeamName } from '../../shared/media';
import { Component, OnDestroy, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { rxResource } from '../../core/resource';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { filter, interval, of } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Realtime, Toast, formatInr } from '../../core/services';
import { Monogram, PageHeader, PlayerAvatar, StateView, StatusChip, confirmAction } from '../../shared/ui';

@Component({
  selector: 'app-live-auction',
  imports: [PlayerName, TeamName, PageHeader, StateView, StatusChip, Monogram, PlayerAvatar, RouterLink, InrPipe, DatePipe, DecimalPipe, MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <div class="page">
      <app-state [loading]="res.isLoading() && !live()" [error]="res.error()" (retry)="res.reload()">
        @if (live(); as a) {
          <div class="row">
            <app-page-header [title]="a.name" [subtitle]="'Round ' + a.round + ' · ' + a.remaining + ' remaining · ' + a.sold + ' sold · ' + a.unsold + ' unsold'" />
            <span class="spacer"></span><app-chip [text]="a.status" />
            @if (auth.has(P.auctionsManage)) {
              @if (a.status === 'Draft' || a.status === 'Ready') { <button mat-flat-button (click)="status('start', 'Start the auction? All franchises will be notified.')">Start auction</button> }
              @if (a.status === 'Live') { <button mat-stroked-button (click)="status('pause')">Pause</button> }
              @if (a.status === 'Paused') { <button mat-flat-button (click)="status('resume')">Resume</button> }
              @if ((a.status === 'Live' || a.status === 'Paused') && !a.currentLot) {
                @if (a.allowReopenUnsold && a.unsold > 0) { <button mat-stroked-button (click)="reopen()">Reopen unsold</button> }
                <button mat-stroked-button (click)="status('complete', 'Complete the auction? No more players can be sold.')">Complete</button>
              }
            }
            <a mat-button [routerLink]="['/auction', a.id, 'results']">Results</a>
          </div>
          <div class="grid live">
            <section class="card block">
              @if (a.currentLot; as lot) {
                <div class="lot-head">
                  <app-avatar [name]="lot.playerName" [photoUrl]="lot.photoUrl" color="#1e40af" [size]="84" />
                  <div>
                    <h2 class="name">{{ lot.playerName }}</h2>
                    <p class="muted">{{ lot.role }} · {{ lot.country }}{{ lot.age !== null ? ' · age ' + lot.age : '' }} · {{ lot.isCapped ? 'Capped' : 'Uncapped' }}@if (lot.isOverseas) { <span class="tag os">Overseas</span> }</p>
                    <p class="muted small">Set {{ lot.setNumber }} — {{ lot.setName }} · lot {{ lot.lotNumber }}</p>
                  </div>
                </div>
                <div class="prices">
                  <div><span class="muted small">Base price</span><strong>{{ lot.basePrice | inr }}</strong></div>
                  <div class="current"><span class="muted small">Current bid</span><strong>{{ lot.currentBid ? (lot.currentBid | inr) : 'No bids' }}</strong>
                    @if (lot.currentBidFranchise) { <span class="leader">{{ lot.currentBidFranchise }}</span> }</div>
                  <div><span class="muted small">Next bid</span><strong>{{ a.nextBid | inr }}</strong></div>
                </div>
                @if (a.bidTimerSeconds > 0 && lot.lastBidAt && a.status === 'Live') {
                  <mat-progress-bar mode="determinate" [value]="timerPct()" [color]="secondsLeft() <= 5 ? 'warn' : 'primary'" />
                  <p class="small muted">{{ secondsLeft() > 0 ? secondsLeft() + 's since the last bid until the auctioneer may close' : 'Timer elapsed — auctioneer may close the lot' }}</p>
                }
                <div class="row actions">
                  @if (a.me; as me) {
                    <button mat-flat-button class="bid" [disabled]="!me.canBid || busy()" (click)="bid(a)"><mat-icon>pan_tool</mat-icon>Bid {{ me.nextBid | inr }}</button>
                    @if (!me.canBid && me.cannotBidReason) { <span class="small warn-text">{{ me.cannotBidReason }}</span> }
                  }
                  <span class="spacer"></span>
                  @if (auth.has(P.auctionsManage)) {
                    <button mat-flat-button [disabled]="!lot.currentBid" (click)="close(true, lot)"><mat-icon>gavel</mat-icon>Sold</button>
                    <button mat-stroked-button (click)="close(false, lot)">Unsold</button>
                  }
                </div>
              } @else {
                <div class="state"><mat-icon>hourglass_empty</mat-icon><span>No player on the block.</span>
                  @if (auth.has(P.auctionsManage) && a.status === 'Live') { <button mat-flat-button (click)="openNext()">Bring next player</button> }</div>
              }
              @if (lastEvent()) { <p class="event">{{ lastEvent() }}</p> }
            </section>
            @if (advice.value(); as ad) {
              <section class="card assistant" [attr.data-verdict]="ad.verdict">
                <h2><mat-icon>psychology</mat-icon>AI auction assistant <span class="small muted">· {{ ad.playerName }}</span></h2>
                <div class="verdict"><span class="vtag">{{ verdictLabel(ad.verdict) }}</span><strong>{{ ad.headline }}</strong></div>
                <div class="nums">
                  <div><b>Your stop price</b><span>{{ ad.stopAt | inr }}</span></div>
                  <div><b>Model value</b><span>{{ ad.modelLow | inr }} – {{ ad.modelHigh | inr }}</span></div>
                  <div><b>Rating</b><span>{{ ad.rating | number: '1.0-0' }}/100 · {{ ad.dataConfidence }} data</span></div>
                  <div><b>Squad fit</b><span>{{ ad.fitScore >= 4 ? 'Priority' : ad.fitScore >= 2 ? 'Good' : 'Depth only' }}</span></div>
                  <div><b>Rivals able to bid</b><span>{{ ad.rivalsAbleToBid }}</span></div>
                  <div><b>Your max bid allowed</b><span>{{ ad.maxBidAllowed | inr }}</span></div>
                </div>
                @for (r of ad.reasons; track r) { <div class="why"><mat-icon>check_circle</mat-icon>{{ r }}</div> }
                @for (c of ad.cautions; track c) { <div class="why warn"><mat-icon>warning</mat-icon>{{ c }}</div> }
                @if (ad.alternatives.length) {
                  <p class="small muted">Similar players still to come: @for (x of ad.alternatives; track x.playerId) { <app-pname [id]="x.playerId" [name]="x.name" [size]="24" /> ({{ x.basePrice | inr }} base){{ $last ? '' : ', ' }} }</p>
                }
                <p class="small muted">{{ ad.disclaimer }}</p>
              </section>
            }
            <section class="card"><h2>Bidding history</h2>
              @for (b of a.bids; track b.id) { <div class="list-item"><app-team [key]="b.franchise" [size]="22" /><span class="spacer"></span>{{ b.amount | inr }}<span class="small muted">{{ b.placedAt | date: 'HH:mm:ss' }}</span></div> }
              @empty { <p class="muted">No bids yet.</p> }
            </section>
          </div>
          <section class="card"><h2>Franchise purses</h2>
            <div class="table-wrap"><table class="data">
              <tr><th>Franchise</th><th class="num">Purse left</th><th class="num">Max bid allowed</th><th class="num">Squad</th><th class="num">Slots left</th><th class="num">Overseas slots</th><th class="num">Bought</th><th class="num">Spent here</th></tr>
              @for (f of a.franchises; track f.franchiseId) {
                <tr [class.me]="f.franchiseId === auth.user()?.activeFranchiseId" [class.leading]="f.franchiseId === a.currentLot?.currentBidFranchiseId">
                  <td><span class="row"><app-monogram [name]="f.name" [code]="f.shortName" [color]="f.primaryColor" [size]="22" />{{ f.name }}</span></td>
                  <td class="num">{{ f.purseRemaining | inr }}</td><td class="num">{{ f.maxBidAllowed | inr }}</td><td class="num">{{ f.squadSize }}</td>
                  <td class="num">{{ f.slotsRemaining }}</td><td class="num">{{ f.overseasSlotsRemaining }}</td><td class="num">{{ f.bought }}</td><td class="num">{{ f.spent | inr }}</td></tr>
              }
            </table></div>
            <p class="muted small">Max bid keeps enough purse to complete the minimum squad at the minimum base price (season rule). Every bid is validated on the server.</p>
          </section>
        }
      </app-state>
    </div>
  `,
  styles: `
    .assistant { border-left: 4px solid var(--team-accent); }
    .assistant h2 { display: flex; align-items: center; gap: 8px; }
    .assistant .verdict { display: flex; gap: 10px; align-items: center; margin-bottom: 10px; font-size: 15px; }
    .assistant .vtag { padding: 3px 10px; border-radius: 6px; font-weight: 900; font-size: 12px; letter-spacing: 0.06em; background: #475569; color: #fff; }
    .assistant[data-verdict='Bid'] .vtag, .assistant[data-verdict='Leading'] .vtag { background: #16a34a; }
    .assistant[data-verdict='Consider'] .vtag { background: #d97706; }
    .assistant[data-verdict='Pass'] .vtag, .assistant[data-verdict='CannotBid'] .vtag { background: #dc2626; }
    .assistant .nums { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 10px; margin-bottom: 10px; }
    .assistant .nums b { display: block; font-size: 11px; color: var(--muted); text-transform: uppercase; letter-spacing: 0.06em; }
    .assistant .nums span { font-weight: 800; }
    .assistant .why { display: flex; gap: 6px; align-items: center; font-size: 13px; margin: 3px 0; }
    .assistant .why mat-icon { font-size: 17px; width: 17px; height: 17px; color: var(--good); }
    .assistant .why.warn mat-icon { color: var(--warn); }
    .live { grid-template-columns: 2fr 1fr; }
    @media (max-width: 1000px) { .live { grid-template-columns: 1fr; } }
    .block { display: flex; flex-direction: column; gap: 16px; border: 1px solid rgba(56, 189, 248, 0.35); }
    .lot-head { display: flex; gap: 16px; align-items: center; }
    .avatar { width: 84px; height: 84px; border-radius: 18px; background: linear-gradient(135deg, #1e40af, #0ea5e9); display: grid; place-items: center; font-size: 30px; font-weight: 700; }
    .name { font-size: 26px; margin: 0; }
    .prices { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }
    .prices div { background: var(--bg-2); border-radius: 12px; padding: 12px; display: flex; flex-direction: column; gap: 4px; }
    .prices strong { font-size: 22px; }
    .current { border: 1px solid var(--gold); }
    .current strong { color: var(--gold); font-size: 28px !important; }
    .leader { font-weight: 600; }
    .bid { --mat-button-filled-container-color: #16a34a; font-size: 16px; padding: 22px 26px; }
    tr.me td { background: rgba(56, 189, 248, 0.08); }
    tr.leading td { color: var(--gold); }
    .event { color: var(--accent); font-weight: 600; }
  `,
})
export class LiveAuctionPage implements OnDestroy {
  readonly id = input.required<string>();
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly dialog = inject(MatDialog);
  private readonly realtime = inject(Realtime);
  readonly res = rxResource({ params: () => this.id(), stream: ({ params }) => this.api.get<any>(`auctions/${params}/live`) });
  readonly live = computed(() => this.res.value());
  /** Bid advice for the player on the block (franchise users with valuation access); refreshed on every bid. */
  readonly advice = rxResource({
    params: () => {
      const lot = this.live()?.currentLot;
      return lot && this.auth.activeFranchise() && this.auth.has(P.valuationRead) ? { lot: lot.id, bid: lot.currentBid, leader: lot.currentBidFranchiseId } : null;
    },
    stream: ({ params }) => (params ? this.api.get<any>(`auctions/${this.id()}/assistant`) : of(null)),
  });
  verdictLabel(v: string): string {
    return ({ Bid: 'BID', Consider: 'MAYBE', Pass: 'PASS', Leading: 'LEADING', CannotBid: 'CAN\'T BID' } as Record<string, string>)[v] ?? v;
  }
  readonly busy = signal(false);
  readonly lastEvent = signal<string | null>(null);
  private readonly now = signal(Date.now());
  private offset = 0; // server time − browser time
  readonly secondsLeft = computed(() => {
    const a = this.live();
    const last = a?.currentLot?.lastBidAt;
    if (!a || !last) return a?.bidTimerSeconds ?? 0;
    return Math.max(0, Math.ceil(a.bidTimerSeconds - (this.now() + this.offset - new Date(last).getTime()) / 1000));
  });
  readonly timerPct = computed(() => (this.live()?.bidTimerSeconds ? (this.secondsLeft() / this.live().bidTimerSeconds) * 100 : 0));
  private joined?: string;

  constructor() {
    interval(500).pipe(takeUntilDestroyed()).subscribe(() => this.now.set(Date.now()));
    this.realtime.events.pipe(filter((e) => e.name === 'auction' && e.payload?.auctionId === this.id()), takeUntilDestroyed()).subscribe((e) => {
      const p = e.payload;
      if (p.kind === 'sold') this.lastEvent.set(`SOLD: ${p.payload.player} for ${formatInr(p.payload.price)}`);
      if (p.kind === 'unsold') this.lastEvent.set(`Unsold: ${p.payload.player}`);
      if (p.kind === 'lotOpened') this.lastEvent.set(null);
      this.res.reload();
    });
  }

  ngOnInit(): void {
    this.joined = this.id();
    this.realtime.joinAuction(this.joined);
  }

  ngOnDestroy(): void {
    if (this.joined) this.realtime.leaveAuction(this.joined);
  }

  initials(name: string): string {
    return name.split(' ').map((w) => w[0]).slice(0, 2).join('');
  }

  private apply(obs: ReturnType<Api['post']>, ok?: string): void {
    this.busy.set(true);
    obs.subscribe({
      next: (a: any) => {
        if (a?.serverTime) this.offset = new Date(a.serverTime).getTime() - Date.now();
        if (a?.id) this.res.set(a);
        if (ok) this.toast.ok(ok);
        this.busy.set(false);
      },
      error: (e) => { this.toast.error(e); this.busy.set(false); this.res.reload(); },
    });
  }

  bid(a: any): void {
    this.apply(this.api.post(`auctions/${this.id()}/bids`, { lotId: a.currentLot.id, amount: a.me.nextBid, idempotencyKey: crypto.randomUUID().replace(/-/g, '') }));
  }

  openNext(): void {
    this.apply(this.api.post(`auctions/${this.id()}/open-lot`, { lotId: null }));
  }

  close(sold: boolean, lot: any): void {
    confirmAction(this.dialog, {
      title: sold ? `Sell ${lot.playerName}?` : `Mark ${lot.playerName} unsold?`,
      message: sold ? `Sold to ${lot.currentBidFranchise} for ${formatInr(lot.currentBid)}. The purse is debited and the player joins their squad.` : 'The player can be reopened in a later round if allowed.',
      confirmText: sold ? 'Sold' : 'Unsold',
    }).subscribe((yes) => yes && this.apply(this.api.post(`auctions/${this.id()}/close-lot`, { sold })));
  }

  status(op: string, question?: string): void {
    const go = () => this.apply(this.api.post(`auctions/${this.id()}/${op}`));
    if (!question) return go();
    confirmAction(this.dialog, { title: question, message: 'This is recorded in the audit log.' }).subscribe((yes) => yes && go());
  }

  reopen(): void {
    this.api.post<any>(`auctions/${this.id()}/reopen-unsold`, { lotIds: null }).subscribe({
      next: (r) => { this.toast.ok(`${r.reopened} players back in the pool for the next round.`); this.res.reload(); },
      error: (e) => this.toast.error(e),
    });
  }
}
