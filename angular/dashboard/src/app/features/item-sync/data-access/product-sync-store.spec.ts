import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MessageService } from 'primeng/api';
import { API_BASE_PATH } from '../../../core/api/api-base-path';
import { ProductSyncStore } from './product-sync-store';

describe('ProductSyncStore', () => {
  let store: ProductSyncStore;
  let httpMock: HttpTestingController;
  let messages: { add: ReturnType<typeof vi.fn> };

  const base = 'http://localhost:5257';

  beforeEach(() => {
    vi.useFakeTimers();
    messages = { add: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        ProductSyncStore,
        { provide: API_BASE_PATH, useValue: base },
        { provide: MessageService, useValue: messages },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    store = TestBed.inject(ProductSyncStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('enqueues, polls, refreshes once and toasts on success', () => {
    let completed = 0;
    store.syncNow(() => (completed += 1));

    httpMock.expectOne(`${base}/product-sync`).flush({ jobId: 'j1', alreadyRunning: false });
    expect(store.syncing()).toBe(true);

    vi.advanceTimersToNextTimer(); // first poll
    httpMock.expectOne(`${base}/jobs/j1`).flush({ id: 'j1', state: 'Processing' });
    expect(store.state()).toBe('Processing');

    vi.advanceTimersToNextTimer(); // second poll → terminal
    httpMock.expectOne(`${base}/jobs/j1`).flush({ id: 'j1', state: 'Succeeded' });

    expect(completed).toBe(1);
    expect(store.syncing()).toBe(false);
    expect(messages.add).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }));
  });

  it('stops on Failed without calling the completion callback', () => {
    let completed = 0;
    store.syncNow(() => (completed += 1));
    httpMock.expectOne(`${base}/product-sync`).flush({ jobId: 'j2', alreadyRunning: false });

    vi.advanceTimersToNextTimer();
    httpMock.expectOne(`${base}/jobs/j2`).flush({ id: 'j2', state: 'Failed' });

    expect(completed).toBe(0);
    expect(store.syncing()).toBe(false);
    expect(messages.add).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }));
  });

  it('surfaces a trigger failure as an error toast', () => {
    store.syncNow(() => undefined);
    httpMock
      .expectOne(`${base}/product-sync`)
      .flush('nope', { status: 500, statusText: 'Server Error' });

    expect(store.syncing()).toBe(false);
    expect(messages.add).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }));
  });

  it('ignores a second syncNow while one is in flight', () => {
    store.syncNow(() => undefined);
    httpMock.expectOne(`${base}/product-sync`).flush({ jobId: 'j3', alreadyRunning: false });

    vi.advanceTimersToNextTimer();
    httpMock.expectOne(`${base}/jobs/j3`).flush({ id: 'j3', state: 'Processing' });

    store.syncNow(() => undefined);
    httpMock.expectNone(`${base}/product-sync`);
  });
});
