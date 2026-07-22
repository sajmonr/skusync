import { httpResource } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { API_BASE_PATH } from '../../../core/api/api-base-path';
import { ApiRequestError } from '../../../core/api/api-request-error';
import { PagedResponse } from '../../../core/api/paged-response';
import { ItemSyncListItem, ItemSyncStatus } from '../models/item-sync-list-item';

export interface ItemSyncQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly search: string;
  readonly status: ItemSyncStatus | 'all';
}

const initialQuery: ItemSyncQuery = {
  page: 1,
  pageSize: 25,
  search: '',
  status: 'all',
};

const emptyResponse: PagedResponse<ItemSyncListItem> = {
  items: [],
  totalCount: 0,
  page: initialQuery.page,
  pageSize: initialQuery.pageSize,
};

@Injectable()
export class ItemSyncStore {
  private readonly apiBasePath = inject(API_BASE_PATH);
  private readonly query = signal<ItemSyncQuery>(initialQuery);
  private readonly itemSyncResource = httpResource<PagedResponse<ItemSyncListItem>>(
    () => ({
      url: `${this.apiBasePath}/item-sync`,
      params: this.toQueryParams(this.query()),
    }),
    { defaultValue: emptyResponse },
  );

  readonly items = computed(() => this.itemSyncResource.value().items);
  readonly totalCount = computed(() => this.itemSyncResource.value().totalCount);
  readonly loading = this.itemSyncResource.isLoading;
  readonly error = computed(() => this.getErrorMessage(this.itemSyncResource.error()));

  load(query: ItemSyncQuery): void {
    if (!this.queriesMatch(this.query(), query)) {
      this.query.set(query);
    }
  }

  retry(): void {
    this.itemSyncResource.reload();
  }

  private toQueryParams(query: ItemSyncQuery): Record<string, string | number> {
    const parameters: Record<string, string | number> = {
      page: query.page,
      pageSize: query.pageSize,
    };

    if (query.search) {
      parameters['search'] = query.search;
    }

    if (query.status !== 'all') {
      parameters['status'] = query.status;
    }

    return parameters;
  }

  private getErrorMessage(error: unknown): string | null {
    if (error === undefined) {
      return null;
    }

    if (error instanceof ApiRequestError) {
      return error.problemDetails.detail ?? error.problemDetails.title;
    }

    return 'Item sync data could not be loaded. Please try again.';
  }

  private queriesMatch(left: ItemSyncQuery, right: ItemSyncQuery): boolean {
    return (
      left.page === right.page &&
      left.pageSize === right.pageSize &&
      left.search === right.search &&
      left.status === right.status
    );
  }
}
