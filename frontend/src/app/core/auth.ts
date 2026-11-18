import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Observable, catchError, finalize, firstValueFrom, map, of, shareReplay, switchMap, throwError } from 'rxjs';
import { API_BASE, ApiEnvelope } from './api';

export interface UserFranchise {
  id: string;
  name: string;
  shortName: string;
  code: string;
  primaryColor: string;
  secondaryColor: string;
  roles: string[];
}

export interface Me {
  id: string;
  email: string;
  fullName: string;
  activeFranchiseId: string | null;
  isLeagueWide: boolean;
  leagueRoles: string[];
  franchises: UserFranchise[];
  permissions: string[];
}

interface LoginResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: Me;
}

/**
 * Access token lives only in memory; the refresh token is an HttpOnly cookie the browser sends to /api/v1/auth.
 * On reload the app silently refreshes; on a 401 one shared refresh is attempted before giving up.
 */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private refreshing$: Observable<string | null> | null = null;
  private timer?: ReturnType<typeof setTimeout>;

  readonly user = signal<Me | null>(null);
  readonly token = signal<string | null>(null);
  readonly ready = signal(false);
  readonly isLoggedIn = computed(() => !!this.user());
  readonly permissions = computed(() => new Set(this.user()?.permissions ?? []));
  readonly activeFranchise = computed(() => {
    const u = this.user();
    return u?.franchises.find((f) => f.id === u.activeFranchiseId) ?? null;
  });

  has(...perms: string[]): boolean {
    const set = this.permissions();
    return perms.some((p) => set.has(p));
  }

  /** Restores the session from the refresh cookie on start-up. */
  async init(): Promise<void> {
    try {
      await firstValueFrom(this.refresh());
    } finally {
      this.ready.set(true);
    }
  }

  login(email: string, password: string): Observable<Me> {
    return this.http.post<ApiEnvelope<LoginResponse>>(`${API_BASE}/auth/login`, { email, password }).pipe(map((r) => this.accept(r.data)));
  }

  /** One refresh at a time; concurrent callers share the result (avoids refresh-token reuse detection). */
  refresh(): Observable<string | null> {
    this.refreshing$ ??= this.http.post<ApiEnvelope<LoginResponse>>(`${API_BASE}/auth/refresh`, {}).pipe(
      map((r) => this.accept(r.data) && r.data.accessToken),
      catchError(() => {
        this.clear();
        return of(null);
      }),
      finalize(() => (this.refreshing$ = null)),
      shareReplay(1),
    );
    return this.refreshing$;
  }

  switchFranchise(franchiseId: string): Observable<Me> {
    return this.http.post<ApiEnvelope<LoginResponse>>(`${API_BASE}/auth/switch-franchise`, { franchiseId }).pipe(map((r) => this.accept(r.data)));
  }

  logout(): void {
    this.http.post(`${API_BASE}/auth/logout`, {}).subscribe({ complete: () => this.afterLogout(), error: () => this.afterLogout() });
  }

  private afterLogout(): void {
    this.clear();
    void this.router.navigate(['/login']);
  }

  private accept(r: LoginResponse): Me {
    this.token.set(r.accessToken);
    this.user.set(r.user);
    clearTimeout(this.timer);
    // Refresh a minute before the access token expires.
    const ms = new Date(r.accessTokenExpiresAt).getTime() - Date.now() - 60_000;
    this.timer = setTimeout(() => this.refresh().subscribe(), Math.max(10_000, ms));
    return r.user;
  }

  private clear(): void {
    clearTimeout(this.timer);
    this.token.set(null);
    this.user.set(null);
  }
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const isAuthCall = req.url.includes('/auth/login') || req.url.includes('/auth/refresh') || req.url.includes('/auth/logout');
  const withToken = (r: HttpRequest<unknown>, token: string | null) =>
    token && !isAuthCall ? r.clone({ setHeaders: { Authorization: `Bearer ${token}` }, withCredentials: true }) : r.clone({ withCredentials: true });

  return next(withToken(req, auth.token())).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && !isAuthCall && auth.token()) {
        return auth.refresh().pipe(
          switchMap((token) => (token ? next(withToken(req, token)) : throwError(() => err))),
        );
      }
      return throwError(() => err);
    }),
  );
};

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(Auth);
  const router = inject(Router);
  return auth.isLoggedIn() ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Route guard: requires at least one of the permissions (the server checks again). */
export function permissionGuard(...perms: string[]): CanActivateFn {
  return () => {
    const auth = inject(Auth);
    const router = inject(Router);
    if (!auth.isLoggedIn()) return router.createUrlTree(['/login']);
    return perms.length === 0 || auth.has(...perms) ? true : router.createUrlTree(['/forbidden']);
  };
}
