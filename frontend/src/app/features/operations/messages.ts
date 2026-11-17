import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { filter, of } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Realtime, Toast, WordsPipe } from '../../core/services';
import { PageHeader, StateView } from '../../shared/ui';

@Component({
  selector: 'app-messages',
  imports: [PageHeader, StateView, RouterLink, WordsPipe, DatePipe, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <div class="page">
      <app-page-header title="Communication centre" subtitle="Internal discussions, franchise-to-franchise messages and trade negotiations">
        @if (auth.has(P.messagesSend)) { <button mat-flat-button (click)="composing.set(!composing())"><mat-icon>edit</mat-icon>New conversation</button> }
      </app-page-header>
      @if (composing()) {
        <section class="card">
          <div class="row">
            <mat-form-field><mat-label>Type</mat-label><mat-select [(ngModel)]="type"><mat-option value="Internal">Internal (my franchise)</mat-option><mat-option value="InterFranchise">With another franchise</mat-option></mat-select></mat-form-field>
            @if (type === 'InterFranchise') {
              <mat-form-field><mat-label>Franchise</mat-label><mat-select [(ngModel)]="counterparty">@for (f of franchises(); track f.id) { <mat-option [value]="f.id">{{ f.name }}</mat-option> }</mat-select></mat-form-field>
            }
            <mat-form-field class="wide"><mat-label>Subject</mat-label><input matInput [(ngModel)]="subject" /></mat-form-field>
          </div>
          <mat-form-field class="full"><mat-label>Message</mat-label><textarea matInput rows="3" [(ngModel)]="firstMessage"></textarea></mat-form-field>
          <button mat-flat-button [disabled]="!subject || !firstMessage || (type === 'InterFranchise' && !counterparty)" (click)="create()">Start conversation</button>
        </section>
      }
      <div class="grid msgs">
        <section class="card"><h2>Conversations</h2>
          <app-state [loading]="threads.isLoading()" [error]="threads.error()" [empty]="!threads.value()?.length" emptyText="No conversations yet." (retry)="threads.reload()">
            @for (t of threads.value() ?? []; track t.id) {
              <a class="list-item thread" [class.active]="t.id === id()" [routerLink]="['/messages', t.id]">
                <div class="stack0"><strong>{{ t.subject }}</strong><span class="small muted">{{ t.type | words }}{{ t.counterparty ? ' · ' + (t.franchiseId === auth.user()?.activeFranchiseId ? t.counterparty : t.franchise) : '' }}</span>
                  <span class="small">{{ t.lastMessage }}</span></div>
                <span class="spacer"></span>@if (t.unread) { <span class="chip" data-tone="warn">{{ t.unread }}</span> }
              </a>
            }
          </app-state>
        </section>
        <section class="card conv">
          @if (id()) {
            <app-state [loading]="messages.isLoading() && !messages.value()" [error]="messages.error()" (retry)="messages.reload()">
              <div class="bubbles">
                @for (m of messages.value() ?? []; track m.id) {
                  <div class="bubble" [class.mine]="m.isMine"><div class="small muted">{{ m.senderName }} · {{ m.senderFranchise }} · {{ m.sentAt | date: 'd MMM HH:mm' }}</div><div class="pre">{{ m.body }}</div></div>
                }
              </div>
              @if (auth.has(P.messagesSend)) {
                <div class="row"><mat-form-field class="full"><mat-label>Reply</mat-label><textarea matInput rows="2" [(ngModel)]="reply" (keydown.control.enter)="send()"></textarea></mat-form-field>
                  <button mat-flat-button [disabled]="!reply.trim()" (click)="send()">Send</button></div>
              }
              <p class="muted small">Conversations with another franchise are visible only to the two franchises involved.</p>
            </app-state>
          } @else { <app-state [empty]="true" emptyText="Choose a conversation." /> }
        </section>
      </div>
    </div>
  `,
  styles: `
    .msgs { grid-template-columns: 340px 1fr; } @media (max-width: 900px) { .msgs { grid-template-columns: 1fr; } }
    .thread { color: var(--text); text-decoration: none !important; border-radius: 8px; } .thread.active { background: rgba(56,189,248,0.1); }
    .stack0 { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .conv { display: flex; flex-direction: column; gap: 12px; }
    .bubbles { display: flex; flex-direction: column; gap: 10px; max-height: 60vh; overflow: auto; }
    .bubble { background: var(--bg-2); border-radius: 12px; padding: 10px 12px; max-width: 75%; }
    .bubble.mine { align-self: flex-end; background: rgba(56, 189, 248, 0.15); }
  `,
})
export class MessagesPage {
  readonly id = input<string>();
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly router = inject(Router);
  readonly composing = signal(false);
  readonly threads = rxResource({ stream: () => this.api.get<any[]>('messages') });
  readonly messages = rxResource({ params: () => this.id(), stream: ({ params }) => (params ? this.api.get<any[]>(`messages/${params}`) : of([])) });
  private readonly franchiseList = rxResource({ stream: () => this.api.get<any[]>('franchises') });
  readonly franchises = computed(() => (this.franchiseList.value() ?? []).filter((f) => f.id !== this.auth.user()?.activeFranchiseId));
  type = 'Internal';
  counterparty = '';
  subject = '';
  firstMessage = '';
  reply = '';

  constructor() {
    inject(Realtime).events.pipe(filter((e) => e.name === 'message'), takeUntilDestroyed()).subscribe((e) => {
      if (e.payload?.threadId === this.id()) this.messages.reload();
      this.threads.reload();
    });
    effect(() => {
      // Opening a thread marks it read; refresh unread counts.
      if (this.messages.value()) this.threads.reload();
    });
  }

  create(): void {
    this.api.post<{ id: string }>('messages', { subject: this.subject, type: this.type, counterpartyFranchiseId: this.type === 'Internal' ? null : this.counterparty, tradeOfferId: null, message: this.firstMessage })
      .subscribe({ next: (r) => { this.composing.set(false); this.subject = this.firstMessage = ''; this.threads.reload(); void this.router.navigate(['/messages', r.id]); }, error: (e) => this.toast.error(e) });
  }

  send(): void {
    if (!this.reply.trim()) return;
    this.api.post(`messages/${this.id()}`, { body: this.reply }).subscribe({ next: () => { this.reply = ''; this.messages.reload(); }, error: (e) => this.toast.error(e) });
  }
}
