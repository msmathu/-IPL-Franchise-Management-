import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { rxResource } from '../../core/resource';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Router } from '@angular/router';
import { filter } from 'rxjs';
import { Api, Paged } from '../../core/api';
import { Realtime, Toast, WordsPipe } from '../../core/services';
import { PageHeader, StateView } from '../../shared/ui';

@Component({
  selector: 'app-notifications',
  imports: [PageHeader, StateView, WordsPipe, DatePipe, MatButtonModule, MatButtonToggleModule, MatPaginatorModule],
  template: `
    <div class="page">
      <app-page-header title="Notifications">
        <button mat-stroked-button (click)="readAll()">Mark all as read</button>
      </app-page-header>
      <mat-button-toggle-group [value]="unread()" (change)="unread.set($event.value); page.set(1)">
        <mat-button-toggle [value]="false">All</mat-button-toggle><mat-button-toggle [value]="true">Unread</mat-button-toggle>
      </mat-button-toggle-group>
      <section class="card">
        <app-state [loading]="res.isLoading()" [error]="res.error()" [empty]="res.value()?.items?.length === 0" emptyText="You're all caught up." (retry)="res.reload()">
          @for (n of res.value()?.items ?? []; track n.id) {
            <div class="list-item note" [class.unread]="!n.isRead" (click)="open(n)">
              <span class="dot"></span>
              <div><strong>{{ n.title }}</strong><div class="small">{{ n.body }}</div><div class="small muted">{{ n.type | words }} · {{ n.createdAt | date: 'd MMM HH:mm' }}</div></div>
            </div>
          }
          <mat-paginator [length]="res.value()?.total ?? 0" [pageSize]="25" [pageIndex]="page() - 1" (page)="paged($event)" />
        </app-state>
      </section>
      <p class="muted small">Choose which notifications you receive (in the app or by e-mail) in Settings → Notifications.</p>
    </div>
  `,
  styles: `.note { cursor: pointer; } .dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; } .unread .dot { background: var(--accent); } .unread strong { color: var(--accent); }`,
})
export class NotificationsPage {
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly router = inject(Router);
  private readonly realtime = inject(Realtime);
  readonly unread = signal(false);
  readonly page = signal(1);
  readonly res = rxResource({ params: () => ({ unreadOnly: this.unread(), page: this.page() }), stream: ({ params }) => this.api.get<Paged<any>>('notifications', { ...params, pageSize: 25 }) });

  constructor() {
    this.realtime.events.pipe(filter((e) => e.name === 'notification'), takeUntilDestroyed()).subscribe(() => this.res.reload());
  }

  paged(e: PageEvent): void {
    this.page.set(e.pageIndex + 1);
  }

  open(n: any): void {
    this.api.post(`notifications/${n.id}/read`).subscribe(() => this.realtime.refreshUnread());
    if (n.link) void this.router.navigateByUrl(n.link);
    else this.res.reload();
  }

  readAll(): void {
    this.api.post('notifications/read-all').subscribe({ next: () => { this.realtime.refreshUnread(); this.res.reload(); }, error: (e) => this.toast.error(e) });
  }
}
