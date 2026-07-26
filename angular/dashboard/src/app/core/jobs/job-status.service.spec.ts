import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_PATH } from '../api/api-base-path';
import { JobStatus, JobStatusService } from './job-status.service';

describe('JobStatusService', () => {
  let service: JobStatusService;
  let httpMock: HttpTestingController;
  const base = 'http://localhost:5257';
  const jobUrl = `${base}/jobs/j1`;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        JobStatusService,
        { provide: API_BASE_PATH, useValue: base },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(JobStatusService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('emits distinct states and completes on a terminal state', () => {
    const states: string[] = [];
    let completed = false;
    service.observe('j1').subscribe({
      next: (status: JobStatus) => states.push(status.state),
      complete: () => (completed = true),
    });

    vi.advanceTimersToNextTimer();
    httpMock.expectOne(jobUrl).flush({ id: 'j1', state: 'Processing' });
    vi.advanceTimersToNextTimer();
    httpMock.expectOne(jobUrl).flush({ id: 'j1', state: 'Processing' }); // duplicate → suppressed
    vi.advanceTimersToNextTimer();
    httpMock.expectOne(jobUrl).flush({ id: 'j1', state: 'Succeeded' });

    expect(states).toEqual(['Processing', 'Succeeded']);
    expect(completed).toBe(true);
  });

  it('retries a transient status failure before surfacing the value', () => {
    const states: string[] = [];
    // A far-off poll interval so only the retry-delay timers fire during the test.
    service.observe('j1', { pollIntervalMs: 1_000_000 }).subscribe({
      next: (status) => states.push(status.state),
    });

    vi.advanceTimersToNextTimer();
    httpMock.expectOne(jobUrl).flush('boom', { status: 503, statusText: 'Service Unavailable' });
    vi.advanceTimersByTime(1000); // retry backoff elapses
    httpMock.expectOne(jobUrl).flush({ id: 'j1', state: 'Succeeded' });

    expect(states).toEqual(['Succeeded']);
  });

  it('errors once the status request keeps failing past its retries', () => {
    let error: unknown;
    service.observe('j1', { pollIntervalMs: 1_000_000 }).subscribe({ error: (e) => (error = e) });

    vi.advanceTimersToNextTimer();
    httpMock.expectOne(jobUrl).flush('x', { status: 503, statusText: 'Service Unavailable' });
    vi.advanceTimersByTime(1000);
    httpMock.expectOne(jobUrl).flush('x', { status: 503, statusText: 'Service Unavailable' });
    vi.advanceTimersByTime(1000);
    httpMock.expectOne(jobUrl).flush('x', { status: 503, statusText: 'Service Unavailable' });

    expect(error).toBeInstanceOf(HttpErrorResponse);
  });

  it('errors with an AbortError when the signal aborts', () => {
    const controller = new AbortController();
    let error: unknown;
    service.observe('j1', { signal: controller.signal }).subscribe({ error: (e) => (error = e) });

    vi.advanceTimersToNextTimer();
    httpMock.expectOne(jobUrl).flush({ id: 'j1', state: 'Processing' });
    controller.abort();

    expect((error as DOMException).name).toBe('AbortError');
  });
});
