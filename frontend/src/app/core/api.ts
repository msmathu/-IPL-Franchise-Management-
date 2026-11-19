import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

/** Server envelope: { success, data, error }. */
export interface ApiEnvelope<T> {
  success: boolean;
  data: T;
  error?: { code: string; message: string; field?: string; traceId?: string };
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

export const API_BASE = '/api/v1';

type Query = Record<string, string | number | boolean | null | undefined | string[]>;

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  get<T>(path: string, query?: Query): Observable<T> {
    return this.http.get<ApiEnvelope<T>>(`${API_BASE}/${path}`, { params: toParams(query) }).pipe(map((r) => r?.data as T));
  }

  post<T>(path: string, body: unknown = {}): Observable<T> {
    return this.http.post<ApiEnvelope<T>>(`${API_BASE}/${path}`, body).pipe(map((r) => r?.data as T));
  }

  put<T>(path: string, body: unknown): Observable<T> {
    return this.http.put<ApiEnvelope<T>>(`${API_BASE}/${path}`, body).pipe(map((r) => r?.data as T));
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<ApiEnvelope<T>>(`${API_BASE}/${path}`).pipe(map((r) => r?.data as T));
  }

  upload<T>(path: string, file: File, fields: Record<string, string> = {}): Observable<T> {
    const form = new FormData();
    form.append('file', file);
    for (const [k, v] of Object.entries(fields)) form.append(k, v);
    return this.http.post<ApiEnvelope<T>>(`${API_BASE}/${path}`, form).pipe(map((r) => r.data));
  }

  /** Downloads a file response and saves it with the server's file name. */
  download(path: string, query?: Query): Observable<void> {
    return this.http.get(`${API_BASE}/${path}`, { params: toParams(query), responseType: 'blob', observe: 'response' }).pipe(
      map((res) => {
        const disposition = res.headers.get('content-disposition') ?? '';
        const name = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(disposition)?.[1] ?? 'download';
        const url = URL.createObjectURL(res.body!);
        const a = document.createElement('a');
        a.href = url;
        a.download = decodeURIComponent(name);
        a.click();
        URL.revokeObjectURL(url);
      }),
    );
  }
}

function toParams(query?: Query): HttpParams {
  let params = new HttpParams();
  for (const [k, v] of Object.entries(query ?? {})) {
    if (v === null || v === undefined || v === '') continue;
    if (Array.isArray(v)) v.forEach((x) => (params = params.append(k, x)));
    else params = params.set(k, String(v));
  }
  return params;
}

/** A readable message from any HTTP error (uses the server's message when present). */
export function errorMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    const env = err.error as ApiEnvelope<unknown> | null;
    if (env?.error?.message) return env.error.message;
    if (err.status === 0) return 'Cannot reach the server. Check your connection.';
    if (err.status === 403) return 'You do not have permission to do that.';
    if (err.status === 404) return 'Not found.';
    if (err.status === 429) return 'Too many requests. Please wait a moment.';
    return `Something went wrong (${err.status}).`;
  }
  return err instanceof Error ? err.message : 'Something went wrong.';
}
