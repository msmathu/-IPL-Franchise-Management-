import { Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { Api, errorMessage } from '../../core/api';
import { Auth } from '../../core/auth';

const passwordPattern = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{10,}$/;

const shellStyles = `
  .auth { min-height: 100vh; display: grid; place-items: center; padding: 24px;
    background: radial-gradient(circle at 20% 10%, rgba(37,99,235,0.35), transparent 40%), radial-gradient(circle at 90% 80%, rgba(245,158,11,0.18), transparent 40%), var(--bg); }
  .panel { width: min(420px, 100%); background: var(--card); border: 1px solid var(--line); border-radius: 18px; padding: 28px; display: flex; flex-direction: column; gap: 14px; }
  .panel h1 { font-size: 22px; }
  .brand { display: flex; gap: 10px; align-items: center; color: var(--accent); }
  .err { color: var(--bad); font-size: 13px; }
  .ok { color: var(--good); font-size: 13px; }
  form { display: flex; flex-direction: column; gap: 12px; }
  .demo { font-size: 12px; color: var(--muted); border-top: 1px solid var(--line); padding-top: 12px; }
  .demo code { color: var(--text); }
`;

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, RouterLink],
  template: `
    <main class="auth">
      <section class="panel">
        <div class="brand"><mat-icon>sports_cricket</mat-icon><strong>Franchise HQ</strong></div>
        <h1>Sign in</h1>
        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field><mat-label>E-mail</mat-label><input matInput formControlName="email" type="email" autocomplete="username" />
            @if (form.controls.email.touched && form.controls.email.invalid) { <mat-error>Enter a valid e-mail.</mat-error> }</mat-form-field>
          <mat-form-field><mat-label>Password</mat-label><input matInput formControlName="password" [type]="show() ? 'text' : 'password'" autocomplete="current-password" />
            <button mat-icon-button matSuffix type="button" (click)="show.set(!show())" aria-label="Show password"><mat-icon>{{ show() ? 'visibility_off' : 'visibility' }}</mat-icon></button>
            @if (form.controls.password.touched && form.controls.password.invalid) { <mat-error>Enter your password.</mat-error> }</mat-form-field>
          @if (error()) { <p class="err">{{ error() }}</p> }
          <button mat-flat-button type="submit" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
        </form>
        <a routerLink="/forgot-password">Forgot password?</a>
        <div class="demo">Demo accounts (password <code>Demo&#64;2026pass</code>): <code>owner.mi&#64;demo.local</code>, <code>cricket.director.mi&#64;demo.local</code>,
          <code>auction.manager.mi&#64;demo.local</code>, <code>scout.mi&#64;demo.local</code>, <code>medical.staff.mi&#64;demo.local</code>, <code>owner.csk&#64;demo.local</code>,
          <code>league&#64;demo.local</code>, <code>admin&#64;demo.local</code>.</div>
      </section>
    </main>
  `,
  styles: shellStyles,
})
export class LoginPage {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  readonly returnUrl = input<string>();
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly show = signal(false);

  constructor() {
    if (this.auth.isLoggedIn()) void this.router.navigateByUrl('/');
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => void this.router.navigateByUrl(this.returnUrl() && this.returnUrl()!.startsWith('/') ? this.returnUrl()! : '/'),
      error: (e) => {
        this.error.set(errorMessage(e));
        this.busy.set(false);
      },
    });
  }
}

@Component({
  selector: 'app-forgot',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, RouterLink],
  template: `
    <main class="auth"><section class="panel">
      <h1>Reset your password</h1>
      @if (sent()) { <p class="ok">If that e-mail has an account, we've sent a reset link. It works for 1 hour.</p> }
      @else {
        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field><mat-label>E-mail</mat-label><input matInput formControlName="email" type="email" /></mat-form-field>
          @if (error()) { <p class="err">{{ error() }}</p> }
          <button mat-flat-button [disabled]="form.invalid || busy()">Send reset link</button>
        </form>
      }
      <a routerLink="/login">Back to sign in</a>
    </section></main>
  `,
  styles: shellStyles,
})
export class ForgotPasswordPage {
  private readonly api = inject(Api);
  readonly form = inject(FormBuilder).nonNullable.group({ email: ['', [Validators.required, Validators.email]] });
  readonly sent = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  submit(): void {
    this.busy.set(true);
    this.api.post('auth/forgot-password', this.form.getRawValue()).subscribe({
      next: () => this.sent.set(true),
      error: (e) => { this.error.set(errorMessage(e)); this.busy.set(false); },
    });
  }
}

@Component({
  selector: 'app-set-password',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, RouterLink],
  template: `
    <main class="auth"><section class="panel">
      <h1>{{ invitation ? 'Set up your account' : 'Choose a new password' }}</h1>
      @if (done()) { <p class="ok">{{ done() }}</p><a routerLink="/login">Sign in</a> }
      @else {
        <form [formGroup]="form" (ngSubmit)="submit()">
          @if (invitation) { <mat-form-field><mat-label>Your name</mat-label><input matInput formControlName="fullName" /></mat-form-field> }
          <mat-form-field><mat-label>New password</mat-label><input matInput formControlName="password" type="password" autocomplete="new-password" />
            <mat-hint>At least 10 characters with upper and lower case letters and a number.</mat-hint></mat-form-field>
          <mat-form-field><mat-label>Repeat password</mat-label><input matInput formControlName="repeat" type="password" autocomplete="new-password" /></mat-form-field>
          @if (error()) { <p class="err">{{ error() }}</p> }
          <button mat-flat-button [disabled]="busy()">Save password</button>
        </form>
      }
    </section></main>
  `,
  styles: shellStyles,
})
export class ResetPasswordPage {
  private readonly api = inject(Api);
  readonly token = input<string>();
  /** Set from route data on /accept-invitation. */
  readonly invitationMode = input(false, { alias: 'invitation' });
  get invitation(): boolean { return this.invitationMode(); }
  readonly form = inject(FormBuilder).nonNullable.group({ fullName: [''], password: ['', [Validators.required, Validators.pattern(passwordPattern)]], repeat: [''] });
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  submit(): void {
    const v = this.form.getRawValue();
    if (this.form.invalid) return this.error.set('Use at least 10 characters with upper and lower case letters and a number.');
    if (v.password !== v.repeat) return this.error.set('The passwords do not match.');
    if (!this.token()) return this.error.set('This link is missing its token. Use the link from your e-mail.');
    this.busy.set(true);
    const call = this.invitation
      ? this.api.post<{ message: string }>('auth/accept-invitation', { token: this.token(), password: v.password, fullName: v.fullName || null })
      : this.api.post<{ message: string }>('auth/reset-password', { token: this.token(), newPassword: v.password });
    call.subscribe({ next: (r) => this.done.set(r.message), error: (e) => { this.error.set(errorMessage(e)); this.busy.set(false); } });
  }
}

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, MatIconModule],
  template: `<div class="page"><div class="state"><mat-icon>lock</mat-icon><h2>You don't have access to this page</h2>
    <p>Your role doesn't include this area. Ask your franchise administrator if you need it.</p><a routerLink="/dashboard">Go to dashboard</a></div></div>`,
})
export class ForbiddenPage {}

@Component({
  selector: 'app-not-found',
  imports: [RouterLink, MatIconModule],
  template: `<div class="page"><div class="state"><mat-icon>search_off</mat-icon><h2>Page not found</h2><a routerLink="/dashboard">Go to dashboard</a></div></div>`,
})
export class NotFoundPage {}
