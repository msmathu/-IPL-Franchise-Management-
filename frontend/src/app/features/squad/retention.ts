import { DatePipe } from '@angular/common';
import { PlayerName } from '../../shared/media';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { InrPipe, P, Toast, formatInr } from '../../core/services';
import { PageHeader, StatCard, StateView, StatusChip, confirmAction } from '../../shared/ui';

@Component({
  selector: 'app-retention',
  imports: [PlayerName, PageHeader, StateView, StatCard, StatusChip, InrPipe, DatePipe, FormsModule, MatButtonModule, MatCheckboxModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatTabsModule],
  template: `
    <div class="page">
      <app-state [loading]="res.isLoading() && !res.value()" [error]="res.error()" (retry)="res.reload()">
        @if (res.value(); as o) {
          <app-page-header title="Retention & releases" [subtitle]="o.season + (o.rules.deadline ? ' · deadline ' + (o.rules.deadline | date: 'd MMM yyyy') : '')" />
          @if (o.hasSubmitted) { <div class="demo-banner">Retentions for this season have been submitted.</div> }
          @else if (!o.isBeforeDeadline) { <div class="demo-banner">The retention deadline has passed.</div> }
          <div class="grid stats">
            <app-stat label="Max retentions" [value]="o.rules.maxTotal" [hint]="o.rules.maxCapped + ' capped · ' + o.rules.maxUncapped + ' uncapped · ' + o.rules.maxOverseas + ' overseas'" />
            <app-stat label="Capped slot costs" [value]="costs(o.rules.cappedSlotCosts)" />
            <app-stat label="Uncapped cost" [value]="o.rules.uncappedCost | inr" />
            <app-stat label="Auction purse" [value]="o.rules.purse | inr" />
          </div>
          <mat-tab-group>
            <mat-tab label="Simulator">
              <div class="grid cols-2 tabpad">
                <section class="card"><h2>Current squad</h2>
                  <p class="muted small">Tick players in the order you would use the slots (slot costs apply in that order).</p>
                  @for (p of o.squad; track p.playerId) {
                    <div class="list-item">
                      <mat-checkbox [checked]="picked().includes(p.playerId)" (change)="toggle(p.playerId)" [disabled]="o.hasSubmitted" />
                      <app-pname [id]="p.playerId" [name]="p.name" [size]="24" />@if (p.isOverseas) { <span class="tag os">OS</span> }@if (!p.isCapped) { <span class="tag">Uncapped</span> }
                      <span class="muted small">{{ p.role }}@if (p.age !== null && p.age !== undefined) { · {{ p.age }}y }</span><span class="spacer"></span>
                      @if (p.availability !== 'Available') { <app-chip [text]="p.availability" /> }
                      <span class="small">{{ p.currentSalary | inr }}</span>
                      @if (auth.has(P.retentionManage) && !o.hasSubmitted) { <button mat-icon-button (click)="release(p)" aria-label="Release"><mat-icon>person_remove</mat-icon></button> }
                    </div>
                  }
                </section>
                <section class="card"><h2>Simulation</h2>
                  @if (sim(); as s) {
                    @for (slot of s.slots; track slot.playerId) { <div class="list-item"><strong>#{{ slot.slotNumber }}</strong><app-pname [id]="slot.playerId" [name]="slot.name" [size]="24" /><span class="muted small">{{ slot.category }}</span><span class="spacer"></span>{{ slot.cost | inr }}</div> }
                    @empty { <p class="muted">Select players to see the cost.</p> }
                    <div class="grid stats">
                      <app-stat label="Total cost" [value]="s.totalCost | inr" />
                      <app-stat label="Purse after" [value]="s.purseAfter | inr" [tone]="s.purseAfter < 0 ? 'warn' : 'good'" />
                      <app-stat label="Squad slots left" [value]="s.squadSlotsRemaining" />
                      <app-stat label="Overseas slots left" [value]="s.overseasSlotsRemaining" />
                    </div>
                    @for (v of s.violations; track v) { <p class="bad-text small">✖ {{ v }}</p> }
                    @for (w of s.warnings; track w) { <p class="warn-text small">⚠ {{ w }}</p> }
                    @if (auth.has(P.retentionManage) && !o.hasSubmitted) {
                      <div class="row"><mat-form-field><mat-label>Plan name</mat-label><input matInput [(ngModel)]="planName" /></mat-form-field>
                        <button mat-stroked-button [disabled]="!planName || !s.isValid || !s.slots.length" (click)="savePlan()">Save plan</button></div>
                    }
                  }
                </section>
              </div>
            </mat-tab>
            <mat-tab [label]="'Saved plans (' + o.plans.length + ')'">
              <div class="stack tabpad">
                @for (plan of o.plans; track plan.id) {
                  <section class="card">
                    <div class="row"><h2>{{ plan.name }}</h2>@if (plan.isSubmitted) { <app-chip text="Submitted" /> }<span class="spacer"></span>
                      <span>{{ plan.simulation.totalCost | inr }} · purse after {{ plan.simulation.purseAfter | inr }}</span>
                      @if (!plan.isSubmitted && !o.hasSubmitted) {
                        <button mat-button (click)="loadPlan(plan)">Load</button>
                        @if (auth.has(P.retentionManage)) { <button mat-button (click)="deletePlan(plan.id)">Delete</button> }
                        @if (auth.has(P.retentionSubmit) && o.isBeforeDeadline) { <button mat-flat-button [disabled]="!plan.simulation.isValid" (click)="submit(plan)">Submit as final</button> }
                      }</div>
                    <p class="small">{{ names(plan) }}</p>
                    @for (v of plan.simulation.violations; track v) { <p class="bad-text small">✖ {{ v }}</p> }
                  </section>
                } @empty { <p class="muted">No saved plans yet.</p> }
              </div>
            </mat-tab>
            <mat-tab label="Releases & history">
              <div class="grid cols-2 tabpad">
                <section class="card"><h2>Released players</h2>
                  @for (r of o.releases; track r.id) { <div class="list-item"><app-pname [id]="r.playerId" [name]="r.name" [size]="24" /><span class="muted small">{{ r.reason }}</span><span class="spacer"></span>{{ r.releasedAt | date: 'd MMM yyyy' }}</div> }
                  @empty { <p class="muted">No releases.</p> }</section>
                <section class="card"><h2>Retention history</h2>
                  @for (h of history.value() ?? []; track $index) { <div class="list-item"><strong>{{ h.year }}</strong><span>{{ h.player }}</span><span class="muted small">{{ h.category }} #{{ h.slot }}</span><span class="spacer"></span>{{ h.cost | inr }}</div> }
                  @empty { <p class="muted">No confirmed retentions yet.</p> }</section>
              </div>
            </mat-tab>
          </mat-tab-group>
          <p class="disclaimer">Limits and costs come from this season's configured rules. They are demo defaults until the league sets the official values.</p>
        }
      </app-state>
    </div>
  `,
  styles: `.tabpad { margin-top: 16px; }`,
})
export class RetentionPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  private readonly dialog = inject(MatDialog);
  readonly res = rxResource({ stream: () => this.api.get<any>('retention') });
  readonly history = rxResource({ stream: () => this.api.get<any[]>('retention/history') });
  readonly picked = signal<string[]>([]);
  readonly sim = signal<any>(null);
  planName = '';

  constructor() {
    effect(() => {
      const ids = this.picked();
      this.api.post<any>('retention/simulate', { playerIds: ids }).subscribe({ next: (s) => this.sim.set(s), error: (e) => this.toast.error(e) });
    });
  }

  costs(list: number[]): string {
    return list.map((c) => formatInr(c, 0)).join(' · ');
  }

  loadPlan(plan: any): void {
    this.picked.set(plan.simulation.slots.map((s: any) => s.playerId));
  }

  names(plan: any): string {
    return plan.simulation.slots.map((s: any) => s.name).join(', ');
  }

  toggle(id: string): void {
    this.picked.update((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p, id]));
  }

  savePlan(): void {
    this.api.post('retention/plans', { name: this.planName, playerIds: this.picked() }).subscribe({
      next: () => { this.toast.ok('Plan saved.'); this.planName = ''; this.res.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  deletePlan(id: string): void {
    this.api.delete(`retention/plans/${id}`).subscribe({ next: () => this.res.reload(), error: (e) => this.toast.error(e) });
  }

  submit(plan: any): void {
    confirmAction(this.dialog, {
      title: 'Submit final retention list?',
      message: `${plan.simulation.slots.length} players retained for ${formatInr(plan.simulation.totalCost)}, deducted from the auction purse.\nEvery other squad player is released into the auction pool.\nThis can only be done once per season.`,
      confirmText: 'Submit retentions', danger: true,
    }).subscribe((yes) => yes && this.api.post(`retention/plans/${plan.id}/submit`).subscribe({
      next: () => { this.toast.ok('Retentions submitted.'); this.res.reload(); this.history.reload(); },
      error: (e) => this.toast.error(e),
    }));
  }

  release(p: any): void {
    const reason = prompt(`Reason for releasing ${p.name}:`, 'Released by franchise');
    if (reason === null) return;
    confirmAction(this.dialog, { title: `Release ${p.name}?`, message: 'Their contract ends and they join the auction pool.', danger: true, confirmText: 'Release' })
      .subscribe((yes) => yes && this.api.post('retention/release', { playerId: p.playerId, reason }).subscribe({
        next: () => { this.toast.ok(`${p.name} released.`); this.res.reload(); },
        error: (e) => this.toast.error(e),
      }));
  }
}
