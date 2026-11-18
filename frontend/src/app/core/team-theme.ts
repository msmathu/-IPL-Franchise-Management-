import { Injectable, effect, inject } from '@angular/core';
import { Auth } from './auth';

const DEFAULT = { primary: '#2563eb', secondary: '#0ea5e9' };

function rgb(hex: string): [number, number, number] {
  const h = /^#?([0-9a-f]{6})$/i.exec(hex.trim())?.[1] ?? '2563eb';
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)) as [number, number, number];
}

function luminance(hex: string): number {
  const [r, g, b] = rgb(hex).map((v) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function mix(hex: string, with_: string, amount: number): string {
  const a = rgb(hex);
  const b = rgb(with_);
  return '#' + a.map((v, i) => Math.round(v + (b[i] - v) * amount).toString(16).padStart(2, '0')).join('');
}

/** Text colour that reads well on the given background. */
export function inkFor(hex: string): string {
  return luminance(hex) > 0.4 ? '#14171f' : '#ffffff';
}

/**
 * CSS custom properties for a team: use as [style] on any element to paint it in that team's colours
 * (the whole app uses the signed-in franchise's colours; cards and headers of other teams use their own).
 */
export function teamVars(primary?: string | null, secondary?: string | null): Record<string, string> {
  const p = primary || DEFAULT.primary;
  const s = secondary || mix(p, '#000000', 0.35);
  // Accent used for text and controls on the dark background: lift dark team colours so they stay readable.
  const accent = luminance(p) < 0.18 ? mix(p, '#ffffff', 0.45) : p;
  const [r, g, b] = rgb(p);
  return {
    '--team': p,
    '--team-2': s,
    '--team-ink': inkFor(p),
    '--team-2-ink': inkFor(s),
    '--team-soft': `rgba(${r}, ${g}, ${b}, 0.14)`,
    '--team-line': `rgba(${r}, ${g}, ${b}, 0.45)`,
    '--team-accent': accent,
  };
}

/** Paints the whole app (header, navigation, buttons, tabs, links) in the active franchise's colours. */
@Injectable({ providedIn: 'root' })
export class TeamTheme {
  private readonly auth = inject(Auth);

  constructor() {
    effect(() => {
      const f = this.auth.activeFranchise() as { primaryColor?: string; secondaryColor?: string } | null;
      const vars = teamVars(f?.primaryColor ?? DEFAULT.primary, f?.secondaryColor ?? DEFAULT.secondary);
      const root = document.documentElement.style;
      for (const [k, v] of Object.entries(vars)) root.setProperty(k, v);
      const accent = vars['--team-accent'];
      root.setProperty('--accent', accent);
      // Angular Material system colours: flat buttons, tabs, toggles, checkboxes, progress.
      root.setProperty('--mat-sys-primary', accent);
      root.setProperty('--mat-sys-on-primary', inkFor(accent));
      root.setProperty('--mat-sys-primary-container', vars['--team-soft']);
      document.documentElement.dataset['team'] = f ? 'on' : 'off';
    });
  }
}
