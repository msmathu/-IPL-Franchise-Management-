import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Api, Paged } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Realtime, WordsPipe } from '../../core/services';
import { Monogram, PageHeader, StateView, StatusChip } from '../../shared/ui';

@Component({
  selector: 'app-trading',
  imports: [PageHeader, StateView, StatusChip, Monogram, RouterLink, InrPipe, WordsPipe, DatePipe, MatButtonModule, MatIconModule, MatButtonToggleModule, MatPaginatorModule],
  template: `
    <div class="page">
      <app-page-header title="Trading centre" subtitle="Cash trades, player swaps and multi-player exchanges">
        @if (auth.has(P.tradesCreate) && auth.activeFranchise()) { <a mat-flat-button routerLink="/trading/new"><mat-icon>add</mat-icon>New trade offer</a> }
      </app-page-header>
      <mat-button-toggle-group [value]="status()" (change)="status.set($event.value); page.set(1)">
        <mat-button-toggle value="open">Open</mat-button-toggle>
        <mat-button-toggle value="closed">Closed</mat-button-toggle>
        <mat-button-toggle value="">All</mat-button-toggle>
      </mat-button-toggle-group>
      <section class="card">
        <app-state [loading]="res.isLoading()" [error]="res.error()" [empty]="res.value()?.items?.length === 0" emptyText="No trades here." (retry)="res.reload()">
          <div class="table-wrap"><table class="data">
            <tr><th>Reference</th><th>Initiator</th><th>Counterparty</th><th>Type</th><th class="num">Players</th><th class="num">Consideration</th><th>Status</th><th>Expires</th><th>Updated</th></tr>
            @for (t of res.value()?.items ?? []; track t.id) {
              <tr class="clickable" (click)="router.navigate(['/trading', t.id])">
                <td><strong>{{ t.reference }}</strong>@if (t.awaitingMyResponse) { <span class="tag os">Your move</span> }</td>
                <td><span class="row"><app-monogram [name]="t.initiator.name" [color]="t.initiator.primaryColor" [size]="20" />{{ t.initiator.shortName }}</span></td>
                <td><span class="row"><app-monogram [name]="t.counterparty.name" [color]="t.counterparty.primaryColor" [size]="20" />{{ t.counterparty.shortName }}</span></td>
                <td>{{ t.type | words }}</td><td class="num">{{ t.playerCount }}</td><td class="num">{{ t.cashConsideration ? (t.cashConsideration | inr) : '—' }}</td>
                <td><app-chip [text]="t.status | words" /></td><td class="small">{{ t.expiresAt ? (t.expiresAt | date: 'd MMM HH:mm') : '' }}</td><td class="small muted">{{ t.updatedAt | date: 'd MMM HH:mm' }}</td>
              </tr>
            }
          </table></div>
          <mat-paginator [length]="res.value()?.total ?? 0" [pageSize]="25" [pageIndex]="page() - 1" (page)="paged($event)" />
        </app-state>
      </section>
      <p class="disclaimer">A trade becomes an official transfer only after franchise authorisation, player consent and league approval are all recorded under the configured season rules.</p>
    </div>
  `,
})
export class TradingPage {
  readonly auth = inject(Auth);
  readonly P = P;
  readonly router = inject(Router);
  private readonly api = inject(Api);
  readonly status = signal('open');
  readonly page = signal(1);
  readonly res = rxResource({
    params: () => ({ status: this.status(), page: this.page() }),
    stream: ({ params }) => this.api.get<Paged<any>>('trades', { ...params, pageSize: 25 }),
  });

  constructor() {
    inject(Realtime).events.pipe(filter((e) => e.name === 'trade.updated'), takeUntilDestroyed()).subscribe(() => this.res.reload());
  }

  paged(e: PageEvent): void {
    this.page.set(e.pageIndex + 1);
  }
}
