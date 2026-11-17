import { Injectable, Pipe, PipeTransform, effect, inject, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { Api, errorMessage } from './api';
import { Auth } from './auth';

/** Success/error toasts. */
@Injectable({ providedIn: 'root' })
export class Toast {
  private readonly bar = inject(MatSnackBar);
  ok(message: string): void {
    this.bar.open(message, 'OK', { duration: 3500, panelClass: 'toast-ok' });
  }
  error(err: unknown): void {
    this.bar.open(errorMessage(err), 'Dismiss', { duration: 7000, panelClass: 'toast-error' });
  }
}

export interface LiveEvent {
  name: string;
  payload: any;
}

/** One SignalR connection for notifications, trade/message updates and live auctions. */
@Injectable({ providedIn: 'root' })
export class Realtime {
  private readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private connection?: signalR.HubConnection;
  private auctions = new Set<string>();
  readonly events = new Subject<LiveEvent>();
  readonly connected = signal(false);
  readonly unreadNotifications = signal(0);

  constructor() {
    effect(() => {
      const user = this.auth.user();
      const franchise = user?.activeFranchiseId;
      void this.restart(!!user, franchise);
    });
  }

  joinAuction(id: string): void {
    this.auctions.add(id);
    if (this.connection?.state === signalR.HubConnectionState.Connected) void this.connection.invoke('JoinAuction', id);
  }

  leaveAuction(id: string): void {
    this.auctions.delete(id);
    if (this.connection?.state === signalR.HubConnectionState.Connected) void this.connection.invoke('LeaveAuction', id);
  }

  refreshUnread(): void {
    this.api.get<{ count: number }>('notifications/unread-count').subscribe({ next: (r) => this.unreadNotifications.set(r.count), error: () => {} });
  }

  private async restart(loggedIn: boolean, _franchise?: string | null): Promise<void> {
    await this.connection?.stop().catch(() => {});
    this.connected.set(false);
    if (!loggedIn) return;
    this.refreshUnread();
    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/live', { accessTokenFactory: () => this.auth.token() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();
    for (const name of ['notification', 'auction', 'trade.updated', 'message']) {
      conn.on(name, (payload) => {
        this.events.next({ name, payload });
        if (name === 'notification') this.unreadNotifications.update((n) => n + 1);
      });
    }
    conn.onreconnected(() => {
      this.connected.set(true);
      this.auctions.forEach((id) => void conn.invoke('JoinAuction', id));
    });
    conn.onclose(() => this.connected.set(false));
    this.connection = conn;
    try {
      await conn.start();
      this.connected.set(true);
      this.auctions.forEach((id) => void conn.invoke('JoinAuction', id));
    } catch {
      this.connected.set(false);
    }
  }
}

/** ₹ amounts in crore / lakh, the way franchises talk about money. */
@Pipe({ name: 'inr' })
export class InrPipe implements PipeTransform {
  transform(value: number | null | undefined, digits = 2): string {
    return formatInr(value, digits);
  }
}

export function formatInr(value: number | null | undefined, digits = 2): string {
  if (value === null || value === undefined) return '—';
  const abs = Math.abs(value);
  const sign = value < 0 ? '−' : '';
  if (abs >= 1e7) return `${sign}₹${trim((abs / 1e7).toFixed(digits))} Cr`;
  if (abs >= 1e5) return `${sign}₹${trim((abs / 1e5).toFixed(digits))} L`;
  return `${sign}₹${abs.toLocaleString('en-IN')}`;
}

function trim(s: string): string {
  return s.includes('.') ? s.replace(/\.?0+$/, '') : s;
}

/** "AcceptedInPrinciple" → "Accepted in principle". */
@Pipe({ name: 'words' })
export class WordsPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '';
    const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/[._]/g, ' ');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
  }
}

/** Permission codes used by the UI (mirrors the server's Perm class). */
export const P = {
  franchisesRead: 'franchises.read', franchisesEdit: 'franchises.edit', franchisesManage: 'franchises.manage',
  playersRead: 'players.read', playersManage: 'players.manage', statsRead: 'stats.read', statsImport: 'stats.import',
  comparisonsRead: 'comparisons.read', comparisonsManage: 'comparisons.manage',
  tradesRead: 'trades.read', tradesCreate: 'trades.create', tradesApprove: 'trades.approve', tradesConsent: 'trades.consent', tradesLeague: 'trades.league_approve',
  retentionRead: 'retention.read', retentionManage: 'retention.manage', retentionSubmit: 'retention.submit',
  auctionsRead: 'auctions.read', auctionsManage: 'auctions.manage', auctionsBid: 'auctions.bid',
  squadRead: 'squad.read', squadManage: 'squad.manage', xiManage: 'playingxi.manage',
  scoutingRead: 'scouting.read', scoutingReport: 'scouting.report', scoutingManage: 'scouting.manage',
  valuationRead: 'valuation.read', valuationRun: 'valuation.run',
  financeRead: 'finance.read', financeManage: 'finance.manage', financeApprove: 'finance.approve', contractsRead: 'contracts.read', contractsManage: 'contracts.manage',
  messagesRead: 'messages.read', messagesSend: 'messages.send', medicalRead: 'medical.read', medicalManage: 'medical.manage',
  availabilityRead: 'availability.read', availabilityManage: 'availability.manage', reportsRead: 'reports.read', reportsExport: 'reports.export',
  usersManage: 'users.manage', usersAdmin: 'users.admin', settingsManage: 'settings.manage', seasonsManage: 'seasons.manage', auditRead: 'audit.read',
} as const;
