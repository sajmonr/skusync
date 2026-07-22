import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { ProductVariantsStore } from './product-variants-store';

describe('ProductVariantsStore', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ProductVariantsStore,
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting()
      ]
    });
  });

  it('should load product variants from the reactive Gridify query', async () => {
    const store = TestBed.inject(ProductVariantsStore);
    const httpTestingController = TestBed.inject(HttpTestingController);
    TestBed.tick();

    const initialRequest = httpTestingController.expectOne(
      (request) => request.url === 'http://localhost:5257/product-variants'
    );
    expect(initialRequest.request.params.get('page')).toBe('1');
    expect(initialRequest.request.params.get('pageSize')).toBe('25');
    expect(initialRequest.request.params.get('orderBy')).toBe('updatedOnUtc desc, id');
    initialRequest.flush({
      items: [
        {
          id: '019d7e72-ad6c-7c7c-85cd-15d18a6089ef',
          productId: 8470520922297,
          variantId: 46874686914745,
          displayName: 'Blue shirt',
          sku: 'SHIRT-BLUE',
          barcode: '100',
          pendingSync: false,
          failedSyncAttempts: 0,
          active: true,
          updatedOnUtc: '2026-04-11T21:29:00Z'
        }
      ],
      totalCount: 1,
      page: 1,
      pageSize: 25
    });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.items()).toHaveLength(1);
    expect(store.items()[0].sku).toBe('SHIRT-BLUE');
    expect(store.totalCount()).toBe(1);
    expect(store.loading()).toBe(false);

    store.load({
      page: 2,
      pageSize: 10,
      filter: 'sku=*shirt/i',
      orderBy: 'sku, id'
    });
    TestBed.tick();

    const filteredRequest = httpTestingController.expectOne(
      (request) => request.url === 'http://localhost:5257/product-variants' && request.params.get('page') === '2'
    );
    expect(filteredRequest.request.params.get('filter')).toBe('sku=*shirt/i');
    expect(filteredRequest.request.params.get('orderBy')).toBe('sku, id');
    filteredRequest.flush({ items: [], totalCount: 0, page: 2, pageSize: 10 });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.items()).toEqual([]);
    expect(store.totalCount()).toBe(0);
    httpTestingController.verify();
  });

  it('should expose Problem Details errors and retry the resource', async () => {
    const store = TestBed.inject(ProductVariantsStore);
    const httpTestingController = TestBed.inject(HttpTestingController);
    TestBed.tick();

    httpTestingController.expectOne('http://localhost:5257/product-variants?page=1&pageSize=25&orderBy=updatedOnUtc%20desc,%20id').flush(
      {
        title: 'Unable to load variants.',
        status: 503,
        detail: 'The database is unavailable.'
      },
      { status: 503, statusText: 'Service Unavailable' }
    );
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.error()).toBe('The database is unavailable.');

    store.retry();
    TestBed.tick();
    httpTestingController
      .expectOne('http://localhost:5257/product-variants?page=1&pageSize=25&orderBy=updatedOnUtc%20desc,%20id')
      .flush({ items: [], totalCount: 0, page: 1, pageSize: 25 });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.error()).toBeNull();
    httpTestingController.verify();
  });
});
