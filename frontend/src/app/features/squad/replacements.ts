import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { rxResource } from '../../core/resource';
import { InrPipe, P, Toast } from '../../core/services';
import { PageHeader, StateView, StatusChip } from '../../shared/ui';
import { PlayerPicker } from '../players/player-profile';

@Component({
  selector: 'app-replacements',
  imports: [PageHeader, StateView, StatusChip, PlayerPicker, RouterLink, DatePipe, InrPipe, FormsModule, MatButtonModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatSelectModule],
  template: `
    <div class="page">
      <app-page-header title="Injury replacements" subtitle="Replace an injured squad player with an unsigned player, subject to league approval" />
      @if (rules.value(); as r) {
        <p class="small muted">
          <mat-icon class="role-ico">gavel</mat-icon>Season rules: replacements {{ r.enabled ? 'allowed' : 'not allowed' }} ·
          fee {{ r.feeCappedAtInjuredFee ? 'capped at the injured player\\'s fee' : 'not capped' }} ·
          fee {{ r.feeAffectsPurse ? 'is deducted from the auction purse' : 'does not affect the auction purse' }}
          (Settings → Seasons &amp; rules).</p>
      }

      @if (canRequest()) {
        <section class="card"><h2><mat-icon class="role-ico">healing</mat-icon>Request a replacement</h2>
          @if (injured().length === 0) {
            <p class="muted">No squad player is marked <strong>Injured</strong>. Mark the player injured in Medical &amp; Availability first.</p>
          } @else {
            <div class="row">
              <mat-form-field><mat-label>Injured player</mat-label>
                <mat-select [(ngModel)]="injuredId">@for (p of injured(); track p.playerId) { <mat-option [value]="p.playerId">{{ p.name }}{{ p.price ? ' · ' : '' }}{{ p.price ? (p.price | inr) : '' }}</mat-option> }</mat-select></mat-form-field>
              <app-player-picker label="Replacement (unsigned player)" (picked)="replacement.set($event)" />
              @if (replacement(); as rp) { <span class="chip">{{ rp.fullName }}{{ rp.franchise ? ' · signed by ' + rp.franchise.code : '' }}</span> }
              <mat-form-field><mat-label>Fee (₹ lakh)</mat-label><input matInput type="number" min="0" [(ngModel)]="feeLakh" /></mat-form-field>
              <mat-form-field class="wide"><mat-label>Reason / medical summary (no confidential details)</mat-label><input matInput [(ngModel)]="reason" /></mat-form-field>
              <button mat-flat-button [disabled]="!injuredId || !replacement() || feeLakh === null" (click)="submit()"><mat-icon>send</mat-icon>Send to league</button>
            </div>
          }
        </section>
      }

      <app-state [loading]="list.isLoading()" [error]="list.error()" [empty]="!list.value()?.length" emptyText="No replacement requests yet." (retry)="list.reload()">
        <section class="card table-wrap">
          <table class="data">
            <tr><th>Requested</th>@if (auth.user()?.isLeagueWide) { <th>Team</th> }<th>Injured player</th><th>Replacement</th><th class="num">Fee</th><th>Reason</th><th>Status</th><th></th></tr>
            @for (r of list.value() ?? []; track r.id) {
              <tr>
                <td>{{ r.requestedAt | date: 'd MMM yyyy' }}</td>
                @if (auth.user()?.isLeagueWide) { <td><strong>{{ r.franchiseCode }}</strong></td> }
                <td><a [routerLink]="['/players', r.injuredPlayerId]">{{ r.injuredPlayerName }}</a>@if (r.injuredPlayerFee) { <div class="small muted">fee {{ r.injuredPlayerFee | inr }}</div> }</td>
                <td><a [routerLink]="['/players', r.replacementPlayerId]">{{ r.replacementPlayerName }}</a>@if (r.replacementIsOverseas) { <span class="tag os">OS</span> }</td>
                <td class="num">{{ r.fee | inr }}</td>
                <td class="small">{{ r.reason ?? '—' }}@if (r.decisionNote) { <div class="muted">League: {{ r.decisionNote }}</div> }</td>
                <td><app-chip [text]="r.status" /></td>
                <td class="right">
                  @if (r.status === 'Pending' && auth.has(P.tradesLeague)) {
                    <button mat-flat-button (click)="approve(r)"><mat-icon>check</mat-icon>Approve</button>
                    <button mat-button (click)="reject(r)">Reject</button>
                  }
                  @if (r.status === 'Pending' && canRequest() && !auth.user()?.isLeagueWide) { <button mat-button (click)="cancel(r)">Cancel</button> }
                </td>
              </tr>
            }
          </table>
        </section>
      </app-state>
    </div>
  `,
})
export class ReplacementsPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly canRequest = computed(() => this.auth.has(P.squadManage) && !!this.auth.activeFranchise());
  readonly rules = rxResource({ stream: () => this.api.get<any>('replacements/rules') });
  readonly list = rxResource({ stream: () => this.api.get<any[]>('replacements') });
  private readonly squad = rxResource({ stream: () => (this.auth.activeFranchise() ? this.api.get<any>('squad') : of(null)) });
  readonly injured = computed(() => (this.squad.value()?.players ?? []).filter((p: any) => p.availability === 'Injured'));
  readonly replacement = signal<any | null>(null);
  injuredId: string | null = null;
  feeLakh: number | null = null;
  reason = '';

  submit(): void {
    this.api.post('replacements', {
      injuredPlayerId: this.injuredId, replacementPlayerId: this.replacement()?.id, fee: Math.round((this.feeLakh ?? 0) * 100000), reason: this.reason || null,
    }).subscribe({
      next: () => {
        this.toast.ok('Replacement request sent to the league.');
        this.injuredId = null; this.replacement.set(null); this.feeLakh = null; this.reason = '';
        this.list.reload();
      },
      error: (e) => this.toast.error(e),
    });
  }

  approve(r: any): void {
    this.api.post(`replacements/${r.id}/approve`, { note: null }).subscribe({
      next: () => { this.toast.ok(`${r.replacementPlayerName} joins ${r.franchiseName}.`); this.list.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  reject(r: any): void {
    const note = prompt('Reason for rejecting this replacement (shown to the franchise):');
    if (!note) return;
    this.api.post(`replacements/${r.id}/reject`, { note }).subscribe({ next: () => this.list.reload(), error: (e) => this.toast.error(e) });
  }

  cancel(r: any): void {
    this.api.post(`replacements/${r.id}/cancel`, {}).subscribe({ next: () => this.list.reload(), error: (e) => this.toast.error(e) });
  }
}
