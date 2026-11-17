import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { rxResource } from '../../core/resource';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { P, Toast, WordsPipe } from '../../core/services';
import { PageHeader, StateView, StatusChip } from '../../shared/ui';

@Component({
  selector: 'app-medical',
  imports: [PageHeader, StateView, StatusChip, RouterLink, WordsPipe, DatePipe, ReactiveFormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatProgressBarModule],
  template: `
    <div class="page">
      <app-page-header title="Medical & availability" [subtitle]="auth.has(P.medicalRead) ? 'Confidential medical records — authorised medical staff only' : 'Squad availability (medical details are restricted)'" />
      <section class="card"><h2>Squad availability</h2>
        <app-state [loading]="availability.isLoading()" [error]="availability.error()" (retry)="availability.reload()">
          <table class="data"><tr><th>Player</th><th>Role</th><th>Status</th><th>Note</th><th>Expected back</th><th>Updated</th></tr>
            @for (a of availability.value() ?? []; track a.playerId) {
              <tr><td><a [routerLink]="['/players', a.playerId]">{{ a.player }}</a>@if (a.isOverseas) { <span class="tag os">OS</span> }</td><td>{{ a.role }}</td>
                <td><app-chip [text]="a.status | words" /></td><td class="small">{{ a.publicNote }}</td><td>{{ a.expectedReturn | date: 'd MMM' }}</td><td class="small muted">{{ a.updatedAt | date: 'd MMM' }}</td></tr>
            }</table>
        </app-state>
      </section>
      @if (auth.has(P.medicalRead)) {
        @if (auth.has(P.medicalManage)) {
          <section class="card"><h2>{{ editing() ? 'Update medical record' : 'Record an injury' }}</h2>
            <form [formGroup]="form" (ngSubmit)="save()" class="form-grid">
              <mat-form-field><mat-label>Player</mat-label><mat-select formControlName="playerId">@for (a of availability.value() ?? []; track a.playerId) { <mat-option [value]="a.playerId">{{ a.player }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field><mat-label>Condition</mat-label><input matInput formControlName="condition" /></mat-form-field>
              <mat-form-field><mat-label>Body part</mat-label><input matInput formControlName="bodyPart" /></mat-form-field>
              <mat-form-field><mat-label>Severity</mat-label><mat-select formControlName="severity">@for (s of ['Minor', 'Moderate', 'Major', 'SeasonEnding']; track s) { <mat-option [value]="s">{{ s | words }}</mat-option> }</mat-select></mat-form-field>
              <mat-form-field><mat-label>Injury date</mat-label><input matInput type="date" formControlName="injuryDate" /></mat-form-field>
              <mat-form-field><mat-label>Expected return</mat-label><input matInput type="date" formControlName="expectedReturn" /></mat-form-field>
              <mat-form-field><mat-label>Recovery %</mat-label><input matInput type="number" min="0" max="100" formControlName="recoveryProgress" /></mat-form-field>
              <mat-form-field><mat-label>Public note (no medical detail)</mat-label><input matInput formControlName="publicNote" /></mat-form-field>
              <mat-form-field class="full"><mat-label>Medical assessment (confidential)</mat-label><textarea matInput rows="2" formControlName="assessment"></textarea></mat-form-field>
              <div class="row"><button mat-flat-button [disabled]="form.invalid">Save</button>@if (editing()) { <button mat-button type="button" (click)="cancel()">Cancel</button> }</div>
            </form>
          </section>
        }
        <section class="card"><h2>Medical records</h2>
          <app-state [loading]="records.isLoading()" [error]="records.error()" [empty]="!records.value()?.length" emptyText="No medical records." (retry)="records.reload()">
            @for (r of records.value() ?? []; track r.id) {
              <div class="record">
                <div class="row"><strong>{{ r.player }}</strong><span>{{ r.condition }} · {{ r.bodyPart }}</span><app-chip [text]="r.severity | words" />
                  <span class="spacer"></span>@if (r.fitnessCleared) { <app-chip text="Cleared" /> } @else { <span class="small">back ~{{ r.expectedReturn | date: 'd MMM' }}</span> }</div>
                <mat-progress-bar mode="determinate" [value]="r.recoveryProgress" />
                <p class="small pre">{{ r.assessment }}</p>
                <div class="row small muted">Injured {{ r.injuryDate | date: 'd MMM yyyy' }} · recovery {{ r.recoveryProgress }}%
                  @if (auth.has(P.medicalManage) && !r.fitnessCleared) { <span class="spacer"></span><button mat-button (click)="edit(r)">Update</button><button mat-stroked-button (click)="clear(r)">Grant fitness clearance</button> }</div>
              </div>
            }
          </app-state>
          <p class="muted small">Opening this list is recorded in the audit log.</p>
        </section>
      }
    </div>
  `,
  styles: `.record { border-bottom: 1px solid var(--line); padding: 12px 0; display: flex; flex-direction: column; gap: 6px; }`,
})
export class MedicalPage {
  readonly auth = inject(Auth);
  readonly P = P;
  private readonly api = inject(Api);
  private readonly toast = inject(Toast);
  readonly availability = rxResource({ stream: () => this.api.get<any[]>('medical/availability') });
  readonly records = rxResource({ stream: () => (this.auth.has(P.medicalRead) ? this.api.get<any[]>('medical/records') : of([])) });
  readonly editing = signal<string | null>(null);
  readonly form = inject(FormBuilder).nonNullable.group({
    playerId: ['', Validators.required], condition: ['', Validators.required], bodyPart: [''], severity: ['Minor'], assessment: [''],
    injuryDate: [new Date().toISOString().slice(0, 10), Validators.required], expectedReturn: [''], recoveryProgress: [0], publicNote: [''],
  });

  edit(r: any): void {
    this.editing.set(r.id);
    this.form.patchValue({ ...r, expectedReturn: r.expectedReturn ?? '', publicNote: '' });
  }

  cancel(): void {
    this.editing.set(null);
    this.form.reset();
  }

  save(): void {
    const v = this.form.getRawValue();
    const body = { ...v, expectedReturn: v.expectedReturn || null, publicNote: v.publicNote || null };
    const id = this.editing();
    (id ? this.api.put(`medical/records/${id}`, body) : this.api.post('medical/records', body)).subscribe({
      next: () => { this.toast.ok('Medical record saved; availability set to Injured.'); this.cancel(); this.records.reload(); this.availability.reload(); },
      error: (e) => this.toast.error(e),
    });
  }

  clear(r: any): void {
    this.api.post(`medical/records/${r.id}/clear`).subscribe({
      next: () => { this.toast.ok(`${r.player} cleared.`); this.records.reload(); this.availability.reload(); },
      error: (e) => this.toast.error(e),
    });
  }
}
