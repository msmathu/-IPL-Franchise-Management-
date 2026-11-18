import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';
import { NAV } from '../app.routes';
import { Auth } from '../core/auth';
import { Realtime, Toast } from '../core/services';
import { TeamTheme } from '../core/team-theme';
import { Monogram } from '../shared/ui';

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, MatSidenavModule, MatToolbarModule, MatListModule, MatIconModule, MatButtonModule,
    MatMenuModule, MatBadgeModule, MatTooltipModule, MatDividerModule, Monogram,
  ],
  template: `
    <mat-sidenav-container class="shell">
      <mat-sidenav [mode]="mobile() ? 'over' : 'side'" [opened]="!mobile() || menuOpen()" (closedStart)="menuOpen.set(false)" class="nav">
        <div class="brand">
          <span class="logo"><mat-icon>sports_cricket</mat-icon></span>
          <div><strong>Franchise HQ</strong><span class="muted small">{{ auth.activeFranchise()?.name ?? 'Management Platform' }}</span></div>
        </div>
        <mat-nav-list>
          @for (item of nav(); track item.path) {
            <a mat-list-item [routerLink]="item.path" routerLinkActive="active" (click)="mobile() && menuOpen.set(false)">
              <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
              <span matListItemTitle>{{ item.label }}</span>
            </a>
          }
        </mat-nav-list>
        <p class="nav-foot muted small">Independent platform. Not affiliated with any official league or board.</p>
      </mat-sidenav>
      <mat-sidenav-content>
        <mat-toolbar class="topbar">
          @if (mobile()) { <button mat-icon-button (click)="menuOpen.set(true)" aria-label="Open menu"><mat-icon>menu</mat-icon></button> }
          @if (auth.activeFranchise(); as f) {
            <button mat-button [matMenuTriggerFor]="franchiseMenu" class="franchise">
              <app-monogram [name]="f.name" [code]="f.code" [color]="f.primaryColor" [size]="30" />
              <span>{{ f.name }}</span>
              @if (switchable()) { <mat-icon>expand_more</mat-icon> }
            </button>
          } @else if (auth.user()?.isLeagueWide) {
            <button mat-button [matMenuTriggerFor]="franchiseMenu" class="franchise"><mat-icon>public</mat-icon><span>League view</span><mat-icon>expand_more</mat-icon></button>
          }
          <mat-menu #franchiseMenu="matMenu">
            @if (auth.user()?.isLeagueWide) { <p class="menu-note muted small">League staff see every franchise. Pick one to act for it:</p> }
            @for (f of auth.user()?.franchises ?? []; track f.id) {
              <button mat-menu-item (click)="switchTo(f.id)"><app-monogram [name]="f.name" [code]="f.code" [color]="f.primaryColor" [size]="24" /> {{ f.name }}</button>
            }
          </mat-menu>
          <span class="spacer"></span>
          <span class="live-dot" [class.on]="realtime.connected()" [matTooltip]="realtime.connected() ? 'Live updates on' : 'Live updates reconnecting'"></span>
          <a mat-icon-button routerLink="/notifications" aria-label="Notifications">
            <mat-icon [matBadge]="realtime.unreadNotifications() || null" matBadgeColor="warn" matBadgeSize="small">notifications</mat-icon>
          </a>
          <button mat-button [matMenuTriggerFor]="userMenu" class="user">
            <mat-icon>account_circle</mat-icon><span class="hide-sm">{{ auth.user()?.fullName }}</span>
          </button>
          <mat-menu #userMenu="matMenu">
            <div class="menu-note"><strong>{{ auth.user()?.fullName }}</strong><br /><span class="muted small">{{ auth.user()?.email }}</span><br />
              <span class="muted small">{{ roles() }}</span></div>
            <mat-divider />
            <a mat-menu-item routerLink="/settings" [queryParams]="{ tab: 'account' }"><mat-icon>lock</mat-icon>Account & security</a>
            <button mat-menu-item (click)="auth.logout()"><mat-icon>logout</mat-icon>Sign out</button>
          </mat-menu>
        </mat-toolbar>
        <div class="demo-strip">Squads: published IPL 2026 lists. Statistics: Cricsheet ball-by-ball data (ODC-BY). Prices and ages of real players are not loaded. Players marked “(Demo)” are fictional.</div>
        <router-outlet />
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    .shell { height: 100vh; }
    .nav { width: 260px; border-right: 1px solid var(--line); }
    .brand { display: flex; gap: 10px; align-items: center; padding: 18px 16px 10px; }
    .brand div { display: flex; flex-direction: column; }
    .logo { width: 38px; height: 38px; border-radius: 10px; background: linear-gradient(135deg, var(--team), var(--team-2)); color: var(--team-ink); display: inline-flex; align-items: center; justify-content: center; box-shadow: 0 4px 14px var(--team-soft); }
    a.active { background: var(--team-soft); border-radius: 10px; box-shadow: inset 3px 0 0 var(--team-accent); }
    a.active mat-icon { color: var(--team-accent); }
    .nav-foot { padding: 12px 16px; }
    .topbar { position: sticky; top: 0; z-index: 5; gap: 8px; color: var(--team-ink);
      background: radial-gradient(rgba(255, 255, 255, 0.16) 1px, transparent 1.4px) 0 0 / 7px 7px, linear-gradient(100deg, var(--team) 0%, var(--team) 55%, var(--team-2) 130%);
      border-bottom: 3px solid var(--team-2); box-shadow: 0 4px 18px rgba(0, 0, 0, 0.35);
      --mat-button-text-label-text-color: var(--team-ink); --mat-icon-button-icon-color: var(--team-ink); }
    :host-context([data-team='off']) .topbar { background: rgba(10, 22, 40, 0.92); color: var(--text); border-bottom: 1px solid var(--line);
      --mat-button-text-label-text-color: var(--text); --mat-icon-button-icon-color: var(--text); }
    .topbar .franchise { font-weight: 700; letter-spacing: 0.02em; }
    .franchise { display: inline-flex; gap: 8px; }
    .franchise span { margin-left: 6px; }
    .menu-note { padding: 8px 16px; max-width: 280px; }
    .live-dot { width: 9px; height: 9px; border-radius: 50%; background: var(--bad); margin-right: 6px; }
    .live-dot.on { background: var(--good); box-shadow: 0 0 8px var(--good); }
    .demo-strip { font-size: 12px; text-align: center; padding: 4px; background: rgba(245, 158, 11, 0.1); color: #fcd34d; border-bottom: 1px solid rgba(245, 158, 11, 0.2); }
    @media (max-width: 700px) { .hide-sm { display: none; } }
  `,
})
export class Shell {
  readonly auth = inject(Auth);
  readonly realtime = inject(Realtime);
  private readonly toast = inject(Toast);
  private readonly router = inject(Router);
  readonly mobile = toSignal(inject(BreakpointObserver).observe('(max-width: 960px)').pipe(map((r) => r.matches)), { initialValue: false });
  readonly menuOpen = signal(false);
  private readonly theme = inject(TeamTheme);
  readonly nav = computed(() => NAV.filter((n) => n.perms.length === 0 || this.auth.has(...n.perms)));
  readonly switchable = computed(() => (this.auth.user()?.franchises.length ?? 0) > 1 || !!this.auth.user()?.isLeagueWide);
  readonly roles = computed(() => {
    const u = this.auth.user();
    return [...(u?.leagueRoles ?? []), ...(this.auth.activeFranchise()?.roles ?? [])].join(', ');
  });

  switchTo(id: string): void {
    this.auth.switchFranchise(id).subscribe({
      next: () => {
        this.toast.ok('Switched franchise.');
        void this.router.navigateByUrl('/');
      },
      error: (e) => this.toast.error(e),
    });
  }
}
