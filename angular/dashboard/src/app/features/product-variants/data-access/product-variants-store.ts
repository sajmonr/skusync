import { httpResource } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { IGridifyQuery } from 'gridify-client';
import { API_BASE_PATH } from '../../../core/api/api-base-path';
import { ApiRequestError } from '../../../core/api/api-request-error';
import { toGridQueryParams } from '../../../core/api/grid-query-params';
import { PagedResponse } from '../../../core/api/paged-response';
import { ProductVariantListItem } from '../models/product-variant-list-item';

const initialQuery: IGridifyQuery = {
  page: 1,
  pageSize: 25,
  filter: '',
  orderBy: 'updatedOnUtc desc, id'
};

const emptyResponse: PagedResponse<ProductVariantListItem> = {
  items: [],
  totalCount: 0,
  page: initialQuery.page,
  pageSize: initialQuery.pageSize
};

@Injectable()
export class ProductVariantsStore {
  private readonly apiBasePath = inject(API_BASE_PATH);
  private readonly query = signal(initialQuery);
  private readonly productVariantsResource = httpResource<PagedResponse<ProductVariantListItem>>(
    () => ({
      url: `${this.apiBasePath}/product-variants`,
      params: toGridQueryParams(this.query())
    }),
    { defaultValue: emptyResponse }
  );

  readonly items = computed(() => this.productVariantsResource.value().items);
  readonly totalCount = computed(() => this.productVariantsResource.value().totalCount);
  readonly loading = this.productVariantsResource.isLoading;
  readonly error = computed(() => this.getErrorMessage(this.productVariantsResource.error()));

  load(query: IGridifyQuery): void {
    if (!this.queriesMatch(this.query(), query)) {
      this.query.set(query);
    }
  }

  retry(): void {
    this.productVariantsResource.reload();
  }

  private getErrorMessage(error: unknown): string | null {
    if (error === undefined) {
      return null;
    }

    if (error instanceof ApiRequestError) {
      return error.problemDetails.detail ?? error.problemDetails.title;
    }

    return 'Product variants could not be loaded. Please try again.';
  }

  private queriesMatch(left: IGridifyQuery, right: IGridifyQuery): boolean {
    return (
      left.page === right.page &&
      left.pageSize === right.pageSize &&
      left.filter === right.filter &&
      left.orderBy === right.orderBy
    );
  }
}
