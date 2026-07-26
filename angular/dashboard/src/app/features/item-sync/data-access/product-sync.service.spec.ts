import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { API_BASE_PATH } from '../../../core/api/api-base-path';
import { JobStatus, JobStatusService } from '../../../core/jobs/job-status.service';
import { ProductSyncService } from './product-sync.service';

describe('ProductSyncService', () => {
  let service: ProductSyncService;
  let httpMock: HttpTestingController;
  let observe: ReturnType<typeof vi.fn>;
  const base = 'http://localhost:5257';
  const triggerUrl = `${base}/product-sync`;

  beforeEach(() => {
    observe = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        ProductSyncService,
        { provide: JobStatusService, useValue: { observe } },
        { provide: API_BASE_PATH, useValue: base },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ProductSyncService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  const status = (state: string): JobStatus => ({ id: 'j1', state });

  it('triggers the sync and emits once on success, then completes', () => {
    observe.mockReturnValue(of(status('Processing'), status('Succeeded')));
    const next = vi.fn();
    let completed = false;
    let errored = false;
    service
      .startSync()
      .subscribe({ next, error: () => (errored = true), complete: () => (completed = true) });

    httpMock.expectOne(triggerUrl).flush({ jobId: 'j1', alreadyRunning: false });

    expect(observe).toHaveBeenCalledWith('j1', {});
    expect(next).toHaveBeenCalledTimes(1);
    expect(completed).toBe(true);
    expect(errored).toBe(false);
  });

  it('errors (without emitting) when the job ends in a non-succeeded state', () => {
    observe.mockReturnValue(of(status('Processing'), status('Failed')));
    const next = vi.fn();
    let error: unknown;
    service.startSync().subscribe({ next, error: (e) => (error = e) });

    httpMock.expectOne(triggerUrl).flush({ jobId: 'j1', alreadyRunning: false });

    expect(next).not.toHaveBeenCalled();
    expect((error as Error).message).toMatch(/Failed/);
  });

  it('errors when observing the job errors', () => {
    observe.mockReturnValue(throwError(() => new Error('poll failed')));
    let error: unknown;
    service.startSync().subscribe({ error: (e) => (error = e) });

    httpMock.expectOne(triggerUrl).flush({ jobId: 'j1', alreadyRunning: false });

    expect((error as Error).message).toBe('poll failed');
  });

  it('errors with the HttpErrorResponse when the trigger is rate limited', () => {
    let error: unknown;
    service.startSync().subscribe({ error: (e) => (error = e) });

    httpMock.expectOne(triggerUrl).flush('slow down', { status: 429, statusText: 'Too Many Requests' });

    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(observe).not.toHaveBeenCalled();
  });
});
