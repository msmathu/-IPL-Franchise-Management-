import { Signal, computed } from '@angular/core';
import { rxResource as ngRxResource } from '@angular/core/rxjs-interop';
import { Observable } from 'rxjs';

export interface SafeResource<T> {
  /** The loaded value, or undefined while loading or after an error (never throws). */
  readonly value: Signal<T | undefined>;
  readonly isLoading: Signal<boolean>;
  readonly error: Signal<unknown>;
  hasValue(): boolean;
  reload(): boolean;
  set(value: T): void;
}

/**
 * Same API as Angular's rxResource, but reading value() after a failed request returns undefined instead of
 * throwing, so a failed call shows the error state rather than breaking the page.
 */
export function rxResource<T, P = unknown>(options: {
  params?: () => P;
  stream: (ctx: { params: P }) => Observable<T>;
}): SafeResource<T> {
  const r = ngRxResource<T, P>(options as never);
  return {
    value: computed(() => (r.hasValue() ? r.value() : undefined)),
    isLoading: r.isLoading,
    error: r.error,
    hasValue: () => r.hasValue(),
    reload: () => r.reload(),
    set: (value: T) => r.set(value),
  };
}
