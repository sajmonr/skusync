import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { filter, Observable, switchMap, tap } from 'rxjs';
import { API_BASE_PATH } from '../../../core/api/api-base-path';
import { JobStatus, JobStatusService } from '../../../core/jobs/job-status.service';

interface TriggerResponse {
  readonly jobId: string;
  readonly alreadyRunning: boolean;
}

/**
 * Starts a product sync and observes it to completion.
 *
 * {@link startSync} triggers the background job, then observes it via {@link JobStatusService}. The
 * returned observable:
 * - emits once, with the terminal status, when the sync **succeeds**, then completes,
 * - errors when the job ends in a non-succeeded terminal state (a plain `Error`), when the trigger
 *   is refused (e.g. a 429 rate limit) or a status request fails (the underlying `HttpErrorResponse`),
 *   or when the caller aborts via the optional signal (an `AbortError`).
 *
 * Success is delivered as a value (not completion) so a caller that tears the subscription down —
 * e.g. via `takeUntilDestroyed` — completes without ever running its success handler. It surfaces no
 * UI of its own; the caller decides what to show for each outcome.
 */
@Injectable({ providedIn: 'root' })
export class ProductSyncService {
  private readonly http = inject(HttpClient);
  private readonly apiBasePath = inject(API_BASE_PATH);
  private readonly jobStatus = inject(JobStatusService);

  startSync(options: { signal?: AbortSignal } = {}): Observable<JobStatus> {
    return this.http.post<TriggerResponse>(`${this.apiBasePath}/product-sync`, {}).pipe(
      switchMap((trigger) => this.jobStatus.observe(trigger.jobId, options)),
      tap((status) => {
        if (status.state === 'Failed' || status.state === 'Deleted') {
          throw new Error(`Product sync did not succeed (ended in state "${status.state}").`);
        }
      }),
      filter((status) => status.state === 'Succeeded'),
    );
  }
}
