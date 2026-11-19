import { Component, Injectable, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../core/api';
import { Auth } from '../core/auth';
import { Monogram, PlayerAvatar } from './ui';

export interface TeamInfo { id: string; code: string; name: string; primaryColor: string; secondaryColor?: string; logoUrl?: string | null }

/** Historical names of current franchises (results and records use the name of the time). */
const FORMER_NAMES: Record<string, string> = {
  'royal challengers bangalore': 'RCB', 'delhi daredevils': 'DC', 'kings xi punjab': 'PBKS',
};

/**
 * Player photos and team badges, loaded once per session, so any screen can show a face or badge next to a name
 * without every API response carrying image data.
 */
@Injectable({ providedIn: 'root' })
export class Media {
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  private readonly photos = signal<Record<string, string>>({});
  private readonly teams = signal<TeamInfo[]>([]);
  private loaded = false;

  private ensure(): void {
    if (this.loaded || !this.auth.user()) return;
    this.loaded = true;
    this.api.get<Record<string, string>>('players/photo-map').subscribe({ next: (m) => this.photos.set(m ?? {}), error: () => (this.loaded = false) });
    this.api.get<TeamInfo[]>('franchises').subscribe({ next: (t) => this.teams.set(t ?? []), error: () => {} });
  }

  /** Re-read photos (after a photo import or a manual change). */
  refresh(): void {
    this.loaded = false;
    this.ensure();
  }

  photo(playerId: string | null | undefined): string | null {
    this.ensure();
    return playerId ? (this.photos()[playerId] ?? this.photos()[playerId.toLowerCase()] ?? null) : null;
  }

  team(key: string | null | undefined): TeamInfo | null {
    this.ensure();
    if (!key) return null;
    const k = key.toLowerCase();
    const code = FORMER_NAMES[k];
    return this.teams().find((t) => t.id === key || t.code.toLowerCase() === k || t.name.toLowerCase() === k || (code && t.code === code)) ?? null;
  }
}

/** A player's face (licensed photo, or initials) followed by the name; links to the profile when an id is known. */
@Component({
  selector: 'app-pname',
  imports: [PlayerAvatar, RouterLink],
  template: `<span class="pname" [class.sm]="size() <= 22">
    <app-avatar [name]="name()" [photoUrl]="photoUrl()" [color]="color()" [size]="size()" />
    @if (link() && id()) { <a [routerLink]="['/players', id()]" (click)="$event.stopPropagation()">{{ name() }}</a> } @else { <span>{{ name() }}</span> }
  </span>`,
})
export class PlayerName {
  private readonly media = inject(Media);
  readonly id = input<string | null | undefined>(null);
  readonly name = input.required<string>();
  readonly size = input(26);
  readonly link = input(true);
  /** Team (id, code or name) for the initials colour when there is no photo. */
  readonly team = input<string | null | undefined>(null);
  readonly photoUrl = computed(() => this.media.photo(this.id()));
  readonly color = computed(() => this.media.team(this.team())?.primaryColor ?? '#334155');
}

/** A team's badge (logo if licensed, otherwise its code on the team colour), optionally followed by the name. */
@Component({
  selector: 'app-team',
  imports: [Monogram],
  template: `<span class="tname">
    <app-monogram [name]="info()?.name ?? key() ?? ''" [code]="info()?.code ?? null" [logoUrl]="info()?.logoUrl ?? null" [color]="info()?.primaryColor ?? '#475569'" [size]="size()" />
    @if (showName()) { <span>{{ label() ?? info()?.name ?? key() }}</span> }
  </span>`,
})
export class TeamName {
  private readonly media = inject(Media);
  /** Franchise id, code or (current or former) name. */
  readonly key = input<string | null | undefined>(null);
  readonly label = input<string | null | undefined>(null);
  readonly size = input(22);
  readonly showName = input(true);
  readonly info = computed(() => this.media.team(this.key()));
}
