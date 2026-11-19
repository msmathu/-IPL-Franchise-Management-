import { DatePipe } from '@angular/common';
import { PlayerName } from '../../shared/media';
import { Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { ChartConfiguration } from 'chart.js';
import { of } from 'rxjs';
import { Api, Paged } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Toast, WordsPipe, formatInr } from '../../core/services';
import { ChartView, PALETTE, PageHeader, StatCard, StateView, StatusChip, confirmAction } from '../../shared/ui';

@Component({
  selector: 'app-finance',
  imports: [PlayerName, PageHeader, StateView, StatCard, StatusChip, ChartView, InrPipe, WordsPipe, DatePipe, FormsModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatTabsModule, MatPaginatorModule],
  template: `
    <div class="page">
      <app-page-header title="Finance & contracts" subtitle="Separate ledgers for auction purse, player compensation, trade consideration and other expenses">
        @if (auth.has(P.reportsExport) && auth.has(P.financeRead)) { <button mat-stroked-button (click)="export('xlsx')"><mat-icon>download</mat-icon>Ledger (Excel)</button> }
      </app-page-header>
      <mat-tab-group>
        @if (auth.has(P.financeRead)) {
          <mat-tab label="Overview">
            <div class="stack tabpad">
              <app-state [loading]="overview.isLoading()" [error]="overview.error()" (retry)="overview.reload()">
                @if (overview.value(); as o) {
                  <div class="grid stats">
                    <app-stat label="Auction purse" [value]="o.purseRemaining | inr" [hint]="'used ' + (o.purseUsed | inr) + ' of ' + (o.purseTotal | inr)" />
                    <app-stat label="Contract commitments" [value]="o.contractCommitments | inr" />
                    <app-stat label="Paid to players" [value]="o.paidToPlayers | inr" [hint]="(o.scheduledPayments | inr) + ' scheduled'" />
                    <app-stat label="Operating budget" [value]="o.operatingBudget | inr" />
                  </div>
                  <div class="grid cols-2">
                    <section class="card"><h2>Ledgers</h2>
                      <table class="data"><tr><th>Category</th><th class="num">Debits</th><th class="num">Credits</th><th class="num">Net</th><th class="num">Budget</th><th class="num">Remaining</th></tr>
                        @for (l of o.ledgers; track l.category) {
                          <tr><td>{{ l.category | words }}</td><td class="num">{{ l.debits | inr }}</td><td class="num">{{ l.credits | inr }}</td><td class="num">{{ l.net | inr }}</td>
                            <td class="num">{{ l.budget | inr }}</td><td class="num" [class.bad-text]="l.budgetRemaining < 0">{{ l.budgetRemaining | inr }}</td></tr>
                        }</table></section>
                    <section class="card"><h2>Budgets vs spending</h2><app-chart [config]="budgetChart()" /></section>
                  </div>
                  <section class="card"><h2>Budget allocations</h2>
                    @for (b of o.budgets; track b.id) { <div class="list-item"><strong>{{ b.label }}</strong><span class="muted small">{{ b.category | words }}</span><span class="spacer"></span>{{ b.amount | inr }}</div> }
                    @if (auth.has(P.financeManage)) {
                      <div class="row">
                        <mat-form-field><mat-label>Category</mat-label><mat-select [(ngModel)]="bCategory"><mat-option value="OtherExpense">Other expenses</mat-option><mat-option value="PlayerCompensation">Player compensation</mat-option></mat-select></mat-form-field>
                        <mat-form-field><mat-label>Label</mat-label><input matInput [(ngModel)]="bLabel" /></mat-form-field>
                        <mat-form-field><mat-label>Amount (₹ lakh)</mat-label><input matInput type="number" [(ngModel)]="bLakh" /></mat-form-field>
                        <button mat-stroked-button [disabled]="!bLabel" (click)="saveBudget()">Save budget</button>
                      </div>
                    }
                  </section>
                }
              </app-state>
            </div>
          </mat-tab>
          <mat-tab label="Transactions">
            <div class="stack tabpad">
              @if (auth.has(P.financeManage)) {
                <section class="card row">
                  <mat-form-field class="wide"><mat-label>Expense description</mat-label><input matInput [(ngModel)]="eDesc" /></mat-form-field>
                  <mat-form-field><mat-label>Amount (₹ lakh)</mat-label><input matInput type="number" [(ngModel)]="eLakh" /></mat-form-field>
                  <mat-form-field><mat-label>Category</mat-label><mat-select [(ngModel)]="eCategory"><mat-option value="OtherExpense">Other expense</mat-option><mat-option value="PlayerCompensation">Player compensation</mat-option></mat-select></mat-form-field>
                  <mat-form-field><mat-label>Reference</mat-label><input matInput [(ngModel)]="eRef" /></mat-form-field>
                  <button mat-flat-button [disabled]="!eDesc || !eLakh" (click)="recordExpense()">Record expense</button>
                  <span class="muted small">Expenses of ₹50 L or more need finance approval rights.</span>
                </section>
              }
              <section class="card">
                <mat-form-field><mat-label>Category</mat-label><mat-select [ngModel]="category()" (ngModelChange)="category.set($event); page.set(1)"><mat-option [value]="null">All</mat-option>
                  @for (c of ['AuctionPurse', 'PlayerCompensation', 'TradeConsideration', 'OtherExpense']; track c) { <mat-option [value]="c">{{ c | words }}</mat-option> }</mat-select></mat-form-field>
                <app-state [loading]="tx.isLoading()" [error]="tx.error()" [empty]="tx.value()?.items?.length === 0" (retry)="tx.reload()">
                  <table class="data"><tr><th>Date</th><th>Category</th><th>Description</th><th class="num">Amount</th><th class="num">Purse after</th></tr>
                    @for (t of tx.value()?.items ?? []; track t.id) {
                      <tr><td>{{ t.createdAt | date: 'd MMM yyyy HH:mm' }}</td><td>{{ t.category | words }}</td><td>{{ t.description }}</td>
                        <td class="num" [class.bad-text]="t.direction === 'Debit'" [class.good-text]="t.direction === 'Credit'">{{ t.direction === 'Debit' ? '−' : '+' }}{{ t.amount | inr }}</td>
                        <td class="num">{{ t.purseBalanceAfter | inr }}</td></tr>
                    }</table>
                  <mat-paginator [length]="tx.value()?.total ?? 0" [pageSize]="25" [pageIndex]="page() - 1" (page)="paged($event)" />
                </app-state>
                <p class="muted small">Transactions are never edited; corrections are recorded as new entries. Every entry is in the audit log.</p>
              </section>
            </div>
          </mat-tab>
        }
        @if (auth.has(P.contractsRead)) {
          <mat-tab label="Player contracts">
            <section class="card tabpad">
              <app-state [loading]="contracts.isLoading()" [error]="contracts.error()" [empty]="!contracts.value()?.length" (retry)="contracts.reload()">
                <table class="data"><tr><th>Player</th><th>Role</th><th>Term</th><th class="num">Annual salary</th><th>Acquisition</th><th>Status</th><th>Payment schedule</th><th></th></tr>
                  @for (c of contracts.value() ?? []; track c.id) {
                    <tr><td><app-pname [id]="c.playerId" [name]="c.player" [size]="24" /></td><td>{{ c.role }}</td><td class="small">{{ c.startSeason }} → {{ c.endSeason }}<br />{{ c.startDate | date: 'd MMM yy' }} – {{ c.endDate | date: 'd MMM yy' }}</td>
                      <td class="num">{{ c.annualSalary | inr }}</td><td>{{ c.acquisition | words }}</td><td><app-chip [text]="c.status" /></td>
                      <td class="small">@for (p of c.payments; track p.id) {
                        <div>{{ p.dueDate | date: 'd MMM' }} · {{ p.amount | inr }} · <span [class.good-text]="p.status === 'Paid'">{{ p.status }}</span>
                          @if (p.status === 'Scheduled' && auth.has(P.financeManage)) { <button mat-button (click)="pay(p, c)">Mark paid</button> }</div> }</td>
                      <td>@if (c.status === 'Active' && auth.has(P.contractsManage) && auth.has(P.financeApprove)) { <button mat-button (click)="terminate(c)">Terminate</button> }</td></tr>
                  }</table>
              </app-state>
            </section>
          </mat-tab>
        }
      </mat-tab-group>
    </div>
  `,
  styles: `.tabpad { margin-top: 16px; }`,
})
export class FinancePage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly dialog = inject(MatDialog);
  readonly category = signal<string | null>(null);
  readonly page = signal(1);
  readonly overview = rxResource({ stream: () => (this.auth.has(P.financeRead) ? this.api.get<any>('finance/overview') : of(null)) });
  readonly tx = rxResource({
    params: () => ({ category: this.category(), page: this.page() }),
    stream: ({ params }) => (this.auth.has(P.financeRead) ? this.api.get<Paged<any>>('finance/transactions', { ...params, pageSize: 25 }) : of(null)),
  });
  readonly contracts = rxResource({ stream: () => (this.auth.has(P.contractsRead) ? this.api.get<any[]>('finance/contracts') : of([])) });
  readonly budgetChart = computed<ChartConfiguration>(() => {
    const l: any[] = (this.overview.value()?.ledgers ?? []).filter((x: any) => x.budget);
    return { type: 'bar', data: { labels: l.map((x) => x.category), datasets: [
      { label: 'Budget', data: l.map((x) => x.budget / 1e7), backgroundColor: PALETTE[3] },
      { label: 'Spent', data: l.map((x) => x.debits / 1e7), backgroundColor: PALETTE[1] },
    ] }, options: { scales: { y: { title: { display: true, text: '₹ Cr' } } } } };
  });
  bCategory = 'OtherExpense';
  bLabel = '';
  bLakh = 0;
  eDesc = '';
  eLakh = 0;
  eCategory = 'OtherExpense';
  eRef = '';

  paged(e: PageEvent): void {
    this.page.set(e.pageIndex + 1);
  }

  saveBudget(): void {
    this.api.put('finance/budgets', { category: this.bCategory, label: this.bLabel, amount: this.bLakh * 1e5 }).subscribe({
      next: () => { this.toast.ok('Budget saved.'); this.bLabel = ''; this.overview.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  recordExpense(): void {
    const amount = Math.round(this.eLakh * 1e5);
    confirmAction(this.dialog, { title: 'Record expense?', message: `${this.eDesc}: ${formatInr(amount)} (${this.eCategory}). Entries cannot be edited afterwards.`, confirmText: 'Record' })
      .subscribe((yes) => yes && this.api.post('finance/expenses', { description: this.eDesc, amount, category: this.eCategory, reference: this.eRef || null, idempotencyKey: crypto.randomUUID() }).subscribe({
        next: () => { this.toast.ok('Expense recorded.'); this.eDesc = ''; this.eLakh = 0; this.tx.reload(); this.overview.reload(); },
        error: (e) => this.toast.error(e),
      }));
  }

  pay(p: any, c: any): void {
    confirmAction(this.dialog, { title: 'Mark instalment as paid?', message: `${c.player}: ${formatInr(p.amount)}, booked to the player-compensation ledger.`, confirmText: 'Mark paid' })
      .subscribe((yes) => yes && this.api.post(`finance/payments/${p.id}/pay`).subscribe({
        next: () => { this.toast.ok('Payment recorded.'); this.contracts.reload(); this.overview.reload(); },
        error: (e) => this.toast.error(e),
      }));
  }

  terminate(c: any): void {
    const reason = prompt(`Reason for terminating ${c.player}'s contract:`);
    if (!reason) return;
    confirmAction(this.dialog, { title: 'Terminate contract?', message: 'Scheduled payments are cancelled. This is audited and cannot be undone.', danger: true, confirmText: 'Terminate' })
      .subscribe((yes) => yes && this.api.post(`finance/contracts/${c.id}/terminate`, { reason }).subscribe({ next: () => this.contracts.reload(), error: (e) => this.toast.error(e) }));
  }

  export(format: string): void {
    this.api.download('reports/finance-ledger/export', { format }).subscribe({ error: (e) => this.toast.error(e) });
  }
}
