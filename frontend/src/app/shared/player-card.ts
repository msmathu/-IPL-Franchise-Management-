import { Component, computed, input, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';

/** Material icon for a playing role. */
export function roleIcon(roleCode: string | null | undefined, isKeeper = false): string {
  if (isKeeper || roleCode === 'wk_batter') return 'back_hand';
  switch (roleCode) {
    case 'batter': return 'sports_cricket';
    case 'allrounder': return 'auto_awesome';
    case 'bowler': return 'sports_baseball';
    default: return 'person';
  }
}

export interface PlayerCardData {
  playerId: string;
  name: string;
  role: string;
  roleCode: string;
  photoUrl?: string | null;
  isCaptain?: boolean;
  isOverseas?: boolean;
  isWicketKeeper?: boolean;
  isCapped?: boolean;
  availability?: string;
  matches?: number;
  runs?: number;
  wickets?: number;
}

/** Portrait player card in the team's colours (set the --team* variables on a parent, see teamVars). */
@Component({
  selector: 'app-player-card',
  imports: [RouterLink, MatIconModule, MatTooltipModule],
  template: `
    @let p = player();
    <a class="pcard" [routerLink]="['/players', p.playerId]">
      <div class="pcard-photo">
        @if (p.photoUrl && !failed()) {
          <img [src]="p.photoUrl" [alt]="p.name" loading="lazy" (error)="failed.set(true)" />
        } @else {
          <span class="initials">{{ initials() }}</span>
        }
        <div class="pcard-badges">
          @if (p.isCaptain) { <span class="b cap" matTooltip="Captain">C</span> }
          @if (p.isWicketKeeper) { <span class="b" matTooltip="Wicketkeeper"><mat-icon>back_hand</mat-icon></span> }
          @if (p.isOverseas) { <span class="b" matTooltip="Overseas player"><mat-icon>flight</mat-icon></span> }
          @if (p.isCapped === false) { <span class="b" matTooltip="Uncapped"><mat-icon>fiber_new</mat-icon></span> }
          <span class="spacer"></span>
          @if (p.availability && p.availability !== 'Available') { <span class="b warn" [matTooltip]="p.availability"><mat-icon>healing</mat-icon></span> }
        </div>
      </div>
      <div class="pcard-band">
        <div class="name">{{ p.name }}</div>
        <div class="meta"><span><mat-icon>{{ icon() }}</mat-icon> {{ p.role }}</span><span class="view">View profile ›</span></div>
        @if (p.matches) {
          <div class="nums"><span><b>{{ p.matches }}</b>IPL M</span><span><b>{{ p.runs }}</b>Runs</span><span><b>{{ p.wickets }}</b>Wkts</span></div>
        } @else {
          <div class="nums"><span>No IPL matches yet</span></div>
        }
      </div>
    </a>
  `,
})
export class PlayerCard {
  readonly player = input.required<PlayerCardData>();
  readonly failed = signal(false);
  readonly icon = computed(() => roleIcon(this.player().roleCode, this.player().isWicketKeeper));
  readonly initials = computed(() =>
    this.player().name.replace(/\(.*\)/, '').split(/\s+/).filter(Boolean).map((w) => w[0]).slice(0, 2).join('').toUpperCase());
}
