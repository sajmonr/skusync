import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { ItemSyncStore } from './item-sync-store';

describe('ItemSyncStore', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ItemSyncStore,
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
  });

  it('should load item sync data and pass search and status query parameters', async () => {
    const store = TestBed.inject(ItemSyncStore);
    const httpTestingController = TestBed.inject(HttpTestingController);
    TestBed.tick();

    const initialRequest = httpTestingController.expectOne(
      (request) => request.url === 'http://localhost:5257/item-sync',
    );
    expect(initialRequest.request.params.get('page')).toBe('1');
    expect(initialRequest.request.params.get('pageSize')).toBe('25');
    expect(initialRequest.request.params.has('search')).toBe(false);
    initialRequest.flush({ items: [], totalCount: 0, page: 1, pageSize: 25 });
    await TestBed.inject(ApplicationRef).whenStable();

    store.load({ page: 1, pageSize: 25, search: 'alpine', status: 'out-of-sync' });
    TestBed.tick();

    const filteredRequest = httpTestingController.expectOne(
      (request) => request.url === 'http://localhost:5257/item-sync' && request.params.get('search') === 'alpine',
    );
    expect(filteredRequest.request.params.get('status')).toBe('out-of-sync');
    filteredRequest.flush({ items: [], totalCount: 0, page: 1, pageSize: 25 });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.loading()).toBe(false);
    httpTestingController.verify();
  });
});
