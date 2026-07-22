import { ConditionalOperator, GridifyQueryBuilder, IGridifyQuery } from 'gridify-client';

export interface ProductVariantQueryState {
  readonly first: number;
  readonly rows: number;
  readonly search: string;
  readonly sortField?: string | readonly string[] | null;
  readonly sortOrder?: number | null;
}

const sortableFields = new Set([
  'displayName',
  'sku',
  'barcode',
  'productId',
  'variantId',
  'pendingSync',
  'failedSyncAttempts',
  'active',
  'updatedOnUtc',
]);

export function buildProductVariantQuery(state: ProductVariantQueryState): IGridifyQuery {
  const builder = new GridifyQueryBuilder()
    .setPage(Math.floor(state.first / state.rows) + 1)
    .setPageSize(state.rows);
  const search = state.search.trim();

  if (search) {
    builder
      .startGroup()
      .addCondition('displayName', ConditionalOperator.Contains, search, false)
      .or()
      .addCondition('sku', ConditionalOperator.Contains, search, false)
      .or()
      .addCondition('barcode', ConditionalOperator.Contains, search, false)
      .endGroup();
  }

  const requestedSortField = Array.isArray(state.sortField) ? state.sortField[0] : state.sortField;
  const sortField =
    typeof requestedSortField === 'string' && sortableFields.has(requestedSortField)
      ? requestedSortField
      : 'updatedOnUtc';

  builder.addOrderBy(sortField, state.sortOrder !== 1).addOrderBy('id');
  return builder.build();
}
