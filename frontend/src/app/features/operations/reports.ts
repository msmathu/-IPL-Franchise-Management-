import { Component, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Toast } from '../../core/services';
import { PageHeader, StateView } from '../../shared/ui';
import { PlayerPicker } from '../players/player-profile';

interface ReportDef {
  type: string;
  title: string;
  description: string;
  perms: string[];
  needsPlayer?: boolean;
}

@Component({
  selector: 'app-reports',
  imports: [PageHeader, StateView, PlayerPicker, RouterLink, MatButtonModule, MatIconModule],
  template: `
    <div class="page">
      <app-page-header title="Reports & analytics" subtitle="Preview and export as PDF, Excel or CSV">
        <a mat-stroked-button routerLink="/dashboard"><mat-icon>dashboard</mat-icon>Role dashboards</a>
      </app-page-header>
      <div class="grid cols-3">
        @for (r of reports; track r.type) {
          @if (canSee(r)) {
            <section class="card">
              <h2>{{ r.title }}</h2><p class="muted small">{{ r.description }}</p>
              @if (r.needsPlayer) { <app-player-picker label="Player" (picked)="player.set($event)" />@if (player()) { <p class="small">Selected: {{ player().fullName }}</p> } }
              <div class="row">
                <button mat-button (click)="preview(r)">Preview</button>
                @if (auth.has(P.reportsExport)) {
                  <button mat-stroked-button (click)="export(r, 'pdf')">PDF</button><button mat-stroked-button (click)="export(r, 'xlsx')">Excel</button><button mat-stroked-button (click)="export(r, 'csv')">CSV</button>
                }
              </div>
            </section>
          }
        }
      </div>
      @if (previewType()) {
        <section class="card table-wrap">
          <app-state [loading]="table.isLoading()" [error]="table.error()" (retry)="table.reload()">
            @if (table.value(); as t) {
              <h2>{{ t.title }}</h2>@if (t.subtitle) { <p class="muted">{{ t.subtitle }}</p> }
              <table class="data"><tr>@for (c of t.columns; track c) { <th>{{ c }}</th> }</tr>
                @for (row of t.rows.slice(0, 100); track $index) { <tr>@for (v of row; track $index) { <td>{{ fmt(v) }}</td> }</tr> }</table>
              @if (t.rows.length > 100) { <p class="muted small">Showing 100 of {{ t.rows.length }} rows; export for the full report.</p> }
              <p class="muted small">{{ t.footnote }}</p>
            }
          </app-state>
        </section>
      }
    </div>
  `,
})
export class ReportsPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly player = signal<any>(null);
  readonly previewType = signal<{ type: string; id?: string } | null>(null);
  readonly table = rxResource({
    params: () => this.previewType(),
    stream: ({ params }) => (params ? this.api.get<any>(`reports/${params.type}/preview`, { id: params.id }) : of(null)),
  });
  readonly reports: ReportDef[] = [
    { type: 'squad', title: 'Squad report', description: 'Current squad with roles, prices, availability and ratings.', perms: [P.squadRead] },
    { type: 'auction-results', title: 'Auction results', description: 'Latest auction: sold and unsold players, prices and buyers.', perms: [P.auctionsRead] },
    { type: 'finance-ledger', title: 'Finance ledger', description: 'Every transaction of this season, all categories.', perms: [P.financeRead] },
    { type: 'contracts', title: 'Player contracts', description: 'Contract terms, salaries and status.', perms: [P.contractsRead] },
    { type: 'trades', title: 'Trades', description: 'Every trade involving the franchise and its status.', perms: [P.tradesRead] },
    { type: 'player-stats', title: 'Player season statistics', description: 'Season-by-season statistics of one player, with sources.', perms: [P.statsRead], needsPlayer: true },
  ];

  canSee(r: ReportDef): boolean {
    return this.auth.has(...r.perms);
  }

  private id(r: ReportDef): string | undefined {
    if (r.needsPlayer && !this.player()) {
      this.toast.error(new Error('Choose a player first.'));
      return 'missing';
    }
    return r.needsPlayer ? this.player().id : undefined;
  }

  preview(r: ReportDef): void {
    const id = this.id(r);
    if (id === 'missing') return;
    this.previewType.set({ type: r.type, id });
  }

  export(r: ReportDef, format: string): void {
    const id = this.id(r);
    if (id === 'missing') return;
    this.api.download(`reports/${r.type}/export`, { format, id }).subscribe({ error: (e) => this.toast.error(e) });
  }

  fmt(v: unknown): string {
    if (v === null || v === undefined) return '';
    if (typeof v === 'number') return v.toLocaleString('en-IN');
    if (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(v)) return v.slice(0, 16).replace('T', ' ');
    return String(v);
  }
}
