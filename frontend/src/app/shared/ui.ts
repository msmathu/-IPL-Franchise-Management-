import { Component, ElementRef, OnDestroy, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import { Observable } from 'rxjs';
import { errorMessage } from '../core/api';

Chart.register(...registerables);
Chart.defaults.color = '#9fb0cc';
Chart.defaults.borderColor = 'rgba(159,176,204,0.15)';
Chart.defaults.font.family = 'Inter, system-ui, sans-serif';

@Component({
  selector: 'app-page-header',
  template: `
    <header class="page-header">
      <div>
        <h1>{{ title() }}</h1>
        @if (subtitle()) { <p class="muted">{{ subtitle() }}</p> }
      </div>
      <div class="actions"><ng-content /></div>
    </header>
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
}

/**
 * Loading / error / empty states for any data area. Every screen wraps its content in this so the three states
 * are always handled.
 */
@Component({
  selector: 'app-state',
  imports: [MatProgressSpinnerModule, MatButtonModule, MatIconModule],
  template: `
    @if (loading()) {
      <div class="state"><mat-spinner diameter="36" /><span class="muted">Loading…</span></div>
    } @else if (error()) {
      <div class="state error">
        <mat-icon>error_outline</mat-icon>
        <span>{{ message() }}</span>
        <button mat-stroked-button (click)="retry.emit()">Try again</button>
      </div>
    } @else if (empty()) {
      <div class="state empty"><mat-icon>inbox</mat-icon><span>{{ emptyText() }}</span><ng-content select="[empty]" /></div>
    } @else {
      <ng-content />
    }
  `,
})
export class StateView {
  readonly loading = input(false);
  readonly error = input<unknown>(undefined);
  readonly empty = input(false);
  readonly emptyText = input('Nothing here yet.');
  readonly retry = output<void>();
  readonly message = computed(() => errorMessage(this.error()));
}

@Component({
  selector: 'app-stat',
  template: `
    <div class="stat-card" [class.warn]="tone() === 'warn'" [class.good]="tone() === 'good'">
      <span class="label">{{ label() }}</span>
      <span class="value">{{ value() }}</span>
      @if (hint()) { <span class="hint">{{ hint() }}</span> }
    </div>
  `,
})
export class StatCard {
  readonly label = input.required<string>();
  readonly value = input.required<string | number | null>();
  readonly hint = input<string>();
  readonly tone = input<'normal' | 'warn' | 'good'>('normal');
}

@Component({
  selector: 'app-chip',
  template: `<span class="chip" [attr.data-tone]="tone()">{{ text() }}</span>`,
})
export class StatusChip {
  readonly text = input.required<string>();
  readonly tone = computed(() => {
    const t = this.text().toLowerCase().replace(/\s/g, '');
    if (/(completed|approved|sold|available|active|paid|confirmed|reviewed|live|sign|healthy)/.test(t)) return 'good';
    if (/(rejected|withdrawn|expired|unsold|injured|terminated|cancelled|unavailable|pass|failed)/.test(t)) return 'bad';
    if (/(pending|awaiting|counter|negotiation|draft|doubtful|scheduled|paused|monitor|ready|submitted)/.test(t)) return 'warn';
    return 'neutral';
  });
}

/**
 * Franchise badge: the franchise's own logo when a licensed logo URL has been saved, otherwise the short code on the
 * team colour (no protected branding is bundled with the app).
 */
@Component({
  selector: 'app-monogram',
  template: `@if (logoUrl() && !failed()) {
      <img class="monogram logo" [src]="logoUrl()" [alt]="name()" [style.width.px]="size()" [style.height.px]="size()" (error)="failed.set(true)" />
    } @else {
      <span class="monogram" [style.background]="color()" [style.width.px]="size()" [style.height.px]="size()" [style.font-size.px]="size() * (label().length > 3 ? 0.28 : 0.36)" [attr.aria-label]="name()">{{ label() }}</span>
    }`,
})
export class Monogram {
  readonly name = input.required<string>();
  readonly color = input('#1d4ed8');
  readonly size = input(32);
  /** Short code such as "MI" or "CSK"; falls back to the initials of the name. */
  readonly code = input<string | null | undefined>(null);
  readonly logoUrl = input<string | null | undefined>(null);
  readonly failed = signal(false);
  readonly label = computed(() => this.code() || this.name().split(/\s+/).filter(Boolean).map((w) => w[0]).slice(0, 3).join('').toUpperCase());
}

/** Player picture: the saved (licensed) photo URL, or initials on a neutral colour. */
@Component({
  selector: 'app-avatar',
  template: `@if (photoUrl() && !failed()) {
      <img class="avatar-img" [src]="photoUrl()" [alt]="name()" [style.width.px]="size()" [style.height.px]="size()" (error)="failed.set(true)" />
    } @else {
      <span class="avatar-img" [style.background]="color()" [style.width.px]="size()" [style.height.px]="size()" [style.font-size.px]="size() * 0.36">{{ initials() }}</span>
    }`,
})
export class PlayerAvatar {
  readonly name = input.required<string>();
  readonly photoUrl = input<string | null | undefined>(null);
  readonly color = input('#334155');
  readonly size = input(32);
  readonly failed = signal(false);
  readonly initials = computed(() => this.name().replace(/\(.*\)/, '').split(/\s+/).filter(Boolean).map((w) => w[0]).slice(0, 2).join('').toUpperCase());
}

@Component({
  selector: 'app-chart',
  template: `<div class="chart-box" [style.height.px]="height()"><canvas #canvas></canvas></div>`,
})
export class ChartView implements OnDestroy {
  readonly config = input.required<ChartConfiguration>();
  readonly height = input(260);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  constructor() {
    effect(() => {
      const cfg = this.config();
      this.chart?.destroy();
      this.chart = new Chart(this.canvas().nativeElement, {
        ...cfg,
        options: { responsive: true, maintainAspectRatio: false, ...(cfg.options ?? {}) },
      } as ChartConfiguration);
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}

export const PALETTE = ['#38bdf8', '#f59e0b', '#a78bfa', '#34d399', '#f472b6', '#fb7185', '#facc15', '#60a5fa'];

export interface ConfirmData {
  title: string;
  message: string;
  confirmText?: string;
  danger?: boolean;
}

@Component({
  selector: 'app-confirm',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content><p class="pre">{{ data.message }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(false)">Cancel</button>
      <button mat-flat-button [class.danger]="data.danger" (click)="ref.close(true)">{{ data.confirmText ?? 'Confirm' }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<ConfirmDialog, boolean>);
}

/** Asks for confirmation before financial or irreversible operations. */
export function confirmAction(dialog: MatDialog, data: ConfirmData): Observable<boolean | undefined> {
  return dialog.open(ConfirmDialog, { data, width: '440px' }).afterClosed();
}
