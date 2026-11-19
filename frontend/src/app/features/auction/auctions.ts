import { DatePipe } from '@angular/common';
import { PlayerName, TeamName } from '../../shared/media';
import { Component, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { ChartConfiguration } from 'chart.js';
import { Api, Paged } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Toast, WordsPipe } from '../../core/services';
import { ChartView, PALETTE, PageHeader, StatCard, StateView, StatusChip } from '../../shared/ui';

@Component({
  selector: 'app-auctions',
  imports: [PageHeader, StateView, StatusChip, RouterLink, InrPipe, DatePipe, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatCheckboxModule],
  template: `
    <div class="page">
      <app-page-header title="Auction centre" subtitle="Configure auctions, run live bidding and review results">
        @if (auth.has(P.auctionsManage)) { <button mat-flat-button (click)="creating.set(!creating())"><mat-icon>add</mat-icon>New auction</button> }
      </app-page-header>
      @if (creating()) {
        <section class="card row">
          <mat-form-field class="wide"><mat-label>Name</mat-label><input matInput [(ngModel)]="name" /></mat-form-field>
          <mat-form-field><mat-label>Scheduled</mat-label><input matInput type="datetime-local" [(ngModel)]="scheduled" /></mat-form-field>
          <mat-form-field><mat-label>Bid timer (s)</mat-label><input matInput type="number" [(ngModel)]="timer" /></mat-form-field>
          <mat-checkbox [(ngModel)]="mega">Mega auction</mat-checkbox>
          <mat-checkbox [(ngModel)]="reopen">Allow unsold players to be reopened</mat-checkbox>
          <button mat-flat-button [disabled]="!name" (click)="create()">Create</button>
        </section>
      }
      <app-state [loading]="res.isLoading()" [error]="res.error()" [empty]="!res.value()?.length" emptyText="No auctions yet." (retry)="res.reload()">
        <div class="grid cols-2">
          @for (a of res.value() ?? []; track a.id) {
            <section class="card">
              <div class="row"><h2>{{ a.name }}</h2><span class="spacer"></span><app-chip [text]="a.status" /></div>
              <p class="muted">{{ a.season }} · {{ a.isMegaAuction ? 'Mega' : 'Mini' }} auction @if (a.scheduledAt) { · {{ a.scheduledAt | date: 'd MMM yyyy HH:mm' }} }</p>
              <p>{{ a.totalLots }} players · {{ a.sold }} sold · {{ a.unsold }} unsold · {{ a.pending }} remaining · spent {{ a.totalSpent | inr }}</p>
              <div class="row">
                <a mat-flat-button [routerLink]="['/auction', a.id, 'live']"><mat-icon>gavel</mat-icon>{{ a.status === 'Live' ? 'Join live' : 'Live screen' }}</a>
                <a mat-stroked-button [routerLink]="['/auction', a.id]">Player pool & setup</a>
                <a mat-button [routerLink]="['/auction', a.id, 'results']">Results</a>
              </div>
            </section>
          }
        </div>
      </app-state>
    </div>
  `,
})
export class AuctionsPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly router = inject(Router);
  readonly res = rxResource({ stream: () => this.api.get<any[]>('auctions') });
  readonly creating = signal(false);
  name = '';
  scheduled = '';
  timer = 30;
  mega = false;
  reopen = true;

  create(): void {
    this.api.post<{ id: string }>('auctions', { name: this.name, isMegaAuction: this.mega, scheduledAt: this.scheduled ? new Date(this.scheduled).toISOString() : null, bidTimerSeconds: this.timer, allowReopenUnsold: this.reopen })
      .subscribe({ next: (r) => void this.router.navigate(['/auction', r.id]), error: (e) => this.toast.error(e) });
  }
}

@Component({
  selector: 'app-auction-setup',
  imports: [PlayerName, PageHeader, StateView, StatusChip, RouterLink, InrPipe, WordsPipe, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatCheckboxModule],
  template: `
    <div class="page">
      <app-page-header title="Player pool & configuration">
        <a mat-flat-button [routerLink]="['/auction', id(), 'live']"><mat-icon>gavel</mat-icon>Live screen</a>
      </app-page-header>
      @if (auth.has(P.auctionsManage)) {
        <section class="card official"><h2><mat-icon class="role-ico">upload_file</mat-icon>Import the official auction list</h2>
          <p class="small">Upload the league's published list (CSV, or Excel .xlsx). Every row becomes a lot with its set and base price; players not in the database
            yet are created from the list. Columns are recognised by name, including the IPL format: <em>Set No., 2027 Set, First Name, Surname, Country, DOB,
            Specialism, Batting, Bowling Style, C/U/A, Reserve Price Rs Lakh</em>.</p>
          <div class="row">
            <mat-form-field class="wide"><mat-label>Source (e.g. "IPL 2027 auction list, iplt20.com, 5 Dec 2026")</mat-label><input matInput [(ngModel)]="listSource" /></mat-form-field>
            <mat-checkbox [(ngModel)]="replaceProvisional">Remove provisional lots not on the list</mat-checkbox>
            <button mat-flat-button [disabled]="!listSource" (click)="listFile.click()"><mat-icon>upload</mat-icon>Choose file and import</button>
            <input #listFile type="file" accept=".csv,.xlsx" hidden (change)="importList(listFile)" />
          </div>
          @if (importResult(); as r) {
            <p class="small"><strong>Imported {{ r.rows }} rows:</strong> {{ r.matched }} matched, {{ r.playersCreated }} new players, {{ r.lotsAdded }} lots added,
              {{ r.lotsUpdated }} updated, {{ r.provisionalRemoved }} provisional removed, {{ r.skipped }} skipped.</p>
            @for (e of r.errors; track e) { <div class="small warn-text">{{ e }}</div> }
          }
        </section>
        <section class="card"><h2>Add players to the pool</h2>
          <div class="row">
            <mat-form-field><mat-label>Set name</mat-label><input matInput [(ngModel)]="setName" /></mat-form-field>
            <mat-form-field><mat-label>Set number</mat-label><input matInput type="number" [(ngModel)]="setNumber" /></mat-form-field>
            <mat-form-field><mat-label>Base price (₹ lakh)</mat-label><input matInput type="number" [(ngModel)]="baseLakh" /></mat-form-field>
            <button mat-stroked-button [disabled]="!chosen().length || !setName" (click)="addChosen()">Add {{ chosen().length }} selected</button>
            <button mat-stroked-button (click)="importEligible()">Add every eligible unsigned player</button>
          </div>
          <div class="eligible">
            @for (p of eligible.value()?.items ?? []; track p.id) {
              <label class="list-item"><mat-checkbox [checked]="chosen().includes(p.id)" (change)="toggle(p.id)" /><app-pname [id]="p.id" [name]="p.fullName" [size]="24" [link]="false" /><span class="muted small">{{ p.role }} · {{ p.country }} · {{ p.isCapped ? 'Capped' : 'Uncapped' }}</span></label>
            } @empty { <p class="muted">No unsigned players available for auction.</p> }
          </div>
          <p class="muted small">Only players marked "available for auction" and not signed by a franchise can be added. Base prices must respect the season's minimum.</p>
        </section>
      }
      <section class="card">
        <h2>Pool ({{ lots.value()?.length ?? 0 }})</h2>
        <app-state [loading]="lots.isLoading()" [error]="lots.error()" [empty]="!lots.value()?.length" emptyText="The pool is empty." (retry)="lots.reload()">
          <div class="table-wrap"><table class="data">
            <tr><th>Set</th><th class="num">Lot</th><th>Player</th><th>Role</th><th>Country</th><th class="num">Base</th><th>Status</th><th class="num">Bid / price</th><th>Round</th><th></th></tr>
            @for (l of lots.value() ?? []; track l.id) {
              <tr><td>{{ l.setNumber }}. {{ l.setName }}@if (l.setName.startsWith('Provisional')) { <span class="tag os">provisional</span> }</td><td class="num">{{ l.lotNumber }}</td><td><app-pname [id]="l.playerId" [name]="l.playerName" [size]="24" />@if (l.isOverseas) { <span class="tag os">OS</span> }</td>
                <td>{{ l.role }}</td><td>{{ l.country }}</td><td class="num">{{ l.basePrice | inr }}</td><td><app-chip [text]="l.status | words" /></td>
                <td class="num">{{ l.currentBid | inr }} {{ l.currentBidFranchise ?? '' }}</td><td>{{ l.round }}</td>
                <td>@if (auth.has(P.auctionsManage) && l.status === 'Pending') { <button mat-icon-button (click)="removeLot(l.id)" aria-label="Remove"><mat-icon>delete</mat-icon></button> }</td></tr>
            }
          </table></div>
        </app-state>
      </section>
    </div>
  `,
  styles: `.eligible { max-height: 280px; overflow: auto; } label { cursor: pointer; }`,
})
export class AuctionSetupPage {
  readonly id = input.required<string>();
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly lots = rxResource({ params: () => this.id(), stream: ({ params }) => this.api.get<any[]>(`auctions/${params}/lots`) });
  readonly eligible = rxResource({ stream: () => this.api.get<Paged<any>>('players', { status: 'AvailableForAuction', freeAgentsOnly: true, pageSize: 200 }) });
  readonly chosen = signal<string[]>([]);
  setName = 'Set A';
  setNumber = 1;
  baseLakh = 20;

  toggle(id: string): void {
    this.chosen.update((c) => (c.includes(id) ? c.filter((x) => x !== id) : [...c, id]));
  }

  addChosen(): void {
    this.api.post<any>(`auctions/${this.id()}/lots`, { playerIds: this.chosen(), setName: this.setName, setNumber: this.setNumber, basePrice: this.baseLakh * 1e5 }).subscribe({
      next: (r) => { this.toast.ok(`${r.added} players added.`); this.chosen.set([]); this.lots.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  listSource = '';
  replaceProvisional = true;
  readonly importResult = signal<any | null>(null);

  importList(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.api.upload<any>(`auctions/${this.id()}/import-list`, file, { source: this.listSource, replaceProvisional: String(this.replaceProvisional) }).subscribe({
      next: (r) => { this.importResult.set(r); this.toast.ok(`Auction list imported: ${r.lotsAdded} added, ${r.lotsUpdated} updated.`); this.lots.reload(); this.eligible.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  importEligible(): void {
    this.api.post<any>(`auctions/${this.id()}/lots/import-eligible`, { basePrice: this.baseLakh * 1e5 }).subscribe({
      next: (r) => { this.toast.ok(`${r.added} players added.`); this.lots.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  removeLot(lotId: string): void {
    this.api.delete(`auctions/${this.id()}/lots/${lotId}`).subscribe({ next: () => this.lots.reload(), error: (e) => this.toast.error(e) });
  }
}

@Component({
  selector: 'app-auction-results',
  imports: [PlayerName, TeamName, PageHeader, StateView, StatCard, ChartView, InrPipe, MatButtonModule, MatIconModule],
  template: `
    <div class="page">
      <app-state [loading]="res.isLoading()" [error]="res.error()" (retry)="res.reload()">
        @if (res.value(); as r) {
          <app-page-header [title]="r.name + ' — results'" [subtitle]="r.status">
            @if (auth.has(P.reportsExport)) {
              <button mat-stroked-button (click)="export('pdf')">PDF</button><button mat-stroked-button (click)="export('xlsx')">Excel</button><button mat-stroked-button (click)="export('csv')">CSV</button>
            }
          </app-page-header>
          <div class="grid stats">
            <app-stat label="Sold" [value]="r.sold" /><app-stat label="Unsold" [value]="r.unsold" /><app-stat label="Total spent" [value]="r.totalSpent | inr" />
            <app-stat label="Average price" [value]="r.averagePrice | inr" /><app-stat label="Highest purchase" [value]="r.highestPurchase?.price | inr" [hint]="r.highestPurchase?.player ?? ''" />
          </div>
          <div class="grid cols-2">
            <section class="card"><h2>Spending by franchise</h2><app-chart [config]="chart()" /></section>
            <section class="card"><h2>Franchise summary</h2>
              <table class="data"><tr><th>Franchise</th><th class="num">Players</th><th class="num">Spent</th><th class="num">Average</th><th class="num">Purse left</th></tr>
                @for (f of r.byFranchise; track f.franchiseId) { <tr><td><app-team [key]="f.franchiseId" [label]="f.franchise" /></td><td class="num">{{ f.players }}</td><td class="num">{{ f.spent | inr }}</td><td class="num">{{ f.averagePrice | inr }}</td><td class="num">{{ f.purseRemaining | inr }}</td></tr> }</table></section>
          </div>
          <div class="grid cols-2">
            <section class="card"><h2>Sold players</h2>
              @for (s of r.soldPlayers; track s.playerId) { <div class="list-item"><app-pname [id]="s.playerId" [name]="s.player" [size]="24" /><span class="muted small">{{ s.role }}</span><span class="spacer"></span><span>{{ s.franchise }}</span><strong>{{ s.price | inr }}</strong></div> }
              @empty { <p class="muted">None yet.</p> }</section>
            <section class="card"><h2>Unsold players</h2>
              @for (s of r.unsoldPlayers; track s.playerId) { <div class="list-item"><app-pname [id]="s.playerId" [name]="s.player" [size]="24" /><span class="muted small">{{ s.role }}</span><span class="spacer"></span>base {{ s.basePrice | inr }}</div> }
              @empty { <p class="muted">None.</p> }</section>
          </div>
        }
      </app-state>
    </div>
  `,
})
export class AuctionResultsPage {
  readonly id = input.required<string>();
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly res = rxResource({ params: () => this.id(), stream: ({ params }) => this.api.get<any>(`auctions/${params}/results`) });
  readonly chart = computed<ChartConfiguration>(() => {
    const rows: any[] = this.res.value()?.byFranchise ?? [];
    return { type: 'bar', data: { labels: rows.map((r) => r.franchise), datasets: [{ label: '₹ Cr', data: rows.map((r) => r.spent / 1e7), backgroundColor: PALETTE[0] }] }, options: { plugins: { legend: { display: false } } } };
  });

  export(format: string): void {
    this.api.download('reports/auction-results/export', { format, id: this.id() }).subscribe({ error: (e) => this.toast.error(e) });
  }
}

