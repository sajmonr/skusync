import { HttpClient } from '@angular/common/http';
import { inject, Injectable, OnDestroy, signal } from '@angular/core';
import { MessageService } from 'primeng/api';
import { catchError, of, Subscription, switchMap, takeWhile, tap, timer } from 'rxjs';
import { API_BASE_PATH } from '../../../core/api/api-base-path';

interface TriggerResponse {
  readonly jobId: string;
  readonly alreadyRunning: boolean;
}

interface JobStatus {
  readonly id: string;
  readonly state: string;
}

const TERMINAL_STATES = new Set(['Succeeded', 'Failed', 'Deleted']);
const POLL_INTERVAL_MS = 2000;

/**
 * Triggers a product sync as a background job and polls its status to completion.
 *
 * Web.Api enqueues the job (returning a job id) and AppServer runs it; this store polls
 * `GET /jobs/{id}` until the job reaches a terminal state, then raises a toast and — on success —
 * lets the caller refresh the table. Unlike a streaming channel there is no long-lived connection:
 * requests go through the normal HttpClient interceptors (so the auth cookie is attached), and a
 * dropped poll just retries on the next tick.
 *
 * Route-scoped (provided alongside {@link ItemSyncStore}) so polling stops when the page unloads.
 */
@Injectable()
export class ProductSyncStore implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly apiBasePath = inject(API_BASE_PATH);
  private readonly messages = inject(MessageService);
  private pollSubscription?: Subscription;

  readonly syncing = signal(false);
  readonly state = signal<string | null>(null);

  /**
   * Starts a sync (or attaches to one already running) and polls to completion. `onCompleted`
   * fires once, only when the job succeeds, so the caller can refresh dependent data. Ignored while
   * a sync is already being tracked — the server enforces the same single-flight guard.
   */
  syncNow(onCompleted: () => void): void {
    if (this.syncing()) {
      return;
    }

    this.syncing.set(true);
    this.state.set('Enqueued');

    this.http
      .post<TriggerResponse>(`${this.apiBasePath}/product-sync`, {})
      .pipe(catchError(() => of(null)))
      .subscribe((response) => {
        if (response === null) {
          this.finish('error', 'Sync could not be started', 'The request to start the sync failed.');
          return;
        }
        this.pollUntilDone(response.jobId, onCompleted);
      });
  }

  ngOnDestroy(): void {
    this.pollSubscription?.unsubscribe();
  }

  private pollUntilDone(jobId: string, onCompleted: () => void): void {
    this.pollSubscription?.unsubscribe();
    this.pollSubscription = timer(0, POLL_INTERVAL_MS)
      .pipe(
        switchMap(() =>
          this.http
            .get<JobStatus>(`${this.apiBasePath}/jobs/${jobId}`)
            .pipe(catchError(() => of<JobStatus>({ id: jobId, state: 'Failed' }))),
        ),
        tap((status) => this.state.set(status.state)),
        takeWhile((status) => !TERMINAL_STATES.has(status.state), true),
      )
      .subscribe((status) => {
        if (!TERMINAL_STATES.has(status.state)) {
          return;
        }
        if (status.state === 'Succeeded') {
          this.finish('success', 'Sync complete', 'The product sync finished.');
          onCompleted();
        } else {
          this.finish('error', 'Sync failed', 'The product sync did not complete successfully.');
        }
      });
  }

  private finish(severity: 'success' | 'error', summary: string, detail: string): void {
    this.syncing.set(false);
    this.messages.add({ severity, summary, detail });
  }
}
