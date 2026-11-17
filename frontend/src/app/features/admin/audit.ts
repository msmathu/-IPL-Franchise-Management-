import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Api, Paged } from '../../core/api';
import { WordsPipe } from '../../core/services';
import { PageHeader, StateView } from '../../shared/ui';

@Component({
  selector: 'app-audit',
  imports: [PageHeader, StateView, WordsPipe, DatePipe, FormsModule, MatFormFieldModule, MatInputModule, MatPaginatorModule],
  template: `
    <div class="page">
      <app-page-header title="Audit logs" subtitle="Every sensitive operation: who, what, when and from where" />
      <section class="card">
        <div class="filters">
          <mat-form-field><mat-label>Action starts with</mat-label><input matInput [ngModel]="action()" (ngModelChange)="action.set($event); page.set(1)" placeholder="trades." /></mat-form-field>
          <mat-form-field><mat-label>User e-mail</mat-label><input matInput [ngModel]="user()" (ngModelChange)="user.set($event); page.set(1)" /></mat-form-field>
          <mat-form-field><mat-label>From</mat-label><input matInput type="date" [ngModel]="from()" (ngModelChange)="from.set($event); page.set(1)" /></mat-form-field>
          <mat-form-field><mat-label>To</mat-label><input matInput type="date" [ngModel]="to()" (ngModelChange)="to.set($event); page.set(1)" /></mat-form-field>
        </div>
        <app-state [loading]="res.isLoading()" [error]="res.error()" [empty]="res.value()?.items?.length === 0" (retry)="res.reload()">
          <div class="table-wrap"><table class="data"><tr><th>When</th><th>User</th><th>Franchise</th><th>Action</th><th>Entity</th><th>Details</th><th>IP</th></tr>
            @for (a of res.value()?.items ?? []; track a.id) {
              <tr><td class="small">{{ a.at | date: 'd MMM yyyy HH:mm:ss' }}</td><td class="small">{{ a.userEmail ?? 'system' }}</td><td>{{ a.franchise }}</td>
                <td>{{ a.action | words }}</td><td class="small">{{ a.entityType }}<div class="muted">{{ a.entityId }}</div></td>
                <td class="small details">{{ a.details }}</td><td class="small muted">{{ a.ipAddress }}</td></tr>
            }</table></div>
          <mat-paginator [length]="res.value()?.total ?? 0" [pageSize]="50" [pageIndex]="page() - 1" (page)="paged($event)" />
        </app-state>
      </section>
    </div>
  `,
  styles: `.details { max-width: 420px; word-break: break-word; font-family: monospace; }`,
})
export class AuditPage {
  private readonly api = inject(Api);
  readonly action = signal('');
  readonly user = signal('');
  readonly from = signal('');
  readonly to = signal('');
  readonly page = signal(1);
  readonly res = rxResource({
    params: () => ({ action: this.action(), user: this.user(), from: this.from(), to: this.to() ? this.to() + 'T23:59:59' : '', page: this.page() }),
    stream: ({ params }) => this.api.get<Paged<any>>('audit', { ...params, pageSize: 50 }),
  });

  paged(e: PageEvent): void {
    this.page.set(e.pageIndex + 1);
  }
}
