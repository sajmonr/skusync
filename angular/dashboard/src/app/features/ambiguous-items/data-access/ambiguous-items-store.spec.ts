import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { AmbiguousItem } from '../models/ambiguous-item';
import { AmbiguousItemsStore } from './ambiguous-items-store';

describe('AmbiguousItemsStore', () => {
  const url = 'http://localhost:5257/ambiguous-items';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AmbiguousItemsStore,
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
  });

  function sampleItem(overrides: Partial<AmbiguousItem> = {}): AmbiguousItem {
    return {
      id: 'a1',
      skulabsItemId: 'src-1',
      name: 'Ambiguous One',
      sku: 'sku',
      upc: 'upc',
      listingCount: 2,
      firstSeenUtc: '2026-07-26T00:00:00Z',
      lastSeenUtc: '2026-07-26T00:00:00Z',
      skulabsUrl: 'https://app.skulabs.com/item?id=src-1',
      listings: [],
      ...overrides,
    };
  }

  it('should request the default page with no search parameter', async () => {
    TestBed.inject(AmbiguousItemsStore);
    const httpTestingController = TestBed.inject(HttpTestingController);
    TestBed.tick();

    const request = httpTestingController.expectOne((r) => r.url === url);
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('25');
    expect(request.request.params.has('search')).toBe(false);
    request.flush({ items: [], totalCount: 0, page: 1, pageSize: 25 });
    await TestBed.inject(ApplicationRef).whenStable();

    httpTestingController.verify();
  });

  it('should expose the mapped items and total count from the response', async () => {
    const store = TestBed.inject(AmbiguousItemsStore);
    const httpTestingController = TestBed.inject(HttpTestingController);
    TestBed.tick();

    const request = httpTestingController.expectOne((r) => r.url === url);
    request.flush({ items: [sampleItem()], totalCount: 1, page: 1, pageSize: 25 });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.totalCount()).toBe(1);
    expect(store.items().length).toBe(1);
    expect(store.items()[0].skulabsItemId).toBe('src-1');
    expect(store.loading()).toBe(false);
    expect(store.error()).toBeNull();
    httpTestingController.verify();
  });

  it('should pass the search query parameter when filtering', async () => {
    const store = TestBed.inject(AmbiguousItemsStore);
    const httpTestingController = TestBed.inject(HttpTestingController);
    TestBed.tick();

    httpTestingController
      .expectOne((r) => r.url === url)
      .flush({ items: [], totalCount: 0, page: 1, pageSize: 25 });
    await TestBed.inject(ApplicationRef).whenStable();

    store.load({ page: 2, pageSize: 50, search: 'widget' });
    TestBed.tick();

    const filtered = httpTestingController.expectOne(
      (r) => r.url === url && r.params.get('search') === 'widget',
    );
    expect(filtered.request.params.get('page')).toBe('2');
    expect(filtered.request.params.get('pageSize')).toBe('50');
    filtered.flush({ items: [], totalCount: 0, page: 2, pageSize: 50 });
    await TestBed.inject(ApplicationRef).whenStable();

    httpTestingController.verify();
  });

  it('should surface the problem-details message when the request fails', async () => {
    const store = TestBed.inject(AmbiguousItemsStore);
    const httpTestingController = TestBed.inject(HttpTestingController);
    TestBed.tick();

    const request = httpTestingController.expectOne((r) => r.url === url);
    request.flush(
      { title: 'Server error', status: 500, detail: 'Something went wrong.' },
      { status: 500, statusText: 'Internal Server Error' },
    );
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.error()).toBe('Something went wrong.');
    expect(store.loading()).toBe(false);
    httpTestingController.verify();
  });
});
