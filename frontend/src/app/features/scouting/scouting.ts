import { DatePipe } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSliderModule } from '@angular/material/slider';
import { MatTabsModule } from '@angular/material/tabs';
import { Router, RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { Api, Paged } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Toast, WordsPipe } from '../../core/services';
import { PageHeader, StateView, StatusChip } from '../../shared/ui';
import { PlayerPicker } from '../players/player-profile';

const RATINGS = ['techniqueRating', 'temperamentRating', 'fitnessRating', 'potentialRating', 'overallRating'] as const;

@Component({
  selector: 'app-scouting',
  imports: [PageHeader, StateView, StatusChip, PlayerPicker, RouterLink, WordsPipe, DatePipe, FormsModule, ReactiveFormsModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatSliderModule, MatTabsModule],
  template: `
    <div class="page">
      <app-page-header title="Scouting centre" subtitle="Assignments, reports and talent discovery">
        @if (auth.has(P.scoutingReport)) { <button mat-flat-button (click)="writing.set(!writing())"><mat-icon>edit_note</mat-icon>New report</button> }
      </app-page-header>
      @if (writing()) {
        <section class="card"><h2>Scouting report</h2>
          <form [formGroup]="form" class="stack">
            <div class="row">
              @if (reportPlayer(); as rp) { <span class="chip">{{ rp.fullName }}</span><button mat-button type="button" (click)="reportPlayer.set(null)">Change</button> }
              @else { <app-player-picker label="Player observed" (picked)="reportPlayer.set($event)" /> }
              <mat-form-field class="wide"><mat-label>Match or session observed</mat-label><input matInput formControlName="matchObserved" /></mat-form-field>
              <mat-form-field><mat-label>Date</mat-label><input matInput type="date" formControlName="observedOn" /></mat-form-field>
              <mat-form-field><mat-label>Assignment</mat-label><mat-select formControlName="assignmentId"><mat-option [value]="null">None</mat-option>
                @for (a of assignments.value() ?? []; track a.id) { <mat-option [value]="a.id">{{ a.title }}</mat-option> }</mat-select></mat-form-field>
            </div>
            <mat-form-field class="full"><mat-label>Strengths</mat-label><textarea matInput rows="2" formControlName="strengths"></textarea></mat-form-field>
            <mat-form-field class="full"><mat-label>Weaknesses</mat-label><textarea matInput rows="2" formControlName="weaknesses"></textarea></mat-form-field>
            <mat-form-field class="full"><mat-label>Observations</mat-label><textarea matInput rows="3" formControlName="observations"></textarea></mat-form-field>
            <div class="row">
              @for (r of ratings; track r) {
                <label class="rating">{{ r.replace('Rating', '') | words }}: <strong>{{ form.get(r)?.value }}</strong>
                  <mat-slider min="1" max="10" step="1"><input matSliderThumb [formControlName]="r" /></mat-slider></label>
              }
              <mat-form-field><mat-label>Recommendation</mat-label><mat-select formControlName="recommendation">
                @for (r of ['Sign', 'StrongMonitor', 'Monitor', 'Pass']; track r) { <mat-option [value]="r">{{ r | words }}</mat-option> }</mat-select></mat-form-field>
            </div>
            <div class="row">
              <button mat-stroked-button type="button" [disabled]="!reportPlayer() || form.invalid" (click)="saveReport(false)">Save draft</button>
              <button mat-flat-button type="button" [disabled]="!reportPlayer() || form.invalid" (click)="saveReport(true)">Submit report</button>
            </div>
            <p class="muted small">Record only what you observed. Ratings are your professional judgement (1–10).</p>
          </form>
        </section>
      }
      <mat-tab-group>
        <mat-tab label="Reports">
          <section class="card tabpad">
            <app-state [loading]="reports.isLoading()" [error]="reports.error()" [empty]="reports.value()?.items?.length === 0" emptyText="No reports yet." (retry)="reports.reload()">
              <table class="data"><tr><th>Player</th><th>Role</th><th class="num">Age</th><th>Scout</th><th>Observed</th><th>Match</th><th class="num">Rating</th><th>Recommendation</th><th>Status</th><th class="num">Docs</th></tr>
                @for (r of reports.value()?.items ?? []; track r.id) {
                  <tr class="clickable" (click)="router.navigate(['/scouting/reports', r.id])"><td>{{ r.playerName }}</td><td>{{ r.role }}</td><td class="num">{{ r.age }}</td><td>{{ r.scout }}</td>
                    <td>{{ r.observedOn | date: 'd MMM yyyy' }}</td><td class="small">{{ r.matchObserved }}</td><td class="num">{{ r.overallRating }}/10</td>
                    <td><app-chip [text]="r.recommendation | words" /></td><td><app-chip [text]="r.status" /></td><td class="num">{{ r.documents }}</td></tr>
                }</table>
            </app-state>
          </section>
        </mat-tab>
        <mat-tab label="Assignments">
          <section class="card tabpad">
            @if (auth.has(P.scoutingManage)) {
              <div class="row">
                <mat-form-field class="wide"><mat-label>Title</mat-label><input matInput [(ngModel)]="aTitle" /></mat-form-field>
                <mat-form-field><mat-label>Scout</mat-label><mat-select [(ngModel)]="aScout">@for (s of scouts.value() ?? []; track s.id) { <mat-option [value]="s.id">{{ s.fullName }}</mat-option> }</mat-select></mat-form-field>
                <mat-form-field><mat-label>Due</mat-label><input matInput type="date" [(ngModel)]="aDue" /></mat-form-field>
                @if (aPlayer(); as ap) { <span class="chip">{{ ap.fullName }}</span> } @else { <app-player-picker label="Player (optional)" (picked)="aPlayer.set($event)" /> }
                <button mat-flat-button [disabled]="!aTitle || !aScout" (click)="createAssignment()">Assign</button>
              </div>
            }
            <app-state [loading]="assignments.isLoading()" [error]="assignments.error()" [empty]="!assignments.value()?.length" emptyText="No assignments." (retry)="assignments.reload()">
              @for (a of assignments.value() ?? []; track a.id) {
                <div class="list-item"><strong>{{ a.title }}</strong>@if (a.playerName) { <span class="muted">· {{ a.playerName }}</span> }<span class="spacer"></span>
                  <span class="small">{{ a.assignedTo }}</span><span class="small muted">{{ a.dueDate ? 'due ' + (a.dueDate | date: 'd MMM') : '' }} · {{ a.reports }} report(s)</span><app-chip [text]="a.status" /></div>
              }
            </app-state>
          </section>
        </mat-tab>
        <mat-tab label="Discover emerging players">
          <section class="card tabpad">
            <div class="row">
              <mat-form-field><mat-label>Max age</mat-label><input matInput type="number" [ngModel]="maxAge()" (ngModelChange)="maxAge.set(+$event)" /></mat-form-field>
              <mat-form-field><mat-label>Category</mat-label><mat-select [ngModel]="category()" (ngModelChange)="category.set($event)"><mat-option [value]="null">Any</mat-option>
                @for (c of categories; track c) { <mat-option [value]="c">{{ c }}</mat-option> }</mat-select></mat-form-field>
            </div>
            <app-state [loading]="emerging.isLoading()" [error]="emerging.error()" [empty]="!emerging.value()?.length" (retry)="emerging.reload()">
              <table class="data"><tr><th>Player</th><th class="num">Age</th><th>Role</th><th>Country</th><th>Team</th><th class="num">Rating</th><th class="num">Potential</th><th class="num">Form</th><th>Confidence</th><th class="num">Our reports</th></tr>
                @for (e of emerging.value() ?? []; track e.playerId) {
                  <tr><td><a [routerLink]="['/players', e.playerId]">{{ e.name }}</a></td><td class="num">{{ e.age }}</td><td>{{ e.role }}</td><td>{{ e.country }}</td><td>{{ e.franchise ?? 'Unsigned' }}</td>
                    <td class="num">{{ e.rating }}</td><td class="num">{{ e.potentialScore }}</td><td class="num">{{ e.formScore }}</td><td>{{ e.confidence }}</td><td class="num">{{ e.scoutReports }}</td></tr>
                }</table>
              <p class="disclaimer">Model-generated ranking from recorded statistics only. Open a player's profile to see exactly how each rating was calculated.</p>
            </app-state>
          </section>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: `.tabpad { margin-top: 16px; } .rating { display: flex; flex-direction: column; font-size: 12px; }`,
})
export class ScoutingPage {
  readonly auth = inject(Auth);
  readonly P = P;
  readonly router = inject(Router);
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly ratings = RATINGS;
  readonly categories = ['Batsman', 'Bowler', 'All-rounder', 'Wicketkeeper', 'Fast bowler', 'Spin bowler', 'Leg spinner', 'Off spinner', 'Left-arm spinner'];
  readonly writing = signal(false);
  readonly reportPlayer = signal<any>(null);
  readonly aPlayer = signal<any>(null);
  readonly maxAge = signal(23);
  readonly category = signal<string | null>(null);
  readonly reports = rxResource({ stream: () => this.api.get<Paged<any>>('scouting/reports', { pageSize: 50 }) });
  readonly assignments = rxResource({ stream: () => this.api.get<any[]>('scouting/assignments') });
  readonly emerging = rxResource({ params: () => ({ maxAge: this.maxAge(), category: this.category() }), stream: ({ params }) => this.api.get<any[]>('scouting/emerging', params) });
  readonly scouts = rxResource({ stream: () => (this.auth.has(P.scoutingManage) ? this.api.get<any[]>('scouting/scouts') : of([])) });
  aTitle = '';
  aScout = '';
  aDue = '';
  readonly form = inject(FormBuilder).nonNullable.group({
    matchObserved: ['', Validators.required], observedOn: [new Date().toISOString().slice(0, 10), Validators.required], assignmentId: [null as string | null],
    strengths: [''], weaknesses: [''], observations: [''], techniqueRating: [6], temperamentRating: [6], fitnessRating: [6], potentialRating: [6], overallRating: [6],
    recommendation: ['Monitor'],
  });

  saveReport(submit: boolean): void {
    this.api.post<any>('scouting/reports', { ...this.form.getRawValue(), playerId: this.reportPlayer().id, submit }).subscribe({
      next: (r) => { this.toast.ok(submit ? 'Report submitted.' : 'Draft saved.'); void this.router.navigate(['/scouting/reports', r.id]); },
      error: (e) => this.toast.error(e),
    });
  }

  createAssignment(): void {
    this.api.post('scouting/assignments', { playerId: this.aPlayer()?.id ?? null, title: this.aTitle, description: '', assignedToUserId: this.aScout, dueDate: this.aDue || null, status: 'Open' }).subscribe({
      next: () => { this.toast.ok('Assignment created.'); this.aTitle = ''; this.aPlayer.set(null); this.assignments.reload(); },
      error: (e) => this.toast.error(e),
    });
  }
}

@Component({
  selector: 'app-scouting-report',
  imports: [PageHeader, StateView, StatusChip, RouterLink, WordsPipe, DatePipe, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule],
  template: `
    <div class="page">
      <app-state [loading]="res.isLoading()" [error]="res.error()" (retry)="res.reload()">
        @if (res.value(); as r) {
          <div class="row"><app-page-header [title]="'Report: ' + r.playerName" [subtitle]="r.matchObserved + ' · ' + (r.observedOn | date: 'd MMM yyyy') + ' · by ' + r.scout" />
            <span class="spacer"></span><app-chip [text]="r.status" /><app-chip [text]="r.recommendation | words" /></div>
          <div class="grid cols-2">
            <section class="card"><h2>Assessment</h2>
              <h3>Strengths</h3><p class="pre">{{ r.strengths || '—' }}</p>
              <h3>Weaknesses</h3><p class="pre">{{ r.weaknesses || '—' }}</p>
              <h3>Observations</h3><p class="pre">{{ r.observations || '—' }}</p>
              <a [routerLink]="['/players', r.playerId]">Open player profile</a></section>
            <section class="card"><h2>Ratings</h2>
              @for (k of keys; track k.key) { <div class="list-item"><span>{{ k.label }}</span><span class="spacer"></span><div class="bar" style="width: 140px"><span [style.width.%]="r[k.key] * 10"></span></div><strong>{{ r[k.key] }}</strong></div> }
              @if (r.reviewedBy) { <p class="small">Reviewed by {{ r.reviewedBy }} on {{ r.reviewedAt | date: 'd MMM' }}: “{{ r.reviewComment }}”</p> }
              @if (r.canReview) {
                <div class="row"><mat-form-field class="wide"><mat-label>Review comment</mat-label><input matInput [(ngModel)]="comment" /></mat-form-field><button mat-flat-button (click)="review()">Mark reviewed</button></div>
              }
            </section>
          </div>
          <section class="card"><h2>Documents</h2>
            @for (d of r.documents; track d.id) {
              <div class="list-item"><mat-icon>description</mat-icon><a href="" (click)="$event.preventDefault(); download(d)">{{ d.fileName }}</a><span class="spacer"></span><span class="small muted">{{ (d.sizeBytes / 1024).toFixed(0) }} KB · {{ d.uploadedAt | date: 'd MMM' }}</span></div>
            } @empty { <p class="muted">No documents.</p> }
            @if (r.canEdit || r.canReview) {
              <button mat-stroked-button (click)="file.click()"><mat-icon>attach_file</mat-icon>Attach file</button><input #file type="file" hidden accept=".pdf,.png,.jpg,.jpeg,.docx,.xlsx,.csv,.txt" (change)="upload(file)" />
              <span class="muted small"> PDF, images, Word, Excel, CSV or text · max 5 MB · confidential to your franchise</span>
            }
          </section>
        }
      </app-state>
    </div>
  `,
})
export class ReportPage {
  readonly id = input.required<string>();
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly res = rxResource({ params: () => this.id(), stream: ({ params }) => this.api.get<any>(`scouting/reports/${params}`) });
  readonly keys = [
    { key: 'techniqueRating', label: 'Technique' }, { key: 'temperamentRating', label: 'Temperament' }, { key: 'fitnessRating', label: 'Fitness' },
    { key: 'potentialRating', label: 'Potential' }, { key: 'overallRating', label: 'Overall' },
  ];
  comment = '';

  review(): void {
    this.api.post(`scouting/reports/${this.id()}/review`, { comment: this.comment || null }).subscribe({ next: () => this.res.reload(), error: (e) => this.toast.error(e) });
  }

  upload(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) return this.toast.error(new Error('Files can be at most 5 MB.'));
    this.api.upload(`scouting/reports/${this.id()}/documents`, file).subscribe({ next: () => { this.toast.ok('Uploaded.'); this.res.reload(); }, error: (e) => this.toast.error(e) });
  }

  download(d: any): void {
    this.api.download(`scouting/reports/${this.id()}/documents/${d.id}`).subscribe({ error: (e) => this.toast.error(e) });
  }
}

