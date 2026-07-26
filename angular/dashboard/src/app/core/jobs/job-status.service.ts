import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import {
  distinctUntilChanged,
  MonoTypeOperatorFunction,
  Observable,
  retry,
  switchMap,
  takeWhile,
  timer,
} from 'rxjs';
import { API_BASE_PATH } from '../api/api-base-path';

export interface JobStatus {
  readonly id: string;
  readonly state: string;
}

export interface ObserveJobOptions {
  /** Aborts the observation — the stream errors with an `AbortError` and polling stops. */
  readonly signal?: AbortSignal;
  /** Interval between status polls, in milliseconds. */
  readonly pollIntervalMs?: number;
}

/** Hangfire states in which a job is no longer running. */
const TERMINAL_STATES = new Set(['Succeeded', 'Failed', 'Deleted']);
const DEFAULT_POLL_INTERVAL_MS = 2000;
// A single flaky poll shouldn't fail the whole observation, so retry a status request a few times
// (with a short backoff) before giving up and surfacing the error.
const POLL_RETRY_COUNT = 2;
const POLL_RETRY_DELAY_MS = 1000;

/**
 * Observes a background job's status by polling the API. Reusable across features and job types.
 *
 * {@link observe} returns a cold observable that:
 * - emits the job's {@link JobStatus} whenever its state changes,
 * - completes once the job reaches a terminal state (Succeeded / Failed / Deleted),
 * - errors if a status request keeps failing after retries, or if the caller aborts via a signal.
 *
 * It takes no callbacks by design — the caller subscribes and handles values, completion and errors
 * however it needs.
 */
@Injectable({ providedIn: 'root' })
export class JobStatusService {
  private readonly http = inject(HttpClient);
  private readonly apiBasePath = inject(API_BASE_PATH);

  observe(jobId: string, options: ObserveJobOptions = {}): Observable<JobStatus> {
    const { signal, pollIntervalMs = DEFAULT_POLL_INTERVAL_MS } = options;

    const polling = timer(0, pollIntervalMs).pipe(
      switchMap(() =>
        this.http
          .get<JobStatus>(`${this.apiBasePath}/jobs/${jobId}`)
          .pipe(retry({ count: POLL_RETRY_COUNT, delay: POLL_RETRY_DELAY_MS })),
      ),
      distinctUntilChanged((previous, current) => previous.state === current.state),
      takeWhile((status) => !TERMINAL_STATES.has(status.state), true),
    );

    return signal ? polling.pipe(abortOn(signal)) : polling;
  }
}

/**
 * Errors the source with an `AbortError` when the supplied signal aborts (or is already aborted),
 * and tears the source down. Lets a caller cancel a long-lived observation without knowing how it
 * is implemented.
 */
function abortOn<T>(signal: AbortSignal): MonoTypeOperatorFunction<T> {
  return (source) =>
    new Observable<T>((subscriber) => {
      if (signal.aborted) {
        subscriber.error(abortError());
        return;
      }

      const onAbort = () => subscriber.error(abortError());
      signal.addEventListener('abort', onAbort, { once: true });
      const subscription = source.subscribe(subscriber);

      return () => {
        signal.removeEventListener('abort', onAbort);
        subscription.unsubscribe();
      };
    });
}

function abortError(): DOMException {
  return new DOMException('Job observation aborted.', 'AbortError');
}
