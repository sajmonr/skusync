import { httpResource } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { API_BASE_PATH } from '../../../core/api/api-base-path';
import { ApiRequestError } from '../../../core/api/api-request-error';
import { PagedResponse } from '../../../core/api/paged-response';
import { AmbiguousItem } from '../models/ambiguous-item';

export interface AmbiguousItemsQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly search: string;
}

const initialQuery: AmbiguousItemsQuery = {
  page: 1,
  pageSize: 25,
  search: '',
};

const emptyResponse: PagedResponse<AmbiguousItem> = {
  items: [],
  totalCount: 0,
  page: initialQuery.page,
  pageSize: initialQuery.pageSize,
};

@Injectable()
export class AmbiguousItemsStore {
  private readonly apiBasePath = inject(API_BASE_PATH);
  private readonly query = signal<AmbiguousItemsQuery>(initialQuery);
  private readonly ambiguousItemsResource = httpResource<PagedResponse<AmbiguousItem>>(
    () => ({
      url: `${this.apiBasePath}/ambiguous-items`,
      params: this.toQueryParams(this.query()),
    }),
    { defaultValue: emptyResponse },
  );

  readonly items = computed(() => this.ambiguousItemsResource.value().items);
  readonly totalCount = computed(() => this.ambiguousItemsResource.value().totalCount);
  readonly loading = this.ambiguousItemsResource.isLoading;
  readonly error = computed(() => this.getErrorMessage(this.ambiguousItemsResource.error()));

  load(query: AmbiguousItemsQuery): void {
    if (!this.queriesMatch(this.query(), query)) {
      this.query.set(query);
    }
  }

  retry(): void {
    this.ambiguousItemsResource.reload();
  }

  private toQueryParams(query: AmbiguousItemsQuery): Record<string, string | number> {
    const parameters: Record<string, string | number> = {
      page: query.page,
      pageSize: query.pageSize,
    };

    if (query.search) {
      parameters['search'] = query.search;
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

    return 'Ambiguous items could not be loaded. Please try again.';
  }

  private queriesMatch(left: AmbiguousItemsQuery, right: AmbiguousItemsQuery): boolean {
    return (
      left.page === right.page &&
      left.pageSize === right.pageSize &&
      left.search === right.search
    );
  }
}
